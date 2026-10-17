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

## Every survivor line voiced, six voices (round vo2)

**Deployed 2026-10-17** (Fable review and re-check: deployable; DLL md5 `25e6fc49af09cc14ef68289ebfda52aa`, commit
baf4027). Your ask (2026-10-17): voice all the dialogue with ElevenLabs; your picks:
three voices per sex - Vane, B. Hardscrabble Oxley and David for men, Jessica, Beth and Mora for women - one take
each. Every line a survivor says in SC's talks is voiced now (147 per voice), the enclaves' included. A line with a
name or a number in it says only the part without them; the panel still shows the whole line. The `.txt` beside each
clip in `Resources/Voices/<voice>/` holds the words it says, and `sc voice play <key>` replays one.

1. `sc voice` lists six voices - female_Beth, female_Jessica, female_Mora, male_David, male_Oxley, male_Vane - with
   147 lines each.
2. Talk to a few survivors of each sex: you hear more than one voice per sex. Someone you have talked to before may
   have another voice now: adding voices re-deals some (expected, once).
3. An enclave's leader and one of their people greet you aloud, by standing. "Tell me about this place": the leader
   says "It's not much, but it keeps the dead out and the rain off. These people are mine to look after." - the
   group's name, count and place are in the panel only. Ask one of their people for the leader: "You'll want to talk
   to the one in charge, not me." aloud, where the leader is in the panel only.
4. A leader with an offer, "What do you need?": the first sentence (by kind and standing) is spoken; the sentence after
   it (what, how much, by when) is text only. Try a few kinds:
   `sc enclave request <id> offer <resource|generator|gun|missing|infestation>` makes one on demand, and
   `sc enclave request <id> accept` skips to the wait (steps 5 and 7).
5. Accept it: their thanks ("Thank you. We'll be here." or the kind's own line) is spoken. Ask again while it is open:
   the wait line is spoken.
6. "Let me think about it.": the talk closes, the leader's word shows as a tell and is spoken - closing the talk does
   not cut it off.
7. Decline one, give one up ("I can't do it."), hand one over, and open the gun pick ("Let's see what you've got.
   Which one can you spare?"): each line is spoken; the standing aside after a reply is text only.
8. The two replies that guard a stale talk have no row you can press: next to a leader,
   `sc voice play scEncReqNotCarried <leader's name>` and `sc voice play scEncReqNotCleared <leader's name>` -
   "You don't seem to have it on you." and "That place is still crawling with them." aloud (without a name, the
   nearest voiced survivor within 20 m says it).
9. A leader with nothing to ask: "Nothing right now. We're managing." aloud; after a request done while you were away,
   their thanks instead (once).
10. Enlist one of their people: "I could come along and watch your back for a while." aloud, the fee in the panel
    only; "Right behind you. Say the word when you want me to head back." when they join. Recruit: "Leave my
    people? ..." aloud, the cost in the panel only.
11. Threaten a leader: "Hand over your supplies." - "You really want to go down that road? ..." aloud; "I'm not
    asking." - "Fine. Take it and get out. ..." aloud. This turns that enclave hostile for good: use one you can
    spare, or a save you can drop. A missing survivor you find: their lines aloud.
12. The 30-second rule holds for these lines too: ask "What do you need?" twice within 30 seconds - the second isn't
    spoken.
13. `sc voice play scEncAsk_gun_friendly` makes the nearest survivor say that line.
14. How the new voices (Oxley, David, Beth, Mora) and the takes sound, and any line worth re-recording.

## Enclave allies (E6)

**Deployed 2026-10-17**, E2-E6 together (Fable review and re-check: deployable; DLL md5 `8cb0deca59c8e46a095c04c14e2d2018`). An Allied enclave's benefit, the Allies / Comfort in Numbers / Enclave lost
morale parts, a new group after a lost one, allies telling you of more, and the radio window's Enclaves column.
`sc enclave list` names each ally and its benefit and the refills owed; `sc enclave info <id>` shows a group's benefit
and whether it applies; `sc enclave standing <id> <points>` sets the standing (75: Allied, 50: Friendly); `sc morale`
shows the parts; `sc enclave refill [now]` the refills. Use known, non-hostile enclaves (`sc enclave reveal all`).

1. **The daily bundle, each kind.** For an enclave of each kind - growers, medics, militia, builders, a road crew
   (`sc enclave info <id>`: "kind growers" and "benefit: Share of the Harvest [harvest] - +1 food a day; not until
   they are Allied") - `sc enclave standing <id> 75`: the tell "You and <enclave> are allies now: Share of the Harvest
   (+1 food a day).", the ledger "allied with <enclave>: Share of the Harvest"; `sc enclave info <id>` now says
   "applies now (Allied)". `sc day`: the day's tooltip has "- allies sent +1 food" (+1 medical, ammo, building
   material or fuel for the others) and the log "allies, day N: +1 food into the stockpile from <enclave>". The bundle
   comes before the day's use is taken, so `sc status` shows one more than the day would have left without it.
2. **Back to Friendly stops it.** `sc enclave standing <id> 50`: "You and <enclave> are no longer allies: their Share
   of the Harvest stops." and a ledger line; `sc day`: no bundle from them. `sc enclave standing <id> 75` again: it is
   back.
3. **Morale +2 per ally, up to +6.** Make 1, 2, 3, then 4 enclaves Allied: `sc morale` shows "Allies +2", "+4", "+6",
   "+6" ("(4 allied: ...)" with their benefits); the community window's morale parts list "Allied enclaves: 4 ... +6".
4. **Comfort in Numbers, Overflow Barracks.** A scavengers' ally with Comfort in Numbers (`sc enclave list` shows
   `comfort`): `sc morale` "Comfort in Numbers +5", and the part in the community window. One with Overflow Barracks
   (`barracks`): `sc morale`'s beds line and the community window's bed line say "(+3 from allies)", the beds count 3
   higher and the beds part follows (more members than beds before: fewer short now).
5. **XP clubs and Training and Advice.** With a Fight Club ally, `sc xp <member> fighting 10`: `sc members` shows 20
   more Fighting XP (more with high morale or a trait: x2 on top of those); Wits XP is unchanged by it. With Training
   and Advice: 15 for 10 in every skill and in the specialty (`sc xp <member> spec 10`). Two clubs for one skill: x3;
   the allies' part never goes past x3 (`EnclaveBenefitXpCap`).
6. **Neighborhood Watch.** With a Watch ally: `sc enclave request <id> offer infestation` at that ally with no
   infestation near it is refused ("... none can start there (no POI within 300 m of <enclave> can be infested)");
   another enclave's offer still starts one, never within 500 m of the Watch ally; `sc infest roll` a few times
   never picks a building within 500 m of them (`sc infest list` with `sc enclave info <id>`'s centre); `sc infest
   spread <id>` of an infestation near them does not spread into a building within 500 m of them. Over a dozen
   `sc day`s an infestation within 500 m of them spreads about half as often as one elsewhere. `sc infest add` still
   works there (the console forces it).
7. **A friend lost costs 5 morale, fading.** A Friendly enclave (`sc enclave standing <id> 40`) whose people are made
   (go near it once, or `sc enclave spawn <id>` from close by), then
   `sc enclave kill <id> all`: "<enclave> is gone." then "Losing <enclave> hits everyone hard (morale -5)."; `sc morale`
   "Enclave lost -5", then -4, -3 ... a point each `sc day`. An Allied one with a benefit: "... and their <benefit> is
   gone with them." and a ledger line. A Neutral or raider enclave lost: no morale change. With SurvivorOrigins'
   Leadership shield the part shrinks.
8. **A new group 5 days later.** After step 7, `sc enclave refill`: one owed, "from day N" (the request clock + 5).
   `sc day` five times: the log "refill for '<lost>': enclave M '<new>' moved into <building> ..."; with a powered
   radio the tell "Word on the radio: a new group has settled near <building>, <where>." and a ledger line.
   `sc enclave list`: the new one, "not found", mode `refill`, in another building than the lost one, with its own
   name; going there finds it (E1). At the map's enclave count (`sc enclave refill` shows "target"), a refill is
   dropped instead (the log: "not owed any more"). `sc enclave refill now` moves every owed one in at once (one anyway
   with none owed).
9. **An ally tells you of one more.** With unknown enclaves left (`sc enclave hide all`, then `sc enclave reveal <id>`
   for one that has not been Allied yet - `sc enclave info <id>` says they "tell of another at once"; standing 75 on
   one already at 75 changes nothing): `sc enclave standing <id> 75`: "Word from <ally>: <other>, holed up at <building>, <where>." with its
   marker on the map and a ledger line - the one nearest to the ally. `sc enclave info <id>`: "tell of another from day
   N+7". `sc enclave standing <id> 50`, then `75` again: no second word. `sc day` seven times while Allied: the next
   one.
10. **"Tell me about this place."** At a non-allied survivor enclave's leader, the reply ends "Stand with us, and
    you'll have our <benefit>."; allied: "And you have our <benefit>, as our allies."; the whole reply fits the line
    panel's four lines. A raider or hostile leader never says it.
11. **The radio window's Enclaves column.** Open a powered radio: the window is wider, the station's own sections on
    the left unchanged. Right: "Enclaves you know", nearest first, with the standing in its marker's colour (blue
    Allied, green Friendly, grey Neutral, red Hostile), the benefit (blue while it applies, "raiders" for raiders) and
    where; more than 15 known: "+N more, farther away". "Their requests": each open request's enclave, what, and
    "answer by day N" / "due by day N" (red on its day). No siege: "No enclave is calling for help." Check it at
    1920x1080 and at the smallest resolution you play: nothing runs off the screen.
12. **On our way.** `sc enclave siege <id> now`: the Siege calls panel shows "<enclave> is under attack! 14:59 left to
    reach them, <where>." counting down, and two buttons. "On our way": the tell "<enclave> on the radio: "Thank you!
    We'll hold them off as long as we can."", the button greys to "They know you're coming", the panel adds "You told
    them you're on your way."; the countdown and the siege run on unchanged (step 8 of E5 still wins it). Once you get
    there and it goes live, the panel shows "<enclave>: the fight is on, N of the dead left." and no buttons.
13. **We can't come.** Another call: press "We can't come" once: "Press We can't come again: they will fall without
    you."; wait 5 seconds: the hint clears and nothing happened. Press it twice: "You told <enclave> you can't come.
    They're on their own.", then as when the clock runs out (E5 step 9): "<enclave> is gone.", the building infested,
    the quest failed - and, Friendly or Allied, "Enclave lost -5"; a refill is owed (step 8).
14. **A world day set back.** With a refill owed, an ally's next word, a recruit cooldown and an open request, note
    their days and the request clock's day (`sc enclave refill`, `sc enclave info <id>`, `sc enclave people <id>`,
    `sc enclave requests`), then `settime <day> <hour> <minute>` to two days earlier: one log line "enclaves: the
    world's day went back from N to M - the request clock, ... moved back R day(s); ... moved back W day(s)". The
    recruit cooldown is W (two) days earlier; the refill, the ally's next word and the request's days are R days
    earlier, R = the request clock's day before the jump minus M (more than two after some `sc day`s, which move the
    request clock ahead of the world) - each is as many days away as it was. `sc day`'s own end never logs it.
15. **Saved.** Save and quit with an ally, a refill owed and an Enclave lost part; load: `sc enclave list` and
    `sc morale` show the same (`benefit`, `allyreveal` in `[enclave]`; `encloss`, `encrefill` in `[community]`).

## Enclave requests, part 2 (E5)

**Deployed 2026-10-17**, E2-E6 together (Fable review and re-check: deployable; DLL md5 `8cb0deca59c8e46a095c04c14e2d2018`). Three more kinds: a missing survivor, an infestation to clear and a siege.
`sc enclave request <id> offer missing|infestation` makes an offer now (only where it can be given:
`sc enclave request <id>` says why not); `sc enclave request <id> fate alive|dead` fixes a missing survivor's fate
before you get there (this session); `sc enclave siege <id>` shows the siege clock and why a call would wait;
`sc enclave siege <id> now [force]` calls one now (`force` skips the radio, the range, discovery and the blood moon);
`... win | lose | clear` settles it. `sc enclave requests` lists everything, the siege's schedule too. Use a known,
non-hostile enclave whose people were made (go there once, or `sc enclave spawn <id>` near it).

1. **Alive, brought home.** `sc enclave request <id> offer missing`, talk to the leader: "What do you need?" shows their
   words, then "Once you agree, I'll tell you who it is and where they were headed...". "Leave it with me.": "Thank
   you. Bring them home if you can. It's <name>. They were headed for <building>. Find them by the end of day N." The
   journal has "Find <name>" ("Find <name> at <building> by the end of day N") with its marker on that building, 200-800
   m away; the person is gone from the enclave - their body vanishes at once, even in view (`sc enclave people <id>`:
   state missing). `sc enclave request <id> fate
   alive`, go there: once you are within 150 m and the building has loaded, the log says "<name> ... is found ALIVE";
   they stand at an indoor spot (often among the building's own dead). Talk to them: "Please, I'm not armed..." with
   "Your people sent me. Let's get you home." -> "Thank God. Lead the way...", the tell "<name> is following you. Get
   them home to <enclave>.", the journal line turns to "Bring <name> home to <enclave>" and its marker moves to the
   enclave. They follow you even with your followers full. Walk home: the moment they are inside the grounds, "<name> is
   home safe at <enclave>.", the quest completes, +15 standing, the ledger "<enclave>: you brought <name> home (+15
   standing)", SurvivorOrigins' "rescue" XP (100). They take up their old spot. Talk to the leader, "What do you need?":
   their thanks ("You brought one of ours home...") once, then "Nothing right now. We're managing."
2. **Wait here, and a reload.** As step 1 up to the follow, then talk to them again: "Wait here for me." - "<name> will
   wait here for you."; walk on: they stay. Talk again: "Your people sent me..." and they follow again. Save and quit
   while they follow; load: they wait where they stood when you quit, not following, and no stray hire is left on you
   (SCore's companion list is empty of them). After a crash instead they wait where they last waited (the building, or
   where you told them to wait), and the log says "<name> (pN) was following the player home when the game was saved".
3. **Alive, dies on the way.** As step 1, and while they follow, let them die (zombies, or vanilla's `kill <entity id>`
   with the id from the log's "<name> (pN, entity N) of <enclave> follows <you> home" line; not `sc enclave kill`,
   which only takes the living):
   "<name> of <enclave> is dead.", the quest fails, -8 standing (Neutral), the ledger "<enclave>: <name> died on the way
   home (-8 standing)"; `sc enclave people <id>`: dead. Another run: `fate alive`, and let the building's own zombies
   kill them before you talk to them (killing them yourself turns the enclave hostile, which withdraws the request):
   the search turns to their remains where they fell (step 4).
4. **Dead: remains, search, tell.** `offer missing`, accept, `fate dead`, go there: the log says "found DEAD" and a corpse
   lies at an indoor spot. Look at it: "Someone's remains"; hold E: "Search the body". Search: "These are <name>'s
   remains. <enclave> should hear it from you.", the journal's line turns to "Tell <enclave> about <name>" (its phase
   2) and the marker moves to the enclave. Search again: "You've searched <name>'s remains already...". Break the
   remains before searching (another run): they are back within seconds while you stand there. Talk to the leader:
   "I found <name>. I'm sorry." -> "I was afraid of that..." (+8), the quest completes, the "favour" XP (60); the
   remains are gone the next time you are there (or at once if loaded). "I can't do it." does not show once the
   remains were searched, nor while a survivor follows you.
5. **An existing infestation named.** `sc infest add` inside a building within 300 m of an enclave (or use one that is
   there), then `offer infestation` at that enclave: "The dead have taken a place near us..." and "It's <building>, <n>
   m <direction> from here...". Just hearing it puts the infestation's purple marker on the map. Accept: the journal has
   "Clear <building>" ("Clear <building> by the end of day N") with its marker on it. Clear it (or `sc infest clear` is
   NOT the way: that withdraws the request, see below). When I1 says "You've cleared the infestation at <building>.",
   also "Word will reach <enclave>: <building> is clear. Talk to their leader.", the quest completes, +15, the "favour"
   XP, the ledger "<enclave>: you cleared <building> (+15 standing)" - even when you are far from the enclave. Talk to
   the leader: their thanks once. A second enclave near the same infestation is never given the same one.
6. **One started for the request.** At an enclave with no infestation within 300 m: `offer infestation`: the log says
   "infestation #N at <building> started near them for their request" (`sc infest list`: how "request"). It is within
   300 m, never an enclave's, claimed or quest-locked building. With the infestations at their cap (`sc infest list`
   says the cap), the offer is refused ("the infestations are at their cap").
7. **Already cleared, on the spot.** `offer infestation`, don't accept; clear that infestation; talk to the leader: the
   offer now shows "I already took care of it." (and neither "Leave it with me." nor "No, I can't help with that."):
   their thanks, +15. Left unanswered past
   its day instead, it is withdrawn with no loss. `sc infest clear <id>` on a request's infestation withdraws the request
   (no standing change).
8. **A siege in time, won.** A powered radio at home, a known enclave within 2 km: `sc enclave siege <id> now`: the tell
   "<enclave> on the radio: "They're coming over the walls! Please, hurry!"", the ledger line, and a tracked journal
   quest "Siege at <enclave>" with "Siege: 14:59 to reach <enclave>" counting down on the HUD. Go there: within 70 m
   all of their people appear (if they were not out), "<enclave> is under attack: N of the dead are coming over the
   walls!", the journal says "N left"; the zombies come one a second from the enclave's far side from you, never popping
   up near you in view, and go for their people. Kill them all: "<enclave> held. You killed N of them.", the quest
   completes, +25, the "rescue" XP; the leader thanks you at the next talk.
9. **Too late.** `sc enclave siege <id> now` and stay away until the clock runs out (or set a short
   `EnclaveSiegeMinutes`): "<enclave> is gone.", "The dead have overrun <building>.", the quest fails; the enclave is
   gone from `sc enclave list`, and `sc infest list` has its building (how "siege", known, the purple marker).
   Every one of their people dead in the fight does the same.
10. **Leave mid-fight.** Go live (step 8), then run more than 140 m away: the horde despawns, the log rolls the hold
    ("N of M killed, K of theirs alive: P% to hold"). Held: "<enclave> held out without you.", the quest completes, no
    standing. Not held: as step 9.
11. **A reload, mid-countdown and mid-fight.** Call one, wait a minute, save and quit, load: the countdown carries on
    from where it was (not reset to 15:00). Go live, kill a few, save and quit, load: about 15 s later all of their
    people still alive are out at once again ("... of their people back out for the fight"), then the zombies still
    standing come back ("siege at <enclave> resumes after a load"), the "N left" count unchanged.
12. **Pause stops the clock.** With a siege counting down, open the pause menu for a minute: the clock has not moved.
    (A sleep through it uses it up.)
13. **Never in the blood-moon window or during a base attack.** On blood-moon day after 10:00, `sc enclave siege <id>`
    says it waits ("the blood moon..."), and no scheduled call fires (the log: "held back"). Earlier that day, from the
    time the 15 minutes to answer would run past 10:00 (about 04:36 with 60-minute days, so all of 08:00-10:00), it says
    "the blood moon's window opens ..." instead. With a base attack live (`sc attack now` at home), a siege call waits
    ("a base attack is being fought"); and with a siege live, `sc attack status` says a call waits ("an enclave's siege
    is being fought") and an outpost attack in reach does not spawn. Called, then reaching an enclave that lies within
    150 m of your home zone while a base attack is live (or within 140 m of an outpost whose attack is being fought;
    skip this when none lies that close): the log says it waits with "its clock stopped", the journal's countdown
    stands still, and the siege comes once that attack is over. An outpost attack you walked away from holds neither a siege's call nor
    its arrival (nor the base attack's call).
14. **The pace.** `sc enclave reveal all`, a powered radio, then in the morning `sc day` a dozen times: each one rolls
    the day's call again on today's clock, and about one in seven leaves "a call is due day N HH:MM" (today, between
    08:00 and 20:00) in `sc enclave requests` - a later miss clears it again ("none scheduled"). Stop at a hit and wait
    for that hour: the call comes, from an enclave within 2 km. In normal play the roll runs once a day, at midnight.
15. **Nobody home but the missing one.** `offer missing`, accept, then `sc enclave kill <id> all`: the log says "nobody
    at home is alive, but one of theirs is missing and may be", and the enclave stands. `fate alive`, find them and
    bring them home: home safe as in step 1, and they lead the enclave now. Another run: after the kill, give the search
    up from the console (`sc enclave request <id> fail`): within a second "<enclave> is gone." (the log: "nobody of
    theirs is alive any more"). Found dead instead: gone as soon as you search the remains.

## Enclave requests, part 1 (E4)

**Deployed 2026-10-17**, E2-E6 together (Fable review and re-check: deployable; DLL md5 `8cb0deca59c8e46a095c04c14e2d2018`). A known, non-hostile enclave's leader asks for a resource, a generator or a gun;
"What do you need?" is their first row. Standing moves with how you answer (Neutral below 30, Friendly 30, Allied 70).
`sc enclave requests` lists the open ones; `sc enclave request <id>` shows one enclave's;
`sc enclave request <id> offer <resource|generator|gun> [food|meds|ammo|mats|fuel] [small|medium|large] [force]` makes
one now; `... accept | complete [points] | fail | lapse | clear` move it. `sc day` moves the request clock one day per
use (the log says so once when the world's day is behind it). Bundles: `giveself scBundleMedsMedium` (and
`...Small`, `...Large`, the old `scBundleMeds`); a generator: `giveself generatorbank`. `sc day` may also bring offers
from other enclaves: `sc enclave requests` tells them apart.

1. The offer: `sc enclave reveal <id>` on a survivor enclave, `sc enclave request <id> offer resource meds medium`. Talk
   to the leader: the first row is "What do you need?"; it shows the offer - their words (by kind and standing), then
   "We need a medium bundle of medicine - packed bundles, any sizes that add up. Once you agree you'll have 4 day(s);
   I can wait for your answer until the end of day N." (N = today + 2). Rows: "Leave it with me.", "No, I can't help
   with that.", "Let me think about it.", done ("I have it right here." only while you carry 2 units of medicine).
2. "Let me think about it.": the talk closes, then the tell "<leader>: "Don't take too long. Our shelves won't wait."
   The offer stands until the end of day N." No standing change (`sc enclave request <id>`).
3. "No, I can't help with that.": "Figured as much. We'll find it somewhere else. (standing -5)"; "Something else." goes
   back to the greeting. The ledger (community window) has "turned down <enclave>'s request (-5 standing)".
   `sc enclave request <id>` shows no open request and "the cooldown runs to day N" (today + 2).
4. Hand it over at once: `offer resource meds medium` again (the console ignores the cooldown), carry 1
   `scBundleMedsMedium`, talk: "I have it right here." - the bundle leaves your bag, "That's more than I expected from a
   stranger. Thank you. (standing +10)", no journal quest. SurvivorOrigins pays its "favour" Leadership XP (60). Ledger:
   "<enclave>: a medium bundle of medicine delivered (+10 standing)".
5. Accept: `offer resource meds medium`, "Leave it with me.": "Thank you. We'll be here. Bring a medium bundle of
   medicine by the end of day M." (M = today + 3). The journal has "Help <enclave>" (objective "Bring a medium bundle of
   medicine to <enclave> by the end of day M"), its marker is on their building (track it: compass and map), and it
   has no Remove button. Talk again: "Still waiting on those supplies. We still need a medium bundle of medicine, by the
   end of day M." with "I can't do it." and done.
6. Deliver it later: carry the bundle, talk: "I have it right here." - the quest completes ("Quest completed: Help
   <enclave>"), the marker goes, +10.
7. A lapse: `offer gun` (don't answer), then `sc day` three times: on the third the tell "<enclave> stopped waiting for
   your answer.", the ledger "never answered <enclave>'s request (standing unchanged)" at Neutral; at Friendly
   (`sc enclave standing <id> 40` first) -3, at Allied (75) -15.
8. Running out of time: `offer gun`, `accept`, then `sc day` four times: on the fourth "You ran out of time on
   <enclave>'s request.", the quest fails ("Quest failed: Help <enclave>"), -8 at Neutral.
9. "I can't do it.": `offer gun`, accept it in the talk, then "I can't do it.": "Then you shouldn't have said yes. Our
   watch stays short. (standing -8)", the quest fails.
10. Mixed bundles: `offer resource meds large` (3 units), carry 1 medium + 2 small medicine bundles: "I have it right
    here." takes the medium and one small (3 units), the other small stays. Again with only 2 medium bundles: both go
    (4 units; the log says "4 meds unit(s) handed over for 3 (the rest a gift)"). With 2 small only: no hand-over row.
    The old one-size `scBundleMeds` counts 1.
11. A generator: `offer generator` (any day), carry a `generatorbank`: "I have it right here." takes it (from the bag
    or the toolbelt), "A generator! You didn't have to do this. Thank you. (standing +15)". The ledger names the item.
12. A gun: `offer gun`, carry 5+ guns (say a pipe pistol, a T1 pistol, a hunting rifle, an AK-47 and a T3 sniper
    rifle, mixed qualities), one of them on the toolbelt. "Take one of these." lists 4: your 2 least and 2 most valuable,
    each "<name> (T<tier>, quality <q>): <worth>" (a quality-3 T1 pistol "they'll be grateful", +6; a quality-5 T3 rifle
    "they'll remember this", +19), then "None of these." (back to the offer) and done. Pick one: it leaves exactly that
    slot (another gun of the same kind stays), "(standing +N)". With 4 or fewer guns, all of them show; bows,
    crossbows and admin guns never do.
13. A gun with mods: put a scope on a pistol and pick it: the pistol goes, mods and all (the log names the slot). A
    second pistol of the same kind without mods stays where it was.
14. RE Inventory's bag: with the grid bag, pick a gun lying in the grid: it leaves the grid (the space is free when you
    reopen the bag, nothing left drawn there); the other items keep their places.
15. Crossing into Friendly: `sc enclave standing <id> 25`, complete a resource request (+10, 35): the tell "<enclave> is
    now Friendly.", the marker turns green, the next talk opens with the Friendly greeting, and E3's trade pays 20 % more
    when you sell. Into Allied: standing 65 and a generator (+15, 80): "is now Allied.", the marker blue, the Allied
    greeting, Allied prices.
16. An ally let down: at 75, `offer resource`, accept, "I can't do it.": -15, 60 - "<enclave> is now Friendly.", the
    marker green.
17. The radio: with a powered radio at home, `offer resource` at another known enclave: "The radio crackles: <enclave>
    is asking for help. Talk to their leader." and the ledger "<enclave> is asking for help". Radio unpowered: no tell,
    no ledger line (the leader asks in person).
18. Hostility closes it: accept a request at an enclave, then `sc enclave hostile <id>`: the quest fails, the ledger
    has "<enclave>'s request is off", the standing is unchanged (compare `sc enclave info <id>` before and after).
19. Across a reload: accept a request, save and quit, load: the quest is still in the journal with its marker, and
    `sc enclave requests` shows it accepted with the same due day; the log has "enclave requests: the journal has 1
    request quest(s) of open requests". Hand it over: it completes.
20. A quest whose request is gone: accept one, quit, delete its `[encrequest]` section from the save's
    `survivor_community.txt`, load: the quest is closed as failed at the first second, nothing else changes.
21. The daily pacing: `sc enclave reveal all`, then `sc day` seven times, `sc enclave requests` after each: at most 3
    open at once, at most one per enclave, none from a hostile or raider enclave, no generator before day 10, and an
    enclave whose request ended on a day asks nothing on the next (`sc enclave request <id>`: "the cooldown runs to day
    N"). Offers nobody answers lapse on the third `sc day` after they came.

## Enclave trade, enlist and recruit (E3)

**Deployed 2026-10-17**, E2-E6 together (Fable review and re-check: deployable; DLL md5 `8cb0deca59c8e46a095c04c14e2d2018`). A survivor enclave's leader trades through vanilla's trade window with their
own stock and prices by standing; a member can come with you for 2 days for dukes; a member can join your community
(their favour first at Neutral). `sc enclave trade <id>` shows the trader, the stock and the prices;
`sc enclave people <id>` shows who is with you, their favours and the recruit cooldown; `sc enclave release <id>` sends
an enlisted person home; `sc enclave standing <id> <points>` sets the standing (Neutral below 30, Friendly 30, Allied 70).

1. Trade at Neutral: talk to a survivor enclave's leader at standing 0. The first row is "Let's see what you have to
   trade."; it opens vanilla's trade window with stock of the group's kind (`sc enclave trade <id>`: trader 200-205 and
   the kind; a farm's growers sell food and seeds, a clinic's medics medicine ...). Trade on the leader's radial (hold E)
   opens it too. Members have no Trade. Note a price you know from a vanilla trader (same item, same quality): equal,
   buying and selling.
2. Your barter perk: with a rank of the barter perk, their prices move as a vanilla trader's do.
3. Prices by standing: `sc enclave standing <id> 40` (Friendly) - selling to them pays 20 % more, buying is unchanged;
   `75` (Allied) - buying costs 20 % less, selling pays 35 % more. The price shown is the price paid (check the dukes).
4. Their stock is theirs: buy something, then leave past 200 m (they despawn) and come back - it is still gone. Save and
   quit, load: still gone. `sc enclave kill <id>` (the leader): the new leader trades the same stock.
5. Restock: `sc enclave trade <id>` shows the last restock; 3 days later (`settime` forward) the next trade has new
   stock.
6. No trade with a hostile enclave: `sc enclave hostile <id>` at another one - no Trade on the radial, no dialog. A raider
   enclave has neither.
7. Turn-ins stay with vanilla's traders: with a challenge reward waiting at a trader, talk to an enclave leader - their
   dialog opens, not the turn-in window, and the camera works.
8. Enlist at Neutral: carry 500+ dukes, ask a member "Can you come with me?" - "I could come along ... 500 dukes, and I'm
   with you for 2 day(s)." - "Deal.": 500 dukes leave your bag, "<name> comes with you for 2 day(s).", they follow you
   and fight. `sc enclave people <id>` shows "enlisted ... with <you> until day N hh:mm". Over the next days Voice
   Control never asks you for money.
9. The fee: without enough dukes the row is greyed "Needs 500 dukes". At Friendly (`standing 40`) it is 250, at Allied
   (`standing 75`) "No charge".
10. The limits: with one of them enlisted, another member of that enclave shows "One of them is already with you"
    (greyed). With your followers at the limit (2 by default: community followers plus enlisted people) the row is
    greyed "You have all the followers you can lead", and a member's Follow from the roster is refused while an enlisted
    person fills the second place.
11. "Go home now." to the enlisted person: "<name> heads back to <enclave>.", near their building they walk to their
    spot; far from it they walk off towards it and are gone once out of sight, at their spot on your next visit. Voice
    Control's spoken "dismiss" does the same ("I'll head back to my people."), and so does `sc enclave release <id>`.
    (SCore's companion window never lists them.)
12. Expiry: with one enlisted, `settime` forward 2 days: "<name>'s time with you is up: they head back to <enclave>."
13. Their enclave turns hostile while one is with you (`sc enclave hostile <id>`): they leave your side first ("leaves
    your side to go back"), then the enclave is hostile.
14. A death while enlisted (let a zombie have them, or `sc enclave kill <id> p<pid>`): "<name> of <enclave> is dead.",
    the ledger has "died at your side" and the standing -5.
15. Their body goes with them enlisted (teleport far away, or save and quit): the log says they went back; on the next
    visit they are at their spot (`sc enclave people`: home), and no leftover companion shows on your HUD.
16. Recruit at Neutral: "Would you join us?" to a member - their favour (SC's own: a POI to clear, "Mark it", or
    supplies, "Sure, I'll see what I can find."), with its journal quest. Leave past 200 m and come back: the favour is
    still theirs (`sc enclave people`: "favour: ..."), and for a POI the quest's "Return to <name>" completes when you
    talk to them. Then "Would you join us?" - "Leave my people? ... standing -10." - "Welcome to the community.": they
    join (stationed at home, or following you home), the standing drops 10, the ledger says "<name> left <enclave> to
    join you".
17. Supplies: carrying what they asked for, "Would you join us?" - the favour statement has "Here are the supplies you
    asked for." - they take them and join at once.
18. Recruit at Friendly: no favour, -10 standing. At Allied: no favour, no cost ("... will let me go with their
    blessing.").
19. The gates: the leader has no recruit row; with only 2 of them alive the row is greyed "They can't spare anyone
    else"; a second recruit within 7 days is greyed "One of them left recently: N more day(s)"; before the group is
    fully made, "Their group is still settling in".
20. The recruit is the same person: note the name, face, weapons, skills and traits (`sc enclave people <id>`, their
    snapshot) before; after joining, the community window shows the same.
21. Recruit someone who is enlisted with you ("Would you join us?" on their own statement): they join, the enclave's
    count drops by one, and the follower place is theirs as a member.
22. A favour across a reload: start an enclave person's favour quest, save and quit, load: the quest is still theirs
    (its marker on them or their building; "Return to <name>" completes when you talk to them).

## Enclave people (E2)

**Deployed 2026-10-17**, E2-E6 together (Fable review and re-check: deployable; DLL md5 `8cb0deca59c8e46a095c04c14e2d2018`). Every enclave gets a leader and 2-5 more people who live in its building: made
the first time you come near, kept as records, spawned while you are within 150 m (once the building's chunks are
built: at the default view distance about 90-100 m out) and gone (health and rounds kept)
when you are 200 m away. They greet you by standing, warn you once if you hit them, turn hostile on a second hit, a
kill or a threat, and their containers are theirs. `sc enclave list` and `sc enclave info <id>` show them;
`sc enclave people <id>` lists each person.

1. A group appears as you approach: `sc enclave list` (find a known survivor enclave; `sc enclave reveal <id>` shows
   one), walk towards it. Within 150 m, once its chunks are built (about 90-100 m out at the default view distance), the
   log has `enclave N 'name': the leader ... joins the group at ...`, then one
   more line a second, `... - K of K, the group is made` on the last (3-5 people; 3-6 for raiders). The leader comes
   first. `sc enclave people <id>` lists them, each with an entity id; `sc enclave info <id>` says "N living, led by ...,
   N in the world" and the kind.
2. Watch them for a day: the leader stands at one spot (`sc enclave people <id>` shows it) all day; the others walk
   about the building and its yard and stand a while between walks; at night they idle near their spots. A zombie that
   comes into the building is fought. None wanders off the building (nor follows a zombie more than a few metres out).
3. Talk to the leader at each standing: at 0 the greeting is wary (Neutral). Ask "Tell me about this place." - the
   reply names the enclave, how many they are, the building and the leader; "Something else." goes back. Then
   `sc enclave standing <id> 40` (Friendly: a warm greeting, the marker turns green) and `75` (Allied: a welcoming one,
   blue). The leader's rows are "Tell me about this place.", "Hand over your supplies." and "Nevermind" (with E3, "Let's
   see what you have to trade." first) - no Hire, no order menus, no recruit or favour rows.
4. Talk to a member: a greeting by standing, "Where's your leader?" names the leader and says where they are from you
   ("12 m north-east"). Saying "hire", "pick me up" or "dismiss" to them by voice is refused.
5. Leave past 200 m (the log: `... its people went`) and come back: the same people come back (`sc enclave people`:
   same names; same faces and weapons), no duplicates, with the health they had when they went.
6. Save and quit next to them, then load: the same people come back from their records (the log has no
   `removed a stale enclave body` line; if it does, it removed the copy a save kept - say so).
7. Their containers: open a loot container in their building - refused, "That belongs to <name>." A container you
   place there opens as usual.
8. One hit on a member (a punch): "<name>: Watch it!" and the ledger has the standing change (-10, `sc enclave info`).
   A shotgun blast is one hit. Hit them again within 2 minutes: "<enclave> will shoot you on sight from now on.", the
   marker turns red, they attack you; your residents, followers and turrets fight them, and they them; you can't talk to
   them any more. (`sc enclave hostile <id>` does the same without the hits.)
9. A threat at another survivor enclave: "Hand over your supplies." - "You really want to go down that road?" -
   "Forget it." goes back to the greeting; ask again, "I'm not asking." - 2-4 medium bundles land in your bag (mostly
   food at a farm, medical at a clinic ...); about 10 seconds after the talk they turn hostile.
10. A raider enclave: they attack on sight and have nothing to say. Pull one away by running off: once you break
    contact they follow only a few metres past the building and go back (one still within melee reach keeps swinging
    first, as residents do). Wipe them out: "You wiped out <name>.", the marker is gone,
    `sc enclave list` no longer lists them, and their containers open.
11. A death and the new leader: `sc enclave kill <id>` (the leader). The log names the new leader (the highest tier,
    then the one made first), "... leads ... now" (tell and ledger), and the new leader walks to the old leader's spot.
    `sc enclave kill <id> all`: "<name> is gone.", the marker comes off, the building is free (a land claim there is
    allowed, its containers open). (While one of theirs is missing on an E5 search and may be alive, the enclave
    stands: a second `all` ends it.)
12. Wild spawns: spend a while (a day and a night) within 100 m of an enclave. No wild survivor or raider spawns in or
    near it (zombies and animals do); `sc enclave list`'s header counts the spawns kept off.
13. Set `Enclaves` to false in `Config/blocks.xml`, restart and load next to an enclave: no people appear and none of
    the above applies; `sc enclave people <id>` still lists their records. Set it back: they come back.

## Infestations (I1)

**Deployed 2026-10-16** (Fable review: deployable after fixes, all fixed; re-check: deployable; DLL md5
`3807a366c37df7fa90a30d16d8acfbac`). Buildings get infested: about twice the zombies, more ferals and one special
(radiated below gamestage 50, then charged or infernal) until you clear them. A daily roll starts them, old ones spread,
a powered radio reports new ones near home, and a purple marker shows the ones you know. `sc infest list` shows them
all; `sc infest info <id>` shows one, with its rooms.

1. Stand in (or right next to) a POI you haven't fought through yet and `sc infest add` (or `sc infest add <x> <z>`
   for one nearby). The log has `infestation #1 at ... [console]: ...`, the ledger (`sc status`) has "found an
   infestation at ...", and a purple marker labelled "Infested: ..." is on the map (and on the compass within 300 m).
   `sc poi` inside it adds an "infestation #1" line.
2. Walk through it. Each room has about twice its usual zombies (1.5 times in a tier 4-5 building); the log's
   `SleeperVolume ...: Spawning` lines show the classes. At a low gamestage about a third of them are ferals (glowing
   eyes). One room brings a radiated zombie below gamestage 50: the log says `the room at ... brings the special
   (GroupAllRadiated, gamestage N, 1 in M)`, then `its special came - zombie...Radiated`. `sc infest info 1` shows the
   special's state.
3. Clear every room. You get "You've cleared the infestation at ...", the ledger has "cleared the infestation at
   ...", the marker is gone and `sc infest list` no longer lists it. The log says `... ended (cleared) after N day(s)`.
4. A cleared POI infested again: clear a POI (the one from step 3 will do), go at least 100 m away and
   `sc infest add <x> <z>` it. The log says `N cleared room(s) due again`. Walk back in: its rooms fill up again as
   you come near them (at spawn points out of your sight). Then try it standing inside a cleared POI (`sc infest add`
   there): the room you're in stays quiet while you're within 8 m of it; the others come due after about 2.5 minutes
   of game time and fill up when you come within 8 m of them (out of your sight). Leave your room for 2.5 minutes,
   come back, and it fills up too. The infestation can't end before each of those rooms has filled up and been
   cleared. Best in a POI
   with a trap room (one a door sets off; `sc infest info <id>` counts "N trigger"): leave that door open, and the
   infestation still ends once the other rooms are cleared again.
5. Save and quit in the middle of a fight in an infested building, then load. The log says `infestations: 1 loaded, 0
   dropped ...`, the infestation is still listed and its marker comes back, and the zombies you left (ferals and the
   special too) are back, asleep, when you return.
6. `sc day` a few times, and `sc infest roll` a few times: new ones start (`... [roll]` in the log), at most the cap
   (`sc infest list` says "at most N on this world": 8 on a 6k map, 13 on 8k). At the cap, `sc infest roll` says
   "at the cap". None is in the first week above tier 2.
7. Spread: `sc infest spread <id>`. The nearest eligible POI within 300 m gets infested (`[spread from #id]` in the
   log). When you knew the first one, you get "The infestation at ... has spread to ...", and its marker. (On its own
   this needs 3 days, then a 30% roll a day.)
8. The radio: with a powered radio at home (or any powered radio, without a home), `sc infest roll` until one starts
   within 2 km: "The radio picks up a warning: ... is infested, 900 m north-east.", a ledger line and its marker. One
   farther away stays unknown until you come within 150 m of it: "Infested: .... More of them, ferals, and something
   worse."
9. Never in an enclave's building, your home zone, a claimed building or near a trader: `sc infest add` inside an
   enclave's building, your home, or a building with your land claim or bedroll is refused, with the reason. A
   building within 150 m of a trader is never rolled or spread to (the trader's own POI included).
10. `sc day` gives no warning any more: a second after it, the log says `sc day: the simulated day N is over - back to
    the world's day M` instead of `WRN ... the world day went backwards`.
11. Set `Infestations` to false in `Config/blocks.xml`, restart and load. The log says `infestations: off
    (Infestations=false) - N kept ...`, no infestation marker shows and rooms spawn as usual. Set it back to true and
    load again: the same infestations are back.

## Enclaves on the map (E1)

**Deployed 2026-10-16** (Fable review: deployable, its 12 findings fixed; re-check: deployable; DLL md5
`01e63cc8ae5c143393a56e3aaaacd642`). **Your test (2026-10-16), on a new world and an existing save:** seeding,
discovery, no zombies, claims and outposts refused, reload and the names were all fine. The survivors you saw at
enclaves were SC's own wild spawns; keeping those off enclave grounds is part of E2. The steps you haven't checked stay
open. Groups of survivors (enclaves) are seeded into POIs, named, found and marked on the map, and their buildings are
kept free of zombies, quests, claims and outposts. There are no people in them yet. `sc enclave list` shows them all;
`sc enclave info <id>` shows one.

1. Start a new game. The log has a line like `enclaves: seeded 11 (3 hostile) for a new save on a 6144 m world, D 1019
   m, nearest 714 m from you`. `sc enclave list` shows about 0.3 per km²: 4 on a 2k map, 11 on 6k, 20 on 8k. Up to
   about a quarter are hostile (fewer on a small map), and none is found yet.
2. Up to two of them are not hostile and 400-1500 m from where you spawned (the distances in `sc enclave list`, and the
   log line's "nearest"). No hostile one is within 1200 m of you (600 m on a map under 4 km).
3. Before you walk off, `sc enclave sim` lists the same enclaves, with the same names, as `sc enclave list`.
4. Walk to one (`sc enclave info <id>` says how far and which way). At about 150 m from its building you get "You've
   found ..., holed up at ... (Neutral)", a grey fortress with its name appears on the map, and the ledger has "found
   ... at ...". A hostile one says (Hostile), with a red skull that also shows on the compass within 300 m.
5. Walk through every room of its building, doors and trigger rooms too. No zombie spawns. `sc enclave info <id>` then
   says all the generated sleeper areas are cleared and never respawn. `sc poi` inside it adds an "enclave" line.
6. Try to place a land claim inside it, or close enough that the claim's square reaches it. It is refused, and the
   tooltip names the enclave. An Outpost Flag is refused the same way.
7. `sc enclave reveal all`: every enclave shows on the map, grey fortresses and red skulls, each labelled "Name
   (Neutral)" or "Name (Hostile)". `sc enclave hide all` takes them all off again.
8. Save, quit and load. The log says `enclaves: 11 loaded, 0 moved, 0 dropped, 0 added`. The same enclaves are listed,
   and the markers of the ones you found come back.
9. Load a save from before this build. The log says `enclaves: seeded ... late into this save ...`. None is within 150
   m of any land claim, none is in the area around you, and none is on a building a radio scout marked. Up to two
   non-hostile ones are 400-1500 m from home.
10. The radio needs an enclave you haven't found (`sc enclave hide all` first if you revealed them) and a powered radio
    at home, or any powered radio when you have no home. Then, with that enclave within 2 km of home (of you, without
    one), `sc day`: "The radio picks up voices: ..., holed up at ..., 900 m north-east." Its marker appears. Another
    `sc day` reveals nothing: the next one is 3 days later (`sc enclave list` shows the day). With no unfound enclave
    within 2 km, it tries again the next day.
11. Raise `EnclavesPerKm2` in `Config/blocks.xml` (0.5, say), restart and load. The log says `... added N ... to reach
    the raised target ...`. Lower it again: none is removed.
12. Trader quests never send you to an enclave's building, and radio contacts, favours, supply runs and radio scouting
    never pick one.
13. `sc enclave names 20` and `sc enclave names 10 hostile` print sample names. Tell me which ones you don't like.
14. `sc enclave reseed confirm` drops every enclave and seeds again with the late rules (testing only).
15. Set `Enclaves` to false in `Config/blocks.xml`, restart and load. The log says `enclaves: off (Enclaves=false) - N
    kept ...`, with how many sleeper areas respawn after the loot respawn wait, and no enclave marker shows. Set it back
    to true and load again: the same enclaves are back, and their rooms are kept clear again.

## The NPC's line above the responses, and voiced lines (round vo, the voice test)

**Deployed 2026-10-16** (Fable review and re-check: deployable; DLL md5 `b7e562ac3ab28cabcf9cd0828d82b2fc`). **Your first
test (2026-10-16): you heard the voice and saw the line, with no issues spotted;** the log showed Voice Control's lip sync
bound to the jaw. The steps you haven't checked stay open. Your answers (2026-10-16): survivors' lines never showed;
they should sit above the dialogue choices as subtitles, and the goal is voiced dialogue made with ElevenLabs. The seven
lines are voiced by Vane (men) and Jessica (women), your picks from ElevenLabs' library, one take each.

1. Talk to any survivor. Their line ("Hello friend, how can I help you?") shows in a panel above the responses, lined
   up with them, and a voice of their sex says it.
2. Their mouth moves while the clip plays (Voice Control's lip sync). If it doesn't, or the jaw opens the wrong way, say
   so: Voice Control's jaw axis and angle for these bodies are guesses in its `modconfig.xml`.
3. "I could use someone like you…": the favour line shows and is spoken. Pick a response while they are still talking:
   the next line replaces it, and the old clip stops.
4. Close the talk mid-line: the voice stops.
5. Reopen the talk within 30 seconds: the greeting shows but isn't spoken again. After 30 seconds it is. Going back to
   the first menu inside a talk doesn't repeat it either.
6. A member: the supply run asks "What should I look for? I'll be back tomorrow." aloud; "leave the community" asks
   "You're sure? If I go, I won't be back." aloud (then pick Nevermind).
7. A trader shows its line in the panel, with no voice. The nurse keeps SCore's own subtitle at the bottom (she is
   SCore's older NPC type). Raiders still can't be talked to.
8. No hitch when a line starts. The first clip of a session loads from disk.
9. The panel covers nothing: the survivor card stays on the left, and the responses below it.
10. `sc voice` lists the voices (six with 147 lines each since round vo2), and names the player in use (Voice
    Control's, or SC's own). `sc voice play scRunPick` makes the nearest survivor say it, and `sc voice stop` stops it. A line played
    this way counts for the 30-second rule, so test item 5 with lines the console hasn't just played.
11. Voice Control's own speech (its greeting as you walk up, chat replies) keeps its own voice, and a dialog line cuts it
    off when it starts. Tell me whether the two voices clash.
12. How the two voices and the takes sound in game, and any line worth re-recording.

## Radio window restyle (built 2026-10-13)

Tools/generate_radio_ui.py rebuilt windowScRadio on the community window's look (scui_* plates, <button> controls,
icons); XUiC_ScRadio gained dropready / callready / scoutready / barterready. With AshfallUI the plates are premium.
- [ ] The radio opens; every button still works (drop, call, the five scout runs, barter give/get/trade, close).
- [ ] A control that can't act (cooldown, no power) keeps its plate, loses its hover, and its label dims and says why.
- [ ] With a raiders' demand open, the red callout and Pay / Refuse show; without one, nothing there.
- [ ] Contacts, distress and seven outpost lines fit their plates; the message line shows under barter.

## T3 guns, tier skill floors and standout survivors (round t3)

**Deployed 2026-10-15** (Fable review and re-check: deployable; DLL md5 `ba1cdc49cbde95e1fd1f2fcf5e2e43ea`). Your asks (2026-10-15): at gamestage 100 T3 survivors and raiders mostly carried bows - a T3
should carry a firearm 85% of the time; T2/T3 survivors should start with better skills (T2 at least 2 stars in every
skill, T3 at least 3), and any survivor may rarely have one skill of 5+ stars (a little likelier at higher tiers: 4%,
6%, 8%). An ordinary start now stops at 4 stars (the +2 traits such as Sharpshooter used to push a third of T3s to 5+),
so a 5+ skill is always a standout. Existing survivors keep the profile they already have; new spawns roll the new way.

1. `sc loadout sim T3` and `sc loadout sim hostile T3`: the line says `N% of all of them carry a firearm`, N about 85 (of
   the ranged weapons it is about 87%, and about 2.5% are melee-only). `sc loadout sim T2`: as before (about 10% of all,
   12% of the ranged weapons).
2. `sc skills sim`: T2 shows no skill below 2, T3 none below 3 ("skills below the floor 0"), and "with a 5+ skill" near
   4% / 6% / 8% for T1 / T2 / T3.
3. In play at gamestage 100: most new T3 survivors and raiders have a gun; a new T3 stranger's snapshot card shows at
   least 3 stars in every skill (T2: 2).

## Shooting skill aims the gun (round sh)

**Deployed 2026-10-13** (Fable review and two re-checks: deployable; DLL md5 `25ab0e2458034e36a5a177dd3187fede`). Your ask (2026-10-13): Shooting should set how well and how far NPCs shoot - at 1 star they miss
most shots unless the target is within 5 m, +5 m a star. Your choices: everyone (members, strangers, raiders), a gradual
fall-off, and they open fire out to twice that range. So: hit range 5 m x Shooting stars (5 m at 1 star, 35 m at 7);
inside it nearly every shot lands, about one in five at twice the range, almost none at three times. They start
shooting at twice that (at least 12 m - the
framework backs a shooter off to 10 m - and never past the gun's own range). Shots now go at the chest, not the head.

1. `sc xnpc <id>` on a survivor with a gun: its `uai:` line ends `(shooting N star(s) (member/profile/raider...): hits
   within X m, engages within Y m, aim error Z deg (growing past X m), at the chest)`. A stranger shows `profile`, a
   raider `raider`.
2. A 1-2 star member: zombies at 15-20 m are mostly missed (or not shot at all past the engage range); at 5 m nearly every
   shot hits. Watch the bullet impacts on walls behind the zombie.
3. A high-star member (5+) still hits reliably at 20-30 m.
4. Raiders: a T1 raider walks in much closer before it fires than before, and misses you at range.
5. Return fire: a 1-2 star member being shot at by a raider 30-40 m away shoots back (and mostly misses) once the
   raider has hit them, or sooner if it is within about 30 m (the AI only notices a still-missing raider that close), and
   stops once the raider has not hurt them for about 25 s and is no longer aiming at them. `sc xnpc <id>` shows `(returning
   fire)` on the `uai:` line meanwhile.
6. Bows and crossbows miss the same way (the arrow wanders).
7. `sc xnpc` (no id): the `shooting skill:` line counts shots aimed, launcher shots, engagements refused and fire
   returned - send it if something looks off.
8. Fights take longer now, and survivors take more damage before a zombie drops; tell me if a 1-star survivor is useless
   or a 7-star one still feels like an aimbot (the numbers are all in blocks.xml).

## The first painted survivor no longer hitches (round vb)

**Deployed 2026-10-13** (Fable review and re-check: deployable; DLL md5 `a55c736f10cab14fd8ebd96b391bc83b`; zSpawnProbe
DLL `7ed75646b936598c96d9de3e5ad0b2c7`). From the probe's build timing (2026-10-13): Kyle's first build took 479 ms, and 392 ms of it was SC
looking Vanity Vault up by name (`AccessTools.TypeByName`, which scans every type in the game, six times) the first time a
survivor with a paint or tattoo was built. Now SC finds those types directly and binds Vanity Vault during the loading
screen; Vanity Vault's own one-time lookup for its custom hairs is filled ahead too. The broad look pre-load (heads,
outfits) was measured and dropped: those parts cost 5-45 ms each on first use, and loading them all would take well over
1 GB.

1. In the game log, `Vanity Vault 3.2.0: paints and tattoos on survivors - it knows ...` now comes during loading, right
   after `NPC magazines: ...`, not when you first meet a painted survivor, and it contains `(bound in N ms)`: N should be
   a few ms at most (the old way took about 390).
2. The first painted or tattooed survivor of a session: no big hitch. In `zSpawnProbe\spawnprobe.log` their `build ...`
   line should total under about 150 ms (Kyle's was 479 ms).
3. Vanity Vault's hairs: `spawnprobe.log` has no `typename DataLoader ...` line (that was Vanity Vault's own one-time
   lookup, now filled ahead; with `DebugLogging` true in SC's blocks.xml, the game log says `its hair colour lookup filled ahead`). The first
   `VanityVault.TryLoadHair` of a session may still take a while - it loads the hair itself - but tell me what it shows
   (Hal's was 152 ms).
4. Paints, tattoos and Vanity Vault hairs still show on survivors as before.
5. `spawnprobe.log` now has `typename <name> N ms <- callers` for every `AccessTools.TypeByName` call over 5 ms: there
   should be none from SurvivorCommunity once you are in the world. Send me any others you see in play.

## NPC weapon models loaded ahead (round wp)

**Deployed 2026-10-12** (Fable review: deployable; DLL md5 `880ca43913a467431e460e734e4917df`). Your report (2026-10-12): a big hitch when Carl, a stranger, spawned. Of his 0.9 s, about 0.3 s was
his Izayo Tec9's model loaded for the first time in that frame. Now the models of the weapons NPCs can carry at your
gamestage load in the background, one at a time, from 20 s after the world is up.

1. About 20 s after loading in, the log says `weapon warm: loading N NPC weapon model(s) in the background (gamestage
   tier T, ...)`, and a little later `weapon warm: N model(s) loaded in X s, memory (RSS) +M MB` (the first deploy printed a broken +0 MB). Send me those numbers
   (M is the memory cost; if it is too much, `XnpcWarmWeapons` false in blocks.xml turns it off).
2. No hitch while it runs (it loads in the background).
3. The next fresh stranger with a gun: smaller hitch. `zSpawnProbe\spawnprobe.log` has `force id=...` lines naming what
   forced the spawn and `done in N ms` for how long it took - send me those too.
4. When your gamestage passes 50 or 100, a new `weapon warm: loading ...` line follows within two minutes.

## Residents walk about the base; the home zone stays loaded (round hz)

**Deployed 2026-10-12** (Fable review, re-check and final check: deployable; DLL md5 `dafab068456d22bf6015457ff05bb700`). Your asks (2026-10-12): residents should walk about inside the base rather than stand at one spot,
and the hitch from survivors rebuilt at the edge of your view should go. By day each resident walks to a random spot
anywhere in the home zone, stands 10-20 s, and walks on; at night or with an enemy within 30 m they go back to their
spot. While you are within 250 m of the base its whole zone stays loaded.

1. By day at home: every resident keeps walking between spots all over the base, with short stops; none stands at one
   spot for minutes, and none walks out of the base.
2. Nobody is teleported back to their spot while walking (the log has no "moved back to the post ... m from the post"
   line for them), and nobody gets stuck pacing at a wall.
3. At dusk they walk back to their spots and stand quiet; at dawn they set off again.
4. A zombie in the base: residents within 22 m of it go after it, seen or not; the ones farther off head back to their
   spots. `sc xnpc <member>` names the verdict (`beyond DefendBaseRadius of the resident` for one too far away).
5. The log shows `home hold: keeping the home zone loaded around ...` as you come within 250 m, and `home hold:
   released the home zone (no player within 300 m)` once you go past 300 m.
6. The hitch test of the section below again: no LIPSYNC line, no `force spawning pending entity` for a survivor, while you
   walk back and forth near the base (their bodies stay built).
7. `ResidentRoamZone` false in blocks.xml brings back the old short roam round each spot.

## No hitch when a survivor's chunk loads (pending bodies)

**Deployed 2026-10-12** (Fable review: deployable; DLL md5 `4b07781e65b36bb69026acb1a1330569`). Your report (2026-10-12): a frame hitch with the LIPSYNC line. Cause: when a chunk loads, the game
builds its survivors over the next frames, but SC looked every member up each second, and a lookup forces the build
there and then (`[EntityAsync] force spawning pending entity`, the player prefab loaded again synchronously). Hal and Ty
stand on the edge of view distance 7 from where you were, so their chunk reloaded 8 times in 4 minutes. SC now leaves a
body that is still being built alone and links it a second later.

1. Stand where a survivor's spot is about 7 chunks away (Hal and Ty from about z -600 to -617) and walk back and forth
   across the chunk line. Expect the hitch smaller, not gone: the first body of each class no longer reloads the player
   prefab synchronously, but every rebuild still takes about 0.16 s on the main thread (the game finishes a body in one
   frame either way). Only keeping the chunk loaded removes it.
2. In the log, `[EntityAsync] force spawning pending entity npcSdcsSurvivor...` no longer repeats about once a second
   for each member while their chunk loads (one doubled line at the first load is another mod's). If it still does,
   send the log.
3. `sc xnpc` shows `pending-body-skips` above 0 after step 1.
4. Members still settle at home after a load: the window shows them, none is marked Missing, and no survivor is doubled.
5. An outpost with staff: walk up to it, and its stand-ins appear once each (not twice).

## Talking to a survivor with a reward waiting (X14)

**Deployed 2026-10-11** (Fable review: deployable; DLL md5 `32ca5289936deed49642c9d02cda4dd0`). Your report (2026-10-11): with a challenge reward waiting at the trader, Talk on any survivor
opened the reward claim window instead, and the camera stayed zoomed with movement broken. Cause: SCore's survivors take
vanilla's trader path, which shows any "claim at a trader" quest first, and the window crashed on a survivor (it has no
trader). Now a survivor without a trader never offers a turn-in.

Tested 2026-10-12: with a challenge reward waiting, Talk on survivors opens the dialog, and the reward is claimed at the
trader. Still open:

1. Talk to the trader again with nothing to claim: the trader dialog and Trade work as before.
2. A recruit favour (talk to the survivor after the job) still finishes at that survivor.
3. `sc xnpc` shows `x14-no-trader-turnin-hidden` above 0 after talking to a survivor with a reward waiting.

## Water, luxuries, bundle sizes, upgradable wells and dew collectors, the Activities tab

**Deployed 2026-10-10** (Fable review + re-check: deployable; DLL md5 `73c0337919ef0a707c56713a17acef74`, Config with it). Your asks (2026-10-10): small / medium / large bundles (1 / 2 / 3) in loot; a Water resource
(drinks that hydrate, not alcohol; water outposts; the Oakraven wells inside the community give water by level);
a Luxuries resource (alcohol) spent on a new Activities tab ("Prepare a round of drinks"); and upgrading the wells
and dew collectors with materials by holding E. Your answers: dew collectors give community water by level (1..5);
upgrades cost 4 / 8 / 12 / 16 materials; 1 water per member a day, thirst like hunger; loot keeps today's value;
luxury bundles in liquor piles and beer coolers; all four drunk drinks are luxuries; one round of drinks per game
day; water outposts at the Water Works and the water towers. Then (same day): water in supply runs and the radio
barter, and more medical bundles in Pop-N-Pills crates and food / water bundles in Shamway crates. Old saves load as they are: no water or luxuries
yet, old bundles count as one unit each. Handy: `sc give water 10`, `sc give luxury 8`, `sc give mats 40`,
`sc facility`, `sc drinks`, `sc day`.

Steps:
- [ ] **Startup:** `patch verification: all 90 targets patched` (83 without Voice Control; seven more than before). The
  `config:` line has `WaterPerMemberDay=1 luxuryItems=4 wells=5x2 dew=1x1 upgrades=4/8/12/16 (wells place L1)`
  and `drinks=4lux+1food +15 for 15min`. At the first scan: `facilities: 5 well level(s) (of 5 listed), 1 dew
  collector block(s) recognised`. Once in the world: `facilities: the well kit OakravenWaterFillerVariantHelper now
  places OakravenWaterFiller1 (Level 1) only`.
- [ ] **Packing strip:** seven Pack and seven Take buttons fit under the grid with readable labels, and the stock line
  below fits.
- [ ] **Packing sizes:** 15 boiled waters in the box, Pack Water: one Large Water Bundle (5 boiled waters a unit),
  message "Packed 3 water into 1 large bundle(s)." 7 food units' worth of cans: 2 large and 1 small. 4 beers, Pack
  Luxuries: one Small Luxury Bundle (1 beer left over). Pack Food leaves drinks and alcohol in the box.
- [ ] **Icons and names:** Small / Medium / Large Food, Water, Medical, Ammo, Materials, Fuel and Luxury bundles each
  show their own pile or barrel (meds and materials: see README), and the descriptions say one / two / three units.
- [ ] **Deposit:** a Large Food, two Small Water and an old Food Bundle together: "4 bundle(s) added to the stockpile:
  +4 food, +2 water." Take Water hands out a Small one.
- [ ] **Thirst:** with members and `sc give water -99`, `sc day`: the line says NOT ENOUGH WATER, morale shows a Water
  -20 part, the members get the Thirsty buff, `sc status` shows `thirsty=True`. `sc give water 20`, `sc day`: gone.
- [ ] **Well kit:** craft it: no model picker; it places a Barrel Well (Level 1), turned the way the ghost showed
  (try all four turns, and against a wall: the well sits where the ghost was).
- [ ] **Well upgrades:** inside the home claim, look at it: "Press E to check Oak's Barrel Well (level 1) - hold E to
  upgrade". A tap shows the Check line (level, 2 water a day, next cost). Hold E: Check and Upgrade. Upgrade with
  under 4 materials: refused with what it needs. `sc give mats 40`, upgrade four times: Stone Well, Lantern Well,
  Brick Well, Standing Pipe; each says its level, water a day (4/6/8/10) and the materials spent; the model swaps
  with no leftover pieces, faces about the same way, and filling a jar still works on each. At level 5 there is no
  Upgrade and Check says top level.
- [ ] **Outside the community:** a well outside the claim: a tap says it gives the stockpile no water; no Upgrade.
- [ ] **Dew collector:** hold E: Search (a tap still opens it), Take when it can be taken, Check, Upgrade. Level 2 for
  4 materials: Check says 2 water a day. Save and reload: still level 2 (`sc facility`). Pick it up and put it back:
  level 1.
- [ ] **Water every day:** `sc facility` lists each well / collector with its level and water (its x y z is the
  parent block, the one `sc facility level x y z n` wants); `sc day` says "the wells
  gave N water" and the stock goes up by that.
- [ ] **Water outposts:** a water tower (tier 0) with a land claim: Establish outpost works, type "water supply",
  about 0.9 water a day (map marker: the water drop). The Water Works classifies as water too (`sc poi` there). The
  Outposts tab's perks show "Cistern - thirst hurts morale less", and while thirsty the Water part is -10 instead of -20.
- [ ] **Community window:** seven resource cells, nothing overlapping; water shows its days left or "+N a day from the
  wells". Tabs MEMBERS / OUTPOSTS / ACTIVITIES fit with their counts.
- [ ] **Activities, short:** under 4 luxuries: the row says "Not enough luxuries or food", the cost is red, the button
  grey; pressing it says what is missing.
- [ ] **A round of drinks:** `sc give luxury 4`, press "Pour a round": the message and tooltip, -4 luxuries -1 food,
  morale +15 ("A round of drinks (14:58 left)" under HELPING), the row says "Running - mm:ss left". Again: "still going".
  Pause (ESC) for a minute: the time does not move. Save and reload mid-round: about the same time left. After 15
  minutes "The round of drinks has worn off." and morale drops back; the row says "Poured today - again tomorrow"
  until the next day.
- [ ] **HUD panel:** a water icon and number between food and meds, nothing clipped at the right; water turns red when
  the next rollover will run short.
- [ ] **Bring-supplies favour:** a survivor who asks for food is not satisfied by water or beer any more (real food only).
- [ ] **Loot (as you play):** food, medical, ammo and liquor piles now and then hold a small / medium / large bundle
  (rare, as before); water bundles only in Shamway crates.
- [ ] **Crates:** Pop-N-Pills crates hold a medical bundle about one time in five (Small 15%, Medium 5%); Shamway
  crates a food bundle as often and a water bundle about one time in six (Small 12%, Medium 4%).
- [ ] **Water runs:** a member's dialog, "I need you to go find supplies.": Water is the second choice (and the menu still fits);
  in the community window the run picker has six buttons (FOOD WATER MEDICAL AMMO MATERIALS FUEL) with readable
  labels. A water run comes back with water; a physical runner heads for a water tower / water works when one is in
  range. A gardener brings more (`sc run <member> water` to skip the wait).
- [ ] **Water barter:** the radio's Give / Get buttons cycle through food, medical, ammo, materials, fuel and water
  (never luxuries); 3 water for 1 food and 3 food for 1 water both go through. `sc barter luxury food` is refused.

## Gear by class tier, melee-only NPCs, handing over pack guns, the panel by the compass

**Deployed 2026-10-09** (Fable review + re-check: deployable; DLL md5 `57ede10fe447de0b9e47143f4c095311`, Config with it). Your asks (2026-10-09): T2 NPCs never with T0 gear; a chance an NPC has only a melee weapon; a
follower given an IZY gun can be told to use it; the community panel between the compass and the quest tracker. No save
format change: NPCs you've already met keep their weapons and clothes (a melee-only record reads "none" for the ranged
weapon; an older build would give them one).

What it does:
- **Gear follows the class tier.** Melee weapons, bows and guns all follow the tier in their name now (before, only the
  guns did, and e.g. the Machete was a "tier 0" weapon): T1 NPCs T0/T1, T2 NPCs T1/T2, T3 NPCs T2/T3/T4. The Primitive
  armor set is T1-only. Clothes quality by tier as before.
- **The gamestage picks the class instead.** Below gamestage 50 a T2 or T3 survivor or raider from a spawn group comes
  in as a T1 (below 100 a T3 comes in as a T2). Before, it kept its class but rolled T1 gear, which is what you saw at
  gamestage 46. `sc spawn T2` makes a real T2 at any gamestage.
- **Melee-only:** 15% of new survivors and raiders carry only a melee weapon (such a raider always charges).
- **Handing over:** the weapon menu gets "Use the weapons I gave you." (one line: that menu shows ten at most) when
  their inventory window holds a weapon they can carry (any XnpcWeapons entry - IZY's guns included; not IZY's duals or
  launchers). It takes the highest tier gun or bow and the highest tier melee weapon there, puts the ranged one in their
  hand, and gives you back the ones they replace.
- **The panel** sits at the top, right of the compass, 394 wide (was 430 at the top left).

Steps:
- [ ] **Startup:** `patch verification: all 83 targets patched` (76 without Voice Control; one more than before). The
  `config:` line has `meleeOnly=0.15/0.15 classByGamestage=True`.
- [ ] **No T0 gear on T2:** `sc loadout sim 5000 T2` (and `hostile`): by name tier, melee/bow/firearm show only T1 and T2
  (bows only T1). `sc loadout sim 5000 T3`: only T2/T3 (and T4 with IZY). `sc gear sim 3000 T2`: no Primitive (it shows
  as light, so check `sc spawn T2` a dozen times: nobody in Primitive armor or with a T0 item).
- [ ] **Gamestage picks the class:** at gamestage under 50, raiders and survivors met in towns at night or downtown are
  T1 classes (`sc loadout near` lists the class); turn on `DebugLogging` to see `spawn: scRaiderMaleT3 comes in as
  scRaiderMaleT1` lines.
- [ ] **Melee-only:** the sims print about 15% melee-only. `sc spawn meleeonly` and `sc spawn hostile meleeonly`: only a
  melee weapon, and the raider charges. Save and reload: still melee-only (no bow appears).
- [ ] **Hand a gun to a melee-only member:** put a gun in their window, Weapon Menu, "Use the weapons I gave you.": they
  hold it and keep it after a reload.
- [ ] **Hand an IZY gun to a follower:** put e.g. an IZY M4 in their inventory window, open the Weapon Menu: "Use the
  weapons I gave you." is the first line and "Nevermind." is still at the bottom; pick it: they hold the M4, their old gun
  comes back to you (storage, inventory or a bag), and the message names both. With a gun and a melee weapon in the
  window both are taken. With nothing usable in the window the line is hidden.
- [ ] **The panel:** inside the home zone it shows at the top, right of the compass, clear of the compass, its day/time
  line and the quest tracker (and the biome name above the tracker). All five numbers and the noise line still fit.

## Mod guns and outfits (IZY Classic, LittleRedSonja's armor)

**Deployed 2026-10-08** (Fable review + re-check: deployable; DLL md5 `5cec517ceb3d310fce1ed2fe176b241b`, Config with it). Your picks (2026-10-08): IZY Classic's guns for survivors and raiders by the tier in their name
(no IZY melee weapons), Locked & Loaded magazines for them, the Hunting Rifle's tags fixed, clothes by tier with 20
whole LittleRedSonja outfits at 35%. No save format change; survivors you've already met keep their weapons and clothes.

What it does:
- **"NPC tier" = the tier its weapons use:** its class (T1/T2/T3) capped by your gamestage (T1 below 50, T2 below 100).
  So at gamestage 30 every NPC rolls as a T1, whatever its class.
- **Guns** (12% of ranged weapons, as before): T1 NPCs get T0 guns 61% and T1 guns 39%; T2 NPCs T1 61%, T2 39%; T3
  NPCs T2 42%, T3 42%, T4 17%. This covers IZY's 51 new guns and vanilla's guns too (the pipe guns are T0, IZY's AK-47
  replacement is T1 and so on, by the name Combat Sandbox gives them). The VH Automatic Crossbow is a T4 bow (about 7%
  of a T3 NPC's bows).
- **Magazines:** guns with detachable magazines start with 2-4 Locked & Loaded magazines; revolvers, bolt-actions,
  tube shotguns, the musket and the crossbow start with loose rounds.
- **Clothes:** 35% wear a whole mod outfit (when one fits), the rest vanilla sets weighted light early, heavy late.
  Armor quality 1-2 (T1), 2-4 (T2), 3-5 (T3).

Steps:
- [ ] **Startup:** the `config:` line has `xnpcWeapons=93` and `outfits=20 (share 0.35/0.35) gearQuality=1-2,2-4,3-5`;
  no `XnpcWeapons: no item ...` or `config Outfits` warnings. `sc weapons` lists the IZY guns with their short names.
- [ ] **Gun odds:** `sc loadout sim 5000 T1 any`, then `T2 any`, `T3 any`. Its "firearms by name tier T0..T5" should be
  about 61/39 split between T0 and T1 (T1), T1 and T2 (T2), and 42/42/17 over T2/T3/T4 (T3), with nothing else. Same with
  `hostile` for raiders.
- [ ] **An IZY gun with magazines:** `sc spawn T3 HK416`: it aims, fires and reloads it, and its inventory window holds
  Locked & Loaded HK416 magazines. A raider too: `sc spawn hostile T3 M249` (watch the aim and reload on the body).
- [ ] **Loose rounds:** `sc spawn T3 Mosin` (or `Enfield`): it starts with loose rounds and reloads them.
- [ ] **Hunting Rifle:** `sc spawn T1 HRifle`: it keeps its distance and shoots instead of walking up to club you. Your
  own Hunting Rifle still works (it now counts as ranged for perks and mods, like vanilla's).
- [ ] **Crossbow:** `sc spawn T3 VHCrossbow`: it shoots bolts.
- [ ] **Outfit odds:** `sc gear sim 3000 T1 any`, `T2 any`, `T3 any` (and with `hostile`, and `male`/`female`): about 35%
  mod outfits, only outfits for that side and sex (no TactiGirls on `male`, no Motorgang on `female`), heavier vanilla
  sets at T3, quality 1-2 / 2-4 / 3-5.
- [ ] **Outfits look right:** on a woman `sc gear <id> HitGirl`, `BoomGirlGreen`, `TactiCool`, `Hitman`, `GeekWhite`; on a
  man `sc gear <id> Motorgang`, `Tank`, `Rick`, `Michonne` (keeps their own hair), `Coyote` (a bearded man: does the
  beard poke through the bandana?). `sc gear <id> HitGirl` on a man is refused. Hitman, TactiCool and the Geek are
  built on a male body: if one looks wrong on a woman, tell me and I'll make it men-only.
- [ ] **Spawned at random:** `sc spawn` and `sc spawn hostile` a dozen times each: a mix of outfits and vanilla sets,
  no one bare-chested, no clipping between an outfit and vanilla pieces.
- [ ] **Kept:** a member in a mod outfit keeps it after save and reload.
- [ ] **Old survivors:** someone you met before this build keeps their weapons and clothes. (A raider carrying
  vanilla's AK-47, Pistol, Hunting Rifle or Double Barrel now counts it as T1: worth +100% kill XP, not +133%.)

## Survivor paints and tattoos (Vanity Vault)

**Deployed 2026-10-07** (Fable review + re-check: deployable; DLL md5 `b651c719c18c229a8d860ed4268ef83d`, Config with it). Your ticks in `SurvivorPaints_Choices.md` (game root): paints for anyone (not
just light skin), the suggested rates. **Two new Harmony targets: 82** (75 without Voice Control). No save format
change for unpainted survivors; a painted one's look gets three more fields (an older build reads the look and drops
the paints).

What it does:
- A new survivor rarely comes with a **body paint set** (1%; raiders 6%): survivors get Clean (women: smoother skin,
  own tone) or Broken (men: bruises, taped fingers, chest tattoo); raiders get Broken, Harley (women) or Skeleton. A body
  paint comes with its matching face paint (Clean needs none).
- Otherwise maybe a **face paint alone** (2%; raiders 10%): survivors Broken or Furiosa; raiders also Tribal,
  Braveheart, Voodoo, Skeleton, Catrina, Clown, Joker and Harley.
- And maybe one **tattoo** (15%; raiders 35%) from the shapes you ticked, in a dark ink (mostly black; also brown,
  deep blue, blue, dark green, grey; raiders add three dark reds). Raiders also get Oni, KISS and the two Harley ones.
- Only new looks get paints: survivors you've already met keep theirs. `sc look <id>` re-rolls a face with paints.
- The paints show in their portrait and on the snapshot card too.
- All of it is in Config/blocks.xml (`SurvivorBodyPaintChance`... `TattooMode`). The log says which Vanity Vault was
  found (at the first new survivor) and lists any configured name it doesn't know.
- **`sc paint <id|name>`** shows a survivor's paints and what the lists offer. To try one without waiting for a roll:
  `sc paint <id> body Sonja_Broken_Full_M`, `face Sonja_Furiosa`, `tattoo Maori 03/Royal Lapis` (shape, then the ink
  after a `/`), `none`, or `roll` (uses the chances, so often nothing). The body is rebuilt with it on the spot.
- Each paint loads its textures the first time it shows (a brief hitch is possible), shared by everyone wearing it.
- A survivor whose body is rebuilt while out of view (a runner coming back, `sc paint` on someone far off) gets the
  paints when they come into view.

Steps:
- [ ] **Startup:** `patch verification: all 82 targets patched` (75 without Voice Control). When the first new survivor
  is rolled (or at your first `sc paint`), a line `Vanity Vault 3.2.0: paints and tattoos on survivors - it knows 15 body paints, 20 face paints, 1374 tattoo variants`
  with no "not found there" list. The `config:` line ends with `paints=body 0.01/0.06 of 2/4, face 0.02/0.1 of 2/10,
  tattoo 0.15/0.35 of 20/24 Blend`.
- [ ] **Try each kind:** on a nearby survivor, `sc paint <id> face Sonja_Furiosa`, then `tattoo Maori 03`, then
  `body Sonja_Broken_Full_M` (a man; its set brings the Broken face paint in place of Furiosa) or
  `body Sonja_Clean_Vanilla_Full_F` (a woman; Clean takes the face paint off): each shows on the body right away, the
  tattoo stays on top of a paint, and the rest of them (hair, eyes, clothes) is unchanged. `sc paint <id> none` takes
  it all off. A paint with no version for their sex is refused.
- [ ] **Skin looks right:** a tattooed survivor's skin (face, hands) looks the same as before apart from the tattoo
  (a tattoo moves their skin to Vanity Vault's shader). A body paint on a dark-skinned survivor won't match their tone
  (your choice: paints for anyone).
- [ ] **Raiders:** `sc spawn raider` a dozen times: some face paints and tattoos, now and then a skeleton or Harley body.
  `sc paint <id>` on one lists the raider choices.
- [ ] **Kept:** paint a member (`sc paint`), save and reload: the paint is back, and their portrait in the community
  window shows it.
- [ ] **Out of view:** paint a survivor, walk 100+ m away, `sc paint <id> tattoo Star 01` (rebuilt while unseen), walk
  back: the paints are on when they come into view (`sc xnpc` counts `paints-hidden` and `paints-late`).
- [ ] **Old survivors:** someone you met before this build has no paints and keeps their look.
- [ ] (Optional) the female Skeleton body uses the male texture: check it lines up on a woman (`sc paint <id> body
  Sonja_Skeletton_Full_F`); if not, take it out of `RaiderBodyPaints`.

## Survivor snapshot: a card while you talk

**Deployed 2026-10-07** (one DLL for this section and the three next to it - snapshot, trust, looks, favours round 2: md5 `3f332219c4cb76cd90887de0320526de`, with Config, Localization and UIAtlases; Fable review + re-check of each: deployable). Your design: while you talk to a survivor, a card on the left of the screen
(clear of their face and of the spoken line at the bottom, its bottom at the XP bar's line) shows their name, role,
portrait, health, stamina, mood, traits and skills - State of Decay 2's card without the standing. No new Harmony
target; new files: `XUiC_ScSnapshot.cs`, `ScProfile.cs`, `Tools/generate_snapshot_ui.py`, four mood faces
(`UIAtlases/UIAtlas/scui_mood_*.png`), the window in `Config/XUi_InGame/windows.xml` and `xui.xml`.

What it shows:
- **Name** in the title bar, **role** under it (Medic, Cook... from their specialty), **portrait** at the top right
  (shot the same way as the community window's; a silhouette until it's ready - a stranger's picture is kept in
  memory only, so it's shot again on each talk).
- **Health** (current / max), **stamina** (from Cardio: 100, +10 per star above one - NPCs have no real stamina, so it
  always shows full), **mood**: a face and a word (Happy, Content, Uneasy, Miserable) in the mood's colour.
- **Traits**: a member's by name; anyone else's as "????" until their favour is done.
- **Skills**: Cardio, Wits, Fighting, Shooting and their specialty, as 7 stars each (unlit stars are the same star,
  dimmed).
- Someone not in your community gets their traits, skills and mood rolled the first time you talk to them and brings
  exactly those when they join - their mood too, instead of starting at the community's morale. Survivors you
  already met before this build get theirs on your next talk. It lives on the body, so it lasts as long as the body
  does: once you've asked about their favour they're kept (unloads, save/reload); one you only chatted with goes with
  the chunk like any wild survivor, and whoever spawns later is someone new.
- A member who leaves takes their record as it is then (trained stars, current mood) and shows their traits by name.
- The card sits clear of XNPCCore's voice-subtitle box too (the dark box a voice line shows in).
- Multiplayer: profiles are rolled by the host, so another player sees a stranger's card once the host has talked to
  them (until then, no card). Members show for everyone.
- "Personal morale" in the community window is called "Mood" now.

Steps:
- [ ] **Card shows:** talk to a survivor: the card is on the left, nothing overlaps their face, the spoken line or a
  voice line's subtitle box, the dialog choices on the right still work. Try at your 1440p, and if you can at 1080p (the card should still fit).
- [ ] **Stranger:** traits read "????", skills and mood filled in; close and talk again - the same values.
- [ ] **Favour done:** a survivor whose favour is done (before they join) shows their trait names.
- [ ] **Join keeps it:** recruit someone whose card you saw: the community window shows the same traits, skills and mood.
- [ ] **Member:** talk to a member: their own traits (named), skills and mood.
- [ ] **Not for others:** a trader or a non-survivor NPC shows no card.
- [ ] **Portrait:** the picture appears within a second or two (silhouette first) - for a stranger and for a member.
- [ ] **Save/reload:** a survivor whose favour you've asked about keeps the same card after a reload.
- [ ] **Dismissed member:** dismiss a member and talk to them: their traits by name, the stars and mood they had.

## Survivor trust: hits land, and a hit makes a survivor hostile for good

**Deployed 2026-10-07** (one DLL for this section and the three next to it - snapshot, trust, looks, favours round 2: md5 `3f332219c4cb76cd90887de0320526de`, with Config, Localization and UIAtlases; Fable review + re-check of each: deployable). Your design: wild survivors are Neutral, Friendly once their favour is done,
and Hostile for good once you hit them - any hit (you chose this over a warning first). **Three new Harmony targets:
80** (73 without Voice Control). No save format change: a grudge is a cvar on the survivor.

Why you did little or no damage: SCore refused every melee swing on a survivor outright (it counts their Neutral
standing toward you as friendly fire and its melee check returns that answer before the hit is resolved). Shots,
arrows and thrown weapons went through a different check SC had already opened, so they landed; what made guns feel
weak is armour (kept at half), XNPCCore's free self-heal for unhired NPCs (+30 HP and bleeding cleared at 70% health,
every 30 s) and SC's bandages.

What changed:
- **Melee lands** on any SCore NPC (survivors, and any NPC pack on SCore's classes) who isn't a trader, one of your people (members, stand-ins) or a hired companion.
- **Hired companions can't be hurt by you any more** (before this, shots got through and melee didn't): one stray
  shot in a shared fight would otherwise turn them hostile for good. They have no trust either.
- **The first hit of yours that lands** (melee, shot, arrow, thrown weapon, your explosion, running them over; not a
  bleed ticking on, a molotov's fire, or your turrets and traps)
  makes that survivor hostile to you for good: "<name> won't forget that - they're hostile to you now." They attack
  you on sight, their talk prompt is gone for you, a favour they offered is off (its quest closes as failed, with the
  usual toast, and leaves the journal), and they can't be recruited. If one ever does join (another player's doing),
  the grudge is dropped. They're kept through chunk unloads and reloads. Your companions and community fight them; your turrets
  shoot them like raiders, and they keep a POI from reading clear.
- Members don't have trust (their mood is the mechanic), raiders are hostile anyway, traders keep their protection.
- A hostile survivor can't be hired by you either - Voice Control's spoken "hire" gets "Not for all the dukes in the
  world." (with "*glares*" in front, unless Voice Control strips it) Anyone hired or recruited anyway (another player's doing) drops the grudge. A hostile prospect who
  was waiting for you at their spot stops guarding it.
- `CommunityFriendlyFire` (Config/blocks.xml, default off) works for members again: with it on you can hurt your own
  people, and they never turn hostile.
- **Changes two favour tests below** ("A survivor dies with their quest open" and round 2's "Supplies, death"):
  killing them yourself now fails their quest at your first hit, even at "Return to <name>". Let zombies do the
  killing to test the death path. A forgiven survivor offers a new favour.
- `sc trust <id|name>` says where a survivor stands with you; `sc trust <id|name> forgive` clears a grudge (testing;
  it reaches other players too). `sc xnpc <id>` now also shows `youCanMelee` and `trust`.

Steps:
- [ ] **Startup:** `patch verification: all 80 targets patched` (73 without Voice Control).
- [ ] **Melee lands:** hit a wild survivor with a melee weapon: damage numbers/blood, their health drops.
- [ ] **Hostile on the first hit:** the message shows, they turn and attack you (gun or melee), the talk prompt is gone,
  `sc trust <id>` says Hostile. If they had a favour quest for you, it shows as failed.
- [ ] **Stays hostile:** walk far away (chunk unload) and back, and save/reload: still hostile, still no prompt.
- [ ] **Others unaffected:** a different survivor nearby is still Neutral and talks to you.
- [ ] **Your people are safe:** melee and shoot a member and a hired companion: no damage, no hostility.
- [ ] **Turrets:** a hostile survivor in your turret's view gets shot (with the turret's zombie or enemy targeting on,
  and `RaiderTurretsAsEnemies` true, the default). A neutral survivor walking past your turret or into your dart trap
  does not turn hostile.
- [ ] **Forgive:** `sc trust <id> forgive` - they stop attacking and the prompt comes back.
- [ ] (Voice Control) **No hire:** say "hire" to a survivor who is hostile to you: the refusal line, no hire window.

## Survivor looks: Vanity Vault filter, rare dyed hair

**Deployed 2026-10-07** (one DLL for this section and the three next to it - snapshot, trust, looks, favours round 2: md5 `3f332219c4cb76cd90887de0320526de`, with Config, Localization and UIAtlases; Fable review + re-check of each: deployable). Your ticks in
`SurvivorLooks_Filter.md` (game root): survivors and raiders no longer roll
Vanity Vault's 9 tinted skins (Obsidian, Ash, Mutant Green, Demon Red, Orchid, Brimstone, Cobalt, Amethyst, Ember) or
any of its 9 eyes; its hair styles and all faces stay. Dyed hair (vanilla colours 11-24: yellow, orange, the reds,
pinks, blues, purples, greens) is rolled 6% of the time for women and 0.5% for men; everyone else gets a natural
colour. Survivors you've already met keep their looks. All of it is in Config/blocks.xml (`SurvivorExcludeSkins`,
`SurvivorExcludeEyes`, `SurvivorExcludeHairs`, `SurvivorDyedHairColors`, `SurvivorDyedHairChanceFemale` / `Male`).

Steps:
- [ ] **Startup:** the `config:` log line ends with `looks=excluded 9 skins/9 eyes/0 hairs, dyed hair F0.06/M0.005 of 14`.
- [ ] **New survivors look natural:** `sc spawn` a dozen or so (or meet new ones): no tinted skin, no Sonja eyes;
  Sonja hair styles still turn up; dyed hair only now and then on a woman, almost never on a man. `sc spawn` ends
  its line with the look (skin, hair and colour, eyes), so you can read them off the console. `sc look <id>`
  re-rolls one to see more.
- [ ] **Raiders too:** `sc spawn raider` a few times: same rules.
- [ ] **Old survivors unchanged:** someone you'd already met with a tinted skin keeps it. (Don't run `sc look` on a
  survivor you want to keep: it replaces their look.)

## Recruit favours round 2: fewer clicks, community first, a supplies quest

**Deployed 2026-10-07** (one DLL for this section and the three next to it - snapshot, trust, looks, favours round 2: md5 `3f332219c4cb76cd90887de0320526de`, with Config, Localization and UIAtlases; Fable review + re-check of each: deployable). From your t4 results. No new Harmony target (still 77); one new quest class
(`quest_scSupplies` in Config/quests.xml) and new localization rows ship with it.

What changed:
- **"Mark it" only leads on when there is something to say.** When the POI doesn't read clear, "Mark it. I'll take
  care of it." marks it and ends the talk, as it did before the first round. Only when it already reads clear does
  the survivor answer, with "Actually, I've already cleared that.".
- **"Reads clear" now matches vanilla's quest.** Your Soyer Family Farm test (log, 16:40): the quest moved on 4
  seconds after "Mark it" - vanilla saw every sleeper area cleared - but SC's own test said no (it also refuses areas
  due to respawn, enemies inside, areas not generated), so the line stayed hidden. The line now also shows whenever
  vanilla's quest would complete at once. The log says, on every "Mark it", whether the POI read clear and if not why
  (`<POI> for <name> is not clear yet: ...`).
- **Nothing before a community.** "I could use someone like you in my community." (and so every favour line) only
  shows once you have a community home (your land claim). Talking to a survivor before that no longer rolls a favour
  or pins them in place.
- **Supplies quest.** The survivor's answer now offers "Sure, I'll see what I can find.": it puts "Supplies for
  <name>" in the journal ("Bring 15 food items to <name>"), with the marker on the survivor, and ends the talk. From
  then on "Here are the supplies you asked for..." is at the start of the talk while you carry them; handing them
  over completes the quest and they join. If you already carry them when they ask, "Here are the supplies..." is in
  their answer straight away (no quest needed). Their death removes the quest; joining closes it. Survivors you asked
  with the previous build get "Sure, I'll see what I can find." like anyone else (the line only checks your journal).
  If the join is ever refused after the hand-over went through, "You've got what you needed. Come join my
  community." is at the start of their talk.
- `sc status` says "taken on" / "not taken on" for each prospect (survivors asked with the previous build say "taken
  on" - it meant "asked" there).
- This replaces four bits of the "Recruit favours" section below: the survivor no longer answers after every "Mark
  it", "reads clear" is no longer exactly `sc poi`'s test, `sc status` no longer says "asked", and supplies survivors
  asked before don't need asking again.

Steps:
- [ ] **No community yet:** in a game where you have no land claim, talk to a survivor: no "I could use someone like
  you..." line, and `sc status` lists no new prospect. Place a land claim, talk again: the line is there.
- [ ] **POI not clear:** "I could use someone like you..." -> "Mark it. I'll take care of it." ends the talk, the quest is
  in the journal. Log: `<POI> for <name> is not clear yet: ...`.
- [ ] **POI already clear (t4.2 again):** ask a survivor (don't mark it), clear their POI, come back, "Mark it": the
  survivor answers, "Actually, I've already cleared that." is there and completes the quest; then "Then come join my
  community." Log: `<POI> for <name> already reads clear`. If the line still doesn't show, send me that log line.
- [ ] **Supplies, taken on:** without the items, "I could use someone like you..." -> "Sure, I'll see what I can find.":
  the talk ends, "Supplies for <name>" is in the journal with the objective, the marker is on the survivor (and
  follows them). Fetch the items, talk: "Here are the supplies..." at the start; "Quest completed: Supplies for
  <name>", then they join. Also try it on a supplies survivor you asked with the previous build.
- [ ] **Supplies, already carried:** with the items in your pack, ask a new supplies survivor: their answer offers
  "Here are the supplies..." and "Sure, I'll see what I can find."; handing over joins them with no quest left.
- [ ] **Supplies, reload:** take one on, save and reload: the quest and its marker are back, and the hand-over still works.
- [ ] **Supplies, death:** a survivor whose supplies quest is open dies: "Quest failed: Supplies for <name>" and the
  quest is gone (`sc recruit` on them does the same).

## Recruit favours: completed only through the favour

**Deployed 2026-10-07** (Fable review + re-check: deployable; DLL md5 `04cb2923022cd269a54d7a0d242b3a82`, Config and Localization with it). Parts of it are replaced by "Recruit favours round 2" above. From your report: a POI cleared with no quest completed a survivor's favour
(the "is clear - go back to ..." message) and let you recruit them, and "Here are the supplies" showed before you had
ever asked about the favour. No new Harmony target (still 77); the registry gains a `started=1` line per prospect (an
older build ignores it).

What changed:
- **A POI favour completes only through its quest.** Clearing the building without the quest does nothing any more,
  before or after the survivor asked. The favour is no longer completed on the spot when the survivor first names a POI
  that already reads clear.
- **"Actually, I've already cleared that."** After "Mark it. I'll take care of it." the survivor answers ("It's on your
  map...") and, when that POI already reads clear, a new line appears: "Actually, I've already cleared that." Choosing it
  completes the favour, closes the quest as completed, and the survivor answers ("You already did? ...") with "Then come
  join my community." / "Not yet. I'll come back for you." Otherwise only "I'm on it." shows. "Reads clear" is the same
  test as `sc poi` (no sleepers left or due to respawn, every sleeper area visited, no enemy inside while it is loaded).
- **Supplies:** "Here are the supplies..." is offered only once the survivor has told you what they want (after "I could
  use someone like you in my community."), in that answer and from then on at the start of every talk while you carry
  the items. Survivors who asked for supplies before this build need asking once more.
- `sc status` marks each prospect "asked" / "not asked yet".
- Favour quests are now found by the survivor who gave them, not by the building: a survivor's death closes their own
  quest ("Quest failed" while clearing, "Quest completed" once it reached "Return to <name>" - that one used to stay in
  the journal), and joining closes one still open.
  On a POI that is already clear the quest jumps to "Return to <name>" right after "Mark it" (vanilla); the "is clear -
  go back to <name>" message waits until you close that survivor's dialog, so it no longer pops up mid-conversation.

Steps:
- [ ] **No completion without the favour (your report):** talk to a new survivor (don't pick the community line) or pick
  it and walk away without "Mark it". Clear their POI. Expect no "is clear - go back to ..." message and no join line at
  the survivor. (A survivor whose favour already completed in an older build stays completed.)
- [ ] **Already cleared:** with a survivor whose POI you've cleared (or one `sc poi` reports cleared while you stand in
  it), pick "I could use someone like you..." then "Mark it. I'll take care of it.". Expect the survivor's answer with
  "Actually, I've already cleared that." and "I'm on it.". Take your time reading (a few seconds): no "is clear - go
  back to ..." message pops up. Pick the first: "Quest completed", then "Then come join my community." joins them, and
  the journal has no "Secure ..." quest left in progress. Log: `favour done: <POI> was already clear (the player said
  so to <name>)`.
- [ ] **Already cleared, "I'm on it." instead:** the same, but pick "I'm on it.": once the dialog closes, the "<POI> is
  clear - go back to <name>" message shows. Talk to them again: "Quest completed: Secure <POI>" shows as the talk opens
  (the quest's "return to <name>" step), and "That place is clear now. Come join my community." is at the start and joins
  them.
- [ ] **A survivor dies with their quest open:** (kill them yourself, or let the dead do it) once while their quest is still
  clearing - "Quest failed: Secure <POI>" - and once at "Return to <name>" - "Quest completed". Either way no "Secure ..."
  quest is left in progress in the journal.
- [ ] (Edge) a POI with no sleeper areas at all reads clear straight away, so "Actually, I've already cleared that."
  shows on the first "Mark it" (the old build marked such favours done the moment the survivor named the POI).
- [ ] **Not cleared yet:** the same with a POI that still has sleepers: only "I'm on it." shows and the quest is in the
  journal as before. Clear it with the quest: the usual "is clear - go back to <name>" message, and talking to them
  completes the quest and offers the join line, as before.
- [ ] **Back later:** take the quest, clear the POI, and if the quest doesn't move on to "return to <name>" (a POI cleared
  before the quest), talk to them, pick "I could use someone like you..." then "Mark it" again: no "You already have that
  quest." message, and "Actually, I've already cleared that." is there.
- [ ] **Supplies:** with the items already in your pack, talk to a new supplies survivor. The start of the talk has no
  "Here are the supplies". Pick "I could use someone like you...": their answer offers "Here are the supplies...". Close
  the talk and open it again: the line is now at the start too.
- [ ] **Save and reload** between "I could use someone like you..." and handing over the supplies: the line is still
  offered after the reload (`sc status` still says "asked").

## Base defence: the Guard Tower kit

**Deployed 2026-10-06** (Fable review + re-check: deployable). DLL md5 `75a6adb5f99419a50c5c72276c9b5469`, deployed **together with** the same tree's Config (the
`scGuardTower` block and recipe, the `GuardTower*` keys, the localization, the Watch post's `CreativeMode`) and the new
`Prefabs` folder (`scGuardTower.tts`, `.blocks.nim`, `.xml`: your `LocalPrefabs\Guard_Tower` with its Watch post turned to
look away from the ladder). Plan: `BaseDefence_Plan.md` (section 6, Q8 and the Guard Tower answers; milestone 6);
research `_research\base_defence\prefab_tower_report.md`. **One new Harmony target: 77** (70 without Voice Control),
`RenderDisplacedCube::Update`, for the ghost (visual only). No save format change: once up, the tower is ordinary blocks,
and its Watch post registers like a hand-placed one.

**Fixes after your first test: deployed 2026-10-07** (Fable reviews: deployable; DLL md5 `ca42a7c5d62cf24091ea1ae69321e180`, still 77 targets; Config,
Localization, the new `Config\materials.xml` and the re-imported prefab go with it).
- **Fix 1 (the tower sat in the ground):** the bottom layer now stands on the ground in the kit's own layer, the prefab's
  YOffset -1 ignored (it put the legs a block into the ground, replacing terrain and denting the ground around them).
  Clearing grass no longer reshapes the ground. Vanilla's placement box now frames the whole tower, and the kit's own
  cube is hidden. The two towers placed with the first build keep their sunk legs and dented ground: break them (or fill
  the dip) and place them again.
- **Fix 2 (a Watch post in any shape, the tower's post matching the deck):** the Watch Post is now crafted as a shape
  block like vanilla's building blocks (hold Reload on it for the shape menu; "Watch Post - <shape>"). The guard stands
  on top of the post, so a shape takes a guard when its top is a full flat face: the cube, or a plate or catwalk turned
  upside down as in a deck. Other shapes still register but say a guard can't stand on them. The tower's post is now a
  catwalk plate in the deck's paint, turned the deck's way. The old cube Watch Post keeps working where it stands and
  from your inventory ("Watch Post (old cube)") but is no longer crafted. **`Config\materials.xml` is new and must ship
  with it:** without it the shapes don't load (the log warns at game start) and the Guard Tower can't be placed.

What it does:
- **Crafting:** "Guard Tower" in the backpack, 782 wood + 4 nails (its blocks' worth: 76 wood shapes x 10, 4 wooden
  ladders x 3, the Watch post's 10 wood + 4 nails). Its icon is a tinted wooden ladder for now.
- **Placing:** hold it and aim at the ground. A see-through ghost of the whole tower follows your aim, framed by the
  placement box. R turns it. By default the ladder comes toward you and the guard looks the way
  you're looking, so stand inside the base facing out. The ghost is green on your claim and red where it can't go;
  pressing then tells you why.
- **Where:** wholly inside the home zone with room for one more Watch post (4 by default), off other claims and trader
  land, with nothing built, none of your crops, no trees or rocks, no water and nobody standing where its blocks go. Wild grass and
  plants inside it are cleared, terrain up to 2 layers above the ground is cut away, and a leg over a dip reaches down up
  to 3 blocks. The bottom layer stands on the ground, or on a built floor (your prefab's YOffset -1 is ignored).
- **Then:** the tower appears a moment after the click, "Watch post N (...) is ready" shows, and you hold E on the post to
  assign a guard (D3). Once up it's ordinary blocks; there is no pack-up. If the spot stopped fitting by the time the
  tower goes up, the kit comes back to your inventory with the reason ("...The kit is back in your inventory."). With no
  room for it, or for another player in multiplayer (a client can't see the zone or the cap, so the host refuses those
  then), it is left on the ground where it was placed, for an hour ("...The kit is on the ground where you placed it.").
- The Watch post now also shows in the prefab editor's creative menu (`CreativeMode` All).

Known limits (not bugs): the icon is a ladder (a proper one can come with D5). How the ghost looks is untested;
`GuardTowerGhost` in `blocks.xml` switches it between glow (the default), solid (vanilla's block ghost), plain (a flat
see-through tint) and off. Doors or other multi-blocks in a future version of the prefab are untested
(the log warns on load). Whether a guard climbs the ladder is untested; as for any post, SC places them on it after 30 s
while you're not looking. After editing the tower in the editor, run `python Tools\import_guard_tower.py` in the mod folder
(it copies `LocalPrefabs\Guard_Tower` in and turns the post again), then restart the game.

Console: `sc tower` (the prefab: size, anchor, post, blocks), `sc tower check [rot]` (would it fit at the block you look
at, and why not; rot 0-3, default: as the kit would face), `sc tower stamp [rot]` (built there at once, no kit used).

- [ ] **Startup:** `patch verification: all 77 targets patched` (70 without Voice Control); the `config:` line ends with
      `guardTower=preview glow, legs +3, cut 2`. `sc tower` prints `scGuardTower: size (4, 7, 5), anchor (2, 0, 2) (on
      the ground; yOffset -1 ignored), Watch post at (2, 3, 1) rotation 6` and the blocks (one
      `scWatchPostShapes:catwalkPlate`).
- [ ] **Recipe:** the Guard Tower is in the backpack crafting list at 782 wood + 4 nails.
- [ ] **Watch Post shapes (fix 2):** the backpack recipe "Watch Post" (10 wood + 4 nails) gives the shape item, whose
      description is the post's own (not vanilla's generic shape text); hold Reload on it for the shape menu. A cube, or a catwalk or plate placed upside down (its flat side up, as in a deck), takes a
      guard; a ramp or a pole says "...a guard can't stand on this shape" when placed and when you look at it, and
      assigning one says it has no flat top. An old cube Watch Post still works where it stands.
- [ ] **The tower's post (fix 2):** a new Guard Tower's post looks like the deck around it (a catwalk plate in the same
      paint); looking at it shows "Watch post N (...)"; `sc watch` lists it facing away from the ladder; a guard
      assigned to it stands on it at deck height, stays there and looks out.
- [ ] **Ghost:** holding the kit, aimed at flat ground in the home zone: the whole tower shows see-through, following the
      aim inside a placement box around the whole tower, with no extra cube in it; R turns it a quarter at a time; by
      default the ladder is on your side. Walking hides it and standing still fades
      it in, as vanilla's ghost does. If it looks wrong, try `GuardTowerGhost` solid and plain (edit `blocks.xml`, then
      load the save again) and tell me what each looks like.
- [ ] **Red, with the reason on the press:** outside the home zone ("...must stand wholly inside your community's land");
      against a wall, a tree or a crop ("<name> is in the way."); on a steep hillside ("The ground is too uneven here.");
      with yourself where a leg goes ("<you> is standing in the way."). The ghost and the box around it are red together.
- [ ] **Build:** on flat ground the tower appears exactly where the ghost was, turned the same way, standing on the ground
      with its legs full height (three blocks under the deck) and no dip around it, and the kit is used up;
      "Watch post N (...) is ready."; `sc watch` lists the post facing away from the ladder; the log has `guard tower built
      at ...`. Try all four turns once.
- [ ] **Uneven ground:** on a gentle slope the legs on the low side reach down to the ground and the high side's bumps
      inside the tower are cut away; the terrain around the legs looks tidy.
- [ ] **Guard:** assign a guard on it; they climb the ladder (or get placed on the post within ~30 s) and look out, away
      from the ladder.
- [ ] **Stability:** the tower still stands a minute later, every block of it (roof and deck included) - no rock-like
      shards or debris around it, no dip; breaking blocks of it behaves like any build. (With the first build the base
      showed shards and a dip: most likely the terrain mesh, bent by the densities that build wrote, not falling tower
      blocks - wood glue holds the roof several times over. If anything does fall, tell me which part.)
- [ ] **Cap:** with 4 posts already, the ghost is red and the press says "Your community already has 4 watch posts."
- [ ] **Prefab editor:** the Watch post is in the editor's creative menu.
- [ ] **Save and reload:** the tower and its post stay; `sc watch` still lists the post.

## Base defence D4: attacks while you're away, the defence rating, the blood-moon siege

**Deployed 2026-10-06** (Fable review + re-check: deployable). DLL md5 `db1ddf2e28336d4fbbdf4c3601f763b2`, deployed **together with** the same tree's Config (the `BaseAttacksAway`,
`BaseAway*`, `BaseSiege*` and `BaseDefence*` keys in `blocks.xml`, and the localization). Plan: `BaseDefence_Plan.md` in
the game root (section 6, with the D4 answers of 2026-10-06); report and reviews in `_research\base_defence\`
(D4_REPORT.md, fable_review_d4.md, fable_recheck_d4.md). No new Harmony target (still **76**, 69 without Voice Control).
No save format change: new keys in the existing `[community]` section (`attackaway`, `awayused`, `defloss`, `homedef`,
`scandef`, `bmhome`, `awaynews`), which older builds ignore; an older build drops an attack that is being fought without
you (`baseattack=3:...`) rather than misread it.

What changed:
- **Hordes come while you're away.** At full attention a horde is called wherever you are - but only one per trip: after
  one fight without you, the meter waits full until you're home again (as in D2).
- **Near or far:** within 100 m of the claim when it arrives, you fight it live (D2). Farther off, your people fight it
  alone for 2 game hours + 1 per 10 zombies (about 3 h for 8, 4 h for 22), wearing it down at the pace the defence rating
  gives. Come within 100 m before it ends and the ones they haven't dealt with yet are at the walls, live. Leave a live
  fight (150 m) and your people fight the rest alone, the kills so far counted.
- **The result** of a fight you missed: a roll against the defence rating - held, damaged or overrun. Only ammo is used
  (1 bundle per 10 zombies, 1.5x overrun); 1-2 residents are hurt when damaged, about 2/3 when overrun, each using a meds
  bundle at once (no meds: they stay injured). **Anyone at home can die**: about 8% for someone hurt (Fighting + Shooting 6,
  15 zombies), more against bigger hordes or for novices, less for veterans, at most 50%; a tenth of that for those not
  hurt, so even a held base can rarely lose someone. Morale +2 held, -5 damaged, -12 overrun (fading a point a day), plus
  the usual death penalty. Only fights you're at count for Leadership.
- **The defence rating** (`sc defence`): 20, +4 per fit resident, +1 per combat star above 2, +10 per manned watch post
  (at most +30, only with ammo in the stockpile), +1 per trap or turret in the claim (at most +20), -15 with no ammo,
  +-5 for morale 75+/under 25, -1 per zombie over 20 (at most -15).
- **Word of it:** with a powered radio you're told when the horde is called, when it hits and how it ended. Without one you
  hear nothing until you're back home: then one tooltip per result, and the ledger has the line.
- **Blood-moon siege:** a blood moon that passes while you were never within 100 m of the claim is settled the same way, 15 below the rating,
  against a horde of the usual size; told at the end (radio) or on your return.
- A guard on watch who is hurt with no meds comes down from the post.

Known limits (not bugs): nothing happens to the base's blocks - "damaged" and "overrun" cost ammo, meds, people and
morale only. The fight without you is a simulation: residents in sight (100-150 m out) just stand about. Arrival "near"
is now 100 m (D2 waited until you were within 30 m), even with BaseAttacksAway off. Back home while an outpost attack is
still being fought, you don't join (one live fight at a time): your people carry on and it settles as usual. Passing
within 100 m and leaving again restarts the time your people hold out. A horde held back by the blood moon arrives the
morning after, the same dawn a siege may be settled: two results that night is expected.

Console: `sc defence [horde]` (the rating term by term, the odds of a fight without you, the highest death risk),
`sc attack status` (now shows a fight without you, its end and how many are dealt with), `sc attack now` far from the claim
(starts a fight without you; it also forgets this trip's earlier fight), `sc attack settle [held|damaged|overrun]` (ends
the open attack now as a fight without you - rolled, or as given), `sc siege [held|damaged|overrun]` (a siege now). The log
line `base attack without the player ...` / `base SIEGE ...` lists the roll and every resident's death chance.

- [ ] **Startup:** `patch verification: all 76 targets patched`; the `config:` line ends with `awayAttacks=2h+1h/10, join
      100m, band 35, ammo 1/10 (x1.5), death 0.08 (cap 0.5, unhurt x0.1), morale +2/-5/-12 siege=-15 defence=20+4/resident+...`.
- [ ] **Rating:** `sc defence` lists base 20, fit residents, combat stars, manned posts, traps/turrets, ammo, morale and the
      horde, and a total 0-100; place a few spike traps in the claim and after ~15 s the trap count goes up (`sc scan`
      forces it).
- [ ] **Fight without you (no radio):** walk more than 100 m from the claim, `sc attack now 8`: no tooltip. `sc attack
      status` shows "the defenders fight it alone", its end time and the dealt count rising. Stay away until it ends (or
      `settime` past it): still nothing. Walk home: "While you were away: the base held/was damaged/... ; N ammo bundles
      used; ..." and the ledger has the line. The stockpile shows the ammo and meds gone.
- [ ] **With a powered radio:** the same, with the radio up: "Radio from home: the horde is at the walls..." at the arrival
      and "Radio from home: ..." with the result at the end, while you're still away. A natural call (attention 100%) while
      away also gives "a horde of N is gathering to the ... - it hits in about N game hour(s)".
- [ ] **Joining:** start one far away (`sc attack now 12`), wait a game hour, walk back within 100 m: "You're back - the base
      is still under attack! Your people dealt with N ... M are at the walls." and M zombies come from that side (D2's
      fight); killing them all gives D2's "The base held!" (and the Leadership event).
- [ ] **Leaving a live fight:** during a live fight walk more than 150 m away: "You left the fight. Your people hold the base
      against the N dead still standing for about H game hour(s)..."; it then settles without you (or come back to join).
- [ ] **One per trip:** after a fight without you settles, while you stay away a full attention meter waits (`sc noise`:
      "held full"; `sc attack status`: "this absence's fight ... has been had"); once you're home a call comes.
- [ ] **Results:** `sc attack now 30` then `sc attack settle overrun` (at home is fine): about 2/3 of the residents hurt, a
      meds bundle each while they last, then injured ones (mood -15); morale's Defence part -12 (`sc morale`). `sc attack
      settle held`: morale +2, nobody hurt. Deaths are rare at the defaults - the log line shows each chance; `sc attack
      settle overrun` with a big horde (`sc attack now 60`) makes one likely: the member is gone from the list, "lost: Name"
      in the result, the death penalty applies, the body disappears.
- [ ] **A guard hurt:** a guard on watch hurt with no meds left comes down ("X is injured and can't keep watch.").
- [ ] **Siege:** spend a whole blood moon more than 100 m from the claim (or `sc siege`): at its end (dawn) the result, as
      above, "the blood moon horde besieged the base ..."; spend any part of it within 100 m: no siege (log: "no siege (the
      player was home for it)").
- [ ] **Save and reload during a fight without you:** `sc attack status` unchanged after the reload; it still ends at the
      same time and settles.
- [ ] **D2/D3 unchanged at home:** a call while you're home is still a live fight with the guard's call-outs; WideDefence and
      the guard exemptions as before.

## Base defence D3: watch posts and 12-hour guard shifts

**Deployed 2026-10-06** (Fable review + re-check: deployable). DLL md5 `7d2731b0ab47af8926a22101da37f40f`, deployed **together with** the same tree's Config: the
`scWatchPost` block and the `Watch*` keys in `blocks.xml`, its recipe, the `sc_base_attack` marker in `nav_objects.xml`,
the AI gates in `utilityai.xml` (the gates name a class in the DLL: the two must ship together) and the localization.
Plan: `BaseDefence_Plan.md` in the game root (section 6, with the D3 answers of 2026-10-06); report and reviews in
`_research\base_defence\` (D3_REPORT.md, fable_review_d3.md, fable_recheck_d3.md). No new Harmony target (still **76**,
69 without Voice Control). No save format change: new keys in existing sections - `[community]` `watchposts`,
`attackwarn`; `[member]` `watch`, `watchrest` - which older builds ignore (an older build sees a guard as an ordinary
resident whose post is the tower top).

What changed:
- **The Watch Post** (crafted in the backpack: 10 wood, 4 nails) is a full cube with barricade planks on top and
  reinforced wood sides. It only goes inside the community's land, at most 4; elsewhere the ghost is red and the press
  says why. It faces the way you looked when you placed it (R turns it). Leave two blocks of air above it.
- **Assigning a guard:** hold E on the post, "Assign guard": the community window opens on the residents; pick one. A
  shift lasts 12 game hours and takes 3 ammo bundles from the stockpile at once (no shift with fewer). Refused for
  someone injured, unhappy (mood under 25), resting, or already on watch, and when there is no room on the post.
  "Relieve guard" ends it early (no rest, nothing refunded).
- **Getting there:** the guard walks up (ladders work for these bodies). Not there about 30 s after setting off, they
  are put on the post the moment you are not looking at them or the post.
- **On watch** the guard stays on the post: no roaming, no walking to intruders (even during a live attack), no
  backing off, no Cowardly flight, no melee swap up close while the gun has rounds. They face the post's way, never lower
  their weapon, see 50% farther (90 m instead of 60) and shoot whatever comes into range. No watchdog moves them: the
  only fix-up is the 30 s placement.
- **The shift's ammo** pays for that guard's resupplies first (3 x 2000 points); once used up, resupply comes from
  the stockpile as usual. What is left at the end is spent.
- **After a full shift** the post is empty until you assign someone, and that guard rests 12 game hours. A post that
  is picked up, broken or built over sends its guard back to their old post; so does following you, which is refused
  while on watch anyway (as are supply runs and outpost postings).
- **Call-outs**, only while you are at the base (inside the claim or within 30 m): when a horde is called, a guard on
  watch says "X on watch: a horde is gathering to the north!" and an orange skull marker (compass and map) shows that
  side; when a guard first sees an attacker, "X on watch: the horde is coming from the north!" and the marker moves onto
  the horde until the fight ends. Without a guard, nothing changes from D2.
- **Attention:** every manned post cuts attention gain by 15% (at most 45% for three or four).

Known limits (not bugs): until a guard first sees an attacker, the marker stays on the side the horde was called from,
even if the spawns fall back to another side (no spot on theirs for 20 s). The Watch Post's item description names the
default numbers (4 posts, 12 hours, 50% farther, 3 bundles); after retuning the `Watch*` keys it no longer matches.

Console: `sc watch` (or `sc watch list`) lists the posts, which way each faces, each guard's hours left, prepaid points
and sight, who is resting, the attention cut and the call-outs. `sc watch assign <post> <member>` (all the radial's
rules), `sc watch relieve <post|member>`, `sc watch end <post|member>` (as if the 12 hours had run: the rest applies),
`sc watch rest <member> [clear]`, `sc watch place <post|member>` (onto the post now, looked at or not), `sc watch scan`.
A post is its number in the list or `x,y,z`. Config: `WatchPosts` and the `Watch*` keys in `Config/blocks.xml`.

- [ ] **Startup:** `patch verification: all 76 targets patched` (69 without Voice Control); the `config:` line ends with
      `watchPosts=4 posts, shift 12h (rest 12h, 3 ammo), sight x1.5, placed after 30s, attention -0.15/post (max -0.45)`.
- [ ] **The block:** it crafts in the backpack; placed, it is a cube with planks on top and reinforced wood sides, and
      its inventory icon is a cube on the plank texture. Its E prompt reads "Watch post 1 (north): no guard on watch".
- [ ] **Placement rules:** outside the claim the ghost is red and pressing says "A watch post must stand inside your
      community's land."; a fifth post says "Your community already has 4 watch posts."; inside, "Watch post 1 (...) is
      ready. Hold E on it to assign a guard." `sc watch` lists it and the way it faces (the way you looked).
- [ ] **Assigning:** "Assign guard" opens the window on the residents with "Pick a guard for Watch post 1 (...)." in
      the list header; a click closes it with "X takes the watch at Watch post 1 (...) for 12 hours (3 ammo bundles from the
      stockpile)." and the stockpile shows 3 fewer. The list row says "On watch", the selection "On watch at Watch post 1
      (...) - 12 h left".
- [ ] **Refusals:** with fewer than 3 ammo bundles the radial says so at once; picking someone injured, under 25 mood,
      resting or already on watch shows the reason in the list header and keeps the pick open.
- [ ] **Getting there:** the guard walks to the tower and climbs. If they are not up within about 30 s they appear on
      the post the moment you look away (log: `placed on Watch post 1 ... the player not looking`), never while you watch.
- [ ] **Stays put:** on the post the guard faces the post's way, keeps the weapon up (`sc xnpc <id>` with nothing
      about: relax says "on watch"), does not roam by day, does not idle facing elsewhere at night, and is never moved by
      the stuck watchdog.
- [ ] **Fights from the post:** zombies in range are shot from up there; the guard does not back off the edge, chase an
      intruder, swap to the melee weapon up close (while the gun has rounds) or flee if Cowardly. During `sc attack now 6`
      the other residents defend the whole base and the guard stays on the tower.
- [ ] **Sight:** `sc watch` shows the guard's sight as 90 m; when the watch ends the log line `X's watch at ... ended
      (...)` ends with `sight 60 m`.
- [ ] **Call-out at the call:** guard on watch, you at home: `sc attack 6` - within a second "X on watch: a horde is
      gathering to the <side>!" and an orange skull on the compass and the map on the side `sc attack status` names.
      Without a guard on watch nothing is shown (D2).
- [ ] **Call-out on sight:** when the horde arrives, the first time the guard sees one: "X on watch: the horde is
      coming from the <side>!", and the skull moves onto the horde and follows it; it is gone once the attack ends.
- [ ] **Not while away:** called while you are more than 30 m outside the claim (`sc attack` from afar): no call-out
      until you are back; the skull hides when you are more than 150 m out.
- [ ] **Attention:** with one guard on watch `sc noise` adds "gain x0.85 for 1 manned watch post(s)" (x0.7 with two,
      x0.55 with three or four) and the "+N/h" figure is that much smaller.
- [ ] **The shift's ammo:** during the shift the guard's resupplies log "N point(s) from the watch shift, M left" and
      open no bundles until the 6000 points are used up; then they open bundles as usual.
- [ ] **Shift end:** `sc watch end 1` (or a 12-hour `settime` jump): "X's watch at Watch post 1 (...) is over. ...",
      the guard walks back to their old post, `sc watch` shows "X rests 12 more h" and assigning them is refused until
      then (`sc watch rest X clear` lifts it). The real 12 hours end the same way.
- [ ] **Relieve:** "Relieve guard" on the radial: "X comes down from Watch post 1 (...)." and they go back to their
      old post; they can take a new shift at once (3 more ammo).
- [ ] **Post gone:** pick up or break a manned post: "Watch post 1 (...) is gone - X goes back to their post."; `sc
      watch` no longer lists it. Put a block in the cell above a manned post, with the guard nearby (loaded): the watch
      ends ("There's no room to stand on ... any more").
- [ ] **Leaving mid-fight** with a guard on watch: after the horde comes back, the guard calls it out again ("...
      coming from the ...") when they first see it.
- [ ] **A guard is busy:** following you, a supply run and an outpost posting are refused for a guard on watch
      ("They're on watch - relieve them at their watch post first."); `sc repost <guard>` refuses too.
- [ ] **Save and reload mid-shift:** the guard is still on the tower with the same hours left, facing the same way,
      sight 90; `sc watch` unchanged.
- [ ] **Multiplayer** (only if handy): host only, like all of SC - a client can place the block, but only the host's
      radial assigns.

## Base defence D2: live horde attacks on the home

**Deployed 2026-10-06** (Fable review + re-check: deployable). DLL md5 `44050e6e2643eb10e29933b2a5878e75`, 679424
bytes, deployed **together with** the same tree's Config (`blocks.xml` keys, localization). Plan: `BaseDefence_Plan.md`
in the game root (section 6, with the D2 answers of 2026-10-06); report and reviews in `_research\base_defence\`
(D2_REPORT.md, fable_review_d2.md, fable_recheck_d2.md). No new Harmony target
(still **76**, 69 without Voice Control). No save format change: three new keys in the registry's `[community]` section
(`baseattack`, `attacknext`, `defended`), which older builds ignore.

What changed:
- **The call.** When attention reaches 100% while you are home (inside the zone or within 30 m of it), a horde is
  called and attention drops to 0. Nothing tells you: only a guard on watch will see it coming (D3). Away from home the
  call waits for you. No call within 2 days of the last attack's end, from 10:00 on blood-moon day until the blood
  moon ends at dawn, or while an outpost attack is being fought. Whatever holds the call back, a full meter stays at
  100% until the horde is called. Attention gains nothing while an attack is open.
- **The arrival**, 1-2 game hours later, from one side. If you are away at that time (or it is the blood moon), it
  comes 1-2 hours after you are back.
- **The horde.** About 8 zombies early, 14 at gamestage 50, 22 at 100 (cap 40), +5% per resident. The types are
  vanilla's screamer-horde list (`ScoutGSList`) for your gamestage, so ferals and radiated come when they would there.
  They spawn one a second 25-50 m beyond the zone's edge (45-70 m from a default claim's centre), all within 30 degrees
  of their side, never within 30 m of you, and only while vanilla's zombie cap has room. With no spot on their side
  for 20 s they may come from any side; with none at all for 40 s (a base ringed by POI floors or paving, which
  vanilla's spawn rule rejects) from any block they can stand on.
- **At the base** they are vanilla's horde: they bang their way in (ZombieAIOverhaul: only ferals break walls), fight
  whoever hurts them, and you. A zombie with nothing to do for 45 s is sent elsewhere in the base; one that wanders
  more than 30 m off is sent back.
- **Your residents defend the whole base** while the attack is live: any enemy in the zone or up to 10 m outside it,
  wherever their post is (normally 22 m around the post). Cowardly members still run.
- **Held** (every attacker dead): tooltip and ledger line with what the fight cost the stockpile (the resupplies during
  it), a new morale part "Defended the base" (+4, at most 10, fading 1 a day), and Leadership's `sc_ev_defend`.
- **Leaving mid-fight** (more than 150 m from the zone): the attackers still standing vanish and come back, only they,
  1-2 hours after you return. **A reload mid-fight** is the same: only the ones still standing come back, 15 s after the
  load. Nobody fights while you are away.
- **A stalled fight** (4 game hours after it began and no kill in the last hour) ends: the stragglers become ordinary
  zombies and it counts as held.

Console: `sc attack [now] [size]` calls a horde whatever the attention, the 2-day gap and the blood moon ("now": it
arrives at once; a size overrides the usual one; away from home it waits for you). `sc attack status` shows the open
attack, the next horde's size and mix, and what the next call waits for. `sc attack end` despawns the attackers and
closes the attack (nothing won). `sc noise` shows the attack status too. Config: `BaseAttacks` and the `BaseAttack*` keys
in `Config/blocks.xml` (ConfigSurvivorCommunity).

- [ ] **Startup:** `patch verification: all 76 targets patched` (69 without Voice Control); the `config:` line ends with
      `baseAttacks=gap 2d, lead 1-2h, size (6+gs*0.16)*(1+0.05/resident) 4..40, mix ScoutGSList, ring +25..50m +-30deg,
      defend zone+10m, home 30m, leave 150m, max 4h, morale +4`.
- [ ] **Status:** at home, `sc attack status`: `attack: none; a call comes at attention 100%`, then `next horde: N
      zombie(s), ScoutGSList stage S (gamestage G): scoutHordeStageGS...` with N about as above for your gamestage.
- [ ] **A forced attack:** at home, `sc attack now 6`: within a few seconds zombies appear one a second outside the
      claim, all on the side `sc attack status` names; the tooltip "The dead are at the walls! 6 of them came for the
      base." when the first reaches the claim (or a minute after the first spawned).
- [ ] **They come for the base:** they head straight for the base and bang on doors and windows; none stands around
      outside or wanders off for good.
- [ ] **Residents defend the whole base:** residents posted on the far side of the base come over and fight, also just
      outside the claim, without turning back halfway or being teleported to their posts mid-fight. Cowardly members still
      run. Afterwards everyone walks back to their post.
- [ ] **Held:** kill them all: the tooltip "The base held! 6 of the dead lie at the walls; the fight cost ..." and a
      ledger line; `sc morale` and the community window show "Defended the base +4"; `sc attack status` says no call
      before two days later.
- [ ] **The ammo bill:** with resident gunners and ammo in the stockpile, the held message names the rounds resupplied
      during the fight ("N rounds from the stockpile, about X ammo bundles"); with none resupplied, "nothing from the
      stockpile".
- [ ] **Attention calls it:** at home, `sc attention 100`: within a second the log has `base attack called (attention
      100%)`, nothing is shown to you, `sc noise` shows attention 0 and "paused: an attack is open"; 1-2 game hours later
      the horde arrives.
- [ ] **The call waits for you:** more than 30 m outside the claim, `sc attention 100`: no call (`sc attack status`: `a
      call waits: the player is away from home`); stay away a few game hours: `sc noise` still shows 100%; walk home:
      the call comes.
- [ ] **The arrival waits for you:** `sc attack` at home (1-2 hours' lead), leave the claim by more than 30 m and stay
      out past the arrival time: the log says `base attack due but the player is away from home - it waits`. Come home:
      `the player is home - N zombie(s) arrive day ...`, and they come 1-2 hours later.
- [ ] **Leaving mid-fight:** `sc attack now 10`, kill a few, then go more than 150 m from the claim: the attackers vanish
      and "You left the fight..." shows. Come back: 1-2 hours later only the rest arrive (`sc attack status` keeps the
      killed count), and holding counts all 10.
- [ ] **Reload mid-fight:** `sc attack now 10`, kill some, save and quit to the menu, load: about 15 s later only the
      ones still standing come back (`base attack resumes after a load` in the log); holding counts all 10.
- [ ] **Blood moon:** on blood-moon day after 10:00, `sc attention 100` at home: no call (`a call waits: the blood
      moon ...`); once the blood moon is over at dawn the call comes if you are home.
- [ ] **The gap:** right after a held attack, `sc attention 100`: no call (`too soon after the last attack`) until two
      game days after it ended.
- [ ] **The zombie cap:** with many zombies already about, the spawning pauses instead of dropping attackers, and goes
      on as room frees up (`sc attack status`: spawned holds, then rises).
- [ ] **A city base** (only if yours is one): if the log says `no terrain spawn spot around the base - spawning on any
      standable block in the ring`, the horde still comes (from streets or POI floors) instead of never arriving.
- [ ] **The mix:** at a higher gamestage, ferals and radiated turn up as in vanilla's screamer hordes.
- [ ] **Pause:** open the pause menu during an attack: nothing spawns while it is open.
- [ ] **`sc attack end`:** the attackers vanish, the attack closes, nothing is won.
- [ ] **A stalled fight ends** (only if one happens): a zombie stuck out of reach keeps the attack open; 4 game hours
      after it began, with no kill in the last hour, the log has `base attack HELD ... straggler(s) let go` and the
      held tooltip shows (with nothing killed at all: `base attack ended without a fight`, no reward).
- [ ] **A full meter stays full:** with attention at 100% during the 2-day gap or on blood-moon day, `sc noise` says
      `held full until a horde is called`, and the horde is called once the gap ends / at the dawn after the blood moon
      (if you are home).
- [ ] **One live attack at a time** (only if handy): while an outpost attack is being fought (its attackers are out),
      a home call or arrival waits; while a home attack is live, an outpost's attackers do not spawn.
- [ ] **Multiplayer:** host only, as all of SC: "home" and "left the fight" are the host's own player.

## Base defence D1: the Home Status panel and the noise meter

**Deployed 2026-10-05** (Fable review + re-check: deployable). **First look by the user 2026-10-06: panel, red numbers and noise levels work;** the rows still open were not reported either way. DLL md5 `c035ed57eee3402a3cd467472a43b536`, 655360
bytes, deployed **together with** the same tree's Config (`blocks.xml` keys, the XUi window, localization). Plan:
`BaseDefence_Plan.md` in the game root; report and reviews in `_research\base_defence\` (D1_REPORT.md,
fable_review_d1.md, fable_recheck_d1.md). Two new Harmony targets: **76** (69 without
Voice Control). No save format change: three new keys in the registry's `[community]` section (`noiseev`, `attention`,
`noisestamp`), which older builds ignore. Nothing attacks yet: D1 only shows and measures; the hordes come with D2.

What changed:
- **Home Status panel** at the HUD's top left (since 2026-10-09: right of the compass) while you stand inside the home zone, laid out like State of Decay 2's:
  "Home Status" with a morale dot and word (green happy, light content, amber uneasy, red miserable); food, meds,
  ammo, materials and fuel (a number is **red when it is below what the next rollover wants**: food for everyone, meds
  for the injured, 1 ammo while any resident fights with a gun, the outposts' upkeep in materials and haulage in fuel);
  the member count; the base's noise. It hides while a menu (inventory, map, a workstation) is open, and with F7.
- **Noise** = steady (2 per member living at home, 6 per running generator in the zone, 1 per workstation) + events
  that halve every game hour (anything that makes vanilla heat inside the zone - a burning forge or campfire, crafting,
  torches, your gunfire, explosions, broken blocks - and NPC gunfire, which vanilla never hears). Levels: quiet,
  noticeable from 15, loud from 30, very loud from 50.
- **Attention** (0-100%) grows each game hour with noise above 15 and fades 2 an hour; it gains nothing during the blood
  moon. The panel shows it as "Noise: Loud (42%)" once it is above 0.

Console: `sc noise` (the breakdown and what was heard this session), `sc noise add <n>` / `sc noise clear` (event noise),
`sc attention <0-100>`.

- [x] **Startup** (2026-10-05 23:57 log: 76 patched, both new targets, the full `noise=` config line)**:** `patch verification: all 76 targets patched` (69 without Voice Control), with `patched
      AIDirector::NotifyActivity` and `patched ItemActionRanged::ItemActionEffects`; the `config:` line ends with
      `homeHud=True noise=member 2 generator 6 workstation 1, heat x1 npc shot x1, half-life 1h, levels 15/30/50,
      attention +0.14 -2/h`.
- [ ] **The panel shows at home only:** walk into the home zone: the panel appears at the top left; walk out: it goes.
      Open the inventory or the map inside the zone: it hides, and comes back when you close them. F7: gone with the
      rest of the HUD.
- [x] **It reads like the reference** (the user, 2026-10-06: "Panel looks good")**:** title, dot and morale word on the first row (the word matches `sc morale`'s
      band); the five resource icons with numbers that match `sc status`'s stock; the member count; the noise line.
      Nothing overlaps or is cut off; AshfallUI did not dress it as a dialog (it is a plain dark plate with a teal strip).
- [x] **Red when short** (the user, 2026-10-06: food via `sc give food -999` / `6`; meds/ammo not reported)**:** `sc give food -999`: food turns red (it is below the day's need); `sc give food 20`: white
      again. Injure a member with no meds (`sc injure <member>`, `sc give meds -999`): meds red. With a resident gunner,
      `sc give ammo -999`: ammo red. Materials and fuel only go red with outposts that cost upkeep or haulage.
- [x] **Steady noise** (the user, 2026-10-06: "noise levels work"; the generator switch-off not reported)**:** `sc noise`: `members at home` equals your residents, `workstations` the facilities the
      community window counts, `running generators` the generators switched on with fuel inside the zone (switch one
      off: it drops within about 10 s).
- [ ] **Heat events:** light a forge or a campfire in the base and wait a few minutes: `sc noise` shows `heat events`
      rising and `Forge=` / `Campfire=` among the counted kinds, and the event noise grows. Fire a few shots in the base:
      `Sound=` grows. Do the same outside the zone: `heard` rises but `in the zone` does not.
- [ ] **NPC gunfire counts:** let a resident with a gun fight a zombie inside the zone: `NPC shots ... (in the zone N)`
      rises and `npcShot=` appears among the counted kinds. A bow adds nothing (vanilla rates bows as silent for you
      too), and a cop's or spitter's vomit is never counted.
- [ ] **Fuel and materials only with outposts:** with no outposts both stay white whatever the stock; with outposts,
      fuel is red only when the next rollover will actually deliver something (a working radio, or the outpost's relay).
- [ ] **Fading and attention:** `sc noise add 60`: the panel shows "Very loud"; `sc noise` reports attention gaining
      about +4/h; let a game hour or two pass: the event noise has halved each hour and attention has grown. `sc noise
      clear` and `sc attention 0` reset them. Reaching 100% writes one log line (`base attention reached 100% ...`;
      since D2 it is followed by the call once you are home, see the D2 section).
- [ ] **Save and reload:** quit and continue: noise and attention come back as they were (minus the time that passed).
- [ ] **Blood moon:** during the blood moon `sc noise` says `paused: blood moon` and attention does not grow.
- [ ] **Tuning (no pass/fail):** play a normal session at home and note what `sc noise` says for your base on a quiet
      evening and while you work (forge, crafting, a fight). Those numbers set D2's horde frequency.

## Relaxed: arms down and weapon hidden when out of danger (replaces the deferred holster, M7.6)

**Deployed 2026-10-04** (Fable review + re-check: deployable). **Passed in game 2026-10-05** (the user); only the multiplayer row is open. DLL md5 `33573592c0eee3eb51e6c9645061f5d9`, 640000
bytes, deployed **together with** the same tree's `Config/blocks.xml` (new keys `XnpcRelax`, `XnpcRelaxRaiders`,
`XnpcRelaxHideWeapon`, `XnpcRelaxCalmSeconds` 4, `XnpcRelaxRange` 0). Two new Harmony targets: **74** (67 without Voice
Control). No save format change (one new cvar, `sc_relaxed`). Report: `RELAX_REPORT.md`; review: `fable_review_relax.md`.

What changed (your decisions: the NPC's own sight range with awake enemies only, survivors and raiders, every weapon):
- A survivor, member or raider with no danger for 4 s lowers its arms to the bare-hands pose (walking with swinging arms)
  and its weapon disappears; it still holds it (ammo, swaps and the AI are unchanged).
- Danger = knocked down or stunned, fighting someone, hurt by someone, or an awake enemy within its sight range (60 m on
  these bodies; `XnpcRelaxRange` shortens it). Sleepers in a POI don't count. The weapon comes back up the moment danger
  returns, when an attack starts, before every swing or shot, and before every reload; a gun with an empty magazine and
  ammo to load stays up until it is loaded.
- Never with a bandage, food or bare hands in hand. `XnpcRelaxHideWeapon` false only lowers the arms; `XnpcRelax` false
  turns it all off.

- [x] **Startup** (2026-10-05 20:44 log: 74 patched, `relax=after 4s, sight range, weapon hidden, raiders too`)**:** `patch verification: all 74 targets patched` (67 without Voice Control), with `patched
      LegacyAvatarController::_setInt(int)` and `patched LegacyAvatarController::StartAnimationReloading`; the `config:` line contains `relax=after 4s, sight range, weapon hidden,
      raiders too`. `sc xnpc` prints `[SC] relaxed (arms down, weapon hidden): on, N relaxed now; ...`.
- [x] **A member at home relaxes** (the user, 2026-10-05)**:** with no zombie around, watch a resident with a gun (then one with a melee weapon):
      about 4 s after the last danger its arms drop to its sides and the weapon is gone; it walks with swinging arms.
      `sc xnpc <member>`: `relax: RELAXED (arms down, N weapon renderer(s) hidden), cvar 1, calm, ...`.
- [x] **It raises at once** (the user, 2026-10-05)**:** spawn a zombie 30-50 m away (awake, in sight range): the weapon reappears in its hand and the
      hold pose comes back within about half a second, before it starts shooting. `sc xnpc <member>` while it lasts:
      `relax: alert, cvar 0, enemy zombie... #N at 37 m`. Kill the zombie: about 4 s later it relaxes again.
- [x] **First shot is armed** (the user, 2026-10-05)**:** let a relaxed ranged member be surprised (a zombie that wakes up close, or one you spawn
      right beside it): the gun is up and visible for the first shot (no punch animation, never fired from an empty
      hand); the same for a melee member's first swing, which lands. `sc xnpc`: `... (n by an attack starting, ...)` rises.
- [x] **Followers** (the user, 2026-10-05)**:** a follower walking with you through a quiet area walks unarmed-looking; the moment a zombie comes
      within its range (60 m by default) it draws.
- [x] **Raiders** (the user, 2026-10-05)**:** a raider wandering with nobody within 60 m looks empty-handed; when it spots you it has its weapon up
      before attacking. `XnpcRelaxRaiders` false (restart): raiders always armed.
- [x] **Never with an item** (the user, 2026-10-05)**:** a member bandaging itself keeps the bandage visible and plays the use animation as before.
- [x] **Empty magazine** (the user, 2026-10-05)**:** a calm gunner that shot its magazine empty (with spare ammo in its bag) keeps the gun up,
      reloads it (the reload animation plays; `[SDCS-READY] ... event=preload`, no `preload-stuck`), and only then
      relaxes; `sc xnpc <id>` meanwhile: `relax: alert, ... magazine empty (loading it first)` or `reloading`.
- [x] **Bow or crossbow member** (the user, 2026-10-05)**:** relaxes and raises like a gunner; after a fight it nocks/reloads with the weapon up,
      then relaxes (no arrow drawn from an empty hand).
- [x] **Knockdown and get-up** (the user, 2026-10-05)**:** shove a relaxed member: it gets up exactly as before (the `get-up ...` line still ends
      `for Empty`), and it shows its weapon while down (danger: knocked down), then relaxes again after 4 s.
- [x] **Swaps while relaxed** (the user, 2026-10-05)**:** hand a relaxed member a weapon through the weapon menu: the new weapon is hidden too while
      it stays relaxed; when it draws, the new weapon shows. The old one comes back to you visible (an item, not held).
- [x] **Save and reload** (the user, 2026-10-05)**:** quit and continue near relaxed members: they come back relaxed or alert as the situation
      says within a second; no invisible weapon in a raised pose, no floating hold pose with no weapon.
- [ ] **Multiplayer (when there is a second player):** both players see the same arms-down/weapon-hidden state on the
      same NPC.

## T1 + T2: no Trade on members, raiders get up properly

**Deployed 2026-10-03** (Fable review + re-check: deployable). DLL md5 `8d145663f3c5ac9329b73e8617b89816`, 631296
bytes, deployed **together with** the same tree's `Config/blocks.xml` (two new keys, `XnpcGetUpGuard` and
`XnpcGetUpTrace`). No new Harmony target: still **72** (65 without Voice Control). No save format change. Report:
`T1T2_REPORT.md`.

What changed:
- **No "Trade" on members (T1).** Choosing Trade on a member's radial locked the game: these NPCs have no trader
  (0-XNPCCore's npc_info has no `trader_id`, so the trader id is -1), and vanilla's trade path zoomed the camera in,
  took a lock and then crashed on trader -1 before any window opened. Trade is now refused on every NPC of this kind
  (an SCore V4 or older EntityAliveSDX body with no real trader): members, strangers, a joined 0-XNPCCore companion
  and XNPC's own NPCs. Talk is untouched. A real trader keeps its Trade.
- **Get-ups (T2).** Suspected cause, not yet measured: the get-up clip shares the animator's locomotion layer, which
  jumps to the run pose whenever `IsMoving` is on, and a raider knocked down while running at you keeps `IsMoving` on
  through the whole ragdoll (the animator is off then, so nothing updates it); the clip also runs about 0.4 s past the
  end of the ragdoll, when the raider is free to run again. Now `IsMoving` is cleared at the knockdown and held off
  while the knockdown/get-up clip plays, to its end (`XnpcGetUpGuard`). Every knockdown of a survivor or raider writes
  one `get-up ...` log line (`XnpcGetUpTrace`) that shows what the layer did, so the log settles it.

- [x] **Startup** (2026-10-03 21:25 log: 72 patched, `getUp=guarded+trace`; the `sc xnpc` lines not checked):
      `patch verification: all 72 targets patched` (65 without Voice Control); the `config:` line contains
      `getUp=guarded+trace`; no `xnpc-talkprompt threw` error. `sc xnpc` prints `x9-no-trader-trade-refused=...` and a
      new `[SC] get-ups:` line.
- [x] **Trade is gone** (the user, 2026-10-03)**:** look at a member: "Press E to talk" still shows. Hold E for the
      radial: Talk is there (and Remove with the debug menu on), **no Trade**. Talk still opens the member dialog (and
      recruiting still works on a stranger). The same on a stranger and on an `npcSdcsFemaleBow` from the spawn menu.
      `sc xnpc`: `x9-no-trader-trade-refused` above 0. A raider still shows no prompt at all. A vanilla trader still
      offers Trade. With the debug menu off only Talk is left, so E may open the dialog straight away instead of a
      one-entry radial: either is fine.
- [x] **A raider gets up** (the user, 2026-10-03; log: `Knox the Raider ... IsMoving at the knockdown True, on 100/189
      frames, held off 100 ... left GetUpBack at 0.90 for Empty` - the suspected cause confirmed)**:** shove (or
      otherwise knock down) a raider that is running at you. It should lie down, then play the push-up/roll-over get-up
      (about 2.5 s) before it comes at you again. Keep the log: its line reads like `get-up Crow (scRaiderFemaleT1
      #1060, raider, player path): BlendOutGround 0.45[locomotion] > BlendOutStand 0.72[Knockdown-Chest] > Stand
      1.15[GetUpChest] > StandCollide 1.95[GetUpChest] > Off 2.85[GetUpChest] | clip left GetUpChest at 0.90 for Empty
      in Off | IsMoving at the knockdown True, on n/N frames, held off m ...` (times are seconds from the knockdown; the
      bracket is the locomotion layer's state, during a blend the one it leaves). Read it so: `IsMoving at the knockdown
      True` + `held off` above 0 + `left GetUpChest at 0.90 for Empty` = the suspected cause, now held off. A `moving`
      inside a step's bracket (and the `on n/N frames` count) is the controller's write of that frame, which the guard
      clears before the animator runs: not a failure. The failures are in the `clip ...` part - `left GetUpChest at 0.31
      for locomotion (IsMoving)`, `jumped to locomotion`, `never on the locomotion layer` - or `held off 0` with
      `IsMoving at the knockdown True` and the raider still popping up.
- [ ] **No line at all for a raider's knockdown** = it never reached SC's get-up fix: look for a `was knocked down but
      is not one of our bodies` line, `sc xnpc`'s `ragdoll=` count among the hooks that stepped aside, and
      `XnpcRaiderClasses`. A line saying `VANILLA path` means `XnpcBodyGetUp` (or the dev core's switch) was off.
- [x] **Survivors still get up as before** (the user, 2026-10-03)**:** shove a member that stands still: the same clip as before this build, and
      its line ends the clip with `left GetUpChest at 0.90 for Empty` (or `GetUpBack`), `held off` 0 or a few frames.
- [ ] **Optional, the comparison:** `XnpcGetUpGuard` false (restart), shove a running raider again: if the guard is
      what fixed it, the raider pops up again and its line shows `left ... for locomotion (IsMoving)` or `jumped to
      locomotion (IsMoving)`. Set it back to true.
- [ ] Once get-ups look right, `XnpcGetUpTrace` false silences the per-knockdown line (the guard stays).

## M11 fix 2: melee pace and reach, posts with a real floor, the gun stays up

**Deployed 2026-10-02** (Fable review: deployable, low notes only). DLL md5
`60686311b981724eca64b2866e2a5f63`, 624640 bytes, deployed **together with** the same tree's `Config/utilityai.xml`
(new action `ScAtPost`, naming the DLL's new `ScAtPost` consideration and `HoldPostSC` task) and `Config/blocks.xml` (new
keys; `OverwhelmedHoldSeconds` 3 -> 5). Two new Harmony targets: **72** in all (65 without Voice Control). No save format
change. Report: `M11FIX2_REPORT.md`; diagnosis: `MELEE_DIAG.md`, `STUCK_DIAG.md`, `AIM_DIAG.md`.

What changed:
- **Melee pace (your decision: raiders too).** Survivors and raiders swing their melee weapon as soon as it allows (the
  T1 bat every ~1.25 s, as yours) instead of on the framework's 1 s ticks, which gave one swing every 2 s and a lost second
  after every new target. Switches: `MeleePacingRaiders`, `MeleePacing`. Guns unchanged.
- **Melee reach.** The framework judged every melee weapon's reach as 1.5 m; now the weapon's own (the bat 2.4 m) less
  `MeleeReachMargin` (0.4): they swing from about 2 m and stop flipping between walking in and swinging. `MeleeReach`
  false gives the old 1.5 m back. New Harmony target.
- **No bat/gun churn.** They keep the melee weapon while any enemy is within 3 m (no wall between), and for 1.5 s after
  their target dies or is lost. Overwhelmed takes them back to the gun only after the count has held 1.5 s
  (`OverwhelmedSustainSeconds`; one enemy more with the zombie within 1.5 m), then the gun stays at least 6 s
  (`OverwhelmedGunSeconds`); `OverwhelmedHoldSeconds` is now 5.
- **Posts.** A post needs a real floor (a full block or terrain, preferably open, never furniture, a plate, a ramp or a
  railing); a bad post from an older save is replaced the first time it loads (Kyle's bathtub). Roams aim at real
  floors too. A resident within a step of its post just stands (new `ScAtPost`, instead of Guard re-planning for ever).
- **Stuck loop.** Not while the game is paused; a pending path request no longer survives a move back; stuck at the post
  itself, or three moves back within three minutes -> a new post (no teleport); none to give -> the wait doubles. The
  stuck line says which signal counted and where it stood. `sc repost <member>`. The supply runners' watchdog ignores
  paused time too.
- **The gun stays up.** The aim pose holds while a resident waits for you to clear its line (`ScDefendHold`) and while it
  backs away from a zombie (your decision; new Harmony target); the carry pose is repaired if the animator lost it
  (`carryFixes`); "facing away" is judged on the target the task works on.

- [ ] **Startup:** `patch verification: all 72 targets patched` (65 without Voice Control), with `patched
      UAITaskBackupFromTargetSDX::Update` and `patched UAIConsiderationInWeaponRange::GetScore`; the `config:` line shows
      `overwhelmed=... hold 5s sustain 1.5s gun 6s meleePacing=on meleeReach=weapon -0.4m`; no `Type:... was missing`
      for `ScAtPost` or `HoldPostSC`. `sc xnpc` prints a `[SC] melee:` line; `sc xnpc <member>` prints `nav:` and (for a
      member) `post:` lines.
- [ ] **Swing rate:** a member with the T1 bat against one zombie, you standing aside and **not** shoving. Count its
      swings over 20 s: before this build about 10, now about **16** (a stopwatch, or `sc xnpc`'s `[SC] melee:` line:
      `swing(s)` before and after). `sc xnpc <member>` mid-fight: `melee reach=2.0 (the weapon's 2.4 m less 0.4; SCore
      alone: 1.5) ... swing every 1.27 s, last 0.x s ago, paced=True`. `refused by the weapon` should stay near 0 (a few
      after a hit stun is fine). Then a melee-lead raider with the bat (`sc spawn hostile male T1 <bat> melee-lead`, the
      bat's name from `sc weapons`; let it fight a zombie, or you in god mode): also ~16 swings in 20 s. Then
      `MeleePacingRaiders` false (restart): the raider back to ~10, the member still ~16; `MeleePacing` false: both ~10.
      Watch that every swing plays its whole animation and hits (no half swings, no double hits per swing).
- [ ] **No gun/bat churn (2-3 zombies):** a 1-2 star member with bat and pistol, 2-3 zombies at once, you aside. In the
      log, count `[SDCS-SWAP]` lines: after a kill with the next zombie within 3 m there is **no** `[SDCS-CLOSEMELEE]
      exit reason='target-gone'` - it keeps the bat and turns to the next one (hooks `x5-melee-exit-held` +1). `sc xnpc
      <member>`'s overwhelmed line may show `0-XNPCCore wants to leave melee for N s (held: an enemy within 3 m)`. Once
      the last zombie is dead it goes back to the pistol within about 1.5 s. Compare with the 17:40 log (7 bat/gun round
      trips in 81 s): expect at most one or two.
- [ ] **Overwhelmed still pulls the gun when swarmed:** the same member (pistol with rounds) with 3-4 zombies on it.
      After the bat's first 3 s plus about 1.5 s: `[SDCS-SWAP] restore=...` with no `[SDCS-CLOSEMELEE] exit` before it
      (hooks `x5-overwhelmed-exit` +1); with `DebugLogging` true: `... is overwhelmed (3 enemies on them, 2 needed, for
      1.5 s) - back to the gunHandgunT1Pistol, kept for 6 s`. For the next 6 s it does not go back to the bat even with a
      zombie at 1-2 m (`x5-overwhelmed-gun-hold` rises; `sc xnpc` shows `GUN HELD N s more`). Control: exactly 2 zombies
      with one of them at arm's length (under 1.5 m) - it stays on the bat (point-blank needs 3).
- [ ] **Melee reach, no approach/attack flip:** XNPC `VerboseLogging` true (its `Settings.xml`). One zombie, a bat member:
      `[SDCS-UAI] action='MeleeApproach'` / `'MeleeAttack'` flips should be rare - it stops about 2 m from the zombie and
      swings (`sc xnpc` `InWeaponRange=True` at 1.6-2.0 m), and a hit that knocks the zombie back a little does not send it
      walking. Watch for swings that miss at the edge, especially against crawlers; if they do, raise `MeleeReachMargin`
      to 0.6 and say so.
- [ ] **Kyle off the tub (load):** load the 17:04 save. Within a second or two of Kyle's chunk loading, a warning:
      `Kyle's post -414, 43, -748 cannot be used (the post is on decoClawFootBathTub, checked on load) - new post X, Y, Z
      (an open spot on a real floor)`; he walks there. `sc xnpc Kyle`: `post: X, Y, Z floor woodShapes:cube (or a
      terrain block), strict good, relaxed good; at ... (Guard: arrived)`, and `uai: action=ScAtPost task=UAITaskHoldPostSC`
      once he is there. `sc members` shows the new post. No `was stuck` lines in 10 minutes of daylight.
- [ ] **No stuck loop while paused:** with a resident walking somewhere (a roam, or just after `sc repost`), press ESC
      and stay in the pause menu for 40 s: **no** `was stuck` line while paused (before: one every 12 s). Same with a
      physical supply runner out walking: no `stuck ... skipped ahead` and no `stuck` hand-off while paused.
- [ ] **`sc repost <member>`:** on a resident: `[SC] <name>: post A -> B (at ... from the post B ...)` and a warning in the
      log; the member walks to B (no teleport). Again: a different post each time (A is not given back this session). On a
      follower: `is Following, not a resident at a post`.
- [ ] **The stuck line:** wedge a resident (block it into a 1x1 nook with a door it cannot open, or stand it in a pit):
      after 12 s `<name> was stuck (no progress for 12 s) - moved back to the post P [12 s counted: navPlanning N, navPath
      N, moveHelperFar N, blockedTime N, isStuck N; at x, y, z = d m from the post P, dy d, standing on <block>; Guard cell
      ... vs body cell ... (Guard: ...); mh active=T to ... (d m) blockedTime ... flags ... blockedBy -; nav path i/n end
      ..., planning=F, onGround=T, isStuck=F; uai Guard/UAITaskGuard target self; entity ticks +240; paused=F]`. Do it a
      third time within three minutes: instead of a teleport, `<name>'s post P cannot be used (3 moves back to the same
      post within 3 min ...) - new post Q`. Report any line whose signals look wrong, with the whole line.
- [ ] **Arms up while shooting, holding fire and backing off:** a pistol member and one zombie.
      (a) shooting at 8 m: `sc aim <member>`: `state: aiming`, `animator ... WeaponHoldType=1 WeaponCarry=5 (the hold
      type's 5)`; the gun is at the shoulder. (b) you step into its line (a resident defending, `ScDefendHold`): the header's
      `hold fire=N` rises, `state: aiming` - the gun stays up while it waits (before: down after 2 s, `calm`). (c) let the
      zombie come within 4 m (BackupKeepDistance): `back off=N` rises, `state: aiming` while it backs off facing the
      zombie (`facing away` while it walks with its back turned is expected). (d) `carryFixes=` in the header: 0 is fine;
      if it rises, note when (after a knockdown, a rebuild, a chunk reload) - that was the "arms down" bug. `sc anim
      <member>`: `WeaponCarry=5` with the pistol, 3 with the bat, never 0 with a weapon in hand.

**Deployed 2026-10-02** (DLL md5
`2d3b5aa3f7d69d370db51109bcfa5476`, 600064 bytes; Fable review, re-check and final confirmation: deployable; Fable review fixes M1-M6 and M8 and re-check fixes R1, R2, R4
included). One more Harmony patch method (Patch_HoldFireAcross) on targets SC already patches: still 70 targets. Deploy it **together with** the same tree's `Config/utilityai.xml`
and `Config/blocks.xml`: the new actions name the DLL's new `ScIntruder`, `ScCannotEngage` and `ScHoldFire`
considerations and `DefendHoldSC` task, and an XML without its DLL would drop them and let residents chase every enemy
around them. No save format change; an existing world is fine. Report: `M11FIX_REPORT.md` (next to `M11_REPORT.md`;
its "Review fixes" section covers `fable_review_m11fix.md`). Diagnosis: `X13_CHUNK_DIAG.md`, `GUARD_DIAG.md`.

What changed:
- **X13 really works on a chunk unload.** A chunk that unloads behind you is written a frame or more after the game has
  destroyed its NPCs' bodies, and the held-gun save asked Unity's "is it null", which says yes for a destroyed body - so
  every chunk unload skipped the record and the gun came back empty (one magazine fewer with L&L). Quit/continue was
  never affected. Now a plain reference test; the `ammo (D3)` line counts the records written for an unloaded chunk:
  `held guns saved N time(s) (unloaded M)`. The bandage-mid-use save (M11 re-check) now runs on unloads too.
- **Runners stay out of chunk saves.** The "never save a runner into a chunk" veto had the same defect and never fired
  on an unload; the Delete-source backstop hid it. Fixed the same way (the verdict for an unloaded body is fixed once,
  so the chunk's count and its entities always agree).
- **Defend the base (your decision).** A stationed member goes after any awake enemy inside the home zone within
  `DefendBaseRadius` (22 m, new key in `blocks.xml`) of its post, also one it cannot see, walks to it until it can shoot
  (in range, clear shot) or swing, fights it with the normal attacks and walks back. With a gun too (0-XNPCCore's ranged
  package had no approach for guns). Sleepers stay asleep, Cowardly members and followers, runners and stand-ins are
  not affected; `DefendBaseRadius` 0 turns it off. New AI actions `ScDefendBase` and `ScDefendHold` in `SCResident`
  (weight 4). After the Fable review:
  - a defence walk that takes the member more than 23 m from its post (a long detour to a door) pauses the defence for
    20 s - it walks home and tries again - instead of trading turns with the 25 m leash every 0.2 s;
  - with a gun in range but someone in its line of fire it no longer paces between its post and the intruder: with you
    in the line it stands still, facing the intruder, until you step aside (it never closes in on a fight across you);
    with another member or a friendly NPC in the line it closes in to about 6.5 m (which usually opens the line), then
    waits. A deer or a dead body in the line does not stop it. Shots are still only the pack's attack.
  - after the re-check: within about 4.5 m of its target a member never fires while you or a friendly NPC is first in
    its line - the NPC framework skips its sight check at that range, so SC checks the line itself before each shot
    (hook `hold-fire-across`). Farther out the framework re-checks the line five times a second (and briefly trusts a
    target it has just seen), so one shot as you step in can still come: it hits you for no damage and costs a round.
    Expected, not a bug: while it holds fire this way it keeps its gun level and doesn't swap to its melee weapon until
    the zombie gets round you or hits it. This applies to your followers too: in a close scrum with you in front, they
    aim and wait instead of firing past you.
- **Stuck watchdog:** a resident with a live enemy within 30 m gets three times `StuckSeconds` (36 s) before it is moved
  back to its post (`was stuck (no progress for N s, fighting)`); after such a move made during a fight the defence
  pauses for a minute. A plain wedge with no enemy about is cured in 12 s and the defence does not pause. A Cowardly
  member fleeing does not count as fighting.
- **`sc xnpc <id>` has a `uai:` line:** the AI's current action, task and target; for the foe (that target, else the
  attack/revenge target, else the nearest enemy) `OUTSIDE the AI's search box` when the AI cannot consider it at all
  (about 30 m each way around the NPC), `CanSee=`, `clearShot=` / `range=` / `InWeaponRange=` (what the
  ranged attack asks), what the see ray and the shot ray hit first (`target`, `block <name> at <pos>`, `PLAYER <id>`,
  `entity <id>`), how far you stand from the line (`(IN THE LINE)` under 0.6 m), the defence's verdict (`intruder`, or
  why not: `beyond DefendBaseRadius of the post`, `outside the home zone`, `asleep`, `not a resident at a post`,
  `resting (moved back to the post in a fight, or strayed)`), what it would do (`engage=approach (ScDefendBase)`,
  `engage=hold (ScDefendHold)` or `engage=hand over (the pack's attack)`, each with the reason, e.g. `out of range`,
  `no clear shot (a block in the line)`, `the player is in the line`, `the player is in the line (close)`, `an ally is
  in the line - closing in`, `an ally is in the line, within 6.5 m`, `an enemy, an animal or a body is in the line`)
  and the member's own distance from its post (`- BEYOND the 23 m stray limit` past it; with `ResidentRoamZone`,
  `- BEYOND the stray limit, the zone + 6 m` outside it, or `(zone roaming: the stray limit is the zone + 6 m)`).

- [ ] **Startup:** `patch verification: all 70 targets patched` (63 without Voice Control) - no new target; the
      `config:` line shows `DefendBaseRadius=22`; no `Type:... was missing` warning for `ScIntruder`, `ScCannotEngage`,
      `ScHoldFire` or `DefendHoldSC`, and no `AIPackage 'SCResident' not found`. `sc xnpc <member>` prints the `uai:`
      line.
- [ ] **Chunk cycle, re-test of the M11 row (X13):** a magazine member at home holding its gun with a part-used
      magazine seated (`remags npc <id>`: write down the held gun's Meta, `seated` and every magazine) and the `sc xnpc`
      ammo line's `held guns saved N time(s) (unloaded M)`. Walk (or fly) about 300 m away until it unloads (the status
      line's `Ent:` count drops; best with no `Saving N of chunks` autosave line in that minute), wait 5 s: `(unloaded M)`
      has gone up by one for each NPC holding a loaded gun (or mid-bandage) that unloaded - one holding its melee weapon
      writes nothing - and `held guns saved` by the same. Come back: `remags npc <id>`
      shows the same held gun with the same Meta and seated magazine, the same magazines in the bag - not one fewer;
      `back in the hand on load` +1; DebugLogging `<name> (id N) holds its saved gunX again, R round(s) in it`; and in
      the log for that NPC no `[SDCS-READY] ... event=preload` and no `[REMags] ... no magazine was seated` (the old
      bug's signature; X13_CHUNK_DIAG also expects its `[SDCS-WEAPON] ... t0` line to show the rounds rather than
      `meta=0` - note what it shows). Three cycles in a row. Variants: a
      loose-round pipe pistol member (the gun's rounds survive), a bow member with an arrow nocked, a console raider
      (`sc spawn hostile male T2 Rifle`) you leave and come back to.
- [ ] **Bandage on unload (the M11 re-check row, now through a real unload):** the "Saved while bandaging" row's
      walk-away variant: it comes back with the pistol in hand and both bandages; `medic-load-hand-fixed` +1 and
      `(unloaded M)` +1 for it.
- [ ] **A runner is not saved into its chunk:** send a member on a physical run and stay near it until it is out in the
      world; note the hooks line's `x13-held-gun-read=N` (`sc xnpc`). Walk away until its chunk unloads behind you
      (`<name> was captured as their chunk unloaded (Unloaded)`), then go back to that spot before the run brings it
      there again, where no other NPC was: `x13-held-gun-read` has not risen (nothing was read back, because nothing of
      the runner was written; before the fix a copy was written, read and thrown away), no second body, `sc runners`
      shows it offline and it carries on and comes home as usual.
- Note (round hz, 2026-10-12): with `ResidentRoamZone` on, the default now, the defence radius below is measured from
  the resident, not its post (`beyond DefendBaseRadius of the resident`), and the stray limit is 6 m outside the home
  zone instead of 23 m from the post. The 22 m / 23 m steps hold as written only with `ResidentRoamZone` false.
- [ ] **Defend the base, unseen:** a resident with a pistol at its post; a zombie (creative menu / `spawnentity`) inside
      the base, inside the land claim and within 22 m of its post, out of its sight (another room, a closed door
      between, another floor). Within a few seconds `sc xnpc <member>`: `uai: action=ScDefendBase
      task=UAITaskMoveToAttackTargetSDX ... target=<zombie id>` and `defend (radius 22 m): intruder, engage=approach
      (ScDefendBase) - no clear shot (a block in the line)` (or `out of range`). It walks there (opening doors), stops once
      it has a clear shot (`engage=hand over`) and shoots (`remags npc <id>`: the
      rounds go down), and after the kill walks back to its post (`action=Guard`, later `ScNightIdle` / `ScRoam`). The
      same with a melee member: it walks up and swings.
- [ ] **Defend: outside the zone or beyond 22 m - stays:** a zombie just outside the land claim, and one inside the
      claim but more than 22 m from the member's post: the `uai:` line says `outside the home zone` /
      `beyond DefendBaseRadius of the post`, and the member stays at its post unless the zombie comes into sight and
      range (then the old rules: it fights what comes close). A sleeping zombie in a POI base's sleeper volume: `asleep`,
      nobody goes to wake it. A Cowardly member: never `ScDefendBase`; it still flees from 15 m. A following member:
      never `ScDefendBase` (`not a resident at a post`).
- [ ] **Unreachable intruder:** a zombie inside the radius that cannot be reached (on a roof, in a pit, behind bars):
      the member walks as close as it can; at most about 36 s later `<name> was stuck (no progress for 36 s, fighting) -
      moved back to the post`, then the `uai:` line shows `resting (moved back to the post in a fight, or strayed) (rests
      N s more)` and it stays at its post for a minute (it still shoots the zombie if it can see it), then tries again.
      Control: a member wedged in furniture with no enemy within 30 m is moved back after 12 s (`no progress for 12 s`,
      no `, fighting`) and the `uai:` line shows no `rests ...` - it defends at once if an intruder appears.
- [ ] **Long detour - no oscillation (review M1):** a walled yard inside the claim; the member's post on one side of a
      wall, a zombie about 15-20 m from the post on the other side, and the only way round a door that swings the path
      more than 23 m from the post (the `uai:` line's `N m from its post` while it walks). Expect: it walks out; when it
      passes 23 m the `uai:` line shows `BEYOND the 23 m stray limit` and then `resting (...)`, it turns and walks back
      towards its post (`action=Guard`), and about 20 s later tries again - a slow cycle, never a back-and-forth every
      fraction of a second at the 25 m mark. If the zombie comes round into sight, it is fought as usual.
- [ ] **Someone in the line of fire (review M2):** two pistol residents and one zombie inside the base, the residents one
      behind the other in line with the zombie (the rear one 10-15 m from it), no wall between. The rear one's `uai:`
      line: `seeRay=entity <front member id>`, `engage=approach (ScDefendBase) - an ally is in the line - closing in`: it
      walks forward (it does not walk back to its post), and as soon as it has a clear line (`CanSee=True`, `engage=hand
      over`) it shoots; if it gets to about 6.5 m still blocked it stands (`action=ScDefendHold`, `an ally is in the line,
      within 6.5 m`) until the line clears. What you should see: the rear one stops 6.5 m or more from the zombie, does
      not fire into the front one's back, and fires when the line opens. Then you step into the line between a pistol
      resident and the zombie, first at 8-10 m: `engage=hold (ScDefendHold) - the player is in the line`; it stops
      shooting - at most one shot as you step in (the framework re-checks the line five times a second and briefly
      trusts what it just saw: a hit on you does no damage) - stands still facing the zombie, does not walk closer or
      home, and shoots again once you step aside. Then the close case: let the zombie come to 3-4.5 m of the resident
      (a doorway fight) and step in between: no shot at all while you are in the line (hooks `hold-fire-across` rises
      when it had the zombie as its attack target); it backs off to 4 m or stands (`ScDefendHold`, `the player is in
      the line (close)`, or `RangedMeleeAttack` aiming without firing if the zombie has hit it); under 3 m it swaps to
      its melee weapon as before. Report any shot fired while you stand in the line within 4.5 m with the `uai:` line.
- [ ] **The watchdog leaves a fight alone:** a resident fighting a zombie the player is also fighting (it pushes against
      you or a wall): no `was stuck (no progress for 12 s)` while it has the zombie as a target. A real wedge still
      ends with a move back, after 36 s with `, fighting` in the line.
- [ ] **Blood moon sanity:** a blood moon at the base with several residents. Expect: they engage zombies that get inside
      the claim within 22 m of their posts, and walk back to their posts afterwards; no runaway chases past 25 m (the
      leash); frame rate as before; no `watchdog`, `console-uai` or other SC errors. Watch whether residents opening
      doors to fight zombies outside the walls (but inside the claim) lets zombies in - the doors shut 2 s behind them.
      If the night goes badly, `DefendBaseRadius` 0 turns the defence off; note what happened.
- [ ] **"Never fired" control test (GUARD_DIAG cause 3):** a pistol resident and one zombie standing in the open about 8 m
      from it, you off to the side (not between them). `sc xnpc <member>` while it faces the zombie. Expected:
      `action=RangedMeleeAttack`, `CanSee=True clearShot=True ... InWeaponRange=True seeRay=target shotRay=target`,
      `player ... not between them` or a distance well over 0.6 m, and the rounds go down. If it does not shoot, copy the
      whole `uai:` line: `CanSee=False` with `seeRay=...`, or `clearShot=False` with `shotRay=block <name> ...`, names the
      cause; `action=RangedMeleeAttack` with no shots points at the attack task itself (`sc aim <member>`'s hook counts).
      Then step into the line between them: `seeRay=PLAYER <id>`, `player ... (IN THE LINE)`, `CanSee=False`, and it
      stops shooting (at most one shot as you step in, which does you no damage); if the zombie is an intruder (inside
      the claim, within 22 m of its post) it holds still (`engage=hold (ScDefendHold) - the player is in the line`)
      rather than walking off.

## M11: Locked & Loaded NPC magazines

**Deployed 2026-10-02** (DLL md5 `56614e13b1f1a5d5468ef2b2c89665f4`, 587776 bytes; Fable review + two re-checks, all findings fixed).
Needs Locked & Loaded 1.4.0 (`zzzz_2_LockedAndLoaded`, commit 4d18bb2) for the magazine rows;
without it everything is as in round 3. No Config change but comments in `blocks.xml`; an existing world is fine (see
the old-save rows). Report: `_research\xnpc_migration\migrate\M11_REPORT.md`; L&L's side: `LnL_NPC_Magazines_API.md`.

What changed:
- **Magazines at spawn.** With L&L loaded, a new survivor's or raider's gun that takes magazines gets L&L's loaded
  magazines for its class tier (T1 2, T2 3, T3 4; extended or drum chances at T2/T3) and **no loose stack**. Armed
  inside PostInit, before 0-XNPCCore's first reload (`[SDCS-READY] ... event=preload`), so no extra "found" magazine.
  Bows, crossbows and guns without magazines keep `XnpcSpawnAmmo` / `RaiderSpawnAmmo` / the arrows; so does everything
  without L&L. Once per body (the same `sc_ammoSeeded` mark): a saved body is never armed again.
- **Rounds** (`sc raiders`, `sc xnpc <id>`, the resupply threshold, the overwhelmed and lead rules) count the loose
  rounds plus L&L's (its magazines and the gun's own count).
- **Resupply** (members, at home / outposts, as before): into the magazines with L&L's `Refill`; the stockpile pays only
  for the rounds that went in; full magazines take nothing and cost nothing. A member whose gun takes magazines but
  who has none (an old save's, or a gun given through the weapon menu) is **issued** its tier's count of the gun's
  standard magazine, empty, the first time it is there with that gun; its own loose rounds go in, the stockpile fills
  the rest.
- **Loose rounds into magazines:** loose rounds of the gun's ammo in a body's bag (handed over through the window, an
  old save's) go into its magazines whenever they have room - every 5 s for members, on a remote window's close, and
  once when any body loads. Never while its window is open; exactly what the magazines take leaves the bag.
- **Member record:** the registry's `ammo=` value now also carries L&L's text of every magazine
  (`...,@mags:<escaped>`); a re-created body (call-back, stand-in, runner) gets the same magazines back. Old records
  load unchanged; a magazine L&L no longer knows (or every one, without L&L) comes back as loose rounds.
- **X12, a gun put away keeps its rounds:** SCore's weapon swap (`EntityAliveSDXV4.UpdateWeapon(ItemValue, bool)`, the
  close-melee swap, the dry failover, the re-arm) gives the rounds loaded in the gun it writes over back to the bag as
  loose rounds - loose-round guns and bows (a nocked arrow); L&L gives back its own seated magazine first, so nothing
  comes back twice. New target.
- **Death:** the rounds loaded in the gun an NPC holds when it dies go into its bag first, so they drop in the backpack
  (a raider's dropped gun copy is then empty).
- **Bandage fix:** a survivor bandaging while L&L has a magazine seated in its gun no longer gets that magazine twice
  (L&L puts it in the bag when the bandage goes into the hand; SC used to put back a copy of the loaded gun).
- **Review fixes (`fable_review_m11.md`):**
  - **F1, X13 - the gun in the hand survives a save.** SCore saves only the held item's name and the game keeps the
    rest of an NPC's hand nowhere, so every load - each time a body's chunk unloaded behind you and came back, not only
    quit/continue - gave it an empty gun, and with L&L one magazine fewer. SC now writes the held gun (rounds, seated
    magazine) after SCore's own save data and puts it back in the hand when the body loads. New target
    `EntityAliveSDXV4::Read` (the `Write` half shares an existing target): **70 / 63**.
  - **F2:** after an L&L error switches the magazines off, the member record keeps the magazines it last could read
    (it used to erase them within 5 s). Not testable without making L&L throw.
  - **F3:** `sc loadout <name> <gun>` on a body that already has magazines for that gun gives no second set.
  - **F4:** a member is issued magazines only when its bag has room for them and its gun's rounds.
  - **Open item 2:** magazines for the member's gun handed over through its window move into its bag, where L&L uses
    them.
- **Re-check fixes (`fable_review_m11.md`, second pass):**
  - **Saved while bandaging:** a body saved in the middle of a bandage or kit (its chunk unloading, or the game) came
    back holding the bandage as its weapon, with the bandage it was using gone and its gun's loaded rounds lost. Now
    the save keeps the gun the use took out of the hand and the item in use, and the load puts the item back in its
    inventory and the gun - loaded as it was - back in its hand. Dying while bandaging puts the item in its window and
    the gun's rounds in its bag, so both drop.
  - **F4:** a member short of bag room for an issue is not emptied and made to reload every 5 s.
  - **R1:** a runner coming back from its snapshot loses nothing that was in its window or its saved gun.
- Console: `sc raiders` `rounds N (K magazine(s) holding N)`; `sc xnpc <id>` resupply line
  `gunX N round(s) (L loose, K magazine(s) holding M)` and `record ammo=... magazines=<L&L text> (R round(s))`; its
  `ammo:` line `Locked & Loaded: K magazine(s) holding M with the gun's own` (or `no magazines`); the `sc xnpc` ammo
  line `magazines (Locked & Loaded): on - armed ... issued ... refill(s) ... time(s) full ... loose round(s) loaded;
  guns put away gave back N round(s) in K swap(s), dead NPCs' guns N; held guns saved N time(s), back in the hand on
  load N, given back loose N round(s)`; hooks `x12-swap-loaded-gun`, `x13-held-gun-read`, `mags-load-held`,
  `mags-window-to-bag`, `mags-issue-bag-full`, `medic-load-hand-fixed`, `medic-died-mid-use`; `sc weapons` ends with `Locked & Loaded magazines: on - ... (T1/T2/T3:
  2/3/4)`.

L&L's console (`remags npc <id>`: the held item, Meta, the seated magazine, `usesMagazines`, rounds, every magazine and
any loose rounds) is the ground truth below. `DebugLogging` true in `blocks.xml` for SC's `starting magazines ...`,
`loaded ... loose round(s) ...` and `... gave back ...` lines.

- [ ] **Startup:** `patch verification: all 70 targets patched` (63 without Voice Control), no `MISSING PATCH`; the
      line `NPC magazines: Locked & Loaded's NPC API bound - ...` once per game (no `could not be bound`, no `threw`).
      `sc weapons` ends with `Locked & Loaded magazines: on - ... (T1/T2/T3: 2/3/4)`.
- [ ] **Raider arming per tier:** `sc spawn hostile male T1 Pistol ranged-lead` (id printed). In `Player.log` L&L's
      `[REMags] armed ... (tier 1) for gunHandgunT1Pistol: magPistol9mm, magPistol9mm - 30 round(s)` comes BEFORE that
      raider's `[SDCS-READY] ... event=preload`. `remags npc <id>`: 2 magazines, 30 rounds, no loose `ammo9mmBulletBall`;
      after its first reload still 2 magazines (one seated, one in the bag) - never 3. `sc raiders`:
      `rounds 30 (2 magazine(s) holding 30)`. Repeat with `T2 Pistol` (3 magazines, now and then an extended one) and
      `T3 Rifle` (the AK: 4 magazines, now and then a drum).
- [ ] **Survivor arming per tier:** `sc spawn male T2 Pistol` (friendly) and `sc spawn female T3 Rifle`: 3 and 4
      magazines, no loose stack; `sc xnpc <id>` resupply line `gunHandgunT1Pistol 45 round(s) (0 loose, 3 magazine(s)
      holding 45)` (more with an extended one); the `ammo (D3)` line's `armed N survivor(s) and M raider(s)` counts them
      and `starting stacks` does not.
- [ ] **Bows and guns without magazines unchanged:** `sc spawn hostile male T1 LBow` - 15 arrows loose in the bag
      (`remags npc <id>`: no magazines); `sc spawn hostile male T1 PipePistol` - 24 loose rounds; `sc spawn male T1
      DoubleBarrel` - 30 loose shells. They reload and fire as before.
- [ ] **Chunk cycle keeps the gun loaded (F1):** a magazine member at home holding its gun with a part-used magazine
      seated (let it fire a few; `remags npc <id>`: the held gun's Meta, `seated`, every magazine - write them down).
      Walk (or fly) about 300 m away until it unloads, wait a minute, come back: `remags npc <id>` shows the same held
      gun with the same Meta and seated magazine and the same magazines in the bag - not one fewer; the `ammo (D3)`
      line's `held guns saved` and `back in the hand on load` have risen; DebugLogging `<name> (id N) holds its saved
      gunX again, R round(s) in it`. Twice more: still the same count. The same with a loose-round member (a pipe pistol:
      the gun's rounds survive), a bow member with an arrow nocked (it still has it, and shoots it), and a console raider
      (`sc spawn hostile male T2 Rifle`) that you leave and come back to.
- [ ] **Quit and continue keeps it too (F1):** the same notes, then save, quit to the menu, continue: the same held gun,
      Meta, seated magazine and magazines; `sc xnpc <id>` `ammoSeeded=True` and no new magazines (never re-armed).
- [ ] **Saved while bandaging (re-check):** a member with a pipe pistol (loose rounds; note `remags npc <id>`: the
      gun's Meta) and 2 first aid bandages in its window: hurt it below 60% (a zombie, or `CommunityFriendlyFire` on and a hit) and the moment
      it raises the bandage walk or fly 300 m away so its chunk unloads mid-bandage, then come back. It holds its pipe
      pistol, not a bandage; the pistol has the same Meta as before; its window has 2 bandages again (the one it was
      using came back, unused: count before the hurt = count now); the hooks line `medic-load-hand-fixed` +1. It then
      bandages normally. The same with quit to the menu instead of walking away (press Esc while it bandages), and with
      a magazine gun (a Pistol member: the magazine stays in its bag; after the load `remags npc <id>` shows the same
      magazines and it re-arms its gun by itself). Killed while bandaging (`kill <id>` during the animation): its
      backpack holds the bandage it was using and the pistol's rounds; `medic-died-mid-use` +1.
- [ ] **Without L&L (optional, a COPY of a test world - L&L's magazine items vanish from a world loaded without it):**
      move `zzzz_2_LockedAndLoaded` out of `Mods`: the log says `NPC magazines: Locked & Loaded is not loaded`; `sc
      spawn hostile male T1 Pistol` gets 24 loose rounds; `sc weapons` says `Locked & Loaded magazines: off (Locked &
      Loaded is not loaded)`. A member whose record has magazines (`sc xnpc <id>` `magazines=...` before the move),
      re-created (post them at an outpost and go there): the stand-in has those magazines' rounds loose in its bag.
- [ ] **Resupply at home:** recruit a Pistol survivor (`sc spawn male T1 Pistol`, `sc recruit <id>`), `sc give ammo 2`, at
      home let it empty its magazines on zombies until it is under 10 rounds. Within 5 s (60 s cooldown): `<name> (id
      N) resupplied at home: 30 ammo9mmBulletBall into their gunHandgunT1Pistol magazines at P point(s) each`;
      `remags npc <id>` shows 30 more rounds in its magazines and no loose 9 mm; the `ammo (D3)` line's
      `refill(s) of ... round(s)` rises by 30 and the open bundle's points fall by exactly 30 x P (or a bundle opens
      and the rest are left). With magazines that can take only, say, 12: exactly 12 go in and only 12 x P is charged.
- [ ] **Full magazines cost nothing:** set `XnpcResupplyBelow` to 100 in `blocks.xml`, reload the world, the Pistol
      member at home with both magazines full: no resupply line, `time(s) full` rises once a minute, the stockpile and
      the open bundle's points do not move (DebugLogging: `... magazines are full - nothing to resupply`). Set it back.
- [ ] **Loose rounds handed over:** give a magazine member with room in its magazines (some fired) 20 `ammo9mmBulletBall`
      through its inventory window and close: within 5 s `remags npc <id>` shows them in the magazines (the window and
      the bag hold none; `loose round(s) loaded` rises; DebugLogging `loaded N loose round(s) ...`). With its magazines
      full: the rounds wait loose in its bag (`remags npc` loose rounds 20) and go in as soon as a magazine has room.
      With the window held open on your screen nothing moves (`mags-load-held` rises in the hooks line); close it and it
      does. Total rounds (`sc xnpc <id>`) never change by more than what it fires.
- [ ] **Magazines handed over through the window (open item 2):** give a Pistol member two `magPistol9mm` (one full,
      one empty) through its inventory window and close it: within 5 s they leave the window and `remags npc <id>`
      lists them in the bag (the empty one filled from its loose 9 mm, if it has any); `mags-window-to-bag` rises.
      Magazines for another gun (an AK magazine) stay in the window. With the window held open nothing moves. A member
      that had no magazines yet and holds a gun loaded from loose rounds: its gun's rounds go back to the bag first, and
      after its next reload it has exactly the handed-over magazines, not one more.
- [ ] **`sc loadout` gives no second set (F3):** `sc spawn hostile male T1 Pistol`, note `remags npc <id>` (2 magazines),
      then `sc loadout <id> Pistol`: still 2 magazines (and no `[REMags] armed` line). `sc loadout <id> Rifle`: the AK
      gets its tier's set (the 9 mm magazines stay in the bag, unused).
- [ ] **Issue to a member without magazines:** hand a member with a bow a `gunHandgunT1Pistol` through its window and
      pick it in the dialog's weapon menu ("Use ...") at home: within 5 s `<name> (id N) was issued 2 empty magPistol9mm
      for their gunHandgunT1Pistol at home ...` (T1 class; 3 / 4 for T2 / T3); `remags npc <id>` shows them; with
      `sc give ammo 1` it is resupplied into them within a minute. Only once per member and gun this game. (An old-save
      member with loose 9 mm: the same line ends `and loaded N of their own loose rounds into them`.)
- [ ] **Re-created member keeps exactly its magazines:** a magazine member: `sc xnpc <id>` resupply line ends
      `magazines=magPistol9mm:ammo9mmBulletBall:15;... (R round(s))`. Post it at an outpost and go there: the stand-in's
      `remags npc <stand-in>` shows the same magazines with the same rounds (the seated one comes back as a magazine
      item; its first reload seats it) and no fresh tier set; DebugLogging `re-created body N: ... K magazine(s)
      holding R, no starting stack`. Same with `sc restore <member>` on a missing one and a distress stand-in.
      Registry file: the member's `ammo=` line ends `,@mags:magPistol9mm%3Aammo9mmBulletBall%3A15...`.
- [ ] **The swap fix, loose-round gun:** `sc spawn hostile male T1 PipePistol ranged-lead` (no magazines), let it fire a
      few, then walk up to it so it swaps to melee (`[SDCS-CLOSEMELEE] ... enter`): `remags npc <id>` (or `sc xnpc
      <id>` ammo line on the gun later) shows the rounds that were in the gun back as loose rounds in the bag - total
      rounds unchanged by the swap; hooks `x12-swap-loaded-gun` +1, `guns put away gave back N round(s)` rises;
      DebugLogging `weapon swap: ...'s gunHandgunT0PipePistol gave back N ammo9mmBulletBall into the bag`. Back at range
      it re-arms the gun and reloads from them. A bow: a nocked arrow comes back the same way.
- [ ] **The swap with L&L magazines (no double return):** the T1 Pistol raider: walk up to it after a few shots: L&L's
      `[REMags] weapon swap: ... gave back its magPistol9mm holding N` and NO SC `gave back` line for that swap;
      `remags npc <id>`: the magazine count unchanged and no loose 9 mm.
- [ ] **Death drop holds the magazines:** kill a magazine raider (`kill <id>`, or zombies): its backpack holds its
      magazines (the seated one included, with its rounds) and nothing else of them is lost; a loose-round raider's
      backpack holds the rounds that were in its gun as loose rounds (its dropped gun, if rolled, is empty). A member
      the same, with its window's items.
- [ ] **Bandage with a seated magazine:** hurt a magazine survivor (a stranger is fine) below 60% with bandages in its
      window: when it bandages, L&L logs `[REMags] weapon swap: ... gave back its magPistol9mm holding N`; after the
      bandage the gun goes back empty and reloads from the bag: `remags npc <id>` shows the same number of magazines as
      before the bandage, not one more.
- [ ] **Regression:** raiders and survivors with bows still shoot and resupply as before; `sc xnpc` shows no
      `ammo-load-loose`, `mags-`, `xnpc-swap-rounds` errors; a member with a pipe gun is resupplied with loose rounds as
      before.

## M10 round 3: the remote-window guard, turrets, clear-sleeper markers and kill XP for raiders

**Deployed 2026-10-02** (DLL md5 `87350de60369fff5de5323875b1d88dc`, 563712 bytes; Fable review fixes F1-F4 included,
re-check: no defects). Config changes: `blocks.xml` (the `RaiderTurretsAsEnemies` key) and the generated
`entityclasses.xml` (the raider templates' `NavObject` and kill XP passive). A world made with round 2 is fine (raiders pick up the XP and the marker when they next load).
Design: DESIGN_M10_RAIDERS.md sections 5, 1.1 ("Player turrets", "NavObject"), 2.9 and 3.12; review
`fable_review_m10r3.md`.

What changed:
- **A remote player's open NPC inventory window holds things still** (design section 5; dedicated server, or another
  player on a listen server). SCore sends such a player a copy of the window and, when they close it, writes their copy
  back over every slot. Until now the host went on moving ammo out of the window into the NPC's bag (and the NPC on
  using bandages from it) while they had it open, so the close brought those items back: duplicated. Now the server
  marks the window when the request comes in and lets go when the close comes in (then moves the ammo left in it at
  once), when the player disconnects, or 10 minutes after the open if no close ever comes. While it is marked nothing
  is moved out of it, ScMedic neither uses nor stocks anything in it, and a weapon handed over through the dialog's
  weapon menu (or Voice Control's "use") is not kept in the pair (review F2). The last two also wait while the host has
  the window open on their own screen (SC's writes to a window never refreshed an open grid). Two new targets: SCore's
  `NetPackageHarvestInventoryRequest::ProcessPackage` and `NetPackageHarvestInventoryUpdate::ProcessPackage`.
  Note: on a dedicated server SC's once-a-second ticker does not run (it needs a local player), so the members' 5 s
  window-to-bag move only exists in single player and on a listen server; there the guard's work is the medical hold,
  the swap hold and the move on close.
- **An NPC that dies while its window is open** (review F1): its items no longer drop while the same items can still
  be dragged out of the open grid. The host's own grid (single player, listen host) is closed at the death, and the
  items go into the one death backpack. A remote player's grid cannot be closed from the server: the bag and the
  weapons drop at once, the window stays, and when its last holder closes it (or disconnects, or the 10 minutes run
  out, or the world shuts down) what is left drops in a second backpack at the same spot. A copy closed after the
  window has already dropped is thrown away, so no container is left behind for the dead id.
- **Turrets shoot raiders, motion sensors trip on them** (`RaiderTurretsAsEnemies`, default true; X10): the powered
  auto turret with "target zombies" on and the junk turret (and junk sledge) with "target enemies" on shoot raiders, as
  they shot the old raiders, and a motion sensor with "target zombies" on trips on them (review F4). With the key
  false, only "target strangers" makes them react to raiders (vanilla's and SCore's rule, which stays in both cases).
  Survivors, members, traders and 0-XNPCCore's own NPCs are never affected. Three new targets: the turrets' and the
  sensor's `shouldIgnoreTarget`.
- **Clear-sleeper markers** (X11): raiders get the old raiders' `NavObject` (`twitch_spawn,twitch_spawn_other,
  clear_sleeper`), registered the vanilla way (SCore's V4 registers a marker only for a companion), once per raider
  (review F3). A sleeper raider inside your clear quest's area shows the red dot, like a zombie; a wild one shows
  nothing. New target: `EntityAliveSDXV4::HandleNavObject`.
- **Kill XP from the pair** (design 2.9): 600, raised by the raider's ranged weapon - a bow or crossbow +33% (tier 0,
  798) or +67% (tier 1-2, 1002), a firearm +50% / +100% / +133% (tier 0 / 1 / 2: 900 / 1200 / 1398). Written as the
  cvar `sc_raiderXp` on every load from the pair (saved with the raider); the raider classes' `ExperienceGain perc_add
  @sc_raiderXp` reads it. The lead does not count (a melee-lead raider still has its gun).
- Console: `sc raiders` shows `killXp <n> (sc_raiderXp <bonus>)` and `marker on|off (sleeper)`; `sc xnpc <id>` ends its
  `resupply:` line with `window open remotely: none | player <id> for <n> s`; the `sc xnpc` ammo line ends with
  `inventory windows open by remote players now <n>, dead NPCs' windows waiting for them to close <n>`; the hooks line
  has `x10-turret-raider-target`, `x10-sensor-raider-target`, `x11-raider-navobject` and `window-remote-open` /
  `-close` / `-expired` / `-disconnect`, `window-move-held`, `window-medic-held`, `window-swap-held`,
  `window-death-host-closed`, `window-death-held`, `window-death-released`, `window-dead-orphan-removed` (each counted
  once it happens).

- [ ] **Startup:** `patch verification: all 68 targets patched` (61 without Voice Control), no `MISSING PATCH`, no
      `... not found (game or SCore update?)` / `... not found (SCore update?)` line; the config line ends with
      `vanillaDoors=False turretsAsEnemies=True gearSets=9`. On the dedicated server's log too (P15).
- [ ] **P10 kill XP:** `sc spawn hostile male T1 PrimitiveBow club melee-lead` and `sc spawn hostile male T3 Magnum club
      melee-lead`. `sc raiders`: `killXp 798 (sc_raiderXp 0.33)` and `killXp 1398 (sc_raiderXp 1.33)`. Kill both with the
      same weapon: the XP popups say 798 and 1398 (times your world's XP multiplier; a perk that scales kill XP scales
      both alike). Spot-check the others the same way: `LBow` or `CompoundBow` 1002, `PipePistol` 900, `DoubleBarrel`
      1200. `sc loadout <id> Magnum` on the bow raider: its `killXp` turns 1398 at once. Save and reload with a console
      raider alive: the same `killXp`. A raider that bleeds to death after your hit (a bladed weapon) pays the same as a
      direct kill. A wild survivor you kill pays 0-XNPCCore's 500 as before.
- [ ] **P13 turrets, key on (default):** place a junk turret and a powered auto turret. Set "target strangers" OFF and
      "target enemies" (junk) / "target zombies" (auto) ON. `sc spawn hostile male T1 club melee-lead` in front of each:
      the turret shoots the raider until it dies; `sc xnpc` hooks line `x10-turret-raider-target` rises. Behind a turret
      (outside its cone) or dead: not shot. A wild survivor (`sc spawn`), a member, a vanilla trader and one of
      0-XNPCCore's own NPCs (creative menu `npcSdcsTest`) in front of the same turret: never shot, and `x10` does not
      move. Enemies / zombies OFF and strangers OFF: the raider is not shot. Strangers ON (either key value): shot
      (SCore's rule). Optional: a junk sledge with "target enemies" on hits a raider that walks up to it.
- [ ] **Motion sensor (F4), key on:** a powered motion sensor wired to a light (or a spike/dart trap), "target zombies"
      ON, "target strangers" OFF. A raider walking into its cone trips it (the light comes on);
      `x10-sensor-raider-target` rises. A wild survivor or a member in the cone does not trip it. Zombies OFF and
      strangers OFF: the raider does not trip it either.
- [ ] **P13 turrets and sensor, key off:** `RaiderTurretsAsEnemies` false in `Config/blocks.xml`, load the world again
      (the config line says `turretsAsEnemies=False`): with strangers OFF and enemies / zombies ON a raider is NOT shot,
      does not trip the sensor, and both `x10` counters stay 0; with strangers ON it is shot and trips it.
- [ ] **Clear-sleeper markers:** `sc raiders` shows `marker on` for every live raider, and the `sc xnpc` hooks line's
      `x11-raider-navobject` counts one per raider loaded this session (never two for one raider); no error when a
      raider dies or its chunk unloads. A follower you hired the vanilla way still has its cyan companion marker; a wild
      survivor still has none. The red dot itself needs a sleeper raider inside a clear quest's area: no installed POI
      spawns NPC sleepers today (W0 swaps raiders into the framework's `S_-Group_NPC_*` volumes, which only an NPC POI
      pack uses), so check it when such a POI is available - its sleeper raiders show the dot like the zombies and lose
      it when they die.
- [ ] **F1, the host's own window (single player or the listen host):** open a member's inventory window that holds,
      say, 2 bandages and 20 arrows, and kill the member with the window still open (`kill <id>` in the F1 console over
      the window, or let zombies at it while you watch the grid): the window closes at once; one backpack at the body
      holds the window's items and the bag's; nothing is left to drag out. `sc xnpc` hooks: `window-death-host-closed`
      +1, `window-death-held` 0. Hitting the corpse again drops nothing more.
- [ ] **F1, a remote player's window (dedicated server or listen client; see P15 for the setup):** the client opens the
      survivor's window (2 bandages, 20 arrows in it) and keeps it open; kill it from the server console
      (`dedi_cmd.ps1 -Command "kill <id>"`). The client's grid stays open (it is a copy); a first backpack drops at the
      body with the bag's contents only (its ammo); the server logs `<name> (id <id>) died with their
      inventory window open - its contents drop at the body once it is closed`, `window-death-held` +1, and the `sc xnpc`
      ammo line says `dead NPCs' windows waiting for them to close 1`. Take one bandage out of the grid, then close: a
      second backpack drops at the same spot with the other bandage and the 20 arrows - never the bandage you took;
      `window-death-released` +1, `... waiting ... 0`, log `... the inventory window left open at their death was
      closed - 2 stack(s) dropped at the body`. Hitting the corpse again drops nothing.
      Variants: kill it, then quit the client: the second backpack drops at once (`window-remote-disconnect` +1). Kill
      it and keep the grid open over 10 minutes: the window drops within a second of the mark expiring
      (`window-remote-expired`); closing afterwards drops nothing more and leaves no container behind
      (`window-dead-orphan-removed` +1) - only what you take from the still-open grid after those 10 minutes is a
      duplicate (the accepted trade-off of the 10-minute rule).
- [ ] **P15 remote window guard (dedicated server, client + server):** the server's `Mods` needs the same 0-SCore,
      0-XNPCCore and SurvivorCommunity (this build) as the client (it holds only the Grid Inventory test set today);
      a fresh `GameName`; `DebugLogging` true in SC's `blocks.xml` on the server for the `remote window:` lines. Server
      console: `dedi_cmd.ps1 -Command "sc xnpc"` / `"sc xnpc <id>"` (`le` lists entity ids). Use one of the community's
      0-XNPCCore survivors whose dialog offers its inventory (hire one the vanilla way, or a member if you have one):
      - Open its inventory and keep it open: the server's `sc xnpc <id>` says `window open remotely: player <your id> for
        <n> s`, the hooks line `window-remote-open=1`, the log `remote window: player ... opened NPC ...'s inventory`.
      - Medical hold: leave exactly 2 first aid bandages in its window (no kit) and close. Hurt it hard, to about a third
        of its health (a heavy hit or a zombie): it uses one bandage, which is not enough to lift it above 60%. Open its
        window right away and keep it open 30 s: it does NOT use the second one, even after the first one's healing has
        run out (about 15 s; `sc xnpc <id>` medic line `idle`, `bandages=1`; `window-medic-held` rising). Close: within
        about 2 s it uses the second. Open again: no bandage left - not 1 (1 would be the duplicate).
      - Ammo on close: put 20 rounds of its ranged weapon's ammo (stone arrows for a bow) in its window and close: at
        once `window-remote-close` +1 and the `ammo:` line of `sc xnpc <id>` (while it holds that weapon) shows them
        under `bag`, `window 0`; reopen: they are not in the window.
      - Disconnect: open its window and quit the game (Task Manager end-task, or `taskkill /F` for a real crash): the
        server's hooks line `window-remote-disconnect` +1, `sc xnpc <id>` says `window open remotely: none`, and
        `window-medic-held` stops rising. (What you took out of the window before a crash is SCore's business: it never
        got your copy back.)
      - Optional, long: leave a window open over 10 minutes: the server logs `... never closed it - taken as closed`
        (`window-remote-expired`) and it bandages again (only then could a later close bring an item back).
- [ ] **P15 on a listen server (host + one client):** the same rows with the host as the server and the second player
      as the remote one - a member is easy there (`sc recruit` on the host). The host's own opens are never counted
      (`window-remote-open` stays 0 for them; they use the window's own open flag, as before). Members only, which
      needs the host's ticker: with the client holding the member's window open and arrows in it, wait 15 s - nothing
      moves (`window-move-held` rising every 5 s); the client takes 5 out and closes: the other 15 go to its bag at
      once, the client has exactly the 5, and reopening shows none.
- [ ] **F2, the weapon menu under a remote open (listen server: host + one client):** the client opens a member's
      window and puts a weapon from `sc weapons` in it; while the client keeps the window open, the host picks that
      weapon in the member's dialog weapon menu ("Use ..."): the host's log says `<name>: could not keep the handed
      <weapon> (someone has their inventory window open - try again once it is closed)`, `window-swap-held` +1, the
      member just holds it, nothing comes back to you, and `sc loadout <name>` is unchanged. After the client closes,
      the same pick keeps the weapon and returns the old one as before.
- [ ] **Host's own open (single player):** the P15 medical-hold steps on your own screen: 2 first aid bandages in a
      member's window, hurt it to about a third of its health, it uses one; open its window right away and keep it
      open 30 s: it does not use the second (`window-medic-held` rising; the grid shows 1 and that is the truth);
      close: it uses it within about 2 s; reopen: none left.
- [ ] **Regression:** single player: `window-remote-*`, `window-move-held`, `window-swap-held` and `window-death-held`
      stay 0; a member's window -> bag move after you close its window, a survivor's bandage from its window (window
      closed) and the weapon menu work as before; a member who dies with nobody looking drops one backpack (window and
      bag) as before; no talk prompt on raiders (X9); raiders still fight, drop their backpack and count in `sc xnpc`
      (round 1's P1/P8 in brief).

## M10 round 2: the old player-body shim removed, no talk prompt on raiders

**Deployed 2026-10-01** (DLL md5 `6ec3b3ae915059de861c6db6fba0576b`; Fable review: no defects, a doc fix applied). **Needs a new game**, as round 1.
Design: DESIGN_M10_RAIDERS.md section 4 (the talk prompt, section 1.1 "Trader activation", was brought forward from round 3).

What changed:
- **Patches_Sdcs** keeps only the body fixes our XNPC bodies (survivors and raiders) use: hit colliders, eyes and the gaze
  safety net, the asset release on despawn, the jump trigger, the get-up (`Patch_RagdollAsPlayer`, `Patch_RagdollPin`)
  and the physics-height guard. The shim for SC's own player-body classes is gone (the model's Init, mesh build and
  SwitchModelAndView, skin texture, equipment refresh, hold-type mapping, carry and animator speed, motion gate, path
  direction, fade-in, pitch, swing speed, avatar switch), and `Patch_RaiderPostInit` with it. ScLooks, ScAim and ScMedic
  lose their halves for those classes. 15 Harmony targets fewer.
- **The old NPC weapon items** (`meleeNPCSC*` / `gunNPCSC*`, 19) and their localization rows are gone.
- **No talk prompt on raiders** (X9, new target `EntityTrader::AllowActivationCommand`): every activation command is
  refused for a raider body, so no "Press E to talk", no radial and nothing on E.
- The old-save warning (M9) says that raiders saved before M10 are gone too.

- [ ] **Startup:** `patch verification: all 62 targets patched` (55 without Voice Control; 68 / 61 since round 3), no
      `MISSING PATCH`, no `patched an unlisted method`, no `EntityTrader.AllowActivationCommand ... not found`.
- [ ] **Talk prompt, raider:** look at a raider up close: no "Press E to talk to <name> the Raider", and E does nothing
      (no radial, no "sworn enemy" tooltip). `sc xnpc`: the hooks line's `x9-raider-talk-refused` counts up while you
      look at one. Same with the debug menu on (vanilla's debug "remove" command no longer keeps the prompt alive).
- [ ] **Talk prompt, everyone else:** look at a wild survivor (`sc spawn`), a member, a vanilla trader and one of
      0-XNPCCore's own NPCs (creative menu, e.g. `npcSdcsTest`): the prompt is still there and E works as before
      (dialog / radial). `x9-raider-talk-refused` does not move.
- [ ] **Foreign hooks:** `sc xnpc` with only our survivors and raiders around: the "body hooks that met another
      player-body NPC ... stepped aside" line says `none yet`. Spawn `npcSdcsTest` from the creative menu: its hooks
      (`gaze`, `colliders`, ...) count there; ours never do.
- [ ] **Survivor regressions** (DESIGN_M2_M9 section 10): P2 the look applies (`sc xnpc <id>` archetype scSurvivor, X1
      count), P3 eyes stay in their sockets after `sc gear <id> <Set>`, P11 a real jump animation over a block, P12
      knocked down (sledge power attack) she gets up on the spot without floating, P16 a bandage from her window with
      the animation, P17 the aim pose in a ranged fight (`sc aim <id>`: stamps rising, state `aiming`).
- [ ] **Raider regressions:** round 1's P12 (faces, feet on the ground, eyes in place, `x8-self-hit-guard` counting),
      a firearm raider raises its gun and fires (needs Locked & Loaded 1.3.1 or later, which lets NPCs reload), a hurt
      raider bandages from its bag.
- [ ] **Dev core:** `core SurvivorCommunityDev ragpin off` then `ragpin on` answer `ragdoll pin cancel is OFF` / `ON`
      (not "not loaded"), and `ragfix off` / `ragfix on` answer `ragdoll-as-player is OFF` / `ON`.
- [ ] **Items:** the creative menu has no `NPCSC` weapons any more; the log has no item or localization error about them.
- [ ] **Old-save warning, on a copy only:** a pre-migration save logs the M9 line ending in `Raiders it saved (the old
      per-weapon scRaider<Sex><Weapon> classes, e.g. console spawns) are of classes that no longer exist either.`

## M10 round 1: raiders on XNPC bodies

**Deployed 2026-10-01** (DLL md5 `6fa158fe465374f392a4b36e5167cae5`). **Needs a new game**: the old per-weapon raider
classes (`scRaider<Sex><Weapon>`) no longer exist, so a console raider saved in an old world cannot load (kill old raiders
first if you must keep a test world). Design: `_research\xnpc_migration\migrate\DESIGN_M10_RAIDERS.md`; decisions M10-D1..D9
in SurvivorCommunity_Tasks_Migration.md. The old model shim (Patches_Sdcs' EModelSDCS hooks, `Patch_RaiderPostInit`) and
the old `meleeNPCSC*` / `gunNPCSC*` items were still in this round's build; round 2 (above) removes them.

What changed:
- **Raiders are six hostile classes on 0-XNPCCore's player body**, `scRaider{Male,Female}T{1,2,3}` (EntityAliveSDXV4 on
  XNPC's `npcSdcsBase`, Faction `bandits`, IsEnemyEntity, not hirable, `no_voice`), AI `NPCModCoreNoChat,
  SdcsNpcRangedAdvanced, SCRaider` (SC's new package: approach until in weapon range, investigate what they hear).
  They are a second managed set in the DLL (`XnpcRaiderClasses`), so every body fix the survivors have covers them:
  SC look (raider gear: `RaiderGearSets`), eyes, hit colliders, feet offset, self-hit guard (X8), aim pose, items-only
  healing, the pair's melee weapon on failover.
- **Weapons** from the survivors' `XnpcWeapons` list at the class tier cap: **every raider carries both** a melee and a
  ranged weapon (a firearm at `RaiderFirearmChance` 12%, primitive ranged weapons from day 1), and has a **lead**
  (M10-D1, saved as `sc_raiderLead`): `RaiderRangedShare` 30/40/50% (T1/T2/T3) are **ranged-lead** and fight like a
  survivor (gun first, 0-XNPCCore's swap to melee at 3 m and back at 5 m, melee when dry); the rest are **melee-lead**:
  0-XNPCCore's world-entry re-arm is held off so they keep the melee weapon and charge, and they take the gun or bow
  only when the target stays out of reach (farther than `RaiderLeadRangedDist` 8 m, above them, or where their path
  cannot get, for `RaiderLeadRangedSeconds` 3 s) and only while it has rounds; within 3 m 0-XNPCCore's own swap takes
  them back to the pair's melee weapon, and they stay on it until the target is out of reach again. They also put the
  gun away by themselves when the target is back in reach within 5 m (1.5 s, unless overwhelmed) or gone (2 s). Losing
  sight of a target at 3-8 m does not count as "out of reach" (they investigate instead).
  **Ammo** `RaiderSpawnAmmo` 24 / `RaiderSpawnArrows` 15 in the **bag**, never resupplied. No inventory window ever
  (meds go to the bag too). **No XNPC kill levels** (`RaiderXnpcLevels` false). **Doors:** SCore's opener only
  (`RaiderVanillaDoors` false). Names "X the Raider".
- **Death:** a backpack with the bag's leftovers (ammo, meds) plus each pair weapon at `RaiderWeaponDropChance` 25%.
- **Spawning:** `scRaiderGroup{GS01,GS50,GS100}` hold the band's two classes (4% per roll, as before); W0's hostile pack
  NPCs become the raider class of their framework weapon's tier (M10-D9).
- **Outpost raids fixed:** a raid now spawns `AttackerCount` raiders (the band's classes picked directly); before, each
  attacker was rolled from the 96%-"none" spawn group, so almost nobody came. A raider inside an outpost POI now counts
  as an enemy inside (security "contested").
- Console: `sc spawn hostile [male|female] [T1|T2|T3] [weapon] [weapon] [ranged-lead|melee-lead] [Set]`, `sc raiders
  [r]` (new; shows the lead and what a melee-lead raider waits for), `sc weapons` (one list + raider odds),
  `sc loadout sim [n] hostile` (ranged-lead %), `sc loadout <id> [weapon] [ranged-lead|melee-lead]`, `sc xnpc <id>` (new
  `side=` and `hostility:` line with `lead=`), `sc xnpc` (raider count; the `weapons:` line counts melee-lead re-arms
  held off and switches to the gun).

Before you start: `sc r` below means `sc raiders 40`, `sc x <id>` means `sc xnpc <id>`. Stand on open ground, day.

- [ ] **Startup:** `patch verification: all 76 targets patched` (round 1's build; 62 since round 2), no `MISSING PATCH`; the config line
      ends with `raiders=6 classes, ranged-lead 0.3/0.4/0.5 (melee-lead to ranged beyond 8m for 3s) firearm 0.12 ammo
      24/15 levels=off drop 0.25 vanillaDoors=False gearSets=9`. No `SdcsSwapToHand ... not found` or "could not be
      bound" warning. No SC error or `[SC] ... failed` line in a session with raiders.
- [ ] **P1 (gate for round 2) - a hostile V4 fights.** `sc spawn hostile male T1 club melee-lead` (it spawns 6 m ahead).
      `sc x <id>`: `EntityAliveSDXV4`, `side=raider`, `hostility: faction=bandits IsEnemyEntity=True enemyOfYou=True
      youCanHurt=True ... hirable=False ... lead=melee-lead ... no_voice=True pkgs=[NPCModCoreNoChat,SdcsNpcRangedAdvanced,
      SCRaider]`, and a `pair=` with both a ranged and a melee weapon. Stand still: it attacks and your health drops. Hit
      it back: damage lands. Repeat with `sc spawn hostile female T2 bow ranged-lead` and `sc spawn hostile male T3
      firearm ranged-lead` (shots and arrows hit you from range). **If this fails, round 2 must wait.**
- [ ] **P2 talk prompt:** superseded by round 2 (no prompt on raiders any more; its "Talk prompt" rows above). With
      round 1's build alone: a "talk" prompt shows, pressing it gives the "sworn enemy" tooltip, never a dialog or the
      SdcsNPC window.
- [ ] **P3 range and noise:** a bow raider 60 m away in the open walks in (`ScRaiderApproach`) and shoots from range
      instead of hugging you. Hide behind a wall and make noise: it comes to look (`ScRaiderInvestigate`).
- [ ] **P4 doors (D5):** close an unlocked door between you and a raider: it opens it (after a short stall) and the door
      shuts again about 2 s later; `sc r` shows `canOpenDoors False`; no Voice Control `[DOOR]` line for its id. Lock the
      door: it stays shut and the raider is stuck. With `RaiderVanillaDoors` true (new spawn): no stall, door stays open.
- [ ] **P5 ammo, dry, failover:** a ranged-lead bow raider: `sc r` shows 15 rounds; let it shoot - visible arrows hit,
      the count drops; at 0 it switches to its melee weapon and melees (`[SDCS-FAILOVER]` in the log). It never gets more.
- [ ] **P5b ranged-lead up close:** walk up to a ranged-lead raider: within 3 m it swaps to its melee weapon
      (`[SDCS-CLOSEMELEE] enter`), back off past 5 m and it takes the gun again (`exit ... restore='<gun>'`).
- [ ] **P5c melee-lead:** `sc spawn hostile T2 melee-lead` 6 m away: it holds its melee weapon from the start (no
      `[SDCS-REARM]` for its id; `sc xnpc` hooks line counts `x5-raider-melee-lead-no-rearm`) and charges. Run to 15 m and
      keep away: after about 3 s (`sc r` shows `lead melee-lead (target at 15 m for 2.4 of 3 s)`) it takes its gun or bow
      (`x5-raider-lead-to-ranged`, `[SDCS-SWAP] ... remember='<melee>'`) and shoots. Close in to 3 m: it goes back to
      its melee weapon; step back to 6 m: it stays on melee (exit restores the melee weapon) until you are out of
      reach again. Stand on a roof it cannot climb: it takes the gun even closer than 8 m (`above`). Empty its gun
      first (or a melee-lead raider whose ammo is spent): it never takes the gun.
- [ ] **P5e melee-lead back to melee:** make a melee-lead raider take its gun (stay at 10 m), then walk back to 4-5 m:
      within about 1.5 s it puts the gun away (`x5-raider-lead-to-melee`, `[SDCS-SWAP] ... restore='<melee>'`) and
      charges. Again, then die or `kill` yourself / leave: about 2 s later it holds its melee weapon (`sc r`: `lead
      melee-lead (on the gun, target gone ...)` while it waits). A second player then gets charged, not shot.
- [ ] **P5f lost sight is not out of reach:** with a melee-lead raider chasing you indoors, step round a corner 4-6 m
      away and wait: it does not take its gun (`sc r` keeps `target in reach`); it comes looking with its melee weapon.
- [ ] **P5d saved lead:** save and reload with a console raider of each lead nearby: `sc r` shows the same pair and lead
      (`sc_raiderLead` is saved with the entity).
- [ ] **P6 who fights whom:** spawn a zombie next to a raider - they fight. `sc spawn` a survivor next to a raider - they
      fight. A follower engages a raider that shoots at you. Two raiders side by side never hurt each other.
- [ ] **P7 outpost raid (the bug fix):** `sc outpost attack <id> raid`, walk in: the log says `attack at outpost ...: N
      raider(s) from raider classes T<n>` with N = the attacker count (not 0-1); they go for the stand-in or you. Kill all
      -> "held". Then `sc outpost demand <id>`, refuse on the radio -> the same.
- [ ] **P8 death drop:** kill about 8 raiders: each leaves a backpack named after it with its leftover ammo and meds;
      about 1 in 4 of their weapons - melee and ranged alike, every raider has both - (real vanilla items, quality 1-6)
      are in them. Hitting a corpse again never makes a
      second backpack or more weapons. `sc x <id>` on a live raider: `ammo: ... window 0 bag N` (never a window).
- [ ] **P9 chase speed (decision M10-D4):** sprint 100 m away from a melee raider on flat ground (`sc r` shows its
      `speed`). Can you outrun it? If it is too fast or too slow, tell me: `MoveSpeedPanic` (2.9 now) is the knob.
- [ ] **P11 levels off:** in a world past day 8, a new raider's `sc x` shows `levels:` all `-` and `Progression=null`, and
      the hooks line counts `x3-raider-levels-off`. (Optional: `RaiderXnpcLevels` true -> `SdcsNpcLevel` 2+ and
      `SdcsLevelRegen=0`, `x3-raider-regen-off` counting.)
- [ ] **P12 looks:** a screenshot of three raiders: SC faces with rougher gear (Raider, Biker, Enforcer, Assassin, Rogue,
      Commando, Nomad, Scavenger, Primitive pieces), feet on the ground, eyes in place, names "X the Raider". A melee
      raider's swing damages its target (`x8-self-hit-guard` counting in `sc xnpc`).
- [ ] **P14 spawns:** `core SurvivorCommunityDev spawns`: the scRaiderGroup areas roll; wild raiders appear at the band's
      tier (T1 by day in the wild, T3 in towns at night...). The creative menu lists six "Raider" entries.
- [ ] **Odds:** `sc loadout sim 3000 hostile` -> ranged-lead about 30/40/50% (T1/T2/T3), firearms about 12% (below
      gamestage 100: T3 raiders carry one at 85% since round t3), no
      "without a pair". `sc weapons` prints one list and the raider odds line. `sc raiders 200` near a few wild raiders:
      every one shows both weapons and a lead.
- [ ] **Raids/landmines/healing:** a raider walks onto a landmine and takes damage (no immunity any more); a hurt raider
      uses a bandage from its bag with the animation (`sc x` medic line), never XNPC's free heal.
- [ ] **No regressions for survivors:** `sc spawn` a survivor: SC face, pair, 30 rounds / 20 arrows; a member in the base
      still gets resupplied; `sc xnpc` counts survivors and raiders separately; recruiting a raider is refused
      (`sc recruit <raider id>` -> not hireable).

## The XNPCCore migration (M0-M9): start here

Done 2026-10-01 in one session (commits 6acc639 M0, 380a707 M1, 3fbc851 R1, 3bc52db R2, 9fa29e3 R3, b1ee630 R4).
SurvivorCommunity now runs on 0-XNPCCore 3.2.06's own survivors (`npcSdcsSurvivor{Male,Female}T1-T3`); its own
survivor classes are gone (raiders stay `scRaider*`). **Start a new game** - old saves' members can't be found (the
log says "this save predates the XNPCCore migration"). Each round's own rows follow below (round 4 first, M0 last).

**Already checked by Claude in game (DevLink, 2026-10-01; new test world `sCMigrationTest` on The Dead Coast, home at
-398, 35, 1059 - delete it when you like):**
- startup: `patch verification: all 75 targets patched`, recruit dialog injected into 4 dialogs incl. SdcsNPC, no SC
  errors in a full session;
- `sc spawn` makes XNPC survivors with SC's rolled face, mixed armor, SC names and a rolled pair (e.g. primitive bow +
  stone spear) with 20 arrows; XNPC's own look applier stays off; strangers keep XNPC kill levels;
- `sc recruit <id>` (new test command, skips the favour) → Resident at a post, Guard(9), SCResident package, kill
  levels stripped, member door opener on, `ammo=` recorded;
- community window Follow me → Following, Leader and Owner set on the XNPC body, no hire contract seeded
  (`contract-refused`);
- typed `@Lena stay` → held "Stay (holding)", still Following, no snap back; `@Lena dismiss` + `@Lena yes` → sent home
  as Resident, still a member (`dismiss-to-home=1`); Voice Control greetings/subtitles appear;
- a ranged fight (earlier build) drives the aim pose; a resident shot her arrows down to 10 and was resupplied at home
  with 20 from the stockpile (one ammo bundle opened);
- a food run on an XNPC body: she runs (`running=True`), real ground speed about 3.6 m/s - `sc runners` reports 2.39,
  so its measured speed under-reads; consider `RunWalkMps` about 3.2 (0.9 x 3.6) after your own check; recall works;
- raiders still spawn and arm (`sc spawn hostile club`).

**Fixes after your first look (2026-10-01, DLL 49f8286e):**
- [ ] **Bows fire:** an XNPC survivor with a bow or crossbow now shoots visible arrows that hit. Cause: SC kept their
      ammo in the inventory window; SCore reloads from it, but vanilla only makes the arrow object for ammo in the bag or
      toolbelt, so a reload used an arrow and the release fired nothing. SC's ammo now lives in the bag (starting stack,
      resupply, re-created bodies); old window ammo is moved over every 5 s; the bag is in the backpack they drop on
      death. `sc xnpc <id>` shows `bag N` going down as they shoot. Measured before the fix: 9 arrows, 0 hits; XNPC's own
      bow survivor (ammo in its bag) hit.
- [ ] **Feet on the ground:** XNPC bodies used the player prefab's model offset (-0.08 m), sinking 8 cm. Now 0
      (`XnpcModelY` in Config/blocks.xml). Dev check: `core SurvivorCommunityDev feet <id>` - lowest foot bone should be
      about the ground y.
- [ ] **RunWalkMps 3.2** (was 4).
- [ ] **Melee:** measured working (machete 27-30, stone sledge 21-23 per hit, members and strangers alike), but the stone
      sledgehammer lands rarely (about one hit per 20 s) - tell me if you'd rather keep sledgehammers out of the melee roll.

Not covered by that check (needs you): favours and the join dialog, outposts and garrison stand-ins, death/backpack,
healing with the bandage, weapon swaps in the dialog, overwhelmed, formations/wheel, doors and hatches, several days of
play (morale, daily, no billing prompts), and how it all looks and feels.

**Useful:** `sc xnpc <id|name>` (everything about one NPC), `sc xnpc` (counters), `sc recruit <id|name>`, `sc spawn
[male|female] [weapon] [weapon]`, `sc loadout <id>`, `sc arms <id> melee|ranged`, typed `@<name> <order>` in chat (T).

## Migration round 4 (M7b, build sc_r4): ammo resupply, no free ammo for re-created bodies, swap return, overwhelmed

**Deployed 2026-10-01** (DLL md5 6ec03509). **Needs a new game** (decision D5). The community's 0-XNPCCore survivors keep finite ammo (D3), and
now:
- **Resupply** (new `ScAmmo.cs`): a member - Resident, Following or PendingStation - or a posted member's garrison
  stand-in whose gun is down to fewer than `XnpcResupplyBelow` (10) rounds, standing inside the home zone or at one of
  the community's outposts (its POI or the claim square around its flag), gets `XnpcResupplyAmmo` (30) rounds of the
  gun's basic ammo (`XnpcResupplyArrows` 20 for a bow or crossbow) in the inventory window, at most once per
  `XnpcResupplyCooldown` (60 s). Never elsewhere, never strangers, runners, hidden bodies or a rescue stand-in. The
  stockpile pays in packing points: a round costs `PointsEach` (EconomicValue / ValueOverrides) x
  `XnpcResupplyPointsMult`; a bundle (`PointsPerBundleAmmo` 2000) is opened only when the points left in the last one
  (`ammoopen=` in the registry) do not cover it. **No daily ammo upkeep any more**: the daily line's ammo number is the
  bundles opened by resupplies that day, and "- a survivor ran low on ammo and the stockpile had none to give" means a
  resupply found it empty.
- **No starting stack for re-created bodies** (round 2 review P1): every 5 s each member's ammo for the gun (window +
  stores + loaded magazine) is written to the registry (`ammo=`); a body the community re-creates (call-back, garrison
  or rescue stand-in, a runner's fresh body) starts with that instead of 30. Only a genuinely new survivor (wild spawn,
  radio contact, walk-in, `sc spawn`) gets the starting stack.
- **Swap return** (new `ScArms.cs`, postfixes on SCore's `DialogActionSwapWeapon.PerformAction` and Voice Control's
  `VCCommandRouter.MutateSwapWeapon`): a member told to use a weapon you put in their window keeps it in place of the
  weapon of that kind; the replaced weapon comes back as a real item (base storage, else your inventory, else a bag at
  your feet).
- **Overwhelmed** (inside the X5 prefix on `SdcsCloseRangeTick`): with at least X enemies targeting them within 12 m
  (X = 2 + (Fighting stars - 1) / 2, at most 5; 3 for non-members) and a gun that has rounds, a survivor does not take
  0-XNPCCore's melee swap at 3 m, and one already on melee goes back to the gun after 0-XNPCCore's 3 s minimum hold.
- **Holster (M7.6): not done** - see R4_REPORT.md; nothing to test.

`sc xnpc` gains an `ammo (D3):` line (resupplies, rounds, bundles opened, refusals, "ran low outside", the stockpile and
the open bundle's points, starting stacks and re-created bodies) and a `weapons:` line (overwhelmed on/bound, swap
returns by destination). `sc xnpc <id>` gains `resupply:` (the gun's rounds, the cost per round, eligibility, where it
stands, the cooldown, the registry record) and `overwhelmed:` (enemies on them, X, the state, 0-XNPCCore's close-melee
flag and remembered weapon). Hook line: `x5-overwhelmed-keep-gun`, `x5-overwhelmed-exit`. Turn `DebugLogging` on for
the verbose lines named below.

- [ ] **Startup:** `patch verification: all 75 targets patched` (round 3's 73 plus `DialogActionSwapWeapon::PerformAction`
      and, with Voice Control, `VCCommandRouter::MutateSwapWeapon`), no `MISSING PATCH`, no `patched an unlisted
      method`; `overwhelmed rule: 0-XNPCCore's close-range swap state bound` (no "could not be bound"). The config line
      ends with `resupply=below 10 +30/20 every 60s x1 overwhelmed=2..5/3 in 12m`.
- [ ] **P18, resupply at home:** with ammo bundles in the stockpile, let a ranged member at home shoot her window empty
      (or take the ammo out of her window by hand). Within about 5 s of dropping below 10 rounds Player.log has
      `<name> (id N) resupplied at home: 30 <ammo> for <gun> at <p> point(s) each - opened 1 ammo bundle(s), K left
      (… point(s) left in the open bundle)`; her window holds the rounds; the stockpile's ammo dropped by one; if she
      had failed over to melee, `[SDCS-FAILOVER] … restore '<gun>'` follows. Empty her again at once: no second
      resupply until 60 s have passed (`sc xnpc <id>` `next resupply in … s`). The next resupplies take points from the
      open bundle and open no new bundle until it is used up (about 7 for 9 mm, 8 for stone arrows).
- [ ] **P18, never outside:** the same 200 m outside the base and away from any outpost: no resupply, she stays on her
      melee weapon; `sc xnpc <id>` `resupply: … here: outside the base and the outposts`; the `ran low outside`
      counter rises once (DebugLogging: `<name> … is low on ammo outside the base and the outposts`). Walk her home: she
      is refilled within about 5 s of entering the claim.
- [ ] **At an outpost:** a follower run dry inside one of your outposts (the POI or its claim square) is refilled
      (`resupplied at <outpost name>`); so is a posted member's stand-in that shot itself dry defending it
      (`<name>'s stand-in … resupplied at …`). A rescue stand-in at a distress POI is never refilled.
- [ ] **Strangers and runners never:** a wild survivor that shot itself dry inside your base gets nothing (`sc xnpc
      <id>` `not resupplied: not a member`); a member walking out on a physical run is not refilled even inside the
      claim (`OnRun (out on the community's business …)`).
- [ ] **Empty stockpile:** `sc give ammo -99`, then let a member at home run low: Player.log `<name> needs ammo at home
      but the stockpile has none (pack some at the packing station)` (at most once per 5 minutes per NPC); `sc day`: the
      daily line ends with `- a survivor ran low on ammo and the stockpile had none to give`. Pack and deposit an ammo
      bundle: she is refilled at the next check after her cooldown.
- [ ] **No daily ammo upkeep:** with ranged residents and ammo in the stockpile, `sc day` a few times without any
      fighting: the ammo stock does not change and the daily line says `… 0 ammo`. After a day with one resupply that
      opened a bundle: `… 1 ammo`. The registry's `[community]` has `ammoopen=`, `ammoopened=`, `ammoshort=` and no
      `ammocarry=`.
- [ ] **The ammo record:** after `sc save`, each member with a gun has `ammo=<item>:<n>` in their `[member]` record
      (`ammo=none` when she carries nothing), matching `sc xnpc <id>` `record ammo=` and her window (plus a loaded
      magazine).
- [ ] **P1, a stand-in brings no fresh ammo:** post a member with a gun at an outpost and go there. The stand-in's window
      holds what the member last carried (`ammo=`), not a fresh 30; `sc xnpc <stand-in>` `ammoSeeded=True`; DebugLogging:
      `re-created body N: X round(s) from the record (…), no starting stack`; the `ammo (D3):` line's
      `re-created bodies given their record` rises and `starting stacks` does not. Let it shoot some, leave until it is
      removed, come back: the new stand-in has what the last one had left (plus any resupply at the outpost).
- [ ] **P1, call-back and a runner's fresh body:** the round 3 M4.4 call-back and a runner whose snapshot is unusable
      (fresh body) come back with their recorded rounds, never 30. A member who never stood loaded with a gun since
      joining (`record ammo=never recorded`) comes back with an empty window and is refilled at home.
- [ ] **New survivors still get the starting stack:** `sc spawn female`, a radio contact and an outpost walk-in each
      start with 30 rounds (20 arrows/bolts), `starting stacks` rises; a survivor that joins keeps what she has.
- [ ] **P19, swap return (dialog):** with a following member, put a weapon of the survivors' list in her window (a
      Hunting Rifle - `gunRifleT1HuntingRifle`), open the dialog's weapon menu, Rifles, "Use Hunting Rifle". Player.log:
      `<name> keeps the gunRifleT1HuntingRifle (dialog) in place of the <old gun>, returned to storage - loadout now …`;
      the tooltip "<name> keeps the Hunting Rifle. The <old> is in the base storage."; the rifle is gone from her window;
      `sc xnpc <id>` `pair=` and `record=` have the rifle; `sc save` → her `loadout=` has it; the old weapon is in one of
      the base's storage boxes (linked storage shows it). She holds the rifle. No free ammo came with it.
- [ ] **P19, the other destinations:** do it out in the world (the base unloaded): the old weapon goes into your
      inventory (`returned to inventory`); with your inventory full: a bag at your feet with the weapon
      (`returned to ground`). With the base loaded but every box full: your inventory.
- [ ] **P19, melee and mods:** swap her melee weapon the same way (Melee menu): the old melee weapon comes back. Hand her
      a gun with a mod attached: the mod comes back as an item next to the old gun. A weapon not on the survivors' list
      (anything not in `XnpcWeapons`) is held from the window but not kept (`… not kept in their pair`), and the window
      keeps it. "Use" one of her own two weapons changes nothing.
- [ ] **P19, voice:** typed `@<name> use <weapon>` (Voice Control's SwapWeapon) with the weapon in her window does the
      same, route `voice`, returns going to you (her leader).
- [ ] **Swap while on melee:** let her gun run dry outside so she fails over to melee, then hand her a new gun with ammo
      in her window and "Use" it: she holds the new gun; `sc xnpc <id>` `overwhelmed: … remembered='…'` never names the
      returned gun afterwards.
- [ ] **Overwhelmed keeps the gun (P17 too):** a 1-2 star member (`sc members`) with a loaded gun, two or more zombies
      on her within 12 m: when one reaches 3 m she keeps the gun (`x5-overwhelmed-keep-gun` rises; no
      `[SDCS-CLOSEMELEE] … enter` for her id), backs off and shoots; `sc aim <id>` shows the aim pose up; `sc xnpc <id>`
      `overwhelmed: 2 enemy(ies) on them … overwhelmed at 2 … OVERWHELMED`. With a single zombie she still swaps to melee
      at 3 m and back at 5 m as before.
- [ ] **Overwhelmed exit:** one zombie gets her onto melee (`[SDCS-CLOSEMELEE] enter`), then two more arrive: after she
      has been on melee 3 s she goes back to the gun (`x5-overwhelmed-exit`, DebugLogging `<name> … is overwhelmed (3 of
      2 enemies on them) - back to the <gun>`), and does not flip back to melee within 3 s of the count dropping
      (`OverwhelmedHoldSeconds`).
- [ ] **Thresholds:** raise her Fighting to 7 stars (`sc xp <name> fighting <n>`): she needs 5 enemies (`overwhelmed at 5`); a stranger (wild
      survivor) needs 3; a stand-in uses its member's stars. A dry gun (no rounds anywhere) always goes to melee.
      `Overwhelmed` false in the config: 0-XNPCCore's swap as before, the counters stay 0.
- [ ] **P16 again (X5 now has two jobs):** during a bandage use the close-range tick is still skipped
      (`x5-SdcsCloseRangeTick` rises during the use, no `x5-overwhelmed-*`), no `[SDCS-CLOSEMELEE]` / `[SDCS-FAILOVER]`
      line during the use.
- [ ] *Probe:* no error lines `arms-closerange`, `arms-swap`, `arms-dialog-swap`, `ammo-tick`, `ammo-record`,
      `storage-deposit-zone` in Player.log after an evening of fights.

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
      `sc restore <name>`): she respawns at her post with her face, name, pair and the ammo she last carried (round 4:
      the registry's `ammo=`, never a new 30), log `<name>: entity id
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
      bow, the tier 0 melee list). `sc loadout sim 2000` prints about 12% firearms (below gamestage 100; round t3
      arms T3s at 85%) and the tier spread for your
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
      name (`sc loadout <id> bow`) lists the matches. A new gun from the console comes with a new starting stack for
      it (a test tool; a weapon handed over through the weapon menu does not - round 4).
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
- [ ] **Firearms are rare:** `sc loadout sim 2000` reads about 12% below gamestage 100 (and `sc loadout sim 2000
      hostile`; at 100+ T3s carry one at 85% since round t3). In the world,
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
