#!/usr/bin/env python3
"""
Generates the member inventory panel (round n10), windowScMemberInv and its spacer windowScMemberInvSpace, into
Config/XUi_InGame/windows.xml between their GENERATED markers (added before </configs> the first time). Run from anywhere:
    python Tools/generate_member_inventory_ui.py

"Show me your inventory" on one of the community's members opens this instead of the game's looting screen (the user's
design, 2026-11-14, drawn at 1920x1080 and scaled by 0.8 to XUi units: 556 x 740, on the left; Phase 1). XUiC_ScMemberInv
answers every binding; the group is "scmemberinv" (Config/XUi_InGame/xui.xml, XUiC_ScMemberInvGroup), which opens the
game's backpack group beside it as the looting screen does - vanilla's backpack and item info, or AshfallUI's INSPECT and
inventory columns:

    +--------------------------------------------------------+
    | NAME (drag bar)              Member . At base . T2  [x]|
    | +- card (232) ----------+ +- equipment (300) ---------+|
    | | [pic] Medic           | | WEAPONS ------------------||
    | |       + 100 / 130     | | [slot] RANGED   [ammo]    ||
    | |                       | |        name / info        ||
    | |       > 120 / 120     | | [slot] MELEE  name / info ||
    | |       (face) Happy    | | ARMOR ---------------------||
    | | TRAITS -------------- | | [head][chest][hands][feet]||
    | |  . three rows         | | JOB TOOLS ----------------||
    | | SKILLS -------------- | | [chop][mine][dig][butcher]||
    | | [ico] Cardio  * * * . | |  [repair]  (shared)       ||
    | |  ... five rows        | |                           ||
    | +-----------------------+ +---------------------------+|
    | +- storage ---------------------------------------------+|
    | | PACK                      12 of 64       [TAKE ALL] ||
    | | 10 x 3 cells in view, scrolling (her inventory window)||
    | | BAG                        7 of 45       [TAKE ALL] ||
    | | 10 x 2 cells in view, scrolling (npc.bag; host only) ||
    | +-------------------------------------------------------+|
    +--------------------------------------------------------+

Rules (the snapshot's and the community window's, Tools/generate_snapshot_ui.py):
- Every plate is a scui_* sprite handed [white] (AshfallUI ships premium art under the same names); icons are vanilla menu
  symbols or SC's scui_mood_* faces; no [darkGrey]/menu_empty plate or boxbg in AshfallUI's look. Written twice in one
  <conditional> (Tools/vanilla_look.py): AshfallUI's look when it is loaded, vanilla's otherwise (vanilla's message box for
  the window, a header bar for the title, [darkGrey] panels with black rims, vanilla's buttons and scroll bars, white text).
- The card is the talk snapshot's (same snap* bindings, XUiC_ScMemberCard = XUiC_ScSnapshot bound to the member), narrowed to
  232 (the concept's width, the user's choice). Its portrait texture has no texture=/textures=/autounload attribute.
- Item cells are vanilla's item_stack template, which AshfallUI restyles itself: the PACK's on vanilla's LootContainer
  controller (drag and drop, shift-click, take-all as for any container), the BAG's on XUiC_ScBagGrid, the equipment's on
  XUiC_ScGearSlot (display only in Phase 1; usable since round n12 through its own input handling - nothing in the XML
  changed). A cell shows while its index is below container_slots (the template's own rule): the window answers it for
  the pack, the bag grid for the bag, a gear slot for itself. Every item_stack sits in a
  repeat_content grid (the template reads repeat_i).
- Each scroll view is vanilla's looting one: a controller rect with on_scroll/on_press/on_hover (a drop between the cells
  lands in the grid), a defaultscrollbar beside it (its scroll view finds it as a sibling) and the scrollview over the grid,
  at depth 8 so its panel clears the window's (xui-gotchas). Cells of 50 are under 70, so each view is CELL*3//5 + 28 - CELL
  (8) taller than its rows: the count label of the last row reaches that far (maxboundsdepth 3), and a view just as tall
  as its rows would scroll by those few units.
- The window is anchored at the screen's top-left and placed by XUiC_ScMemberInv (dragged by its title bar, "dragBar", which
  carries on_press for the right-click reset and on_drag) - not Center, Left or Right: XUi lays out every window of a group
  whose parent transform is NAMED like a stack panel, and the anchors' names are not ours to know. windowScMemberInvSpace,
  an empty window of the same size in the Left stack panel, reserves the looting window's place so the backpack's columns
  lay out as for looting, and gives the panel its default place. Even sizes throughout (an odd one makes half-unit
  positions in templates).
"""

import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import vanilla_look  # noqa: E402
MOD = os.path.dirname(HERE)
TARGET = os.path.join(MOD, "Config", "XUi_InGame", "windows.xml")
BEGIN = "\t<!-- GENERATED:BEGIN scmemberinv (Tools/generate_member_inventory_ui.py) -->"
END = "\t<!-- GENERATED:END scmemberinv -->"
CONTROLLER = "SurvivorCommunity.ScMemberInv, SurvivorCommunity"
CARD_CONTROLLER = "SurvivorCommunity.ScMemberCard, SurvivorCommunity"
BAG_CONTROLLER = "SurvivorCommunity.ScBagGrid, SurvivorCommunity"
SLOT_CONTROLLER = "SurvivorCommunity.ScGearSlot, SurvivorCommunity"

# ---------------------------------------------------------------- geometry
W, H = 556, 740
M = 8
POS = (40, -100)                          # anchor TopLeft; XUiC_ScMemberInv places it (the spacer's place, or the player's)
TITLE_Y, TITLE_H = 8, 36
ROW_Y, ROW_H = 52, 340                    # the card and the equipment
CARD_W = 232
EQ_X = M + CARD_W + 8                     # 248
EQ_W = W - M - EQ_X                       # 300
STORE_Y = ROW_Y + ROW_H + 8               # 400
STORE_W = W - 2 * M                       # 540
STORE_H = H - M - STORE_Y                 # 332
CELL = 50
COLS = 10
PACK_ROWS, PACK_VIEW = 10, 3              # 100 cells for SCore's 64 (8 x 8); three rows in view
BAG_ROWS, BAG_VIEW = 6, 2                 # 60 cells for the NPC bag's 45; two rows in view
LIP = max(0, CELL * 3 // 5 + 28 - CELL)   # the last row's count label below its plate (xui-gotchas)
GRID_W = COLS * CELL + 3                  # vanilla's: the grid at pos 3,0 inside its view
BAR_X, BAR_W = GRID_W + 1, 16             # the scroll bar beside the view (one unit off it: an even box of 520)
PACK_VIEW_H = PACK_VIEW * CELL + LIP
BAG_VIEW_H = BAG_VIEW * CELL + LIP
TAKE_W, TAKE_H = 100, 22

WEAPON_CELL, WEAPON_STEP = 50, 54
AMMO_W = 150                              # round U1: the ammo line's button, right of the RANGED heading
ARMOR_CELL, ARMOR_STEP = 50, 72
TOOL_CELL, TOOL_STEP = 46, 58

# ---------------------------------------------------------------- palette (the snapshot's)
TITLE = "226,219,203,255"
BODY = "226,220,208,255"
DIM = "190,182,166,255"
GOLD = "206,178,120,255"

SKILLS = (("ui_game_symbol_cardio", "scSkillCardio"),
          ("ui_game_symbol_intellect", "scSkillWits"),
          ("ui_game_symbol_melee", "scSkillFighting"),
          ("ui_game_symbol_rifle", "scSkillShooting"))
TRAIT_ROWS = 3
STARS, STAR_X, STAR_STEP, STAR_SIZE = 7, 104, 17, 14
ARMOR = ("scInvHead", "scInvChest", "scInvHands", "scInvFeet")
JOBS = ("scInvJobChop", "scInvJobMine", "scInvJobDig", "scInvJobButcher", "scInvJobRepair")
CLAMP = 'overflow="ClampContent" overflow_ellipsis="true" max_line_count="1"'


def plate(sprite, x, y, w, h, depth=0, extra=""):
    return ('<sprite depth="%d" sprite="%s" type="sliced" color="[white]" pos="%d,%d" width="%d" height="%d"%s/>'
            % (depth, sprite, x, y, w, h, (" " + extra) if extra else ""))


def icon(sprite, cx, cy, size, depth=3, color="[white]", extra=""):
    return ('<sprite depth="%d" sprite="%s" pos="%d,%d" width="%d" height="%d" pivot="center" color="%s"%s/>'
            % (depth, sprite, cx, cy, size, size, color, (" " + extra) if extra else ""))


def label(x, y, w, h, size, color, text=None, key=None, extra=""):
    t = ('text="%s"' % text) if text is not None else ('text_key="%s"' % key)
    return ('<label depth="4" pos="%d,%d" width="%d" height="%d" font_size="%d" %s color="%s"%s/>'
            % (x, y, w, h, size, t, color, (" " + extra) if extra else ""))


def button(name, x, y, w, h, extra=""):
    return ('<button depth="2" name="%s" pos="%d,%d" width="%d" height="%d" sprite="scui_btn" hoversprite="scui_btn_hover" '
            'defaultcolor="[white]" hovercolor="[white]" type="sliced" hoverscale="1.0" sound="[paging_click]"%s/>'
            % (name, x, y, w, h, (" " + extra) if extra else ""))


def heading(x, y, w, key):
    return [label(x, -y, w, 18, 15, TITLE, key=key, extra='upper_case="true"'),
            plate("scui_divider", x, -(y + 18), w, 2, depth=1)]


def nest(open_tag, body, close_tag="</rect>"):
    return [open_tag] + ["\t" + line for line in body] + [close_tag]


# ---------------------------------------------------------------- the title bar
DRAG_BAR = plate("scui_row_selected", M, -TITLE_Y, W - 2 * M, TITLE_H, depth=1,
                 extra='name="dragBar" on_press="true" on_drag="true" gamepad_selectable="false" snap="false" tooltip_key="scInvDragTip"')
DRAG_BAR_VANILLA = ('<sprite depth="1" sprite="ui_game_panel_header" type="sliced" color="%s" pos="%d,%d" width="%d" height="%d" '
                    'globalopacitymod="0" name="dragBar" on_press="true" on_drag="true" gamepad_selectable="false" snap="false" tooltip_key="scInvDragTip"/>'
                    % (vanilla_look.LIGHT, M, -TITLE_Y, W - 2 * M, TITLE_H))
CLOSE_X = W - M - 6 - 30


def title():
    return [DRAG_BAR,
            label(M + 10, -(TITLE_Y + 5), 250, 26, 22, TITLE, text="{invname}", extra='upper_case="true" ' + CLAMP),
            label(M + 260, -(TITLE_Y + 9), CLOSE_X - 8 - (M + 260), 20, 15, GOLD, text="{invsub}", extra='justify="right" ' + CLAMP),
            button("btnClose", CLOSE_X, -(TITLE_Y + 4), 30, 28, extra='tooltip_key="scInvCloseTip"'),
            icon("ui_game_symbol_x", CLOSE_X + 15, -(TITLE_Y + 18), 16, depth=4)]


# ---------------------------------------------------------------- the card (the snapshot's bindings)
SKILL_TOP, SKILL_H, SKILL_STEP = 196, 26, 28


def skill_row(i, sprite, name_line):
    y = SKILL_TOP + i * SKILL_STEP
    o = [plate("scui_row_thin", 4, -y, CARD_W - 8, SKILL_H, depth=1),
         icon(sprite, 16, -(y + SKILL_H // 2), 16),
         name_line(y)]
    for j in range(STARS):
        cx = STAR_X + j * STAR_STEP + STAR_SIZE // 2
        o.append(icon("ui_game_symbol_star", cx, -(y + SKILL_H // 2), STAR_SIZE, color="{snapsk%dstar%d}" % (i, j + 1)))
    return o


def card():
    body = [plate("scui_portrait", 8, -8, 72, 72, depth=3),
            icon("ui_game_symbol_player", 44, -44, 40, depth=4, extra='visible="{snapnoportrait}"'),
            '<texture depth="5" name="snapPortrait" pos="12,-12" width="64" height="64" visible="{snaphasportrait}"/>',
            label(88, -8, CARD_W - 96, 20, 16, GOLD, text="{snaprole}", extra=CLAMP),
            icon("ui_game_symbol_add", 96, -40, 16),
            label(108, -31, CARD_W - 116, 20, 16, BODY, text="{snaphealth} / {snaphealthmax}"),
            icon("ui_game_symbol_run", 96, -60, 16),
            label(108, -51, CARD_W - 116, 20, 16, BODY, text="{snapstamina} / {snapstaminamax}"),
            icon("{snapmoodicon}", 96, -80, 20),
            label(110, -71, CARD_W - 118, 20, 16, "{snapmoodcolor}", text="{snapmoodword}", extra=CLAMP)]
    body += heading(8, 92, CARD_W - 16, "scUiTraits")
    for k in range(TRAIT_ROWS):
        y = 114 + k * 18
        body += nest('<rect depth="1" visible="{snaptrait%dvisible}">' % (k + 1),
                     [icon("ui_game_filled_circle", 14, -(y + 9), 6, color=DIM),
                      label(24, -y, CARD_W - 32, 18, 14, BODY, text="{snaptrait%d}" % (k + 1), extra=CLAMP)])
    body += heading(8, 172, CARD_W - 16, "scUiSkills")
    for i, (sprite, key) in enumerate(SKILLS):
        body += skill_row(i, sprite, lambda y, k=key: label(28, -(y + 4), 74, 18, 14, BODY, key=k, extra=CLAMP))
    spec = skill_row(len(SKILLS), "{snapspecicon}", lambda y: label(28, -(y + 4), 74, 18, 14, BODY, text="{snapspecname}", extra=CLAMP))
    body += nest('<rect depth="1" visible="{snapspecvisible}">', spec)
    inner = nest('<rect depth="0" visible="{snapvisible}" width="%d" height="%d">' % (CARD_W, ROW_H), body)
    return nest('<rect name="card" depth="0" pos="%d,%d" width="%d" height="%d" controller="%s">' % (M, -ROW_Y, CARD_W, ROW_H, CARD_CONTROLLER),
                [plate("scui_window", 0, 0, CARD_W, ROW_H)] + inner)


# ---------------------------------------------------------------- the equipment
def slot_grid(name, x, y, rows, cols, cell_w, cell_h, size):
    return nest('<grid name="%s" depth="2" pos="%d,%d" rows="%d" cols="%d" cell_width="%d" cell_height="%d" repeat_content="true">'
                % (name, x, -y, rows, cols, cell_w, cell_h),
                ['<item_stack name="0" controller="%s" cell_size="%d"/>' % (SLOT_CONTROLLER, size)], "</grid>")


ARMOR_Y = 28 + 2 * WEAPON_STEP + 2        # 138: the ARMOR heading
TOOLS_Y = ARMOR_Y + 22 + ARMOR_CELL + 22  # 232: the JOB TOOLS heading
ARMOR_X = 15
TOOL_X = 12


def equipment():
    body = [plate("scui_window", 0, 0, EQ_W, ROW_H)]
    body += heading(10, 6, EQ_W - 20, "scInvWeapons")
    body += slot_grid("weaponSlots", 10, 28, 2, 1, WEAPON_CELL, WEAPON_STEP, WEAPON_CELL)
    tx = 10 + WEAPON_CELL + 8
    for i, (key, nb, ib) in enumerate((("scInvRanged", "invrname", "invrinfo"), ("scInvMelee", "invmname", "invminfo"))):
        y0 = 28 + i * WEAPON_STEP
        head_w = (EQ_W - 10 - AMMO_W - 4 - tx) if i == 0 else (EQ_W - 10 - tx)
        body += [label(tx, -y0, head_w, 16, 14, GOLD, key=key, extra='upper_case="true"'),
                 label(tx, -(y0 + 16), EQ_W - 10 - tx, 20, 16, BODY, text="{%s}" % nb, extra=CLAMP),
                 label(tx, -(y0 + 34), EQ_W - 10 - tx, 16, 14, DIM, text="{%s}" % ib, extra=CLAMP)]
    # round U1: the gun's ammo type (ScAmmoChoice) - the type and her rounds of it; a click takes the next type she has, a
    # right click the one before; shown on the host for a gun with more than one ammo type ({invammovisible})
    ammo = [button("btnAmmo", 0, 0, AMMO_W, 16, extra='tooltip_key="scInvAmmoTip"'),
            label(4, 0, AMMO_W - 8, 16, 13, BODY, text="{invammo}", extra='justify="center" ' + CLAMP)]
    body += nest('<rect name="ammoLine" depth="2" pos="%d,%d" width="%d" height="16" visible="{invammovisible}">'
                 % (EQ_W - 10 - AMMO_W, -28, AMMO_W), ammo)
    body += heading(10, ARMOR_Y, EQ_W - 20, "scInvArmor")
    body += slot_grid("armorSlots", ARMOR_X, ARMOR_Y + 22, 1, 4, ARMOR_STEP, ARMOR_CELL, ARMOR_CELL)
    for i, key in enumerate(ARMOR):
        cx = ARMOR_X + i * ARMOR_STEP + ARMOR_CELL // 2
        body.append(label(cx - ARMOR_STEP // 2, -(ARMOR_Y + 22 + ARMOR_CELL + 2), ARMOR_STEP, 16, 14, DIM, key=key, extra='justify="center"'))
    body += heading(10, TOOLS_Y, EQ_W - 20, "scInvJobTools")
    body += slot_grid("toolSlots", TOOL_X, TOOLS_Y + 22, 1, len(JOBS), TOOL_STEP, TOOL_CELL, TOOL_CELL)
    for i, key in enumerate(JOBS):
        cx = TOOL_X + i * TOOL_STEP + TOOL_CELL // 2
        ly = TOOLS_Y + 22 + TOOL_CELL + 2
        body += [label(cx - TOOL_STEP // 2, -ly, TOOL_STEP, 16, 14, DIM, key=key, extra='justify="center"'),
                 label(cx - TOOL_STEP // 2, -(ly + 16), TOOL_STEP, 16, 14, GOLD, text="{invtoolnote%d}" % i, extra='justify="center"')]
    return nest('<rect name="equipment" depth="0" pos="%d,%d" width="%d" height="%d">' % (EQ_X, -ROW_Y, EQ_W, ROW_H), body)


# ---------------------------------------------------------------- the storage
def scrollbar(view_h):
    return ('<defaultscrollbar pos="%d,0" barheight="%d" thumbheight="%d" ash_narrow="true" ash_inset="true" ash_track_idle="0,0,0,0"/>'
            % (BAR_X, view_h, view_h - 8))


def scrollbar_vanilla(view_h):
    return ('<defaultscrollbar pos="%d,0" barheight="%d" thumbheight="%d" thumb_color_hover="[hoverColor]" thumb_color_active="[white]" '
            'thumb_color_inactive="[darkGrey]" border_color_active="[black]" border_color_inactive="[black]" '
            'background_color_active="[mediumGrey]" background_color_inactive="[mediumGrey]"/>' % (BAR_X, view_h, view_h - 8))


def grid_view(name, controller, x, y, rows, view_h, extra=""):
    body = [scrollbar(view_h),
            '<scrollview width="%d" height="%d" depth="8" showcondition="always" maxboundsdepth="3">' % (GRID_W, view_h),
            '\t<grid pos="3,0" depth="12" name="queue" rows="%d" cols="%d" cell_width="%d" cell_height="%d" repeat_content="true">'
            % (rows, COLS, CELL, CELL),
            '\t\t<item_stack name="0" cell_size="%d"/>' % CELL,
            '\t</grid>',
            '</scrollview>']
    return nest('<rect name="%s" depth="0" pos="%d,%d" width="%d" height="%d" controller="%s" on_scroll="true" on_press="true" '
                'on_hover="true" snap="false" gamepad_selectable="false"%s>' % (name, x, -y, BAR_X + BAR_W, view_h, controller, extra), body)


def storage_header(y, key, count, take):
    return [label(10, -y, 120, 18, 15, TITLE, key=key, extra='upper_case="true"'),
            label(130, -y, STORE_W - 10 - TAKE_W - 10 - 130, 18, 15, DIM, text=count, extra='justify="right"'),
            button(take, STORE_W - 10 - TAKE_W, -(y - 2), TAKE_W, TAKE_H, extra='tooltip_key="scInvTakeAllTip"'),
            label(STORE_W - 10 - TAKE_W, -(y + 1), TAKE_W, 18, 14, BODY, key="scInvTakeAll", extra='justify="center" upper_case="true"')]


PACK_Y = 28
BAG_Y = PACK_Y + PACK_VIEW_H + 6          # 192: the BAG section (its header 4 below)


def storage():
    body = [plate("scui_window", 0, 0, STORE_W, STORE_H)]
    body += storage_header(6, "scInvPack", "{invpackcount}", "btnPackTakeAll")
    body += grid_view("pack", "LootContainer", 10, PACK_Y, PACK_ROWS, PACK_VIEW_H)
    bag = storage_header(2, "scInvBag", "{invbagcount}", "btnBagTakeAll")
    bag += grid_view("bag", BAG_CONTROLLER, 10, 24, BAG_ROWS, BAG_VIEW_H)
    body += nest('<rect name="bagArea" depth="0" pos="0,%d" width="%d" height="%d" visible="{invbagvisible}">'
                 % (-(BAG_Y + 4), STORE_W, 24 + BAG_VIEW_H), bag)
    note = [label(10, -2, 120, 18, 15, TITLE, key="scInvBag", extra='upper_case="true"'),
            label(10, -28, STORE_W - 20, 40, 15, DIM, key="scInvBagHost")]
    body += nest('<rect name="bagNote" depth="0" pos="0,%d" width="%d" height="%d" visible="{invbagnote}">'
                 % (-(BAG_Y + 4), STORE_W, 24 + BAG_VIEW_H), note)
    return nest('<rect name="storage" depth="0" pos="%d,%d" width="%d" height="%d">' % (M, -STORE_Y, STORE_W, STORE_H), body)


# ---------------------------------------------------------------- the window
MAIN = plate("scui_window", 0, 0, W, H, depth=-1)     # under the panels drawn at 0 (NGUI orders equal depths as it likes)


def window():
    """The window's append in AshfallUI's look, one element per line at the block's indent."""
    o = ['\t<append xpath="/windows">',
         '\t\t<window name="windowScMemberInv" anchor="TopLeft" pos="%d,%d" width="%d" height="%d" controller="%s" cursor_area="true">'
         % (POS[0], POS[1], W, H, CONTROLLER),
         "\t\t\t" + MAIN]
    for line in title() + card() + equipment() + storage():
        o.append("\t\t\t" + line)
    o += ["\t\t</window>", "\t</append>"]
    return o


def build():
    ash = window()
    overrides = {"\t\t\t" + MAIN: vanilla_look.window_plate("\t\t\t", W, H)}
    for line in ash:
        body = line.lstrip("\t")
        indent = line[:len(line) - len(body)]
        if body == DRAG_BAR:
            overrides[line] = [indent + DRAG_BAR_VANILLA]
        for view_h in (PACK_VIEW_H, BAG_VIEW_H):
            if body == scrollbar(view_h):
                overrides[line] = [indent + scrollbar_vanilla(view_h)]
    van = vanilla_look.vanilla(ash, overrides=overrides)
    o = ['\t<!-- The member inventory panel (round n10), GENERATED by Tools/generate_member_inventory_ui.py - edit that, not this.',
         '\t     Its window group is "scmemberinv" (xui.xml, XUiC_ScMemberInvGroup); XUiC_ScMemberInv places the window and',
         '\t     answers its bindings, the card is the talk snapshot\'s (XUiC_ScMemberCard). windowScMemberInvSpace is an empty',
         '\t     window of the same size in the Left stack panel: it keeps the looting window\'s place, so the backpack\'s columns',
         '\t     lay out as for looting, and gives the panel its default place. Two looks: AshfallUI\'s scui_* plates when it is',
         '\t     loaded, vanilla\'s otherwise (Tools/vanilla_look.py). -->',
         '\t<append xpath="/windows">',
         '\t\t<window name="windowScMemberInvSpace" width="%d" height="%d" panel="Left" cursor_area="false"/>' % (W, H),
         '\t</append>']
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
        if text.count("</configs>") != 1:
            raise SystemExit("REFUSING: expected one </configs> in %s" % TARGET)
        text = text.replace("</configs>", "\n" + block + "</configs>")
    with io.open(TARGET, "w", encoding="utf-8", newline="") as f:
        f.write(text)
    print("generate_member_inventory_ui: wrote windowScMemberInv %dx%d (card %d, equipment %d, storage %d tall from %d),"
          " pack %dx%d cells (%d in view), bag %dx%d (%d in view), view lip %d"
          % (W, H, CARD_W, EQ_W, STORE_H, STORE_Y, COLS, PACK_ROWS, PACK_VIEW, COLS, BAG_ROWS, BAG_VIEW, LIP))
    # every size even (a template's ${x/2} of an odd one is a half-unit position)
    for v in (W, H, CARD_W, EQ_W, STORE_W, STORE_H, ROW_H, TITLE_H, CELL, WEAPON_CELL, ARMOR_CELL, TOOL_CELL, PACK_VIEW_H, BAG_VIEW_H, BAR_X + BAR_W):
        assert v % 2 == 0, "odd size %d" % v
    assert EQ_X + EQ_W == W - M, "the equipment does not end at the margin"
    assert SKILL_TOP + 4 * SKILL_STEP + SKILL_H <= ROW_H - 4, "the skill rows run past the card"
    assert STAR_X + STARS * STAR_STEP <= CARD_W - 8, "the stars run past the card"
    assert 114 + TRAIT_ROWS * 18 <= 172, "the traits run into the skills heading"
    assert TOOLS_Y + 22 + TOOL_CELL + 2 + 32 <= ROW_H - 2, "the job tool labels run past the equipment"
    assert ARMOR_X + 3 * ARMOR_STEP + ARMOR_CELL <= EQ_W - 10, "the armor slots run past the equipment"
    assert EQ_W - 10 - AMMO_W - 4 - (10 + WEAPON_CELL + 8) >= 60, "the RANGED heading has no room beside the ammo line"
    assert TOOL_X + (len(JOBS) - 1) * TOOL_STEP + TOOL_CELL <= EQ_W - 10, "the tool slots run past the equipment"
    assert 10 + BAR_X + BAR_W <= STORE_W - 10, "the grids run past the storage"
    assert BAG_Y + 4 + 24 + BAG_VIEW_H <= STORE_H - 2, "the bag runs past the storage"
    assert PACK_ROWS * COLS >= 64 and BAG_ROWS * COLS >= 45, "too few cells for SCore's container (64) or the NPC bag (45)"


if __name__ == "__main__":
    main()
