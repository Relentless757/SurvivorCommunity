#!/usr/bin/env python3
"""
Generates the radio window, windowScRadio, into Config/XUi_InGame/windows.xml between its GENERATED
markers. Run from anywhere:  python Tools/generate_radio_ui.py

Written by the AshfallUI session (2026-10-13, user: make the radio window look like Ashfall UI) on the
community window's rules (Tools/generate_community_ui.py) - same palette, plates and controls:

    [radio] RADIO STATION                                   [x]
            Powered - 2 of 3 contacts out there
    +- [AIR DROP button] ----------+- [CALL button] ----------+
    |  what it does / cooldown     |  what it does / cooldown |
    +- SURVIVORS HEARD ON THE RADIO --------------------------+
    +- SUPPLY RUNS CALLING FOR HELP --------------------------+
    +- OUTPOSTS REPORTING IN ---------------------------------+
    [ raiders' demand                      [PAY] [REFUSE] ]     (only while one is open)
    +- SCOUT FOR OUTPOSTS  [food][meds][ammo][mats][fuel] ----+
    +- BARTER  [give ..] [get ..] [TRADE] -------------------+
    the last action's message

RULES THAT MATTER (the community window's, unchanged):
- Buttons are <button> views: XUiC_ScRadio hooks each control's own OnPress (GetChildById), which a
  simplebutton's clickable child never raises. Each is a scui_btn plate with a label (and icon) over it.
- Every plate is a scui_* sprite handed [white]; AshfallUI ships premium art under the same names.
  Icons are vanilla menu symbols (AshfallUI draws them as their brass twins).
- A control that cannot act now keeps its plate but loses its hover plate, and its label dims: the
  {dropready} / {callready} / {scoutready} / {barterready} bindings say which. The labels still carry
  the reason (the controller writes it into {droplabel} / {calllabel} / {scoutdesc} / {barterdesc}).
- Control names are the controller's: btnClose, btnDrop, btnCall, btnScout<Resource>, btnBarterGive,
  btnBarterGet, btnBarterTrade, btnDemandPay, btnDemandRefuse.
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
WIN_W, WIN_H = 760, 940               # 940 tall: the community window is 924, a 1080p screen ~987
M = 24
PW = WIN_W - 2 * M                    # a section plate's width
PAD = 16                              # a section's content inset
CALLS_Y, CALLS_H = 84, 140
CONTACTS_Y, CONTACTS_H = 232, 106
DISTRESS_Y, DISTRESS_H = 346, 90
OUTPOSTS_Y, OUTPOSTS_H = 444, 150
DEMAND_Y, DEMAND_H = 600, 40
SCOUT_Y, SCOUT_H = 648, 104
BARTER_Y, BARTER_H = 760, 104
MSG_Y = 872
HEAD_H = 38                           # a section's heading band: icon, title, rule
LINE = 16                             # a 14-point text line

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


def section(y, h, sprite, key):
    """A section plate with its heading band: icon, upper-case title, a rule under them."""
    return [plate("scui_window", M, -y, PW, h),
            icon(sprite, M + PAD + 11, -(y + 19), 22),
            label(M + PAD + 30, -(y + 9), PW - 2 * PAD - 30, 24, 18, TITLE, key=key, extra='upper_case="true"'),
            plate("scui_divider", M + PAD, -(y + HEAD_H - 4), PW - 2 * PAD, 3, depth=1)]


def body(y, h, binding):
    """A section's text under its heading."""
    return label(M + PAD, -(y + HEAD_H + 2), PW - 2 * PAD, h - HEAD_H - 6, 14, BODY, text="{%s}" % binding)


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
            "\t" + button("btnDemandPay", PW - 2 * bw - PAD - 8, -5, bw, DEMAND_H - 10),
            "\t" + centred(PW - 2 * bw - PAD - 8, -5, bw, DEMAND_H - 10, 15, BODY, key="scRadioDemandPay",
                           extra='justify="center" upper_case="true"'),
            "\t" + button("btnDemandRefuse", PW - bw - PAD, -5, bw, DEMAND_H - 10, sprite="scui_btn_danger"),
            "\t" + centred(PW - bw - PAD, -5, bw, DEMAND_H - 10, 15, DANGER_TEXT, key="scRadioDemandRefuse",
                           extra='justify="center" upper_case="true"'),
            "</rect>"]


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
    inner += section(DISTRESS_Y, DISTRESS_H, "ui_game_symbol_map_waypoint_set", "scRadioDistressHeader")
    inner.append(body(DISTRESS_Y, DISTRESS_H, "distress"))
    inner += section(OUTPOSTS_Y, OUTPOSTS_H, "ui_game_symbol_map_town", "scRadioOutpostsHeader")
    inner.append(body(OUTPOSTS_Y, OUTPOSTS_H, "outposts"))
    inner += demand()
    inner += scout()
    inner += barter()
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
        text = text.split(BEGIN)[0] + block + text.split(END)[1].lstrip("\n")
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
    print("generate_radio_ui: wrote windowScRadio %dx%d" % (WIN_W, WIN_H))


if __name__ == "__main__":
    main()
