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

## Base defence D1: the Home Status panel and the noise meter

**Deployed 2026-10-05** (Fable review + re-check: deployable). DLL md5 `c035ed57eee3402a3cd467472a43b536`, 655360
bytes, deployed **together with** the same tree's Config (`blocks.xml` keys, the XUi window, localization). Plan:
`BaseDefence_Plan.md` in the game root; report and reviews in `_research\base_defence\` (D1_REPORT.md,
fable_review_d1.md, fable_recheck_d1.md). Two new Harmony targets: **76** (69 without
Voice Control). No save format change: three new keys in the registry's `[community]` section (`noiseev`, `attention`,
`noisestamp`), which older builds ignore. Nothing attacks yet: D1 only shows and measures; the hordes come with D2.

What changed:
- **Home Status panel** at the HUD's top left while you stand inside the home zone, laid out like State of Decay 2's:
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

- [ ] **Startup:** `patch verification: all 76 targets patched` (69 without Voice Control), with `patched
      AIDirector::NotifyActivity` and `patched ItemActionRanged::ItemActionEffects`; the `config:` line ends with
      `homeHud=True noise=member 2 generator 6 workstation 1, heat x1 npc shot x1, half-life 1h, levels 15/30/50,
      attention +0.14 -2/h`.
- [ ] **The panel shows at home only:** walk into the home zone: the panel appears at the top left; walk out: it goes.
      Open the inventory or the map inside the zone: it hides, and comes back when you close them. F7: gone with the
      rest of the HUD.
- [ ] **It reads like the reference:** title, dot and morale word on the first row (the word matches `sc morale`'s
      band); the five resource icons with numbers that match `sc status`'s stock; the member count; the noise line.
      Nothing overlaps or is cut off; AshfallUI did not dress it as a dialog (it is a plain dark plate with a teal strip).
- [ ] **Red when short:** `sc give food -999`: food turns red (it is below the day's need); `sc give food 20`: white
      again. Injure a member with no meds (`sc injure <member>`, `sc give meds -999`): meds red. With a resident gunner,
      `sc give ammo -999`: ammo red. Materials and fuel only go red with outposts that cost upkeep or haulage.
- [ ] **Steady noise:** `sc noise`: `members at home` equals your residents, `workstations` the facilities the
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
      clear` and `sc attention 0` reset them. Reaching 100% only writes one log line (`base attention reached 100% ...
      no horde is called yet`).
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
  and the member's own distance from its post (`- BEYOND the 23 m stray limit` past it).

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
- [ ] **Odds:** `sc loadout sim 3000 hostile` -> ranged-lead about 30/40/50% (T1/T2/T3), firearms about 12%, no
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
      bow, the tier 0 melee list). `sc loadout sim 2000` prints about 12% firearms and the tier spread for your
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
