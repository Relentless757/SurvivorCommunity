#!/usr/bin/env python3
"""
Generates SC's own linked storage window (round s1), windowScLinkedStorage, into Config/XUi_InGame/windows.xml between
its GENERATED markers (added before </configs> the first time). Run from anywhere:
    python Tools/generate_linked_storage_ui.py

Why it exists (the user's report, 2026-11-14: "Linked storage without RE Inventory is not expanding beyond 90 slots").
Linked storage (ScStorage, ScLinkedContainer34.cs) shows every box of the base as one container. Without RE Inventory it
opened in vanilla's looting window, whose grid has 90 item widgets (rows="10" cols="9"), and on 3.4 the view could
never have more slots than that: vanilla's refresh (XUiC_LootContainer.OnTileEntityChanged) indexes one widget per
container slot, unguarded, so a bigger view throws on every change (the round n10 trap). 90 stacks filled the view and
the boxes' 102 free slots had no cell to drop into.

This window is vanilla's looting window again - the same controllers (LootWindow, ContainerStandardControls,
LootContainer) in a window group with vanilla's XUiC_LootWindowGroup (Config/XUi_InGame/xui.xml, "sclinkedstorage"), so
drag and drop, shift-click, take-all (button and R), sort, the search filter, E to close and the 32 m auto-close are the
game's own - but its grid has ROWS x COLS cells in a scrolling view. ScStorage opens linked storage here only when RE
Inventory is not loaded (with it, its own grid draws the view in the looting window, as before), and sizes the view to
this window's widget count, read from the window itself. Vanilla's windowLooting is not touched: every other container
keeps its window, and nothing is added to it.

    vanilla's look (no AshfallUI): windowLooting exactly, at the size it has for a 9 x 10 crate - the window 691 wide
        and declared 378 tall (vanilla's own number, which its stack-panel layout uses), the header, the actions bar
        (filters, sort, fill, take all) at -46, the content box at -83 that grows with the rows up to 10 (753) and
        scrolls past them, and SC's linked strip 3 under the box (as generate_strips_ui.py hangs it under vanilla's grid)
    AshfallUI's look: AshfallUI's own looting window without RE Inventory (its make_filters.py): the window as tall as
        its view plus 97, the view up to 7 rows (528) and scrolling past them, the actions in the header (the search
        icon and its box over the title, sort, fill, take all), no gray box and frame, the narrow inset scroll bar, the
        scrollview at depth 8 over a plate 8 outside the window - and SC's linked strip in the 40-unit band under the
        header where AshfallUI's loot window has its filter tabs (the content at -91, as there). Same size as AshfallUI's
        own loot window for a 9-wide crate, so INSPECT and the inventory lay out beside it exactly as for looting.

Positions are fixed for 9 columns of 75 (ScLinkedContainer.Cols; vanilla's loot cell), where vanilla anchors to the grid
and the scroll bar: x 3 is the grid's left edge less 3 (vanilla's "queue,0,-3"), x 697 the scroll bar's right edge
("scrollbarborder,1,0"). Heights follow the view's rows through vanilla's own {% gridDimensions %} binding. Each
scrollview is as tall as its box's largest size (vanilla's is a fixed 800): a box with fewer rows has nothing under its
grid to show, and a clip region that never changes size cannot go stale after the view opens.
"""

import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import vanilla_look  # noqa: E402
import generate_strips_ui as strips  # noqa: E402  (the linked strip's own look and widgets)

MOD = os.path.dirname(HERE)
TARGET = os.path.join(MOD, "Config", "XUi_InGame", "windows.xml")
BEGIN = "\t<!-- GENERATED:BEGIN sclinkedstorage (Tools/generate_linked_storage_ui.py) -->"
END = "\t<!-- GENERATED:END sclinkedstorage -->"
WINDOW = "windowScLinkedStorage"          # xui.xml: window group "sclinkedstorage" (ScStorage.OwnWindowGroup)

# ---------------------------------------------------------------- geometry
COLS = 9                                  # ScLinkedContainer.Cols
CELL = 75                                 # vanilla's loot cell (windowLooting's grid)
ROWS = 40                                 # 360 cells: the most slots the view can have (ScStorage.SizeFor reads them)
GRID_W = COLS * CELL + 3                  # 678: the content box (vanilla: v2i_x(gridDimensions) * 75 + 3)
WIN_W = COLS * CELL + 16                  # 691: the window (vanilla: v2i_x(gridDimensions) * 75 + 16)
BAR_W = 16                                # the defaultscrollbar template's width
LEFT = 3                                  # the grid's left edge less 3 (the grid stands 3 into its box at x 3)
RIGHT = LEFT + GRID_W + BAR_W             # 697: the scroll bar's right edge
HEAD_W = RIGHT - LEFT                     # 694: the header and the actions bar
VIEW_ROWS = {"van": 10, "ash": 7}         # rows in view: vanilla's 9 x 10 crate / AshfallUI's loot window
CONTENT_Y = {"van": 83, "ash": 91}        # the content box's top
STRIP_GAP = 3                             # vanilla's look: the strip under the box (generate_strips_ui: 6 under the grid)
BAND_Y, BAND_H = 47, 40                   # AshfallUI's look: the strip's band under the header (its filter row: 47, 36)
REF_W, REF_H = strips.REF_W, strips.REF_H


def view_max(look):
    return VIEW_ROWS[look] * CELL + 3     # 753 / 528


def box_h(look, plus=0):
    """The content box's height for the view's rows, plus a constant: vanilla's Min( rows * 75 + 3, max )."""
    expr = "Min( v2i_y(gridDimensions) * %d + 3, %d )" % (CELL, view_max(look))
    if plus > 0:
        expr += " + %d" % plus
    elif plus < 0:
        expr += " - %d" % -plus
    return "{% " + expr + " }"


def nest(open_tag, body, close_tag="</rect>"):
    return [open_tag] + ["\t" + line for line in body] + [close_tag]


# ---------------------------------------------------------------- the header (vanilla's, both looks)
def header(look):
    # lootName: vanilla anchors it from the grid's left + 39 (x 45) to the scroll bar's right less 170 (AshfallUI: less
    # 205, its search box sits over the title), 2 inside the header's top and bottom; pivot left centers the text on pos
    right = RIGHT - (205 if look == "ash" else 170)
    return nest('<rect name="header" pos="%d,0" width="%d" height="43" depth="0">' % (LEFT, HEAD_W), [
        '<sprite depth="1" name="headerbackground" sprite="ui_game_panel_header" pos="0,0" width="%d" height="43"/>' % HEAD_W,
        '<sprite depth="3" name="windowIcon" style="icon32px" pivot="center" pos="21,-21" sprite="ui_game_symbol_loot_sack"/>',
        '<label depth="2" name="lootName" pos="%d,-21" width="%d" height="38" text="{lootcontainer_name}" font_size="32" pivot="left"/>'
        % (45 - LEFT, right - 45)])


# ---------------------------------------------------------------- the actions (ContainerStandardControls)
FILTER_CATEGORY = ('<iconbutton name="btnFilterCategory" sprite="server_filter" pos="20,${ -Round(parentinnerheight/2,0) }" width="30" '
                   'height="30" selectable="true" visible="%s" tooltip_key="xuiInventoryFilterByCategory"/>')
CATEGORY_GRID = ['<grid name="categoryFiltersArea" pos="100,${ -Round(parentinnerheight/2,0) }" cols="10" rows="1" cell_width="34" '
                 'repeat_content="true" controller="CategoryList" allow_key_paging="false" visible="%s">',
                 '\t<category_icon size="30" collider_scale="1"/>',
                 '</grid>']
GRID8 = "{% v2i_x(gridDimensions) &gt;= 8 }"
FILTERING_OFF = 'enabled="{% !filtering_active }" disabledcolor="[disabledColor]" disabled_tooltip_key="xuiCanNotBeUsedWhileFiltering"'


def buttons(h):
    """vanilla's lock, sort, fill-and-smart and take-all, 162 wide at the scroll bar's right edge."""
    return nest('<rect pos="%d,0" width="162" height="%d" visible="{buttons_visible}">' % (RIGHT - 162, h), [
        '<iconbutton depth="3" name="btnToggleLockMode" sprite="ui_game_symbol_lock" tooltip_key="xuiStashLockModeToggle" pos="20, -20" '
        'width="30" height="30" selectable="true" selected_blink="true" enabled="{% !filtering_active }" visible="{hasslotlocks}" '
        'disabled_tooltip_key="xuiCanNotBeUsedWhileFiltering"/>',
        '<button depth="3" name="btnSort" sprite="ui_game_symbol_sort" tooltip_key="lblSortContainer" pos="60, -20" '
        'style="icon30px, press, hover" pivot="center" sound="[paging_click]" %s/>' % FILTERING_OFF,
        '<button depth="3" name="btnMoveFillAndSmart" sprite="ui_game_symbol_store_similar_up" tooltip_key="xuiLootTakeFillAndSmart" '
        'pos="100, -20" style="icon30px, press, hover" pivot="center" sound="[paging_click]" %s/>' % FILTERING_OFF,
        '<button depth="3" name="btnMoveAll" sprite="ui_game_symbol_store_all_up" tooltip="{take_all_tooltip}" pos="140, -20" '
        'style="icon30px, press, hover" pivot="center" sound="[paging_click]" %s/>' % FILTERING_OFF])


def actions_vanilla():
    """windowLooting's actions bar: a gray bar at -46, the category and text filters on the left, the buttons right."""
    box = 'pivot="topleft" type="sliced" globalopacitymod="0" ignoreparentpadding="true" pos="%d,0" width="%d" height="40"' % (LEFT, HEAD_W)
    filters = nest('<rect name="filters" pos="2,0" width="600">', [
        FILTER_CATEGORY % GRID8,
        '<iconbutton name="btnFilterText" sprite="ui_game_symbol_search" pos="60,${ -Round(parentinnerheight/2,0) }" width="30" '
        'height="30" selectable="true" visible="%s" tooltip_key="xuiInventoryFilterByText"/>' % GRID8]
        + [CATEGORY_GRID[0] % "{% filter_categories }"] + CATEGORY_GRID[1:] + nest(
        '<rect name="textSearchArea" pos="80,-5" width="250" height="30" visible="{% filter_text_selected }">', [
            '<textfield depth="5" width="${parentinnerwidth}" height="${parentinnerheight}" name="txtFilterText" focus_on_open="false" '
            'search_field="false" close_group_on_tab="true" clear_button="true"/>']))
    return nest('<rect name="actions" pos="0,-46" width="%d" height="40" controller="ContainerStandardControls">' % RIGHT, [
        '<sprite depth="2" name="sprBorder" fillcenter="false" sprite="menu_empty2px" color="[black]" %s/>' % box,
        '<sprite depth="0" name="sprBackground" fillcenter="true" sprite="menu_empty" color="[darkGrey]" %s/>' % box]
        + filters + buttons(40))


def actions_ashfall():
    """AshfallUI's: the actions bar moved into the header (pos 0,-2, 43 tall, no sprites), the search icon 202 left of the
    scroll bar's right edge with its box 205 left of that (over the title), the category filter hidden."""
    filters = nest('<rect name="filters" pos="%d,0" width="40" height="43">' % (RIGHT - 202), [
        FILTER_CATEGORY % "false",
        '<iconbutton name="btnFilterText" depth="3" sprite="ui_game_symbol_search" pos="20,-20" width="30" height="30" '
        'selectable="true" visible="%s" tooltip_key="xuiInventoryFilterByText"/>' % GRID8]
        + [CATEGORY_GRID[0] % "false"] + CATEGORY_GRID[1:] + nest(
        '<rect name="textSearchArea" pos="-205,-6" width="200" height="30" visible="{% filter_text_selected }">', [
            '<textfield depth="5" width="${parentinnerwidth}" height="${parentinnerheight}" name="txtFilterText" focus_on_open="false" '
            'search_field="false" close_group_on_tab="true" clear_button="true" background_sprite="scui_row_thin" '
            'backgroundcolor="[white]" bordercolor="[transparent]" font_size="22"/>']))
    return nest('<rect name="actions" pos="0,-2" width="%d" height="43" controller="ContainerStandardControls">' % RIGHT,
                filters + buttons(43))


# ---------------------------------------------------------------- the grid
def content(look):
    h = box_h(look)
    vmax = view_max(look)
    rect = ('<rect name="content" pos="%d,-%d" width="%d" height="%s" on_scroll="true" on_press="true" on_hover="true" snap="false" '
            'gamepad_selectable="false" controller="LootContainer">' % (LEFT, CONTENT_Y[look], GRID_W, h))
    grid = ['<scrollview width="%d" height="%d" depth="%d" showcondition="%s" maxboundsdepth="3">'
            % (GRID_W, vmax, 2 if look == "van" else 8, "always" if look == "van" else "OnlyIfNeeded"),
            '\t<grid pos="3,0" depth="12" name="queue" rows="%d" cols="%d" cell_width="%d" cell_height="%d" repeat_content="true">'
            % (ROWS, COLS, CELL, CELL),
            '\t\t<item_stack name="0"/>',
            '\t</grid>',
            '</scrollview>']
    if look == "van":
        box = 'pivot="topleft" type="sliced" globalopacitymod="0" ignoreparentpadding="true" width="%d" height="%s"' % (GRID_W, h)
        body = ['<sprite depth="20" name="sprBorder" fillcenter="false" sprite="menu_empty3px" color="[black]" %s/>' % box,
                '<sprite depth="0" name="sprBackground" fillcenter="true" sprite="menu_empty" color="[darkGrey]" %s/>' % box,
                '<defaultscrollbar pos="%d,0" barheight="%s" thumbheight="%s" thumb_color_hover="[hoverColor]" thumb_color_active="[white]" '
                'thumb_color_inactive="[darkGrey]" border_color_active="[black]" border_color_inactive="[black]" '
                'background_color_active="[mediumGrey]" background_color_inactive="[mediumGrey]"/>' % (GRID_W, h, box_h(look, -8))]
    else:
        body = ['<defaultscrollbar pos="%d,0" barheight="%s" thumbheight="%s" ash_narrow="true" ash_inset="true" ash_track_idle="0,0,0,0"/>'
                % (GRID_W, h, box_h(look, -8))]
    return nest(rect, body + grid)


# ---------------------------------------------------------------- the linked strip (XUiC_ScLinkedStrip)
def strip_vanilla():
    """generate_strips_ui's vanilla strip, 3 under the content box instead of 6 under the grid (which scrolls here)."""
    lines, h = strips.linked(strips.LOOKS["van"])
    y = "{%% v2i(%d, -%d - Min( v2i_y(gridDimensions) * %d + 3, %d )) }" % (LEFT, CONTENT_Y["van"] + STRIP_GAP, CELL, view_max("van"))
    head = ('<rect name="scLinkedStrip" depth="14" controller="%s" visible="{sclvisible}" pos="%s" width="%d" height="%d">'
            % (strips.LINK_CONTROLLER, y, GRID_W, h))
    return [head] + lines[1:]


def strip_ashfall():
    """The strip's text and Refresh in the band under the header, on the window's plate (no plate of its own)."""
    look = strips.LOOKS["ash"]
    pad = 8
    head = ('<rect name="scLinkedStrip" depth="14" controller="%s" visible="{sclvisible}" pos="%d,-%d" width="%d" height="%d">'
            % (strips.LINK_CONTROLLER, LEFT, BAND_Y, HEAD_W, BAND_H))
    body = ['<label depth="4" font_size="14" pivot="left" justify="left" text="{sclheader}" color="%s" %s/>'
            % (look["text"], strips.anchors("0", pad, "1", -(pad + REF_W + 8), 2, BAND_H - 2))]
    ry = (BAND_H - REF_H) // 2
    body += strips.button(look, "btnLinkedRefresh", strips.anchors("1", -(pad + REF_W), "1", -pad, ry, ry + REF_H),
                          "scStorageRefresh", font=look["font"])
    return nest(head, body)


# ---------------------------------------------------------------- the window
def window(look):
    if look == "van":
        win = ('<window name="%s" width="%d" height="378" controller="LootWindow" panel="Left" cursor_area="true">'
               % (WINDOW, WIN_W))
        body = header(look) + actions_vanilla() + content(look) + strip_vanilla()
    else:
        win = ('<window name="%s" width="%d" height="%s" controller="LootWindow" panel="Left" cursor_area="true">'
               % (WINDOW, WIN_W, box_h(look, CONTENT_Y["ash"] + 6)))
        plate = ('<sprite name="scLinkedPlate" depth="-2" sprite="scui_window" type="sliced" color="[white]" pos="-8,8" '
                 'width="%d" height="%s"/>' % (WIN_W + 16, box_h(look, CONTENT_Y["ash"] + 6 + 16)))
        body = [plate] + header(look) + actions_ashfall() + strip_ashfall() + content(look)
    return ['\t<append xpath="/windows">'] + ["\t\t" + line for line in nest(win, body, "</window>")] + ["\t</append>"]


def build():
    o = ['\t<!-- SC\'s own linked storage window (round s1), GENERATED by Tools/generate_linked_storage_ui.py - edit that, not this.',
         '\t     Vanilla\'s looting window with %d cells (%d rows of %d) in a scrolling view: the window group "sclinkedstorage"'
         % (ROWS * COLS, ROWS, COLS),
         '\t     (xui.xml) has vanilla\'s XUiC_LootWindowGroup, the window vanilla\'s loot controllers. ScStorage opens linked',
         '\t     storage here when RE Inventory is not loaded (vanilla\'s windowLooting has 90 cells) and sizes the view to',
         '\t     this grid. Two looks: vanilla\'s windowLooting, and AshfallUI\'s loot window when it is loaded. -->']
    o += vanilla_look.conditional(window("ash"), window("van"))
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
        if text.count("</configs>") != 1 or not text.endswith("</configs>\n"):
            raise SystemExit("REFUSING: expected one </configs> at the end of %s" % TARGET)
        text = text[:-len("</configs>\n")] + block + "</configs>\n"
    with io.open(TARGET, "w", encoding="utf-8", newline="") as f:
        f.write(text)
    print("generate_linked_storage_ui: wrote %s %d wide, %d cells (%d x %d of %d), %d rows in view (%d with AshfallUI)"
          % (WINDOW, WIN_W, ROWS * COLS, COLS, ROWS, CELL, VIEW_ROWS["van"], VIEW_ROWS["ash"]))
    # every fixed size even (a template's ${x/2} of an odd one is a half-unit position)
    for v in (GRID_W, HEAD_W, BAND_H, REF_W, REF_H):
        assert v % 2 == 0, "odd size %d" % v
    assert ROWS * COLS >= 300, "fewer cells than the sane cap the user asked for (300+)"
    assert VIEW_ROWS["van"] * CELL + 3 == 753, "vanilla's look must not outgrow vanilla's 9 x 10 crate (753)"
    assert BAND_Y + BAND_H <= CONTENT_Y["ash"] - 4, "the strip's band runs into the grid"
    assert 8 + REF_W + 8 + 300 <= HEAD_W, "no room for the strip's text beside Refresh"


if __name__ == "__main__":
    main()
