#!/usr/bin/env python3
"""
Generates the community window, windowScCommunity, into Config/XUi_InGame/windows.xml between its
GENERATED markers. Run from anywhere:  python Tools/generate_community_ui.py

Written by the AshfallUI session (2026-09-25) on the user's premium reference; XUiC_ScCommunity /
XUiC_ScMemberRow answer every binding and control here (the SurvivorCommunity session's work):

    [cabin] COMMUNITY                                  [OPEN STORAGE] [x]
            <community name>
    +- FOOD -+- WATER -+- MEDICAL -+- MATERIALS -+- AMMO -+- FUEL -+- LUXURIES -+
    +[MEMBERS][OUTPOSTS][FACILITIES][ENCLAVES]+ NAME -+ +- COMMUNITY MORALE -+
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
- The left panel has three tabs (btnTabMembers / btnTabOutposts / btnTabFacilities, {tabmembers} / {taboutposts} /
  {tabfacilities}): the Outposts tab swaps the member list for the outpost rows (XUiC_ScOutpostRow) and the survivor
  detail for the outpost detail and the network summary; the Facilities tab (round m1f, 2026-11-17; it replaced round
  wl's Activities tab) for the facility rows (btnFac1..8, plain buttons, paged by btnFacPrev / btnFacNext) and the
  selected facility's detail (four facts, a details text) with up to three action buttons (btnFacDo1..3; ScFacilityTab,
  XUiC_ScCommunityFacilities.cs); the morale panel stays. Wrappers that switch tabs are depth 0, so every depth inside
  them is what it was before the tabs existed (XUi depth is cumulative).
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
- Round MP7 (part U, 2026-11-27): the community modes (XUiC_ScCommunityPlayers.cs answers these). A PLAYERS button in the header
  ({playersbtnvisible}, btnPlayers) swaps the morale panel's content ({showmorale}) for the players list ({showplayers}: PL_ROWS
  rows of name, role and an online dot, and Leave community, btnLeaveCommunity, {leavecommunityvisible}). A player with only their
  own relations sees the no-community page ({nocommunity}: how to start or join one, what they can do now, the enclaves they know,
  their favors and requests) in place of the three panels ({hascommunity}), and no Open storage ({storagevisible}). Ask to leave
  and Disband take their label colour from {leavetextcolor} / {opdisbandcolor} (grayed when the owner's orders are not the
  player's). Shared mode with one community answers every one of these as before, so the window looks the same there.
  Round MP7 (review U): the players list (PL_ROWS) and the no-community page's three lists page - Showing X of Y on the left
  ({plshowingtext}, {ncencshowing}, {ncfavshowing}, {ncreqshowing}) and a Prev / Next pager on the right (pager(): btnPlPrev /
  btnPlNext, btnNcEnc*, btnNcFav*, btnNcReq*, shown by their {...pagevisible}); the player's own role line has three lines.
- Round db1 (2026-11-28): Disband community (btnDisbandCommunity, {disbandcommunityvisible}, {disbandcommunitylabel}) for the owner
  of a founded community, in Leave community's place (an owner never has Leave); the first press's hint is {playersmessage}. The
  PLAYERS button and the players view also show for the owner in single player and in Shared with one community (XUiC_ScCommunity's
  View.PanelOn), where the list is otherwise off.
- Round en1 (2026-11-28): a fourth tab, ENCLAVES (btnTabEnclaves, {tabenclaves}, {enclavecount}); four to a row the tabs are 94
  wide, so each name sits over its count. The tab's left panel lists every enclave the player's record knows (ENC_ROWS rows of
  enc_row: btnEnc1..6, paged by btnEncPrev / btnEncNext; the name over the standing level, a bar toward the next level with its
  points - none for a hostile group, a line in its place - and the kind, size and distance with a request or favor badge); the
  detail panel shows the selected one (four facts, the standing bar, what the level means) with btnEncMark. A row clicked marks
  the enclave on the map (XUiC_ScCommunityEnclaves.cs, ScEnclaveList.cs). The no-community page's enclave list is the same rows
  (ncenc, btnNcEnc1..6; a marked row is lit), six to a page where it had eight plainer ones. Round en1 (review): the tab is hidden
  with Enclaves off ({enclavestabvisible}), and a row's kind, size and distance line is wider (ENC_BADGE_W for the badge).
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
# (button, label key, width weight: about the English label's letters + 2 - round 33c's eight kinds share the row by it)
RUNS = (("btnRunFood", "scRunFood", 6), ("btnRunWater", "scRunWater", 7), ("btnRunMeds", "scRunMeds", 9), ("btnRunAmmo", "scRunAmmo", 6),
        ("btnRunMaterials", "scRunMaterials", 11), ("btnRunFuel", "scRunFuel", 6),
        # round 33c (M33, 3.4): mining runs - Mine and Dig clay (XUiC_ScCommunity.OnRunJob)
        ("btnRunMine", "scUiRunMine", 6), ("btnRunClay", "scUiRunClay", 10))
RUN_GAP = 4
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
# round m1f: the Facilities tab - rows (XUiC_ScCommunity.FacilitySlots), facts (FacilityFactSlots), actions (FacilityActionSlots)
FAC_ROWS, FAC_ROW_H, FAC_ROW_GAP = 8, 60, 6
FAC_FACTS, FAC_ACTIONS = 4, 3
FAC_ABOUT_Y, FAC_ABOUT_H = 326, 240
# round MP7 (part U): Disband's label colour is bound ({opdisbandcolor}: grayed when the owner's orders are not the player's)
OP_ACTIONS = (("btnOpTrack", "optracklabel", BODY, False), ("btnOpAssign", "opassignlabel", BODY, False),
              ("btnOpUpgrade", "opupgradelabel", "{opupgradecolor}", False), ("btnOpDisband", "opdisbandlabel", "{opdisbandcolor}", True))
# round MP7 (part U): the players list (XUiC_ScCommunity.PlayerSlots) in the morale panel, Leave community under it, the header's
# PLAYERS button, and the no-community page's rows (NcEnclaveSlots, NcFavourSlots, NcRequestSlots)
# round MP7 (review U): n3 - the player's own role line gets three lines, so 9 rows from 122 (was 10 from 104); m1 - and a pager
PL_ROWS, PL_ROW_H, PL_ROW_GAP, PL_TOP = 9, 40, 4, 122
PL_YOU_H = 64
PAGER_W = 100                         # a pager's two buttons at a list's right edge, and the gap before them
LEAVE_Y, LEAVE_H = 590, 48
PL_BTN_W = 210
NC_ROW_H, NC_ROW_GAP = 56, 6
NC_ENC_TOP = 90                       # round en1: its rows are the ENCLAVES tab's (ENC_ROWS of enc_row)
NC_FAV_ROWS, NC_FAV_TOP = 5, 56
NC_REQ_ROWS, NC_REQ_TOP = 3, 436
# round en1: the ENCLAVES tab's rows (XUiC_ScCommunity.EnclaveSlots) and the no-community page's, the bar in them, the detail's facts
ENC_ROWS, ENC_ROW_H, ENC_ROW_GAP, ENC_BAR_H = 6, 76, 6, 14
ENC_BADGE_W = 130                     # round en1 (review): "Request taken" at 15 px, the longest badge
ENC_FACTS = 4
ENC_ABOUT_Y, ENC_ABOUT_H = 392, 180
ENC_MARK_W = 240
# what a player with no community can do now: (icon, label key)
NC_NOW = (("ui_game_symbol_talk", "scUiNcTalk"), ("ui_game_symbol_quest", "scUiNcFavors"), ("ui_game_symbol_map", "scUiNcMarkers"))


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
    px = sx - 12 - PL_BTN_W
    return [icon("ui_game_symbol_map_cabin", 60, -44, 64),
            label(104, -6, 700, 52, 48, TITLE, key="scUiTitle", extra='upper_case="true"'),
            label(106, -56, 700, 26, 22, GOLD, text="{communityname}", extra='upper_case="true"'),
            # round MP7 (part U): no Open storage for a player with only their own relations (no community: no linked storage)
            '<rect depth="0" pos="0,0" width="%d" height="%d" visible="{storagevisible}">' % (WIN_W, RES_Y),
            "\t" + button("btnStorage", sx, -18, 250, 48),
            "\t" + icon("ui_game_symbol_store_all_up", sx + 34, -42, 30, depth=4),
            "\t" + centred(sx + 56, -18, 180, 48, 20, BODY, key="scUiOpenStorage", extra='justify="center" upper_case="true"'),
            "</rect>",
            # round MP7 (part U): the players list's button (PerPlayer/PerParty, or two or more communities; never in single player).
            # round db1: and for the owner of a founded community anywhere (their Disband community)
            '<rect depth="0" pos="0,0" width="%d" height="%d" visible="{playersbtnvisible}">' % (WIN_W, RES_Y),
            "\t" + button("btnPlayers", px, -18, PL_BTN_W, 48),
            "\t" + icon("ui_game_symbol_players", px + 30, -42, 28, depth=4),
            "\t" + centred(px + 50, -18, PL_BTN_W - 60, 48, 18, BODY, text="{playersbtnlabel}", extra='justify="center" upper_case="true"'),
            "</rect>",
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
        # round fd2: the day's net (+N green / -N red / a dim +-0), colored by its own binding
        o.append(label(x0 + 120, -(RES_Y + 39), gw - 128, 22, 17, "{op%scolor}" % binding[3:], text="{op%s}" % binding[3:], extra='justify="right"'))
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


# (button, label key, count binding, active binding, shown binding or None)
TABS = (("btnTabMembers", "scUiMembers", "membercount", "tabmembers", None),
        ("btnTabOutposts", "scUiOutposts", "outpostcount", "taboutposts", None),
        ("btnTabFacilities", "scUiFacilities", "facilitycount", "tabfacilities", None),
        # round en1; round en1 (review): EN1-n7 - not shown with Enclaves off in the config
        ("btnTabEnclaves", "scUiEnclaves", "enclavecount", "tabenclaves", "enclavestabvisible"))


def tabs():
    """MEMBERS n | OUTPOSTS held/slots | FACILITIES entries | ENCLAVES known: the active tab gets the selected plate over its
    button - scui_tab_selected, its own name since 2026-10-13: the member rows' scui_row_selected is cut for 80-unit rows and
    read too heavy at 44 (user); AshfallUI ships the nav bar's selected plate under it.
    Round en1: four to a row they are 94 wide, so the name (15 px) is centered on top and its count (14 px) under it - three
    to a row they were 128 wide with the count in the last 34 beside the name."""
    w = (MEM_W - 40 - 8 * (len(TABS) - 1)) // len(TABS)
    o = []
    for i, (name, key, count, active, shown) in enumerate(TABS):
        x = 20 + i * (w + 8)
        tab = [button(name, x, -12, w, 44),
               '<rect depth="0" pos="0,0" width="%d" height="%d" visible="{%s}">' % (MEM_W, 60, active),
               "\t" + plate("scui_tab_selected", x, -12, w, 44, depth=3),
               "</rect>",
               label(x + 4, -15, w - 8, 20, 15, TITLE, key=key, extra='justify="center" upper_case="true"'),
               label(x + 4, -35, w - 8, 18, 14, GOLD, text="{%s}" % count, extra='justify="center"')]
        if shown:      # round en1 (review): EN1-n7 - a tab that can be off; its wrapper is depth 0, so its depths are as before
            tab = (['<rect depth="0" pos="0,0" width="%d" height="%d" visible="{%s}">' % (MEM_W, 60, shown)]
                   + ["\t" + line for line in tab] + ["</rect>"])
        o += tab
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


def facility_list():
    """The Facilities tab's left panel (round m1f): a summary line, then a page of FAC_ROWS rows (plain buttons) - the
    registry's entries in order (ScFacilityTab.All), the water sources one by one at the end - and the pager."""
    w = MEM_W - 40
    o = [label(20, -70, w, 24, 17, DIM, text="{facsummary}")]
    for i in range(1, FAC_ROWS + 1):
        y = ROWS_TOP + (i - 1) * (FAC_ROW_H + FAC_ROW_GAP)
        o += ['<rect depth="1" pos="0,0" width="%d" height="%d" visible="{fac%dvisible}">' % (MEM_W, PAN_H, i),
              "\t" + button("btnFac%d" % i, 20, -y, w, FAC_ROW_H, sprite="scui_row", hover="scui_row_hover", extra='on_scroll="true"'),   # fd2: the wheel
              '\t<rect depth="0" pos="0,0" width="%d" height="%d" visible="{fac%dselected}">' % (MEM_W, PAN_H, i),
              "\t\t" + plate("scui_row_selected", 20, -y, w, FAC_ROW_H, depth=3),
              "\t</rect>",
              "\t" + plate("scui_portrait", 26, -(y + 6), 48, 48, depth=4),
              "\t" + icon("{fac%dicon}" % i, 50, -(y + 30), 30, depth=5),
              "\t" + label(86, -(y + 8), w - 100, 24, 20, TITLE, text="{fac%dname}" % i, extra='upper_case="true"'),
              "\t" + label(86, -(y + 35), w - 100, 20, 15, "{fac%dstatuscolor}" % i, text="{fac%dstatus}" % i),
              "</rect>"]
    fy = ROWS_TOP + FAC_ROWS * (FAC_ROW_H + FAC_ROW_GAP) + 4
    o += [label(20, -(fy + 6), 260, 22, 17, DIM, text="{facshowingtext}"),
          '<rect depth="2" pos="0,0" width="%d" height="%d" visible="{facpagevisible}">' % (MEM_W, PAN_H),
          "\t" + button("btnFacPrev", MEM_W - 104, -fy, 38, 34),
          "\t" + icon("ui_game_symbol_arrow_left", MEM_W - 85, -(fy + 17), 18, depth=4),
          "\t" + button("btnFacNext", MEM_W - 58, -fy, 38, 34),
          "\t" + icon("ui_game_symbol_arrow_right", MEM_W - 39, -(fy + 17), 18, depth=4),
          "</rect>"]
    return o


def enc_row(prefix, btn, k, x, y, w, panel_w, bar_w):
    """Round en1: one enclave row - the ENCLAVES tab's (encrow, btnEnc) and the no-community page's (ncenc, btnNcEnc): a button the
    row's size (a click marks the enclave on the map, or takes the mark away), its selected plate (the tab: the one selected; the
    no-community page: the marked ones), the name over the standing level (right, in its color), the bar toward the next level and
    its points (a hostile group: a line in their place), and the kind, size and distance under them with a request or favor badge on
    the right."""
    p = "%s%d" % (prefix, k)
    bx = x + 12
    o = ['<rect depth="1" pos="0,0" width="%d" height="%d" visible="{%svisible}">' % (panel_w, PAN_H, p),
         "\t" + button("btn%s%d" % (btn, k), x, -y, w, ENC_ROW_H, sprite="scui_row", hover="scui_row_hover"),
         '\t<rect depth="0" pos="0,0" width="%d" height="%d" visible="{%sselected}">' % (panel_w, PAN_H, p),
         "\t\t" + plate("scui_row_selected", x, -y, w, ENC_ROW_H, depth=3),
         "\t</rect>",
         "\t" + label(bx, -(y + 6), w - 200, 24, 20, TITLE, text="{%sname}" % p, extra='upper_case="true"'),
         "\t" + label(x + w - 176, -(y + 8), 164, 22, 17, "{%slevelcolor}" % p, text="{%slevel}" % p, extra='justify="right"'),
         '\t<rect depth="0" pos="0,0" width="%d" height="%d" visible="{%sbarvisible}">' % (panel_w, PAN_H, p)]
    o += ["\t\t" + line for line in bar(bx, -(y + 32), bar_w, ENC_BAR_H, "scui_fill_skill", p + "fill", depth=4)]
    o += ["\t\t" + label(bx + bar_w + 10, -(y + 29), w - bar_w - 34, 20, 15, BODY, text="{%spoints}" % p),
          "\t</rect>",
          '\t<rect depth="0" pos="0,0" width="%d" height="%d" visible="{%snobarvisible}">' % (panel_w, PAN_H, p),
          "\t\t" + label(bx, -(y + 29), w - 24, 20, 15, DANGER_TEXT, text="{%snobar}" % p),
          "\t</rect>",
          # round en1 (review): EN1-n7 - the kind, size and distance get the width the badge does not need (it was 190 short)
          "\t" + label(bx, -(y + 52), w - 24 - ENC_BADGE_W - 8, 20, 15, DIM, text="{%sinfo}" % p),
          "\t" + label(x + w - 12 - ENC_BADGE_W, -(y + 52), ENC_BADGE_W, 20, 15, "{%sbadgecolor}" % p, text="{%sbadge}" % p, extra='justify="right"'),
          "</rect>"]
    return o


def enclave_list():
    """Round en1: the ENCLAVES tab's left panel - a summary line (how many, and that a click marks one), a page of ENC_ROWS rows
    (enc_row; best level and standing first, then the nearest), the empty state and the pager."""
    w = MEM_W - 40
    o = [label(20, -70, w, 24, 17, DIM, text="{encsummary}")]
    for k in range(1, ENC_ROWS + 1):
        y = ROWS_TOP + (k - 1) * (ENC_ROW_H + ENC_ROW_GAP)
        o += enc_row("encrow", "Enc", k, 20, y, w, MEM_W, 160)
    fy = ROWS_TOP + ENC_ROWS * (ENC_ROW_H + ENC_ROW_GAP) + 4
    o += ['<rect depth="1" pos="20,-%d" width="%d" height="%d" visible="{encempty}">' % (ROWS_TOP, w, 220),
          "\t" + label(0, -12, w, 30, 24, TITLE, key="scUiEncNone", extra='upper_case="true"'),
          "\t" + label(0, -52, w, 150, 18, BODY, key="scUiNcEncNone"),
          "</rect>",
          label(20, -(fy + 6), 260, 22, 17, DIM, text="{encshowingtext}"),
          '<rect depth="2" pos="0,0" width="%d" height="%d" visible="{encpagevisible}">' % (MEM_W, PAN_H),
          "\t" + button("btnEncPrev", MEM_W - 104, -fy, 38, 34),
          "\t" + icon("ui_game_symbol_arrow_left", MEM_W - 85, -(fy + 17), 18, depth=4),
          "\t" + button("btnEncNext", MEM_W - 58, -fy, 38, 34),
          "\t" + icon("ui_game_symbol_arrow_right", MEM_W - 39, -(fy + 17), 18, depth=4),
          "</rect>"]
    return o


def members():
    o = ['<rect depth="1" name="membersPanel" pos="%d,-%d" width="%d" height="%d" visible="{hascommunity}">' % (MEM_X, PAN_Y, MEM_W, PAN_H),
         "\t" + plate("scui_window", 0, 0, MEM_W, PAN_H)]
    o += ["\t" + t.replace("\n", "\n\t") for t in tabs()]
    o.append('\t<rect depth="0" name="memberList" pos="0,0" width="%d" height="%d" visible="{tabmembers}">' % (MEM_W, PAN_H))
    o += ["\t\t" + t.replace("\n", "\n\t\t") for t in member_list()]
    o.append("\t</rect>")
    o.append('\t<rect depth="0" name="outpostList" pos="0,0" width="%d" height="%d" visible="{taboutposts}">' % (MEM_W, PAN_H))
    o += ["\t\t" + t.replace("\n", "\n\t\t") for t in outpost_list()]
    o.append("\t</rect>")
    o.append('\t<rect depth="0" name="facilityList" pos="0,0" width="%d" height="%d" visible="{tabfacilities}">' % (MEM_W, PAN_H))
    o += ["\t\t" + t.replace("\n", "\n\t\t") for t in facility_list()]
    o.append("\t</rect>")
    o.append('\t<rect depth="0" name="enclaveList" pos="0,0" width="%d" height="%d" visible="{tabenclaves}">' % (MEM_W, PAN_H))   # round en1
    o += ["\t\t" + t for t in enclave_list()]
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
        # round m1t: the specialty row's plate takes the pointer and shows what her life skill does ({skspecdesc})
        tip = 'on_hover="true" tooltip="{skspecdesc}"' if vis else ""
        body = [plate("scui_row_thin", 24, -y, tw, SKILL_H, depth=1, extra=tip),
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
          # round MP7 (part U): grayed when the owner's orders are not the player's ({leavetextcolor})
          "\t\t" + centred(40 + 2 * bw, -ORDER_Y, bw, ORDER_H, 18, "{leavetextcolor}", text="{leavelabel}",
                           extra='justify="center" upper_case="true"'),
          # btnHome is still hooked by the controller; "Follow me" already turns into "Go home".
          "\t\t" + button("btnHome", 24, -ORDER_Y, bw, ORDER_H, extra='visible="false"')]
    # six run kinds since round wl (water), eight since round 33c (Mine, Dig clay): each as wide as its label needs (RUNS'
    # weights), 13 px labels ("MATERIALS" gets 87)
    o.append('\t\t<rect depth="1" visible="{runpicking}">')
    for name, key, x, rw in run_cells(tw):
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


def run_cells(tw):
    """The run picker's buttons across tw: (name, key, x, width) by RUNS' weights, RUN_GAP apart, the last one to the edge."""
    avail = tw - (len(RUNS) - 1) * RUN_GAP
    total = sum(wt for _n, _k, wt in RUNS)
    cells, x = [], 24
    for k, (name, key, wt) in enumerate(RUNS):
        w = (24 + tw - x) if k == len(RUNS) - 1 else int(round(avail * wt / float(total)))
        cells.append((name, key, x, w))
        x += w + RUN_GAP
    return cells


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


def facility_detail():
    """The selected facility (round m1f): its name, a line under it, the icon plate, four facts (caption and colored value),
    the details text, and up to three action buttons with the outcome under them. An empty fact or action slot stays
    hidden by its own {...visible}."""
    tw = DET_W - 48
    o = [label(24, -12, tw, 46, 40, TITLE, text="{facname}", extra='upper_case="true"'),
         icon("{facicon}", 36, -72, 24),
         label(54, -61, tw - 40, 24, 20, GOLD, text="{facsub}"),
         plate("scui_portrait", 24, -92, 176, 176, depth=1),
         icon("{facicon}", 112, -180, 96)]
    for k in range(1, FAC_FACTS + 1):
        y = 96 + (k - 1) * 42
        o += ['<rect depth="1" pos="0,0" width="%d" height="%d" visible="{facfact%dvisible}">' % (DET_W, PAN_H, k),
              "\t" + label(224, -y, tw - 200, 18, 15, DIM, text="{facfact%dlabel}" % k, extra='upper_case="true"'),
              "\t" + label(224, -(y + 18), tw - 200, 22, 18, "{facfact%dcolor}" % k, text="{facfact%d}" % k),
              "</rect>"]
    o += [label(24, -290, tw, 24, 22, TITLE, key="scUiFacDetails", extra='upper_case="true"'),
          plate("scui_divider", 24, -315, tw, 3, depth=1),
          label(24, -FAC_ABOUT_Y, tw, FAC_ABOUT_H, 16, BODY, text="{facabout}"),
          '<rect depth="1" pos="0,0" width="%d" height="%d" visible="{facactionsvisible}">' % (DET_W, PAN_H),
          "\t" + label(24, -580, 240, 24, 22, TITLE, key="scUiOrders", extra='upper_case="true"'),
          "</rect>"]
    bw = (tw - 8 * (FAC_ACTIONS - 1)) // FAC_ACTIONS
    for k in range(1, FAC_ACTIONS + 1):
        x = 24 + (k - 1) * (bw + 8)
        o += ['<rect depth="1" pos="0,0" width="%d" height="%d" visible="{facdo%dvisible}">' % (DET_W, PAN_H, k),
              "\t" + button("btnFacDo%d" % k, x, -ORDER_Y, bw, ORDER_H),
              "\t" + centred(x, -ORDER_Y, bw, ORDER_H, 17, "{facdo%dcolor}" % k, text="{facdo%dlabel}" % k,
                             extra='justify="center" upper_case="true"'),
              "</rect>"]
    o.append(label(24, -(RUN_Y + 2), tw, 32, 15, BODY, text="{facmessage}"))
    return o


def enclave_detail():
    """Round en1: the selected enclave - its name, its kind and size under it, the icon plate, four facts (standing, the next level,
    where, a request or favor), the standing bar with its points (a hostile group: a line instead), what the level means and their
    benefit, and Mark on map / Remove map mark (btnEncMark) with its outcome under it. With none known: the name is the tab's and the
    text says what will show."""
    tw = DET_W - 48
    o = [label(24, -12, tw, 46, 40, TITLE, text="{encselname}", extra='upper_case="true"'),
         icon("{encselicon}", 36, -72, 24),
         label(54, -61, tw - 40, 24, 20, GOLD, text="{encselsub}"),
         plate("scui_portrait", 24, -92, 176, 176, depth=1),
         icon("{encselicon}", 112, -180, 96)]
    for k in range(1, ENC_FACTS + 1):
        y = 96 + (k - 1) * 42
        o += ['<rect depth="1" pos="0,0" width="%d" height="%d" visible="{encselvisible}">' % (DET_W, PAN_H),
              "\t" + label(224, -y, tw - 200, 18, 15, DIM, text="{encselfact%dlabel}" % k, extra='upper_case="true"'),
              "\t" + label(224, -(y + 18), tw - 200, 22, 18, "{encselfact%dcolor}" % k, text="{encselfact%d}" % k),
              "</rect>"]
    o += [label(24, -290, tw, 24, 22, TITLE, key="scUiEncStandingHead", extra='upper_case="true"'),
          plate("scui_divider", 24, -315, tw, 3, depth=1),
          '<rect depth="1" pos="0,0" width="%d" height="%d" visible="{encselbarvisible}">' % (DET_W, PAN_H)]
    o += ["\t" + line for line in bar(24, -330, tw, 20, "scui_fill_skill", "encselfill")]
    o += ["\t" + label(24, -356, tw, 24, 18, BODY, text="{encselpoints}"),
          "</rect>",
          '<rect depth="1" pos="0,0" width="%d" height="%d" visible="{encselnobarvisible}">' % (DET_W, PAN_H),
          "\t" + label(24, -334, tw, 44, 18, DANGER_TEXT, text="{encselnobar}"),
          "</rect>",
          label(24, -ENC_ABOUT_Y, tw, ENC_ABOUT_H, 16, BODY, text="{encselabout}"),
          '<rect depth="1" pos="0,0" width="%d" height="%d" visible="{encselvisible}">' % (DET_W, PAN_H),
          "\t" + label(24, -580, 240, 24, 22, TITLE, key="scUiOrders", extra='upper_case="true"'),
          "\t" + button("btnEncMark", 24, -ORDER_Y, ENC_MARK_W, ORDER_H),
          "\t" + centred(24, -ORDER_Y, ENC_MARK_W, ORDER_H, 17, BODY, text="{encmarklabel}", extra='justify="center" upper_case="true"'),
          "</rect>",
          label(24, -(RUN_Y + 2), tw, 32, 15, BODY, text="{encmessage}")]
    return o


def detail():
    o = ['<rect depth="1" name="detailPanel" pos="%d,-%d" width="%d" height="%d" visible="{hascommunity}">' % (DET_X, PAN_Y, DET_W, PAN_H),
         "\t" + plate("scui_window", 0, 0, DET_W, PAN_H),
         '\t<rect depth="0" name="memberDetail" pos="0,0" width="%d" height="%d" visible="{tabmembers}">' % (DET_W, PAN_H)]
    o += ["\t" + line for line in member_detail()]
    o += ["\t</rect>",
          '\t<rect depth="0" name="outpostDetail" pos="0,0" width="%d" height="%d" visible="{taboutposts}">' % (DET_W, PAN_H)]
    o += ["\t\t" + line.replace("\n", "\n\t\t") for line in outpost_detail()]
    o += ["\t</rect>",
          '\t<rect depth="0" name="facilityDetail" pos="0,0" width="%d" height="%d" visible="{tabfacilities}">' % (DET_W, PAN_H)]
    o += ["\t\t" + line.replace("\n", "\n\t\t") for line in facility_detail()]
    o += ["\t</rect>",
          '\t<rect depth="0" name="enclaveDetail" pos="0,0" width="%d" height="%d" visible="{tabenclaves}">' % (DET_W, PAN_H)]   # round en1
    o += ["\t\t" + line for line in enclave_detail()]
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
    """The morale panel. Round MP7 (part U): its content is a view ({showmorale}) the players list ({showplayers}) swaps with."""
    w = MOR_W - 40
    o = ['<rect depth="1" name="moralePanel" pos="%d,-%d" width="%d" height="%d" visible="{hascommunity}">' % (MOR_X, PAN_Y, MOR_W, PAN_H),
         "\t" + plate("scui_window", 0, 0, MOR_W, PAN_H),
         '\t<rect depth="0" name="moraleView" pos="0,0" width="%d" height="%d" visible="{showmorale}">' % (MOR_W, PAN_H),
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
          "\t</rect>"]
    # round MP7 (part U): the morale view's lines one tab in (its rect opened above), then the players view
    o = o[:3] + ["\t" + line for line in o[3:]] + ["\t</rect>"] + ["\t" + line for line in players()] + ["</rect>"]
    return o


def players():
    """Round MP7 (part U): the players list in the morale panel - the count, the player's own role, PL_ROWS rows (an online dot,
    the name, the role and offline), how many more, and Leave community (two presses) with the answer under it. Round MP7 (review
    U): m1 - Showing X of Y and a pager (btnPlPrev / btnPlNext) in place of "+N more"."""
    w = MOR_W - 40
    o = ['<rect depth="0" name="playersView" pos="0,0" width="%d" height="%d" visible="{showplayers}">' % (MOR_W, PAN_H),
         "\t" + label(20, -14, w - 70, 34, 28, TITLE, key="scUiPlTitle", extra='upper_case="true"'),
         "\t" + label(MOR_W - 90, -18, 70, 30, 26, GOLD, text="{playercount}", extra='justify="right"'),
         "\t" + label(20, -54, w, PL_YOU_H, 16, GOLD, text="{playersyou}")]
    for k in range(1, PL_ROWS + 1):
        y = PL_TOP + (k - 1) * (PL_ROW_H + PL_ROW_GAP)
        o += ['\t<rect depth="0" pos="0,0" width="%d" height="%d" visible="{pl%dvisible}">' % (MOR_W, PAN_H, k),
              "\t\t" + plate("scui_row_thin", 20, -y, w, PL_ROW_H, depth=1),
              '\t\t<sprite depth="3" sprite="ui_game_symbol_player" pos="%d,%d" width="20" height="20" pivot="center" color="{pl%ddotcolor}"/>'
              % (40, -(y + PL_ROW_H // 2), k),
              "\t\t" + label(60, -(y + 8), w - 210, 24, 19, "{pl%dnamecolor}" % k, text="{pl%dname}" % k),
              "\t\t" + label(20 + w - 160, -(y + 10), 148, 22, 15, DIM, text="{pl%drole}" % k, extra='justify="right"'),
              "\t</rect>"]
    fy = PL_TOP + PL_ROWS * (PL_ROW_H + PL_ROW_GAP) + 4
    o += ["\t" + label(20, -(fy + 6), w - PAGER_W, 22, 16, DIM, text="{plshowingtext}")]
    o += ["\t" + line for line in pager("Pl", 20 + w, fy, 34, MOR_W, "plpagevisible")]
    o += ['\t<rect depth="0" pos="0,0" width="%d" height="%d" visible="{leavecommunityvisible}">' % (MOR_W, PAN_H),
          "\t\t" + button("btnLeaveCommunity", 20, -LEAVE_Y, w, LEAVE_H, sprite="scui_btn_danger", hover="scui_btn_danger"),
          "\t\t" + centred(20, -LEAVE_Y, w, LEAVE_H, 18, DANGER_TEXT, text="{leavecommunitylabel}", extra='justify="center" upper_case="true"'),
          "\t</rect>",
          # round db1: Disband community, the owner's, in Leave's place (an owner never has Leave); its hint is {playersmessage}
          '\t<rect depth="0" pos="0,0" width="%d" height="%d" visible="{disbandcommunityvisible}">' % (MOR_W, PAN_H),
          "\t\t" + button("btnDisbandCommunity", 20, -LEAVE_Y, w, LEAVE_H, sprite="scui_btn_danger", hover="scui_btn_danger"),
          "\t\t" + centred(20, -LEAVE_Y, w, LEAVE_H, 18, DANGER_TEXT, text="{disbandcommunitylabel}", extra='justify="center" upper_case="true"'),
          "\t</rect>",
          "\t" + label(20, -(LEAVE_Y + LEAVE_H + 8), w, 44, 15, BODY, text="{playersmessage}"),
          "</rect>"]
    return o


def pager(name, right, y, h, panel_w, visible):
    """Round MP7 (review U): m1, m2 - Prev / Next at a list's right edge (the member list's pager: 38 wide, 8 apart), shown while the
    list has more than one page."""
    return ['<rect depth="2" pos="0,0" width="%d" height="%d" visible="{%s}">' % (panel_w, PAN_H, visible),
            "\t" + button("btn%sPrev" % name, right - 84, -y, 38, h),
            "\t" + icon("ui_game_symbol_arrow_left", right - 65, -(y + h // 2), 18, depth=4),
            "\t" + button("btn%sNext" % name, right - 38, -y, 38, h),
            "\t" + icon("ui_game_symbol_arrow_right", right - 19, -(y + h // 2), 18, depth=4),
            "</rect>"]


def nc_row(prefix, k, x, y, w, panel_w):
    """Round MP7 (part U): one row of the no-community page - a over b (right, in its colour) and c under them."""
    return ['<rect depth="0" pos="0,0" width="%d" height="%d" visible="{%s%dvisible}">' % (panel_w, PAN_H, prefix, k),
            "\t" + plate("scui_row_thin", x, -y, w, NC_ROW_H, depth=1),
            "\t" + label(x + 12, -(y + 6), w - 190, 24, 20, TITLE, text="{%s%da}" % (prefix, k)),
            "\t" + label(x + w - 176, -(y + 8), 164, 22, 17, "{%s%dcolor}" % (prefix, k), text="{%s%db}" % (prefix, k), extra='justify="right"'),
            "\t" + label(x + 12, -(y + 32), w - 24, 20, 15, DIM, text="{%s%dc}" % (prefix, k)),
            "</rect>"]


def nocommunity():
    """Round MP7 (part U): the no-community page in place of the three panels, for a player with only their own relations
    (PerPlayer/PerParty): how to start one and how to join one in this mode, what they can do now and that it carries over; the
    enclaves they know with their standing; their favors and enclave requests."""
    w = MEM_W - 40
    o = ['<rect depth="0" name="noCommunityPage" pos="0,0" width="%d" height="%d" visible="{nocommunity}">' % (WIN_W, WIN_H),
         '\t<rect depth="1" name="ncInfoPanel" pos="%d,-%d" width="%d" height="%d">' % (MEM_X, PAN_Y, MEM_W, PAN_H),
         "\t\t" + plate("scui_window", 0, 0, MEM_W, PAN_H),
         "\t\t" + icon("ui_game_symbol_players", 38, -32, 30),
         "\t\t" + label(64, -14, w - 44, 34, 28, TITLE, key="scUiNcTitle", extra='upper_case="true"'),
         "\t\t" + label(20, -60, w, 66, 17, BODY, key="scUiNcWhy"),
         "\t\t" + plate("scui_divider", 20, -134, w, 3, depth=1),
         "\t\t" + icon("ui_game_symbol_map_cabin", 38, -164, 26),
         "\t\t" + label(64, -150, w - 44, 26, 21, GOLD, key="scUiNcStartTitle", extra='upper_case="true"'),
         "\t\t" + label(64, -180, w - 44, 66, 16, BODY, key="scUiNcStart"),
         "\t\t" + icon("{ncjoinicon}", 38, -264, 26),      # allies (PerPlayer) or a party (PerParty)
         "\t\t" + label(64, -250, w - 44, 26, 21, GOLD, key="scUiNcJoinTitle", extra='upper_case="true"'),
         "\t\t" + label(64, -280, w - 44, 66, 16, BODY, text="{ncjoin}"),
         "\t\t" + plate("scui_divider", 20, -356, w, 3, depth=1),
         "\t\t" + label(20, -368, w, 26, 21, TITLE, key="scUiNcNow", extra='upper_case="true"')]
    for k, (sprite, key) in enumerate(NC_NOW):
        y = 404 + k * 48
        o += ["\t\t" + icon(sprite, 38, -(y + 14), 24),
              "\t\t" + label(64, -y, w - 44, 44, 16, BODY, key=key)]
    o += ["\t\t" + label(20, -(404 + len(NC_NOW) * 48 + 6), w, 120, 16, GOLD, key="scUiNcKeep"),
          "\t</rect>"]
    tw = DET_W - 48
    o += ['\t<rect depth="1" name="ncEnclavePanel" pos="%d,-%d" width="%d" height="%d">' % (DET_X, PAN_Y, DET_W, PAN_H),
          "\t\t" + plate("scui_window", 0, 0, DET_W, PAN_H),
          "\t\t" + label(24, -14, tw, 34, 28, TITLE, key="scUiNcEnclaves", extra='upper_case="true"'),
          "\t\t" + label(24, -54, tw, 22, 17, DIM, text="{ncencsummary}")]
    # round en1: the ENCLAVES tab's rows (bar, kind and size, badge; a click marks the enclave on the map), six to a page
    for k in range(1, ENC_ROWS + 1):
        y = NC_ENC_TOP + (k - 1) * (ENC_ROW_H + ENC_ROW_GAP)
        o += ["\t\t" + line for line in enc_row("ncenc", "NcEnc", k, 24, y, tw, DET_W, 220)]
    ey = NC_ENC_TOP + ENC_ROWS * (ENC_ROW_H + ENC_ROW_GAP)
    o += ['\t\t<rect depth="0" pos="0,0" width="%d" height="%d" visible="{ncencempty}">' % (DET_W, PAN_H),
          "\t\t\t" + label(24, -(NC_ENC_TOP + 6), tw, 120, 18, BODY, key="scUiNcEncNone"),
          "\t\t</rect>",
          # round MP7 (review U): m2 - Showing X of Y and a pager in place of "+N more"
          "\t\t" + label(24, -(ey + 4), tw - PAGER_W, 22, 17, DIM, text="{ncencshowing}")]
    o += ["\t\t" + line for line in pager("NcEnc", 24 + tw, ey, 30, DET_W, "ncencpagevisible")]
    o += ["\t\t" + label(24, -(ey + 36), tw, 60, 16, DIM, key="scUiNcEncNote"),
          "\t</rect>"]
    w = MOR_W - 40
    o += ['\t<rect depth="1" name="ncFavourPanel" pos="%d,-%d" width="%d" height="%d">' % (MOR_X, PAN_Y, MOR_W, PAN_H),
          "\t\t" + plate("scui_window", 0, 0, MOR_W, PAN_H),
          "\t\t" + label(20, -14, w, 34, 28, TITLE, key="scUiNcFavours", extra='upper_case="true"')]
    for k in range(1, NC_FAV_ROWS + 1):
        y = NC_FAV_TOP + (k - 1) * (NC_ROW_H + NC_ROW_GAP)
        o += ["\t\t" + line for line in nc_row("ncfav", k, 20, y, w, MOR_W)]
    fy = NC_FAV_TOP + NC_FAV_ROWS * (NC_ROW_H + NC_ROW_GAP)
    o += ['\t\t<rect depth="0" pos="0,0" width="%d" height="%d" visible="{ncfavempty}">' % (MOR_W, PAN_H),
          "\t\t\t" + label(20, -(NC_FAV_TOP + 6), w, 90, 17, BODY, key="scUiNcFavNone"),
          "\t\t</rect>",
          "\t\t" + label(20, -(fy + 3), w - PAGER_W, 22, 16, DIM, text="{ncfavshowing}")]
    o += ["\t\t" + line for line in pager("NcFav", 20 + w, fy, 28, MOR_W, "ncfavpagevisible")]
    o += ["\t\t" + label(20, -(NC_REQ_TOP - 38), w, 26, 22, TITLE, key="scUiNcRequests", extra='upper_case="true"'),
          "\t\t" + plate("scui_divider", 20, -(NC_REQ_TOP - 10), w, 3, depth=1)]
    for k in range(1, NC_REQ_ROWS + 1):
        y = NC_REQ_TOP + (k - 1) * (NC_ROW_H + NC_ROW_GAP)
        o += ["\t\t" + line for line in nc_row("ncreq", k, 20, y, w, MOR_W)]
    ry = NC_REQ_TOP + NC_REQ_ROWS * (NC_ROW_H + NC_ROW_GAP)
    o += ['\t\t<rect depth="0" pos="0,0" width="%d" height="%d" visible="{ncreqempty}">' % (MOR_W, PAN_H),
          "\t\t\t" + label(20, -(NC_REQ_TOP + 6), w, 60, 17, BODY, key="scUiNcReqNone"),
          "\t\t</rect>",
          "\t\t" + label(20, -(ry + 2), w - PAGER_W, 22, 16, DIM, text="{ncreqshowing}")]
    o += ["\t\t" + line for line in pager("NcReq", 20 + w, ry, 26, MOR_W, "ncreqpagevisible")]
    o += ["\t\t" + label(20, -(ry + 30), w, 40, 15, DIM, key="scUiNcFavNote"),
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
    for line in header() + resources() + members() + detail() + morale() + nocommunity() + footer():
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
    print("generate_community_ui: wrote windowScCommunity %dx%d, %d member rows, %d outpost rows, %d facility rows, %d enclave rows, %d resources"
          % (WIN_W, WIN_H, ROWS, OP_ROWS, FAC_ROWS, ENC_ROWS, len(RESOURCES)))
    assert ROWS_TOP + ROWS * (ROW_H + ROW_GAP) + 40 <= PAN_H, "member rows run past the panel"
    assert ROWS_TOP + OP_ROWS * (ROW_H + ROW_GAP) + 40 <= PAN_H, "outpost rows run past the panel"
    assert ROWS_TOP + FAC_ROWS * (FAC_ROW_H + FAC_ROW_GAP) + 40 <= PAN_H, "facility rows and their pager run past the panel"
    assert 96 + (FAC_FACTS - 1) * 42 + 40 <= 290, "the facility facts run into the details heading"
    assert FAC_ABOUT_Y + FAC_ABOUT_H <= 580 - 4, "the facility details run into the actions heading"
    assert (DET_W - 48 - 8 * (FAC_ACTIONS - 1)) // FAC_ACTIONS >= 140, "a facility action button is too narrow for its label"
    assert STAFF_TOP + 3 * (STAFF_H + STAFF_GAP) <= NET_TOP - 36, "the garrison runs into the network heading"
    assert NET_TOP + len(NETS) * (NET_H + NET_GAP) <= 578, "the network rows run into the orders (heading at 580)"
    assert RUN_Y + RUN_H <= PAN_H - 4, "the orders run past the detail panel"
    assert WORK_MENU_Y + WORK_MENU_H <= ORDER_Y - 2, "the work button runs into the order buttons"
    assert TRAIT_TOP + 3 * (TRAIT_H + TRAIT_GAP) <= WORK_MENU_Y, "the traits run into the work button"
    assert len(ROLE_BUTTONS) * (ROLE_W + 6) + 120 <= DET_W - 48, "no room left for the work button"
    cells = run_cells(DET_W - 48)
    assert cells[-1][2] + cells[-1][3] == 24 + DET_W - 48, "the run picker does not end at the panel's text edge"
    assert min(c[3] for c in cells) >= 44, "a run button is too narrow for a 4-letter label at 13 px"
    assert FOOT_Y + 28 <= WIN_H, "the footer runs past the window"
    assert 3 + HEADER_H <= RES_Y - 4, "the vanilla header bar runs into the resources"
    # round MP7 (part U)
    assert WIN_W - M - 56 - 12 - 250 - 12 - PL_BTN_W >= 106 + 700, "the PLAYERS button runs into the community name"
    # round MP7 (review U): the pagers and the taller role line
    assert 54 + PL_YOU_H <= PL_TOP - 4, "the player's own role line runs into the players rows"
    assert PL_TOP + PL_ROWS * (PL_ROW_H + PL_ROW_GAP) + 4 + 34 <= LEAVE_Y - 4, "the players rows and their pager run into Leave community"
    assert LEAVE_Y + LEAVE_H + 8 + 44 <= PAN_H, "Leave community's answer runs past the panel"
    assert NC_ENC_TOP + ENC_ROWS * (ENC_ROW_H + ENC_ROW_GAP) + 36 + 60 <= PAN_H, "the enclave rows and their pager run past the panel"
    assert NC_FAV_TOP + NC_FAV_ROWS * (NC_ROW_H + NC_ROW_GAP) + 28 <= NC_REQ_TOP - 38 - 2, "the favor rows and their pager run into the requests heading"
    assert NC_REQ_TOP + NC_REQ_ROWS * (NC_ROW_H + NC_ROW_GAP) + 30 + 40 <= PAN_H, "the request rows and their pager run past the panel"
    assert 404 + len(NC_NOW) * 48 + 6 + 120 <= PAN_H, "the no-community notes run past the panel"
    # round en1: the ENCLAVES tab
    assert ROWS_TOP + ENC_ROWS * (ENC_ROW_H + ENC_ROW_GAP) + 40 <= PAN_H, "the enclave rows and their pager run past the panel"
    assert 52 + 20 <= ENC_ROW_H - 2, "an enclave row's last line runs out of its row"
    assert (MEM_W - 40 - 8 * (len(TABS) - 1)) // len(TABS) >= 90, "a tab is too narrow for FACILITIES at 15 px"
    assert 96 + (ENC_FACTS - 1) * 42 + 40 <= 290, "the enclave facts run into the standing heading"
    assert 356 + 24 <= ENC_ABOUT_Y - 4, "the standing points run into the enclave's details"
    assert ENC_ABOUT_Y + ENC_ABOUT_H <= 580 - 4, "the enclave's details run into the orders heading"


if __name__ == "__main__":
    main()
