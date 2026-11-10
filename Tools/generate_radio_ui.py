#!/usr/bin/env python3
"""
Generates the radio window, windowScRadio, into Config/XUi_InGame/windows.xml between its GENERATED
markers. Run from anywhere:  python Tools/generate_radio_ui.py

Written by the AshfallUI session (2026-10-13, user: make the radio window look like Ashfall UI) on the
community window's rules (Tools/generate_community_ui.py) - same palette, plates and controls:

    [radio] RADIO STATION                                                                   [x]
            Powered - 2 of 3 contacts out there
    +- [AIR DROP button] ----------+- [CALL button] ----------+  +- ENCLAVES YOU KNOW -----------------------+
    |  what it does / cooldown     |  what it does / cooldown |  | name      standing  benefit   where       |
    +- SURVIVORS HEARD ON THE RADIO --------------------------+  | ... 8 rows, nearest first, "+N more"     |
    +- SUPPLY RUNS ON THE RADIO ------------------------------+  +- THEIR REQUESTS -------------------------+
    |  [runner >]  where they are        [CALL HOME]          |  |                                          |
    |  who is calling for help                                |  |                                          |
    +- OUTPOSTS REPORTING IN ---------------------------------+  | enclave   what          answer / due by  |
    [ raiders' demand                      [PAY] [REFUSE] ]     +- ALLIES ---------------------------------+
    +- SCOUT FOR OUTPOSTS  [food][meds][ammo][mats][fuel] ----+  | [ally >]  benefit, where                 |
    |                                                         |  | [SEND A FIGHTER]   [SUPPLY DROP]         |
    |                                                         |  | [give ..] [get ..] [TRADE]  hint         |
    +- BARTER  [give ..] [get ..] [TRADE] -------------------+  +- SIEGE CALLS ----------------------------+
    the last action's message                                    | X is under attack!  [ON OUR WAY] [CAN'T] |

E6 (2026-10-17) added the right-hand column, the Enclaves section: the enclaves the player knows (standing in the
markers' colours, benefit - blue while it applies - and where), their open requests (what, and the day to answer by or
deliver by) and a
siege call with its countdown and two buttons (both only while it is called, before the fight). The left column is the
window as it was; the window grew from 760 to 1384 wide - still narrower than the community window (1500), so it fits
wherever that does.

E7 (2026-11-03, research/ideas/DESIGN.md sections 2 and 7) added the Allies panel between the requests and the siege
calls, as the design's layout plan says: the Enclaves list went from 15 rows to 10 and the requests from 4 to 3 to make
room, the siege section lost 8 units of slack, and its bottom stays level with the barter section's. The panel picks one
Allied enclave with a cycling button (nearest first) and shows what they do for you and where; under it the barter with
that ally - Give, Get (a scavengers' group only: theirs is fixed otherwise) and Trade - and a hint line. Its plates,
buttons and icons are the same scui_* names and vanilla symbols as the rest, so AshfallUI's premium art and brass icons
apply to it with no patch.

E9 (2026-11-03, research/ideas/DESIGN.md section 2) added the radio commands' row to the Allies panel, between the pick
and the barter: Send a fighter (the run icon) and Supply drop (the air drop icon), each half the row, each greyed with
the reason in its label as the station's own calls are. The room came from the Enclaves list again (10 rows to 8, 44
units): the requests and the Allies panel moved up, the panel grew from 150 to 194, and the siege section is where it
was. Its icons are ui_game_symbol_run and _airdrop, already in this window (the siege's On our way, the air drop call),
so AshfallUI's brass twins apply here too.

R2 (2026-11-09, round 27, research_radio_DESIGN_R2.md): the handheld radio opens this window from anywhere ("remote", the
Radio tab), relayed through a powered station at home. The distress section became SUPPLY RUNS ON THE RADIO: a row with the
runner pick (a cycling button, by name; its arrow only while there is another), where they are (ScRuns.StatusLine) and Call
home (decision 6: a physical runner who carries a radio, not pinned down, not on the way home already - else the reason in
its label, grayed), {runnone} in the row's place while nobody is out, and under it the distress lines as before. The room
came from the slack under the calls' descriptions (140 to 124: three lines each) and the contacts' list (106 to 96: three
contacts, RadioMaxContacts' default); the outposts and everything under them are where they were. The demand's Pay /
Refuse and the siege's "We can't come" gained ready bindings ({demandready}, {siegecantready}), so the station's controls
all gray out alike while the window is remote with the home station offline. Its new icon spots reuse this window's
ui_game_symbol_run and _arrow_right, so AshfallUI's brass twins apply here too.

RULES THAT MATTER (the community window's, unchanged):
- Buttons are <button> views: XUiC_ScRadio hooks each control's own OnPress (GetChildById), which a
  simplebutton's clickable child never raises. Each is a scui_btn plate with a label (and icon) over it.
- Every plate is a scui_* sprite handed [white]; AshfallUI ships premium art under the same names.
  Icons are vanilla menu symbols (AshfallUI draws them as their brass twins).
- A control that cannot act now keeps its plate but loses its hover plate, and its label dims: the
  {dropready} / {callready} / {scoutready} / {barterready} bindings say which. The labels still carry
  the reason (the controller writes it into {droplabel} / {calllabel} / {scoutdesc} / {barterdesc}).
- Control names are the controller's: btnClose, btnDrop, btnCall, btnScout<Resource>, btnBarterGive,
  btnBarterGet, btnBarterTrade, btnDemandPay, btnDemandRefuse; E6's btnSiegeGo, btnSiegeCant; E7's btnAllyPick,
  btnAllyGive, btnAllyGet, btnAllyTrade; E9's btnAllyFighter, btnAllyDrop.
- E6's rows are bound by index: enc<1..ENC_ROWS><name|standing|standingcolor|benefit|benefitcolor|where|visible>
  and req<1..REQ_ROWS><enc|what|due|duecolor|visible> - XUiC_ScRadio's EncRows / ReqRows must equal ENC_ROWS /
  REQ_ROWS. Bound colours are numeric "r,g,b,a" strings.
- E7's Allies panel binds allyname, allystate, allypickready, allyvisible (the barter row), allynone (the words in its
  place while it is hidden), allygive, allyget, allygetready, allytradeready, allyhint and allyhintcolor.
- E9's commands row binds allycallvisible (the row), allycallnone (the words in its place while it is hidden: no ally yet,
  or the calls switched off), allyfighterready, allyfighterlabel, allydropready and allydroplabel.
- R2's Supply runs row binds runvisible (the row), runnone (the words in its place while it is hidden), runname,
  runpickready, runstate, runrecallready and runrecalllabel; its controls are btnRunPick and btnRunRecall. The demand's
  buttons bind demandready, the siege's "We can't come" siegecantready.
"""

import io
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
MOD = os.path.dirname(HERE)
TARGET = os.path.join(MOD, "Config", "XUi_InGame", "windows.xml")

BEGIN = "\t<!-- GENERATED:BEGIN scradio (Tools/generate_radio_ui.py) -->"
END = "\t<!-- GENERATED:END scradio -->"
# What this generator replaced the first time: the hand-authored radio window - its comment banner, the
# <append xpath="/windows"> that holds it, and that append's close.
LEGACY = re.compile(r"\t<!-- The radio window \(.*?\t\t</window>\n\t</append>\n", re.S)

CONTROLLER = "SurvivorCommunity.ScRadio, SurvivorCommunity"

# ---------------------------------------------------------------- geometry
LEFT_W = 760                          # the station's own column: the whole window before E6
WIN_W, WIN_H = 1384, 940              # 940 tall: the community window is 924, a 1080p screen ~987
COMMUNITY_W = 1500                    # the community window's width: it fits the game's resolutions, so <= it does too
M = 24
PW = LEFT_W - 2 * M                   # a left section plate's width
PAD = 16                              # a section's content inset
CALLS_Y, CALLS_H = 84, 124             # R2: 140 before (its descriptions keep three lines)
CONTACTS_Y, CONTACTS_H = 216, 96       # R2: 232, 106 before (three contacts, RadioMaxContacts' default)
DISTRESS_Y, DISTRESS_H = 320, 118      # R2: Supply runs on the radio (346, 90 before: the distress lines alone)
OUTPOSTS_Y, OUTPOSTS_H = 444, 150
DEMAND_Y, DEMAND_H = 600, 40
SCOUT_Y, SCOUT_H = 648, 104
BARTER_Y, BARTER_H = 760, 104
MSG_Y = 872
HEAD_H = 38                           # a section's heading band: icon, title, rule
LINE = 16                             # a 14-point text line
# E6: the Enclaves column, right of the station's
X2 = LEFT_W                           # its plates' left edge: the left plates end M before it
RW = WIN_W - X2 - M                   # its plate width
RIN = RW - 2 * PAD                    # its content width
ROW_H = 22                            # one enclave or request row
COL_H = 20                            # the column-heading row
ENC_Y, ENC_H, ENC_ROWS = CALLS_Y, 258, 8       # E7: 15 rows before the Allies panel; E9: 10 before its commands row
REQ_Y, REQ_H, REQ_ROWS = 350, 148, 3            # E7: 4 rows before
ALLY_Y, ALLY_H = 506, 194             # E7: the Allies panel (550, 150 before E9's commands row)
SIEGE_Y, SIEGE_H = 708, 156           # its bottom level with the barter section's
# (binding field, key of its heading, x within the row, width); the row's labels are 14-point, ellipsis past the width
ENC_COLS = (("name", "scRadioEncColName", 0, 190), ("standing", "scRadioEncColStanding", 194, 74),
            ("benefit", "scRadioEncColBenefit", 272, 162), ("where", "scRadioEncColWhere", 438, 130))
REQ_COLS = (("enc", "scRadioReqColEnclave", 0, 190), ("what", "scRadioReqColWhat", 194, 236), ("due", "scRadioReqColDue", 434, 134))
SIEGE_BW = 270                        # each siege button's width
ALLY_PICK_W = 300                     # E7: the ally pick button
ALLY_GIVE_W, ALLY_GET_W, ALLY_GAP = 200, 170, 10    # E7: the barter row's Give and Get, and the gap between its buttons
ALLY_ROW = 36                         # E7: a row of the panel
ALLY_CALL_W = 279                     # E9: each command button (Send a fighter, Supply drop): half the row
RUN_PICK_W, RUN_CALL_W, RUN_ROW = 200, 190, 36   # R2: the Supply runs row - the runner pick, Call home, the row's height

# ---------------------------------------------------------------- palette (the community window's)
TITLE = "226,219,203,255"
BODY = "226,220,208,255"
DIM = "190,182,166,255"
OFF = "130,124,112,255"               # a control that cannot act now
GOLD = "206,178,120,255"
DANGER_TEXT = "240,150,130,255"

SCOUTS = (("btnScoutFood", "ui_game_symbol_fork", "scRunFood"),
          ("btnScoutMeds", "ui_game_symbol_medical", "scRunMeds"),
          ("btnScoutAmmo", "ui_game_symbol_shape_ammo", "scRunAmmo"),
          ("btnScoutMaterials", "ui_game_symbol_brick", "scRunMaterials"),
          ("btnScoutFuel", "ui_game_symbol_gas", "scRunFuel"))


def plate(sprite, x, y, w, h, depth=0, extra=""):
    return ('<sprite depth="%d" sprite="%s" type="sliced" color="[white]" pos="%d,%d" width="%d" height="%d"%s/>'
            % (depth, sprite, x, y, w, h, (" " + extra) if extra else ""))


def icon(sprite, cx, cy, size, depth=3, extra=""):
    return ('<sprite depth="%d" sprite="%s" pos="%d,%d" width="%d" height="%d" pivot="center" color="[white]"%s/>'
            % (depth, sprite, cx, cy, size, size, (" " + extra) if extra else ""))


def label(x, y, w, h, size, color, text=None, key=None, extra=""):
    t = ('text="%s"' % text) if text is not None else ('text_key="%s"' % key)
    return ('<label depth="4" pos="%d,%d" width="%d" height="%d" font_size="%d" %s color="%s"%s/>'
            % (x, y, w, h, size, t, color, (" " + extra) if extra else ""))


def centred(x, py, w, ph, size, color, text=None, key=None, extra=""):
    """A label centred in a box whose top is py (negative) and height ph."""
    return label(x, py - (ph - size) // 2, w, size + 6, size, color, text=text, key=key, extra=extra)


def button(name, x, y, w, h, ready=None, sprite="scui_btn", hover="scui_btn_hover"):
    """A <button> plate. With `ready` (a binding) it loses its hover plate while that is false."""
    if ready:
        hover = "{# %s ? '%s' : '%s' }" % (ready, hover, sprite)
    return ('<button depth="2" name="%s" pos="%d,%d" width="%d" height="%d" sprite="%s" hoversprite="%s" '
            'defaultcolor="[white]" hovercolor="[white]" type="sliced" hoverscale="1.0" sound="[paging_click]"/>'
            % (name, x, y, w, h, sprite, hover))


def tone(ready):
    """A control label's colour: body text when it can act, dimmed when it cannot."""
    return "{# %s ? '%s' : '%s' }" % (ready, BODY, OFF)


def danger_tone(ready):
    """R2: a danger control's label color: its red when it can act, dimmed when it cannot."""
    return "{# %s ? '%s' : '%s' }" % (ready, DANGER_TEXT, OFF)


def section(y, h, sprite, key, x=M, w=PW):
    """A section plate with its heading band: icon, upper-case title, a rule under them (x, w: E6's right column)."""
    return [plate("scui_window", x, -y, w, h),
            icon(sprite, x + PAD + 11, -(y + 19), 22),
            label(x + PAD + 30, -(y + 9), w - 2 * PAD - 30, 24, 18, TITLE, key=key, extra='upper_case="true"'),
            plate("scui_divider", x + PAD, -(y + HEAD_H - 4), w - 2 * PAD, 3, depth=1)]


def body(y, h, binding):
    """A section's text under its heading."""
    return label(M + PAD, -(y + HEAD_H + 2), PW - 2 * PAD, h - HEAD_H - 6, 14, BODY, text="{%s}" % binding)


ELLIPSIS = 'overflow="ClampContent" overflow_ellipsis="true"'


def table(y, prefix, cols, rows, none_binding, more_binding):
    """E6: a section's rows under a heading row - one <rect> per row, shown by {<prefix><n>visible}, a 14-point label per
    column bound {<prefix><n><field>} (a field with a colour binds {<prefix><n><field>color}); the empty text at the first
    row, the "+N more" line under the last."""
    x0 = X2 + PAD
    top = y + HEAD_H + 2
    o = [label(x0 + cx, -top, cw, COL_H, 13, DIM, key=key, extra=ELLIPSIS) for (_f, key, cx, cw) in cols]
    first = top + COL_H
    for n in range(1, rows + 1):
        ry = first + (n - 1) * ROW_H
        o.append('<rect depth="1" pos="%d,%d" width="%d" height="%d" visible="{%s%dvisible}">' % (x0, -ry, RIN, ROW_H, prefix, n))
        for (f, _key, cx, cw) in cols:
            colour = "{%s%d%scolor}" % (prefix, n, f) if f in COLOURED[prefix] else (DIM if f == "where" else BODY)
            o.append("\t" + label(cx, 0, cw, ROW_H - 2, 14, colour, text="{%s%d%s}" % (prefix, n, f), extra=ELLIPSIS))
        o.append("</rect>")
    o.append(label(x0, -first, RIN, ROW_H - 2, 14, DIM, text="{%s}" % none_binding))
    o.append(label(x0, -(first + rows * ROW_H), RIN, ROW_H - 2, 13, DIM, text="{%s}" % more_binding, extra=ELLIPSIS))
    return o


# which row fields carry a colour binding ({<prefix><n><field>color})
COLOURED = {"enc": ("standing", "benefit"), "req": ("due",)}


def enclaves():
    o = section(ENC_Y, ENC_H, "ui_game_symbol_allies", "scRadioEnclavesHeader", x=X2, w=RW)
    return o + table(ENC_Y, "enc", ENC_COLS, ENC_ROWS, "encnone", "encmore")


def requests():
    o = section(REQ_Y, REQ_H, "ui_game_symbol_quest", "scRadioRequestsHeader", x=X2, w=RW)
    return o + table(REQ_Y, "req", REQ_COLS, REQ_ROWS, "reqnone", "reqmore")


def siege():
    """E6: the siege call - its words and countdown, "On our way" and "We can't come" while it is called, a hint line."""
    x0 = X2 + PAD
    ty = SIEGE_Y + HEAD_H + 2
    by = ty + 54
    o = section(SIEGE_Y, SIEGE_H, "ui_game_symbol_defense", "scRadioSiegeHeader", x=X2, w=RW)
    o += [label(x0, -ty, RIN, 50, 14, "{siegecolor}", text="{siegetext}"),
          '<rect depth="1" pos="%d,%d" width="%d" height="%d" visible="{siegebuttons}">' % (x0, -by, RIN, 36),
          "\t" + button("btnSiegeGo", 0, 0, SIEGE_BW, 36, ready="siegegoready"),
          "\t" + icon("ui_game_symbol_run", 24, -18, 20, depth=4),
          "\t" + centred(40, 0, SIEGE_BW - 48, 36, 15, tone("siegegoready"), text="{siegegolabel}",
                         extra='justify="center" upper_case="true" ' + ELLIPSIS),
          "\t" + button("btnSiegeCant", RIN - SIEGE_BW, 0, SIEGE_BW, 36, ready="siegecantready", sprite="scui_btn_danger"),
          "\t" + centred(RIN - SIEGE_BW, 0, SIEGE_BW, 36, 15, danger_tone("siegecantready"), key="scRadioSiegeCant",
                         extra='justify="center" upper_case="true"'),
          "</rect>",
          label(x0, -(by + 40), RIN, 20, 13, GOLD, text="{siegehint}", extra=ELLIPSIS)]
    return o


def allies():
    """E7: the Allies panel - the ally pick (a cycling button, nearest first; its arrow only while there is another to
    cycle to) with what they do for you and where; under it the barter with them (Give, Get, Trade: shown while an ally is
    picked and the barter is on - else {allynone} says why there) and a hint line (why not, how it works, or the last
    trade). E9 adds the radio commands' row between the pick and the barter: Send a fighter and Supply drop, shown while an
    ally is picked and the calls are on - else {allycallnone} says why there (no ally yet: how to get one) - each label
    the command or why it can't be made now; the hint line also tells of the call that is on."""
    x0 = X2 + PAD
    py = ALLY_Y + HEAD_H + 2              # the pick row
    cy = py + ALLY_ROW + 6                # E9: the commands row
    by = cy + ALLY_ROW + 6                # the barter row
    hy = by + ALLY_ROW + 4                # the hint line
    drop_x = ALLY_CALL_W + ALLY_GAP
    get_x = ALLY_GIVE_W + ALLY_GAP
    trade_x = get_x + ALLY_GET_W + ALLY_GAP
    trade_w = RIN - trade_x
    o = section(ALLY_Y, ALLY_H, "ui_game_symbol_map_trader", "scRadioAlliesHeader", x=X2, w=RW)
    o += [button("btnAllyPick", x0, -py, ALLY_PICK_W, ALLY_ROW, ready="allypickready"),
          centred(x0 + 10, -py, ALLY_PICK_W - 44, ALLY_ROW, 15, BODY, text="{allyname}", extra='justify="center" ' + ELLIPSIS),
          icon("ui_game_symbol_arrow_right", x0 + ALLY_PICK_W - 20, -(py + ALLY_ROW // 2), 18, depth=4, extra='visible="{allypickready}"'),
          centred(x0 + ALLY_PICK_W + 12, -py, RIN - ALLY_PICK_W - 12, ALLY_ROW, 14, DIM, text="{allystate}", extra=ELLIPSIS),
          '<rect depth="1" pos="%d,%d" width="%d" height="%d" visible="{allycallvisible}">' % (x0, -cy, RIN, ALLY_ROW),
          "\t" + button("btnAllyFighter", 0, 0, ALLY_CALL_W, ALLY_ROW, ready="allyfighterready"),
          "\t" + icon("ui_game_symbol_run", 22, -(ALLY_ROW // 2), 20, depth=4),
          "\t" + centred(36, 0, ALLY_CALL_W - 44, ALLY_ROW, 15, tone("allyfighterready"), text="{allyfighterlabel}",
                         extra='justify="center" upper_case="true" ' + ELLIPSIS),
          "\t" + button("btnAllyDrop", drop_x, 0, ALLY_CALL_W, ALLY_ROW, ready="allydropready"),
          "\t" + icon("ui_game_symbol_airdrop", drop_x + 22, -(ALLY_ROW // 2), 20, depth=4),
          "\t" + centred(drop_x + 36, 0, ALLY_CALL_W - 44, ALLY_ROW, 15, tone("allydropready"), text="{allydroplabel}",
                         extra='justify="center" upper_case="true" ' + ELLIPSIS),
          "</rect>",
          label(x0, -cy, RIN, 40, 14, DIM, text="{allycallnone}"),
          '<rect depth="1" pos="%d,%d" width="%d" height="%d" visible="{allyvisible}">' % (x0, -by, RIN, ALLY_ROW),
          "\t" + button("btnAllyGive", 0, 0, ALLY_GIVE_W, ALLY_ROW),
          "\t" + centred(0, 0, ALLY_GIVE_W, ALLY_ROW, 15, BODY, text="{allygive}", extra='justify="center" ' + ELLIPSIS),
          "\t" + button("btnAllyGet", get_x, 0, ALLY_GET_W, ALLY_ROW, ready="allygetready"),
          "\t" + centred(get_x, 0, ALLY_GET_W, ALLY_ROW, 15, BODY, text="{allyget}", extra='justify="center" ' + ELLIPSIS),
          "\t" + button("btnAllyTrade", trade_x, 0, trade_w, ALLY_ROW, ready="allytradeready"),
          "\t" + icon("ui_game_symbol_coin", trade_x + 22, -(ALLY_ROW // 2), 20, depth=4),
          "\t" + centred(trade_x + 36, 0, trade_w - 44, ALLY_ROW, 15, tone("allytradeready"), key="scRadioBarterTrade",
                         extra='justify="center" upper_case="true"'),
          "</rect>",
          label(x0, -by, RIN, 40, 14, DIM, text="{allynone}"),
          label(x0, -hy, RIN, 20, 13, "{allyhintcolor}", text="{allyhint}", extra=ELLIPSIS)]
    return o


def header():
    return [icon("ui_game_symbol_talk", 52, -40, 56),
            label(92, -8, WIN_W - 200, 44, 40, TITLE, text="{title}", extra='upper_case="true"'),
            label(94, -52, WIN_W - 200, 24, 17, GOLD, text="{status}"),
            button("btnClose", WIN_W - M - 56, -18, 56, 48),
            icon("ui_game_symbol_x", WIN_W - M - 28, -42, 24, depth=4)]


def calls():
    """The two calls side by side: each a big button with its icon, the reason it cannot go yet in its
    label, and what it does underneath."""
    gap = 16
    w = (PW - 2 * PAD - gap) // 2
    o = [plate("scui_window", M, -CALLS_Y, PW, CALLS_H)]
    for i, (name, sprite, lab, desc, ready) in enumerate((
            ("btnDrop", "ui_game_symbol_airdrop", "droplabel", "dropdesc", "dropready"),
            ("btnCall", "ui_game_symbol_players", "calllabel", "calldesc", "callready"))):
        x = M + PAD + i * (w + gap)
        by = CALLS_Y + 14
        o += [button(name, x, -by, w, 46, ready=ready),
              icon(sprite, x + 28, -(by + 23), 28, depth=4),
              centred(x + 50, -by, w - 58, 46, 17, tone(ready), text="{%s}" % lab,
                      extra='justify="center" upper_case="true" overflow="ClampContent" overflow_ellipsis="true"'),
              label(x + 2, -(by + 56), w - 4, CALLS_H - 14 - 56 - 6, 14, DIM, text="{%s}" % desc)]
    return o


def demand():
    """The raiders' demand: a red-rimmed callout with Pay / Refuse, only while one is open."""
    bw = 120
    return ['<rect depth="1" pos="%d,%d" width="%d" height="%d" visible="{demandvisible}">' % (M, -DEMAND_Y, PW, DEMAND_H),
            "\t" + plate("scui_btn_danger", 0, 0, PW, DEMAND_H),
            "\t" + centred(PAD, 0, PW - 2 * bw - 3 * PAD, DEMAND_H, 15, DANGER_TEXT, text="{demandtext}"),
            "\t" + button("btnDemandPay", PW - 2 * bw - PAD - 8, -5, bw, DEMAND_H - 10, ready="demandready"),
            "\t" + centred(PW - 2 * bw - PAD - 8, -5, bw, DEMAND_H - 10, 15, tone("demandready"), key="scRadioDemandPay",
                           extra='justify="center" upper_case="true"'),
            "\t" + button("btnDemandRefuse", PW - bw - PAD, -5, bw, DEMAND_H - 10, ready="demandready", sprite="scui_btn_danger"),
            "\t" + centred(PW - bw - PAD, -5, bw, DEMAND_H - 10, 15, danger_tone("demandready"), key="scRadioDemandRefuse",
                           extra='justify="center" upper_case="true"'),
            "</rect>"]


def runs():
    """R2: SUPPLY RUNS ON THE RADIO (the distress section before) - a row: the runner pick (a cycling button, by name; its arrow
    only while there is another), where they are, Call home (its label the command or why not, grayed by {runrecallready});
    {runnone} in the row's place while nobody is out; under it the distress lines, as the section had them."""
    x0 = M + PAD
    cw = PW - 2 * PAD                       # the content width
    ry = DISTRESS_Y + HEAD_H + 2            # the row
    call_x = cw - RUN_CALL_W
    state_x = RUN_PICK_W + 10
    state_w = call_x - 10 - state_x
    ty = ry + RUN_ROW + 4                   # the distress lines
    o = section(DISTRESS_Y, DISTRESS_H, "ui_game_symbol_map_waypoint_set", "scRadioRunsHeader")
    o += ['<rect depth="1" pos="%d,%d" width="%d" height="%d" visible="{runvisible}">' % (x0, -ry, cw, RUN_ROW),
          "\t" + button("btnRunPick", 0, 0, RUN_PICK_W, RUN_ROW, ready="runpickready"),
          "\t" + centred(10, 0, RUN_PICK_W - 44, RUN_ROW, 15, BODY, text="{runname}", extra='justify="center" ' + ELLIPSIS),
          "\t" + icon("ui_game_symbol_arrow_right", RUN_PICK_W - 20, -(RUN_ROW // 2), 18, depth=4, extra='visible="{runpickready}"'),
          "\t" + centred(state_x, 0, state_w, RUN_ROW, 14, DIM, text="{runstate}", extra=ELLIPSIS),
          "\t" + button("btnRunRecall", call_x, 0, RUN_CALL_W, RUN_ROW, ready="runrecallready"),
          "\t" + icon("ui_game_symbol_run", call_x + 22, -(RUN_ROW // 2), 20, depth=4),
          "\t" + centred(call_x + 36, 0, RUN_CALL_W - 44, RUN_ROW, 15, tone("runrecallready"), text="{runrecalllabel}",
                         extra='justify="center" upper_case="true" ' + ELLIPSIS),
          "</rect>",
          label(x0, -ry, cw, 40, 14, DIM, text="{runnone}"),
          label(x0, -ty, cw, DISTRESS_Y + DISTRESS_H - 4 - ty, 14, BODY, text="{distress}")]
    return o


def scout():
    n = len(SCOUTS)
    gap = 10
    bw = (PW - 2 * PAD - (n - 1) * gap) // n
    by = SCOUT_Y + HEAD_H + 4
    o = section(SCOUT_Y, SCOUT_H, "ui_game_symbol_search", "scRadioScoutHeader")
    for i, (name, sprite, key) in enumerate(SCOUTS):
        x = M + PAD + i * (bw + gap)
        o += [button(name, x, -by, bw, 36, ready="scoutready"),
              icon(sprite, x + 20, -(by + 18), 20, depth=4),
              centred(x + 34, -by, bw - 40, 36, 15, tone("scoutready"), key=key, extra='upper_case="true"')]
    o.append(label(M + PAD, -(by + 40), PW - 2 * PAD, 20, 13, DIM, text="{scoutdesc}"))
    return o


def barter():
    by = BARTER_Y + HEAD_H + 4
    x0 = M + PAD
    give_w, get_w, gap = 250, 210, 10
    trade_x = x0 + give_w + gap + get_w + gap
    trade_w = M + PW - PAD - trade_x
    o = section(BARTER_Y, BARTER_H, "ui_game_symbol_barter", "scRadioBarterHeader")
    o += [button("btnBarterGive", x0, -by, give_w, 36),
          centred(x0, -by, give_w, 36, 15, BODY, text="{bartergive}", extra='justify="center"'),
          button("btnBarterGet", x0 + give_w + gap, -by, get_w, 36),
          centred(x0 + give_w + gap, -by, get_w, 36, 15, BODY, text="{barterget}", extra='justify="center"'),
          button("btnBarterTrade", trade_x, -by, trade_w, 36, ready="barterready"),
          icon("ui_game_symbol_coin", trade_x + 22, -(by + 18), 20, depth=4),
          centred(trade_x + 36, -by, trade_w - 44, 36, 15, tone("barterready"), key="scRadioBarterTrade",
                  extra='justify="center" upper_case="true"'),
          label(x0, -(by + 40), PW - 2 * PAD, 20, 13, DIM, text="{barterdesc}")]
    return o


def build():
    o = ['\t<append xpath="/windows">',
         '\t\t<window name="windowScRadio" anchor="Center" pos="%d,%d" width="%d" height="%d" depth="20" '
         'controller="%s" cursor_area="true">' % (-WIN_W // 2, WIN_H // 2, WIN_W, WIN_H, CONTROLLER)]
    inner = [plate("scui_window", 0, 0, WIN_W, WIN_H)]
    inner += header()
    inner += calls()
    inner += section(CONTACTS_Y, CONTACTS_H, "ui_game_symbol_talk", "scRadioContactsHeader")
    inner.append(body(CONTACTS_Y, CONTACTS_H, "contacts"))
    inner += runs()                     # R2: Supply runs on the radio (the distress lines under its row)
    inner += section(OUTPOSTS_Y, OUTPOSTS_H, "ui_game_symbol_map_town", "scRadioOutpostsHeader")
    inner.append(body(OUTPOSTS_Y, OUTPOSTS_H, "outposts"))
    inner += demand()
    inner += scout()
    inner += barter()
    inner += enclaves()                 # E6: the right-hand column
    inner += requests()
    inner += allies()                   # E7
    inner += siege()
    inner.append(label(M + 4, -MSG_Y, PW - 8, WIN_H - MSG_Y - 8, 15, GOLD, text="{message}"))
    o += ["\t\t\t" + line for line in inner]
    o += ["\t\t</window>", "\t</append>"]
    return "\n".join(o) + "\n"


def main():
    with io.open(TARGET, "r", encoding="utf-8", newline="") as f:
        text = f.read()
    if "\r\n" in text:
        raise SystemExit("REFUSING: %s has CRLF lines; this generator writes LF like the rest of the file." % TARGET)
    block = BEGIN + "\n" + build() + END + "\n"
    if BEGIN in text and END in text:
        rest = text.split(END)[1]
        if rest.startswith("\n"): rest = rest[1:]     # the END line's own newline (the block brings it): what follows is kept as it is
        text = text.split(BEGIN)[0] + block + rest
    else:
        found = LEGACY.findall(text)
        if len(found) != 1:
            raise SystemExit("REFUSING: no GENERATED markers and %d hand-authored radio window(s) in %s."
                             % (len(found), TARGET))
        text = LEGACY.sub(lambda _m: block, text)
    with io.open(TARGET, "w", encoding="utf-8", newline="") as f:
        f.write(text)
    # Every section fits the window, and none overlaps the next.
    stack = [(CALLS_Y, CALLS_H), (CONTACTS_Y, CONTACTS_H), (DISTRESS_Y, DISTRESS_H), (OUTPOSTS_Y, OUTPOSTS_H),
             (DEMAND_Y, DEMAND_H), (SCOUT_Y, SCOUT_H), (BARTER_Y, BARTER_H)]
    for (y0, h0), (y1, _) in zip(stack, stack[1:]):
        assert y0 + h0 <= y1, "a section at %d runs into the next at %d" % (y0, y1)
    assert BARTER_Y + BARTER_H <= MSG_Y, "the barter section runs into the message"
    assert MSG_Y + 3 * LINE <= WIN_H, "the message runs past the window"
    assert HEAD_H + 7 * LINE <= OUTPOSTS_H, "the outposts' seven lines run past their plate"
    # R2: the calls keep three lines of description, the contacts three, Supply runs its row and two distress lines
    assert CALLS_H - 14 - 56 - 6 >= 3 * LINE, "the calls' descriptions lose their third line"
    assert CONTACTS_H - HEAD_H - 6 >= 3 * LINE, "the contacts' three lines run past their plate"
    assert HEAD_H + 2 + RUN_ROW + 4 + 2 * LINE + 4 <= DISTRESS_H, "the Supply runs row and two distress lines run past the plate"
    assert RUN_PICK_W + 10 + 160 + 10 + RUN_CALL_W <= PW - 2 * PAD, "the Supply runs row leaves too little room for where they are"
    # E6: the right-hand column beside the left one, inside the window, its sections stacked, its rows inside their plates
    assert M + PW + M <= X2 and X2 + RW + M == WIN_W, "the Enclaves column overlaps the station's, or misses the window's edge"
    assert WIN_W <= COMMUNITY_W, "the window is wider than the community window (which is known to fit)"
    right = [(ENC_Y, ENC_H), (REQ_Y, REQ_H), (ALLY_Y, ALLY_H), (SIEGE_Y, SIEGE_H)]
    for (y0, h0), (y1, _) in zip(right, right[1:]):
        assert y0 + h0 <= y1, "an Enclaves-column section at %d runs into the next at %d" % (y0, y1)
    assert SIEGE_Y + SIEGE_H <= WIN_H - 8, "the siege section runs past the window"
    for y, h, rows in [(ENC_Y, ENC_H, ENC_ROWS), (REQ_Y, REQ_H, REQ_ROWS)]:
        assert HEAD_H + 2 + COL_H + (rows + 1) * ROW_H <= h, "the rows of the section at %d (and its +N line) run past their plate" % y
    for cols in (ENC_COLS, REQ_COLS):
        for (_f, _k, cx, cw) in cols: assert cx + cw <= RIN, "a column (%s) runs past the plate" % _f
        for (a, b) in zip(cols, cols[1:]): assert a[2] + a[3] <= b[2], "columns %s and %s overlap" % (a[0], b[0])
    assert 2 * SIEGE_BW + 8 <= RIN, "the siege buttons overlap"
    assert SIEGE_Y + HEAD_H + 2 + 54 + 40 + 20 <= SIEGE_Y + SIEGE_H, "the siege hint runs past its plate"
    assert SIEGE_Y + SIEGE_H == BARTER_Y + BARTER_H, "the siege section's bottom is not level with the barter section's"
    # E7: the Allies panel's rows inside its plate, its buttons inside the column; E9: its commands row too
    assert HEAD_H + 2 + 3 * ALLY_ROW + 2 * 6 + 4 + 20 <= ALLY_H, "the Allies panel's rows run past its plate"
    assert ALLY_PICK_W + 12 < RIN and ALLY_GIVE_W + ALLY_GET_W + 2 * ALLY_GAP + 120 <= RIN, "the Allies panel's buttons run past the column"
    assert 2 * ALLY_CALL_W + ALLY_GAP <= RIN, "the Allies panel's command buttons run past the column"
    print("generate_radio_ui: wrote windowScRadio %dx%d (Enclaves column %d wide: %d enclave rows, %d request rows, the Allies panel "
          "with its commands; Supply runs with Call home)" % (WIN_W, WIN_H, RW, ENC_ROWS, REQ_ROWS))


if __name__ == "__main__":
    main()
