#!/usr/bin/env python3
"""
Generates the community window, windowScCommunity, into Config/XUi_InGame/windows.xml between its
GENERATED markers. Run from anywhere:  python Tools/generate_community_ui.py

Written by the AshfallUI session (2026-09-25) on the user's premium reference; XUiC_ScCommunity /
XUiC_ScMemberRow answer every binding and control here (the SurvivorCommunity session's work):

    [cabin] COMMUNITY                                  [OPEN STORAGE] [x]
            <community name>
    +- FOOD -+- WATER -+- MEDICAL -+- MATERIALS -+- AMMO -+- FUEL -+- LUXURIES -+
    +[MEMBERS n][OUTPOSTS 2/3][ACTIVITIES n]+- NAME --+ +- COMMUNITY MORALE -+
    | [search][filter]| | role . status          | |  68 / 100  STEADY  |
    | [pic] NAME  stat| | [portrait] health bar  | |  [=========      ] |
    |  ... 6 rows     | |            morale bar  | |  What is affecting |
    | Showing 6 of 8  | | SKILLS  5 rows         | |  HELPING  4 rows   |
    +-----------------+ | TRAITS  3 rows         | |  HURTING  4 rows   |
                        | ORDERS  3 buttons      | |  [beds callout]    |
                        | run picker / message   | +--------------------+
                        +------------------------+
    ESC Back | hint                                  SURVIVE . REBUILD . BELONG

RULES THAT MATTER:
- Buttons are <button> views, not simplebuttons: XUiC_ScCommunity hooks each control's own OnPress,
  which a simplebutton's clickable child never raises. Each is a scui_btn plate with a hover plate
  and a label over it.
- The search box must be the vanilla <textfield> template (the controller needs its text child).
- Portrait textures carry no texture= / textures= / autounload attribute: XUiV_Texture would null or
  destroy the picture the controller hands it.
- Page size is the number of XUiC_ScMemberRow rows (ROWS).
- Every plate is a scui_* sprite (Tools/import_ui_assets.py), handed [white]; AshfallUI ships
  premium art under the same names. Icons are vanilla menu symbols.
- Stamina is not drawn: NPCs have none ({selstaminavisible} is always False).
- The left panel has three tabs (btnTabMembers / btnTabOutposts / btnTabActivities, {tabmembers} / {taboutposts} /
  {tabactivities}): the Outposts tab swaps the member list for the outpost rows (XUiC_ScOutpostRow) and the survivor
  detail for the outpost detail and the network summary; the Activities tab (round wl) for the activity rows
  (btnAct1..4, plain buttons) and the selected activity with its do-button (btnActDo); the morale panel stays.
  Wrappers that switch tabs are depth 0, so every depth inside them is what it was before the tabs existed (XUi
  depth is cumulative).
- C1 (round 22, 3.4): btnWorkMenu on the ORDERS heading opens the work-and-roles row in the run picker's place (the two never
  show together; {msgvisible} hides the message under either): btnRoleCook / Smith / Chemist / Crafter toggle 0-XNPCCore's
  crafting roles ({role0color}..{role3color} lit when on) and btnWork calls a resident to work or ends her shift
  ({worklabel}, {workcolor}). The 3.2 build answers the bindings and never shows the button.
- Round 32b (U32): two looks in one <conditional> - the window above when AshfallUI is loaded (it ships premium art under
  the scui_* names), vanilla's look otherwise (Tools/vanilla_look.py: vanilla's message box for the window, a header bar
  under the title, [darkGrey] panels with black rims, vanilla's buttons and rows, white text). The two morale bars
  (community and member) take their fill from the band of the word beside them: {moralefillsprite} / {selmoodfillsprite}
  pick scui_fill_good / _skill / _health for AshfallUI's art, {moralewordcolor} / {selmoodcolor} tint vanilla's flat fill
  (green from 50, amber from LowMoraleThreshold, red below it - XUiC_ScCommunity.BandColor).
"""

import io
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import vanilla_look  # noqa: E402
MOD = os.path.dirname(HERE)
TARGET = os.path.join(MOD, "Config", "XUi_InGame", "windows.xml")

BEGIN = "\t<!-- GENERATED:BEGIN sccommunity (Tools/generate_community_ui.py) -->"
END = "\t<!-- GENERATED:END sccommunity -->"
# What this generator replaced the first time: the hand-authored window - its comment banner, the
# <append xpath="/windows"> that holds it, and that append's close. The generated block carries its
# own append, so the markers wrap all of it.
LEGACY = re.compile(r"\t<!-- The community window\..*?\t\t</window>\n\t</append>\n", re.S)

CONTROLLER = "SurvivorCommunity.ScCommunity, SurvivorCommunity"
ROW_CONTROLLER = "SurvivorCommunity.ScMemberRow, SurvivorCommunity"
OP_CONTROLLER = "SurvivorCommunity.ScOutpostRow, SurvivorCommunity"

# ---------------------------------------------------------------- geometry
WIN_W, WIN_H, DEPTH = 1500, 924, 20   # at 924 tall it reaches the toolbelt: depth 20, as SO's pages
M = 24
RES_Y, RES_H = 88, 88                 # the resources strip
PAN_Y, PAN_H = 188, 696               # the three panels
FOOT_Y = 892
MEM_X, MEM_W = M, 440
DET_X, DET_W = MEM_X + MEM_W + 12, 560
MOR_X = DET_X + DET_W + 12
MOR_W = WIN_W - M - MOR_X             # 428

ROWS, ROW_H, ROW_GAP, ROWS_TOP = 6, 80, 8, 112
SKILL_TOP, SKILL_H, SKILL_GAP = 292, 28, 2
TRAIT_TOP, TRAIT_H, TRAIT_GAP = 480, 28, 2
ORDER_Y, ORDER_H = 606, 40
RUN_Y, RUN_H = 654, 34
FACTOR_H, FACTOR_GAP = 36, 2
OP_ROWS = 6
STAFF_TOP, STAFF_H, STAFF_GAP = 292, 28, 2      # the garrison: two posts and the level line
NET_TOP, NET_H, NET_GAP = 420, 28, 2

# ---------------------------------------------------------------- palette (static attributes)
TITLE = "226,219,203,255"
BODY = "226,220,208,255"
DIM = "190,182,166,255"
GOLD = "206,178,120,255"
GOOD = "142,208,108,255"
BAD = "226,108,86,255"
DANGER_TEXT = "240,150,130,255"
RULE = "196,168,112,70"
THIN = (3, 3)                         # a bar's fill window inside its rim

# (icon, label key, value binding, extra line binding or None); the green per-day delta is "op" + the binding minus "res"
RESOURCES = (("ui_game_symbol_fork", "scRunFood", "resfood", "fooddays"),
             ("ui_game_symbol_water", "scRunWater", "reswater", "waterdays"),
             ("ui_game_symbol_medical", "scRunMeds", "resmeds", None),
             ("ui_game_symbol_brick", "scRunMaterials", "resmats", None),
             ("ui_game_symbol_shape_ammo", "scRunAmmo", "resammo", None),
             ("ui_game_symbol_gas", "scRunFuel", "resfuel", None),
             ("ui_game_symbol_beer", "scRunLuxury", "resluxury", None))
SKILLS = (("ui_game_symbol_cardio", "scSkillCardio", "skcardio"),
          ("ui_game_symbol_intellect", "scSkillWits", "skwits"),
          ("ui_game_symbol_melee", "scSkillFighting", "skfighting"),
          ("ui_game_symbol_rifle", "scSkillShooting", "skshooting"))
RUNS = (("btnRunFood", "scRunFood"), ("btnRunWater", "scRunWater"), ("btnRunMeds", "scRunMeds"), ("btnRunAmmo", "scRunAmmo"),
        ("btnRunMaterials", "scRunMaterials"), ("btnRunFuel", "scRunFuel"))
# the outposts network summary: icon, label key, value binding, colour binding (None = body text)
NETS = (("ui_game_symbol_map_town", "scUiNetSlots", "netslots", None),
        ("ui_game_symbol_talk", "scUiNetRadio", "netradio", "netradiocolor"),
        ("ui_game_symbol_loot_sack", "scUiNetDeliveries", "netyield", None),
        ("ui_game_symbol_brick", "scUiNetCosts", "netcosts", None),
        ("ui_game_symbol_wrench", "scUiNetStatus", "netstate", "netstatecolor"))
HOW = ("scUiOpHow1", "scUiOpHow2", "scUiOpHow3")
# C1 (round 22): the work-and-roles row - 0-XNPCCore's four crafting roles (button, label key, colour binding: lit when on)
# and the shift's Call to work / End work; a small button on the ORDERS heading (btnWorkMenu) opens it in the run picker's place
ROLE_BUTTONS = (("btnRoleCook", "scUiRoleCook", "role0color"), ("btnRoleSmith", "scUiRoleSmith", "role1color"),
                ("btnRoleChemist", "scUiRoleChemist", "role2color"), ("btnRoleCrafter", "scUiRoleCrafter", "role3color"))
ROLE_W = 80                           # each role toggle; btnWork takes the rest of the row
WORK_MENU_W, WORK_MENU_Y, WORK_MENU_H = 150, 578, 24
ACT_ROWS = 4                          # activity rows on the Activities tab (ScActivities.All, XUiC_ScCommunity.ActivitySlots)
OP_ACTIONS = (("btnOpTrack", "optracklabel", BODY, False), ("btnOpAssign", "opassignlabel", BODY, False),
              ("btnOpUpgrade", "opupgradelabel", "{opupgradecolor}", False), ("btnOpDisband", "opdisbandlabel", DANGER_TEXT, True))


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
    """A label centred in a box whose top is py (negative) and height ph: a label draws its line
    from the top of its own box, so it goes at the middle less half its font size."""
    return label(x, py - (ph - size) // 2, w, size + 6, size, color, text=text, key=key, extra=extra)


def rule(x, y, w, h=2):
    return '<sprite depth="2" pos="%d,%d" width="%d" height="%d" color="%s"/>' % (x, y, w, h, RULE)


def button(name, x, y, w, h, sprite="scui_btn", hover="scui_btn_hover", extra=""):
    return ('<button depth="2" name="%s" pos="%d,%d" width="%d" height="%d" sprite="%s" hoversprite="%s" '
            'defaultcolor="[white]" hovercolor="[white]" type="sliced" hoverscale="1.0" sound="[paging_click]"%s/>'
            % (name, x, y, w, h, sprite, hover, (" " + extra) if extra else ""))


def bar(x, y, w, h, fill, value, depth=2):
    ix, iy = THIN
    return [plate("scui_bar_track", x, y, w, h, depth=depth),
            '<sprite depth="%d" sprite="%s" type="filled" fill="{%s}" color="[white]" pos="%d,%d" width="%d" height="%d"/>'
            % (depth + 1, fill, value, x + ix, y - iy, w - 2 * ix, h - 2 * iy),
            plate("scui_bar_frame", x, y, w, h, depth=depth + 2, extra='fillcenter="false"')]


def header():
    sx = WIN_W - M - 56 - 12 - 250
    return [icon("ui_game_symbol_map_cabin", 60, -44, 64),
            label(104, -6, 700, 52, 48, TITLE, key="scUiTitle", extra='upper_case="true"'),
            label(106, -56, 700, 26, 22, GOLD, text="{communityname}", extra='upper_case="true"'),
            button("btnStorage", sx, -18, 250, 48),
            icon("ui_game_symbol_store_all_up", sx + 34, -42, 30, depth=4),
            centred(sx + 56, -18, 180, 48, 20, BODY, key="scUiOpenStorage", extra='justify="center" upper_case="true"'),
            button("btnClose", WIN_W - M - 56, -18, 56, 48),
            icon("ui_game_symbol_x", WIN_W - M - 28, -42, 24, depth=4)]


def resources():
    """Seven cells of (1500 - 48) / 7 = 207: the icon on the left, label / number / delta / extra line beside it."""
    gw = (WIN_W - 2 * M) // len(RESOURCES)
    o = [plate("scui_window", M, -RES_Y, WIN_W - 2 * M, RES_H)]
    for i, (sprite, key, binding, extra) in enumerate(RESOURCES):
        x0 = M + i * gw
        if i:
            o.append(rule(x0, -(RES_Y + 14), 2, RES_H - 28))
        o.append(icon(sprite, x0 + 38, -(RES_Y + RES_H // 2), 36))
        o.append(label(x0 + 66, -(RES_Y + 10), gw - 72, 20, 15, DIM, key=key, extra='upper_case="true"'))
        o.append(label(x0 + 66, -(RES_Y + 30), 80, 34, 30, TITLE, text="{%s}" % binding))
        o.append(label(x0 + 120, -(RES_Y + 40), gw - 128, 20, 15, GOOD, text="{op%s}" % binding[3:], extra='justify="right"'))
        if extra:
            o.append(label(x0 + 66, -(RES_Y + 66), gw - 72, 18, 13, DIM, text="{%s}" % extra))
    return o


def member_row(i):
    y = -(ROWS_TOP + i * (ROW_H + ROW_GAP))
    w = MEM_W - 40
    st = ("{# rowselected ? 'scui_row_selected' : (rowhovered ? 'scui_row_hover' : 'scui_row') }")
    return "\n".join([
        '<rect depth="2" name="memberRow%d" pos="20,%d" width="%d" height="%d" controller="%s" style="press, hover" '
        'sound="[recipe_click]" sound_play_on_hover="[default_hover]" visible="{rowvisible}">' % (i, y, w, ROW_H, ROW_CONTROLLER),
        "\t" + plate(st, 0, 0, w, ROW_H),
        "\t" + plate("scui_portrait", 6, -6, 68, 68, depth=1),
        "\t" + icon("ui_game_symbol_player", 40, -40, 40, extra='visible="{rownoportrait}"'),
        '\t<texture depth="3" name="rowPortrait" pos="10,-10" width="60" height="60" visible="{rowhasportrait}"/>',
        "\t" + label(86, -12, w - 100, 26, 22, TITLE, text="{rowname}", extra='upper_case="true"'),
        "\t" + icon("{rowroleicon}", 98, -56, 22),
        "\t" + label(114, -45, 140, 22, 18, DIM, text="{rowrole}"),
        "\t" + label(w - 180, -45, 166, 22, 16, BODY, text="{rowstatus}", extra='justify="right"'),
        "</rect>"])


TABS = (("btnTabMembers", "scUiMembers", "membercount", "tabmembers"),
        ("btnTabOutposts", "scUiOutposts", "outpostcount", "taboutposts"),
        ("btnTabActivities", "scUiActivities", "luxcount", "tabactivities"))


def tabs():
    """MEMBERS n | OUTPOSTS held/slots | ACTIVITIES luxuries: the active tab gets the selected plate over its button -
    scui_tab_selected, its own name since 2026-10-13: the member rows' scui_row_selected is cut for 80-unit rows and
    read too heavy at 44 (user); AshfallUI ships the nav bar's selected plate under it.
    Three to a row they are 128 wide, so the label is 15 px with its count in the last 34."""
    w = (MEM_W - 40 - 8 * (len(TABS) - 1)) // len(TABS)
    o = []
    for i, (name, key, count, active) in enumerate(TABS):
        x = 20 + i * (w + 8)
        o += [button(name, x, -12, w, 44),
              '<rect depth="0" pos="0,0" width="%d" height="%d" visible="{%s}">' % (MEM_W, 60, active),
              "\t" + plate("scui_tab_selected", x, -12, w, 44, depth=3),
              "</rect>",
              centred(x + 8, -12, w - 46, 44, 15, TITLE, key=key, extra='upper_case="true"'),
              centred(x + w - 40, -12, 34, 44, 15, GOLD, text="{%s}" % count, extra='justify="right"')]
    return o


def member_list():
    o = [plate("scui_row_thin", 20, -60, 250, 40, depth=1),
         icon("ui_game_symbol_search", 40, -80, 24),
         '<textfield depth="5" name="searchInput" pos="58,-64" width="204" height="32" search_field="true" '
         'close_group_on_tab="true" clear_button="true" backgroundcolor="0,0,0,0"/>',
         button("btnFilter", 280, -60, MEM_W - 300, 40),
         centred(284, -60, MEM_W - 340, 40, 17, BODY, text="{filterlabel}", extra='justify="center"'),
         icon("ui_game_symbol_sort", MEM_W - 36, -80, 20, depth=4)]
    for i in range(ROWS):
        o.append(member_row(i))
    fy = ROWS_TOP + ROWS * (ROW_H + ROW_GAP) + 4
    o += [label(20, -(fy + 6), 260, 22, 17, DIM, text="{showingtext}"),
          '<rect depth="2" pos="0,0" width="%d" height="%d" visible="{pagevisible}">' % (MEM_W, PAN_H),
          "\t" + button("btnPrev", MEM_W - 104, -fy, 38, 34),
          "\t" + icon("ui_game_symbol_arrow_left", MEM_W - 85, -(fy + 17), 18, depth=4),
          "\t" + button("btnNext", MEM_W - 58, -fy, 38, 34),
          "\t" + icon("ui_game_symbol_arrow_right", MEM_W - 39, -(fy + 17), 18, depth=4),
          "</rect>"]
    return o


def outpost_row(i):
    y = -(ROWS_TOP + i * (ROW_H + ROW_GAP))
    w = MEM_W - 40
    st = ("{# oprowselected ? 'scui_row_selected' : (oprowhovered ? 'scui_row_hover' : 'scui_row') }")
    return "\n".join([
        '<rect depth="2" name="outpostRow%d" pos="20,%d" width="%d" height="%d" controller="%s" style="press, hover" '
        'sound="[recipe_click]" sound_play_on_hover="[default_hover]" visible="{oprowvisible}">' % (i, y, w, ROW_H, OP_CONTROLLER),
        "\t" + plate(st, 0, 0, w, ROW_H),
        "\t" + plate("scui_portrait", 6, -6, 68, 68, depth=1),
        "\t" + icon("{oprowicon}", 40, -40, 40),
        "\t" + label(86, -12, w - 100, 26, 22, TITLE, text="{oprowname}", extra='upper_case="true"'),
        "\t" + label(86, -45, 150, 22, 17, DIM, text="{oprowtype}"),
        "\t" + label(w - 196, -45, 182, 22, 16, "{oprowstatuscolor}", text="{oprowstatus}", extra='justify="right"'),
        "</rect>"])


def outpost_list():
    o = [label(20, -70, MEM_W - 40, 24, 17, DIM, text="{opsummary}")]
    for i in range(OP_ROWS):
        o.append(outpost_row(i))
    fy = ROWS_TOP + OP_ROWS * (ROW_H + ROW_GAP) + 4
    o += ['<rect depth="1" pos="20,-%d" width="%d" height="%d" visible="{opempty}">' % (ROWS_TOP, MEM_W - 40, 220),
          "\t" + label(0, -12, MEM_W - 40, 30, 24, TITLE, key="scUiOpNone", extra='upper_case="true"'),
          "\t" + label(0, -52, MEM_W - 40, 150, 18, BODY, key="scUiOpNoneHint"),
          "</rect>",
          label(20, -(fy + 6), 260, 22, 17, DIM, text="{opshowingtext}"),
          '<rect depth="2" pos="0,0" width="%d" height="%d" visible="{oppagevisible}">' % (MEM_W, PAN_H),
          "\t" + button("btnOpPrev", MEM_W - 104, -fy, 38, 34),
          "\t" + icon("ui_game_symbol_arrow_left", MEM_W - 85, -(fy + 17), 18, depth=4),
          "\t" + button("btnOpNext", MEM_W - 58, -fy, 38, 34),
          "\t" + icon("ui_game_symbol_arrow_right", MEM_W - 39, -(fy + 17), 18, depth=4),
          "</rect>"]
    return o


def activity_list():
    """The Activities tab's left panel: what the stockpile holds for them, then one row (a plain button) per activity."""
    w = MEM_W - 40
    o = [label(20, -70, w, 24, 17, DIM, text="{actsummary}")]
    for i in range(1, ACT_ROWS + 1):
        y = ROWS_TOP + (i - 1) * (ROW_H + ROW_GAP)
        o += ['<rect depth="1" pos="0,0" width="%d" height="%d" visible="{act%dvisible}">' % (MEM_W, PAN_H, i),
              "\t" + button("btnAct%d" % i, 20, -y, w, ROW_H, sprite="scui_row", hover="scui_row_hover"),
              '\t<rect depth="0" pos="0,0" width="%d" height="%d" visible="{act%dselected}">' % (MEM_W, PAN_H, i),
              "\t\t" + plate("scui_row_selected", 20, -y, w, ROW_H, depth=3),
              "\t</rect>",
              "\t" + plate("scui_portrait", 26, -(y + 6), 68, 68, depth=4),
              "\t" + icon("{act%dicon}" % i, 60, -(y + 40), 40, depth=5),
              "\t" + label(106, -(y + 12), w - 100, 26, 22, TITLE, text="{act%dname}" % i, extra='upper_case="true"'),
              "\t" + label(106, -(y + 45), w - 106, 22, 16, "{act%dstatuscolor}" % i, text="{act%dstatus}" % i),
              "</rect>"]
    return o


def members():
    o = ['<rect depth="1" name="membersPanel" pos="%d,-%d" width="%d" height="%d">' % (MEM_X, PAN_Y, MEM_W, PAN_H),
         "\t" + plate("scui_window", 0, 0, MEM_W, PAN_H)]
    o += ["\t" + t.replace("\n", "\n\t") for t in tabs()]
    o.append('\t<rect depth="0" name="memberList" pos="0,0" width="%d" height="%d" visible="{tabmembers}">' % (MEM_W, PAN_H))
    o += ["\t\t" + t.replace("\n", "\n\t\t") for t in member_list()]
    o.append("\t</rect>")
    o.append('\t<rect depth="0" name="outpostList" pos="0,0" width="%d" height="%d" visible="{taboutposts}">' % (MEM_W, PAN_H))
    o += ["\t\t" + t.replace("\n", "\n\t\t") for t in outpost_list()]
    o.append("\t</rect>")
    o.append('\t<rect depth="0" name="activityList" pos="0,0" width="%d" height="%d" visible="{tabactivities}">' % (MEM_W, PAN_H))
    o += ["\t\t" + t.replace("\n", "\n\t\t") for t in activity_list()]
    o += ["\t</rect>", "</rect>"]
    return o


def stat(y, sprite, key, value, maxtext, fill, fillsprite, extra_lines=()):
    x = 248
    return ([icon(sprite, 228, -(y + 14), 24),
             label(x, -(y + 4), 170, 20, 16, DIM, key=key, extra='upper_case="true"'),
             label(x + 160, -(y + 2), 128, 22, 18, BODY, text="{%s} / %s" % (value, maxtext), extra='justify="right"')]
            + bar(x, -(y + 28), 288, 16, fillsprite, fill) + list(extra_lines))


def member_detail():
    tw = DET_W - 48
    o = ['\t<rect depth="1" name="noSelection" visible="{# selvisible ? \'false\' : \'true\' }">',
         "\t\t" + label(24, -16, tw, 44, 38, TITLE, text="{selname}", extra='upper_case="true"'),
         "\t\t" + label(24, -70, tw, 90, 20, DIM, key="scWinSelectHint"),
         "\t</rect>",
         '\t<rect depth="1" name="selection" visible="{selvisible}">',
         "\t\t" + label(24, -12, tw, 46, 40, TITLE, text="{selname}", extra='upper_case="true"'),
         "\t\t" + icon("{selroleicon}", 36, -72, 24),
         "\t\t" + label(54, -61, 170, 24, 20, GOLD, text="{selrole}"),
         "\t\t" + label(222, -61, 16, 24, 20, DIM, text="-"),
         "\t\t" + label(240, -61, tw - 216, 24, 20, BODY, text="{selstatus}"),
         "\t\t" + plate("scui_portrait", 24, -92, 176, 176, depth=1),
         "\t\t" + icon("ui_game_symbol_player", 112, -180, 96, extra='visible="{selnoportrait}"'),
         '\t\t<texture depth="3" name="selPortrait" pos="32,-100" width="160" height="160" visible="{selhasportrait}"/>']
    for line in (stat(96, "ui_game_symbol_add", "scUiHealth", "selhealth", "{selhealthmax}", "selhealthfill",
                      "scui_fill_health")
                 + stat(150, "ui_game_symbol_player", "scUiMood", "selmoodvalue", "100", "selmoodfill", "{selmoodfillsprite}",
                        [label(248, -198, 288, 22, 17, GOLD, text="{selmoodword}")])):
        o.append("\t\t" + line)
    o.append("\t\t" + label(248, -224, 288, 44, 16, "{selflagscolor}", text="{selflags}"))

    # skills
    o += ["\t\t" + label(24, -262, 240, 24, 22, TITLE, key="scUiSkills", extra='upper_case="true"'),
          "\t\t" + plate("scui_divider", 24, -287, tw, 3, depth=1)]
    rows = [(s, k, b, None) for s, k, b in SKILLS] + [("{skspecicon}", None, "skspec", "{skspecvisible}")]
    for i, (sprite, key, binding, vis) in enumerate(rows):
        y = SKILL_TOP + i * (SKILL_H + SKILL_GAP)
        name = (label(62, -(y + 4), 150, 22, 17, BODY, key=key) if key
                else label(62, -(y + 4), 150, 22, 17, BODY, text="{skspecname}"))
        body = [plate("scui_row_thin", 24, -y, tw, SKILL_H, depth=1),
                icon(sprite, 44, -(y + SKILL_H // 2), 20),
                name] + bar(216, -(y + 8), 240, 12, "scui_fill_skill", binding + "fill") + [
                label(462, -(y + 4), tw - 438, 22, 16, BODY, text="{%s}" % binding, extra='justify="right"')]
        if vis:
            o.append('\t\t<rect depth="1" visible="%s">' % vis)
            o += ["\t\t\t" + b for b in body]
            o.append("\t\t</rect>")
        else:
            o += ["\t\t" + b for b in body]

    # traits
    o += ["\t\t" + label(24, -450, 240, 24, 22, TITLE, key="scUiTraits", extra='upper_case="true"'),
          "\t\t" + plate("scui_divider", 24, -475, tw, 3, depth=1)]
    for j in range(1, 4):
        y = TRAIT_TOP + (j - 1) * (TRAIT_H + TRAIT_GAP)
        o += ['\t\t<rect depth="1" visible="{trait%dvisible}">' % j,
              "\t\t\t" + plate("scui_row_thin", 24, -y, tw, TRAIT_H, depth=1),
              "\t\t\t" + icon("{trait%dicon}" % j, 44, -(y + TRAIT_H // 2), 22),
              "\t\t\t" + label(62, -(y + 4), 150, 22, 17, BODY, text="{trait%dname}" % j),
              "\t\t\t" + label(216, -(y + 5), tw - 206, 20, 15, DIM, text="{trait%ddesc}" % j),
              "\t\t</rect>"]

    # orders
    bw = (tw - 16) // 3
    o += ["\t\t" + label(24, -580, 240, 24, 22, TITLE, key="scUiOrders", extra='upper_case="true"'),
          "\t\t" + label(264, -583, tw - 240, 20, 15, DIM, text="{runheader}", extra='justify="right" visible="{runpicking}"'),
          # C1: opens the work-and-roles row (hidden while the run picker is open: its heading sits here)
          "\t\t" + button("btnWorkMenu", 24 + tw - WORK_MENU_W, -WORK_MENU_Y, WORK_MENU_W, WORK_MENU_H, extra='visible="{workmenuvisible}"'),
          "\t\t" + centred(24 + tw - WORK_MENU_W, -WORK_MENU_Y, WORK_MENU_W, WORK_MENU_H, 13, BODY, text="{workmenulabel}",
                           extra='justify="center" upper_case="true" visible="{workmenuvisible}"'),
          "\t\t" + button("btnFollow", 24, -ORDER_Y, bw, ORDER_H),
          "\t\t" + centred(24, -ORDER_Y, bw, ORDER_H, 18, BODY, text="{followlabel}", extra='justify="center" upper_case="true"'),
          "\t\t" + button("btnRun", 32 + bw, -ORDER_Y, bw, ORDER_H),
          "\t\t" + centred(32 + bw, -ORDER_Y, bw, ORDER_H, 18,
                           "{# runvisible ? '226,220,208,255' : '140,134,124,255' }",
                           text="{runlabel}", extra='justify="center" upper_case="true"'),
          "\t\t" + button("btnLeave", 40 + 2 * bw, -ORDER_Y, bw, ORDER_H, sprite="scui_btn_danger", hover="scui_btn_danger"),
          "\t\t" + centred(40 + 2 * bw, -ORDER_Y, bw, ORDER_H, 18, DANGER_TEXT, text="{leavelabel}",
                           extra='justify="center" upper_case="true"'),
          # btnHome is still hooked by the controller; "Follow me" already turns into "Go home".
          "\t\t" + button("btnHome", 24, -ORDER_Y, bw, ORDER_H, extra='visible="false"')]
    # six run kinds since round wl (water): 80 wide, so 13 px labels ("MATERIALS" fits)
    rw = (tw - (len(RUNS) - 1) * 6) // len(RUNS)
    o.append('\t\t<rect depth="1" visible="{runpicking}">')
    for k, (name, key) in enumerate(RUNS):
        x = 24 + k * (rw + 6)
        o += ["\t\t\t" + button(name, x, -RUN_Y, rw, RUN_H),
              "\t\t\t" + centred(x, -RUN_Y, rw, RUN_H, 13, BODY, key=key, extra='justify="center" upper_case="true"')]
    o.append("\t\t</rect>")
    # C1: the work-and-roles row in the run picker's place: four role toggles (lit when on) and Call to work / End work
    o.append('\t\t<rect depth="1" visible="{workpicking}">')
    for k, (name, key, binding) in enumerate(ROLE_BUTTONS):
        x = 24 + k * (ROLE_W + 6)
        o += ["\t\t\t" + button(name, x, -RUN_Y, ROLE_W, RUN_H),
              "\t\t\t" + centred(x, -RUN_Y, ROLE_W, RUN_H, 13, "{%s}" % binding, key=key, extra='justify="center" upper_case="true"')]
    wx = 24 + len(ROLE_BUTTONS) * (ROLE_W + 6)
    ww = tw - len(ROLE_BUTTONS) * (ROLE_W + 6)
    o += ["\t\t\t" + button("btnWork", wx, -RUN_Y, ww, RUN_H),
          "\t\t\t" + centred(wx, -RUN_Y, ww, RUN_H, 13, "{workcolor}", text="{worklabel}", extra='justify="center" upper_case="true"'),
          "\t\t</rect>",
          "\t\t" + label(24, -(RUN_Y + 2), tw, 32, 15, BODY, text="{message}", extra='visible="{msgvisible}"'),
          "\t</rect>"]
    return o


def outpost_detail():
    tw = DET_W - 48
    o = ['<rect depth="1" visible="{# opselvisible ? \'false\' : \'true\' }">',
         "\t" + label(24, -16, tw, 44, 38, TITLE, key="scUiOutposts", extra='upper_case="true"'),
         "\t" + label(24, -70, tw, 70, 20, DIM, key="scUiOpSelectHint")]
    for j, key in enumerate(HOW):
        o.append("\t" + label(24, -(150 + j * 30), tw, 24, 16, BODY, key=key))
    o += ["\t" + label(24, -262, tw, 24, 22, TITLE, key="scUiPerks", extra='upper_case="true"'),
          "\t" + plate("scui_divider", 24, -287, tw, 3, depth=1),
          "\t" + label(24, -298, tw, 84, 16, GOOD, text="{opperks}"),
          "</rect>",
          '<rect depth="1" visible="{opselvisible}">',
          "\t" + label(24, -12, tw, 46, 40, TITLE, text="{opname}", extra='upper_case="true"'),
          "\t" + icon("{opicon}", 36, -72, 24),
          "\t" + label(54, -61, tw - 40, 24, 20, GOLD, text="{opsub}"),
          "\t" + plate("scui_portrait", 24, -92, 176, 176, depth=1),
          "\t" + icon("{opicon}", 112, -180, 96)]
    facts = (("scUiOpState", "opstate", "{opstatecolor}"), ("scUiOpProduces", "opyield", BODY),
             ("scUiOpDistance", "opdist", BODY), ("scUiOpWaiting", "opwaiting", BODY))
    for k, (key, binding, color) in enumerate(facts):
        y = 96 + k * 42
        w = tw - 200 - (132 if k == 0 else 0)          # the first fact shares its row with the mode button
        o += ["\t" + label(224, -y, w, 18, 15, DIM, key=key, extra='upper_case="true"'),
              "\t" + label(224, -(y + 18), w, 22, 18, color, text="{%s}" % binding)]
    o += ["\t" + button("btnOpMode", 24 + tw - 124, -94, 124, 38),
          "\t" + centred(24 + tw - 124, -94, 124, 38, 15, BODY, text="{opmodelabel}", extra='justify="center" upper_case="true"')]
    # the garrison: two posts (a member, an empty post, or the post level 2 unlocks) and the level line
    o += ["\t" + label(24, -262, tw, 24, 22, TITLE, key="scUiGarrison", extra='upper_case="true"'),
          "\t" + plate("scui_divider", 24, -287, tw, 3, depth=1)]
    for n in (1, 2):
        y = STAFF_TOP + (n - 1) * (STAFF_H + STAFF_GAP)
        o += ["\t" + plate("scui_row_thin", 24, -y, tw, STAFF_H, depth=1),
              "\t" + icon("{st%dicon}" % n, 44, -(y + STAFF_H // 2), 20),
              "\t" + label(62, -(y + 4), 200, 22, 17, "{st%dnamecolor}" % n, text="{st%dname}" % n),
              "\t" + label(262, -(y + 5), tw - 290, 20, 16, GOOD, text="{st%dbonus}" % n, extra='justify="right"'),
              '\t<rect depth="1" visible="{st%drecallvisible}">' % n,
              "\t\t" + button("btnStaff%dRecall" % n, 24 + tw - 30, -(y + 2), 26, 24),
              "\t\t" + icon("ui_game_symbol_x", 24 + tw - 17, -(y + 14), 14, depth=4),
              "\t</rect>"]
    y = STAFF_TOP + 2 * (STAFF_H + STAFF_GAP)
    o += ["\t" + plate("scui_row_thin", 24, -y, tw, STAFF_H, depth=1),
          "\t" + icon("ui_game_symbol_hammer", 44, -(y + STAFF_H // 2), 20),
          "\t" + label(62, -(y + 4), 200, 22, 17, BODY, text="{oplevel}"),
          "\t" + label(262, -(y + 5), tw - 250, 20, 16, DIM, text="{opnext}", extra='justify="right"'),
          "</rect>"]
    # the network: every outpost together, shown with or without a selection
    o += [label(24, -(NET_TOP - 30), 240, 24, 22, TITLE, key="scUiNetwork", extra='upper_case="true"'),
          plate("scui_divider", 24, -(NET_TOP - 5), tw, 3, depth=1)]
    for i, (sprite, key, binding, color) in enumerate(NETS):
        y = NET_TOP + i * (NET_H + NET_GAP)
        o += [plate("scui_row_thin", 24, -y, tw, NET_H, depth=1),
              icon(sprite, 44, -(y + NET_H // 2), 20),
              label(62, -(y + 4), 130, 22, 17, BODY, key=key),
              label(196, -(y + 5), tw - 180, 20, 16, ("{%s}" % color) if color else BODY, text="{%s}" % binding,
                    extra='justify="right"')]
    bw = (tw - 3 * 8) // 4
    o += ['<rect depth="1" visible="{opselvisible}">',
          "\t" + label(24, -580, 240, 24, 22, TITLE, key="scUiOrders", extra='upper_case="true"')]
    for k, (name, binding, color, danger) in enumerate(OP_ACTIONS):
        x = 24 + k * (bw + 8)
        o += ["\t" + (button(name, x, -ORDER_Y, bw, ORDER_H, sprite="scui_btn_danger", hover="scui_btn_danger") if danger
                      else button(name, x, -ORDER_Y, bw, ORDER_H)),
              "\t" + centred(x, -ORDER_Y, bw, ORDER_H, 17, color, text="{%s}" % binding, extra='justify="center" upper_case="true"')]
    o += ["</rect>",
          label(24, -(RUN_Y + 2), tw, 32, 15, BODY, text="{opmessage}")]
    return o


def activity_detail():
    """The selected activity: name, icon, cost (red when the stockpile is short), effect, status, what it is for, and
    the do-button with the outcome under it."""
    tw = DET_W - 48
    o = [label(24, -12, tw, 46, 40, TITLE, text="{actname}", extra='upper_case="true"'),
         plate("scui_portrait", 24, -92, 176, 176, depth=1),
         icon("{acticon}", 112, -180, 96)]
    facts = (("scUiActCost", "actcost", "{actcostcolor}"), ("scUiActEffect", "acteffect", BODY),
             ("scUiActStatus", "actstatus", "{actstatuscolor}"))
    for k, (key, binding, color) in enumerate(facts):
        y = 96 + k * 42
        o += [label(224, -y, tw - 200, 18, 15, DIM, key=key, extra='upper_case="true"'),
              label(224, -(y + 18), tw - 200, 22, 18, color, text="{%s}" % binding)]
    o += [label(24, -290, tw, 24, 22, TITLE, key="scUiActAbout", extra='upper_case="true"'),
          plate("scui_divider", 24, -315, tw, 3, depth=1),
          label(24, -326, tw, 200, 17, BODY, text="{actdesc}"),
          button("btnActDo", 24, -ORDER_Y, tw, ORDER_H),
          centred(24, -ORDER_Y, tw, ORDER_H, 18, "{actdocolor}", text="{actdolabel}", extra='justify="center" upper_case="true"'),
          label(24, -(RUN_Y + 2), tw, 32, 15, BODY, text="{actmessage}")]
    return o


def detail():
    o = ['<rect depth="1" name="detailPanel" pos="%d,-%d" width="%d" height="%d">' % (DET_X, PAN_Y, DET_W, PAN_H),
         "\t" + plate("scui_window", 0, 0, DET_W, PAN_H),
         '\t<rect depth="0" name="memberDetail" pos="0,0" width="%d" height="%d" visible="{tabmembers}">' % (DET_W, PAN_H)]
    o += ["\t" + line for line in member_detail()]
    o += ["\t</rect>",
          '\t<rect depth="0" name="outpostDetail" pos="0,0" width="%d" height="%d" visible="{taboutposts}">' % (DET_W, PAN_H)]
    o += ["\t\t" + line.replace("\n", "\n\t\t") for line in outpost_detail()]
    o += ["\t</rect>",
          '\t<rect depth="0" name="activityDetail" pos="0,0" width="%d" height="%d" visible="{tabactivities}">' % (DET_W, PAN_H)]
    o += ["\t\t" + line.replace("\n", "\n\t\t") for line in activity_detail()]
    o += ["\t</rect>", "</rect>"]
    return o


def factors(kind, top, color):
    o = []
    for k in range(1, 5):
        y = top + (k - 1) * (FACTOR_H + FACTOR_GAP)
        o += ['\t<rect depth="1" visible="{%s%dvisible}">' % (kind, k),
              "\t\t" + label(20, -(y + 3), 70, 30, 26, color, text="{%s%dvalue}" % (kind, k)),
              "\t\t" + icon("{%s%dicon}" % (kind, k), 108, -(y + FACTOR_H // 2), 26),
              "\t\t" + label(130, -(y + 7), MOR_W - 150, 24, 19, BODY, text="{%s%dlabel}" % (kind, k)),
              "\t</rect>"]
    return o


def morale():
    w = MOR_W - 40
    o = ['<rect depth="1" name="moralePanel" pos="%d,-%d" width="%d" height="%d">' % (MOR_X, PAN_Y, MOR_W, PAN_H),
         "\t" + plate("scui_window", 0, 0, MOR_W, PAN_H),
         "\t" + label(20, -14, w, 34, 28, TITLE, key="scUiMorale", extra='upper_case="true"'),
         "\t" + label(20, -56, 136, 76, 72, TITLE, text="{morale}", extra='justify="right"'),
         "\t" + label(162, -86, 90, 36, 30, DIM, text="/ 100"),
         "\t" + label(MOR_W - 228, -76, 208, 44, 36, "{moralewordcolor}", text="{moraleword}", extra='justify="right"')]
    o += ["\t" + b for b in bar(20, -146, w, 22, "{moralefillsprite}", "moralefill")]
    o += ["\t" + label(20, -184, w, 24, 19, DIM, key="scUiAffecting"),
          "\t" + plate("scui_divider", 20, -210, w, 3, depth=1),
          "\t" + label(20, -222, w, 26, 22, GOOD, key="scUiHelping", extra='upper_case="true"')]
    o += factors("help", 252, GOOD)
    o.append("\t" + label(20, -414, w, 26, 22, BAD, key="scUiHurting", extra='upper_case="true"'))
    o += factors("hurt", 444, BAD)
    o += ['\t<rect depth="1" visible="{bedscallvisible}">',
          "\t\t" + plate("scui_btn_danger", 20, -606, w, 78, depth=1),
          "\t\t" + icon("ui_game_symbol_map_bed", 58, -645, 40),
          "\t\t" + label(92, -618, w - 90, 26, 21, DANGER_TEXT, text="{bedscalltitle}"),
          "\t\t" + label(92, -648, w - 90, 22, 16, BODY, text="{bedscallhint}"),
          "\t</rect>",
          "</rect>"]
    return o


def footer():
    return [plate("scui_row_thin", M, -FOOT_Y, 58, 28, depth=1),
            centred(M, -FOOT_Y, 58, 28, 16, BODY, text="ESC", extra='justify="center"'),
            label(M + 68, -(FOOT_Y + 3), 80, 22, 19, BODY, key="scUiBack"),
            rule(M + 136, -(FOOT_Y + 2), 2, 24),
            label(M + 150, -(FOOT_Y + 3), 700, 22, 17, DIM, key="scUiHint"),
            label(WIN_W - M - 420, -(FOOT_Y + 4), 420, 22, 16, GOLD, key="scUiMotto", extra='justify="right" upper_case="true"')]


HEADER_H = RES_Y - 10                   # the vanilla look's header bar: the title, the name and the two buttons on it


def window():
    """The window's append as it always was (AshfallUI's look), one line per element at the block's indent."""
    o = ['\t<append xpath="/windows">',
         '\t\t<window name="windowScCommunity" anchor="Center" pos="-%d,%d" width="%d" height="%d" depth="%d" '
         'controller="%s" cursor_area="true">' % (WIN_W // 2, WIN_H // 2, WIN_W, WIN_H, DEPTH, CONTROLLER),
         "\t\t\t" + plate("scui_window", 0, 0, WIN_W, WIN_H)]
    for line in header() + resources() + members() + detail() + morale() + footer():
        o.append("\t\t\t" + line.replace("\n", "\n\t\t\t"))
    o += ["\t\t</window>", "\t</append>"]
    return "\n".join(o).split("\n")


def build():
    ash = window()
    main = "\t\t\t" + plate("scui_window", 0, 0, WIN_W, WIN_H)
    van = vanilla_look.vanilla(ash, overrides={main: vanilla_look.window_plate("\t\t\t", WIN_W, WIN_H)
                                               + vanilla_look.header_bar("\t\t\t", WIN_W, HEADER_H)})
    o = ['\t<!-- The community window, GENERATED by Tools/generate_community_ui.py - edit that, not this. Opened by',
         '\t     ScCommunity.OpenWindow (ESC menu entry, land claim radial, member dialog). anchor="Center" still',
         '\t     needs pos="-width/2,+height/2": the anchor only sets the pivot. Two looks (round 32b): AshfallUI\'s',
         '\t     scui_* plates when it is loaded, vanilla\'s otherwise (Tools/vanilla_look.py). -->']
    o += vanilla_look.conditional(ash, van)
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
            raise SystemExit("REFUSING: no GENERATED markers and %d hand-authored community window(s) in %s."
                             % (len(found), TARGET))
        text = LEGACY.sub(lambda _m: block, text)
    with io.open(TARGET, "w", encoding="utf-8", newline="") as f:
        f.write(text)
    print("generate_community_ui: wrote windowScCommunity %dx%d, %d member rows, %d outpost rows, %d activity rows, %d resources"
          % (WIN_W, WIN_H, ROWS, OP_ROWS, ACT_ROWS, len(RESOURCES)))
    assert ROWS_TOP + ROWS * (ROW_H + ROW_GAP) + 40 <= PAN_H, "member rows run past the panel"
    assert ROWS_TOP + OP_ROWS * (ROW_H + ROW_GAP) + 40 <= PAN_H, "outpost rows run past the panel"
    assert ROWS_TOP + ACT_ROWS * (ROW_H + ROW_GAP) + 40 <= PAN_H, "activity rows run past the panel"
    assert 326 + 200 <= ORDER_Y - 4, "the activity description runs into its button"
    assert STAFF_TOP + 3 * (STAFF_H + STAFF_GAP) <= NET_TOP - 36, "the garrison runs into the network heading"
    assert NET_TOP + len(NETS) * (NET_H + NET_GAP) <= 578, "the network rows run into the orders (heading at 580)"
    assert RUN_Y + RUN_H <= PAN_H - 4, "the orders run past the detail panel"
    assert WORK_MENU_Y + WORK_MENU_H <= ORDER_Y - 2, "the work button runs into the order buttons"
    assert TRAIT_TOP + 3 * (TRAIT_H + TRAIT_GAP) <= WORK_MENU_Y, "the traits run into the work button"
    assert len(ROLE_BUTTONS) * (ROLE_W + 6) + 120 <= DET_W - 48, "no room left for the work button"
    assert FOOT_Y + 28 <= WIN_H, "the footer runs past the window"
    assert 3 + HEADER_H <= RES_Y - 4, "the vanilla header bar runs into the resources"


if __name__ == "__main__":
    main()
