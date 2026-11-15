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
this window's grid - its widget count and its columns, read from the window itself. Vanilla's windowLooting is not
touched: every other container keeps its window, and nothing is added to it.

    vanilla's look (no AshfallUI): windowLooting exactly, at the size it has for a 9 x 10 crate - the window 691 wide
        and declared 378 tall (vanilla's own number, which its stack-panel layout uses), the header, the actions bar
        (filters, sort, fill, take all) at -46, the content box at -83 that grows with the rows up to 10 (753) and
        scrolls past them, and SC's linked strip 3 under the box (as generate_strips_ui.py hangs it under vanilla's grid)
    AshfallUI's look: AshfallUI's own looting window without RE Inventory (its make_filters.py): the window as tall as
        its view plus 97, the view up to 7 rows (528) and scrolling past them, the actions in the header (the search
        icon and its box over the title, sort, fill, take all), no gray box and frame, the narrow inset scroll bar, the
        scrollview at depth 8 over a plate 8 outside the window, and the content at -91 under a 40-unit band below the
        header, as there. 8 columns, not 9 (round s1b): the size of AshfallUI's loot window for the widest vanilla
        crate, so INSPECT and the inventory lay out beside it exactly as for looting - see below. SC's linked strip
        hangs 4 under the content box on the window's plate, the window and the plate taller by it (round s1c; it sat
        in the band before). The band holds nothing of SC's: AshfallUI appends its loot filter tab row to this window
        there (its ashScLinkedFilters, 0,-47, 36 tall, depth 3), which SC's strip (depth 14) covered and whose Other
        and Mods tabs its Refresh took the clicks of. At its tallest (7 rows) the window is 669, under AshfallUI's
        INSPECT (787), and the left panel is aligned to the bottom of the tallest one, so the looting row is no taller
        than for a crate.

Why 8 columns with AshfallUI (round s1b; the user, 2026-11-15: "with Ashfall UI, the window is a bit too wide so the
storage/inventory get cut off a little bit"). At the same column count this window and AshfallUI's windowLooting were
already the same to the unit: 691 wide (columns x 75 + 16), the content 678 at 3,-91, the 16-wide scroll bar strip at
678 (ash_narrow and ash_inset move its art, not the strip the header anchors to), the 75 cell, the plate 707 at -8,8,
the header 3..697, and the same window group settings (xui.xml: left_panel_valign_top="false", XUiC_LootWindowGroup,
vanilla's padding 9). The difference was the column count. No crate a player stores in is wider than 8 (loot.xml:
the wood, iron and steel writable storage, drawers, safes, gun safe - 8 x 6 to 8 x 10; nothing in the 3.4 install's
Mods patches a size), so AshfallUI's loot window is never wider than 8 x 75 + 16 = 616, and its looting row - the loot
window, INSPECT (534) and the inventory (574 without RE Inventory), padding 9 between - is built for that. The view's 9
columns made the left column 75 wider; XUi.layoutWindowsInPanel takes a panel's width from its windows' Size.x, and
AshfallUI's AshfallCentre, which centers INSPECT and slides the row to keep both ends on screen, centers the whole row
when it cannot fit, so both outer columns hung off the screen. With 8 columns the window is 616, identical to an
8-wide crate's in every horizontal metric above, and the view keeps its 360 cells as 45 rows of 8. The columns cannot
be set here alone: XUiC_LootContainer.SetSlots sets the grid's cols to the container's width on every open, so
ScStorage.SizeFor reads this grid's cols before the view is laid out (ScLinkedContainer.ViewCols) and the container is
built that wide.

Positions are fixed per look (COLS columns of 75, vanilla's loot cell), where vanilla anchors to the grid and the
scroll bar: x 3 is the grid's left edge less 3 (vanilla's "queue,0,-3"), the scroll bar's right edge is
"scrollbarborder,1,0" (697 at 9 columns, 622 at 8). Heights follow the view's rows through vanilla's own
{% gridDimensions %} binding. Each scrollview is as tall as its box's largest size (vanilla's is a fixed 800): a box
with fewer rows has nothing under its grid to show, and a clip region that never changes size cannot go stale after
the view opens.
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
CELL = 75                                 # vanilla's loot cell (windowLooting's grid)
COLS = {"van": 9, "ash": 8}               # the view's columns (ScStorage.SizeFor reads them from the grid): vanilla's
                                          # 9 x 10 grid / AshfallUI's loot window for the widest vanilla crate (8 x 10)
ROWS = {"van": 40, "ash": 45}             # 360 cells in both: the most slots the view can have (ScStorage.SizeFor)
BAR_W = 16                                # the defaultscrollbar template's width
LEFT = 3                                  # the grid's left edge less 3 (the grid stands 3 into its box at x 3)
VIEW_ROWS = {"van": 10, "ash": 7}         # rows in view: vanilla's 9 x 10 crate / AshfallUI's loot window
CONTENT_Y = {"van": 83, "ash": 91}        # the content box's top
STRIP_GAP = {"van": 3, "ash": 4}          # the strip under the content box (generate_strips_ui: 6 under the grid);
                                          # AshfallUI's 4: the band stands 4 under the header, the content 4 under it
ASH_STRIP_H = 40                          # AshfallUI's look: the strip (two 14-point lines, Refresh 28), the band's height
ASH_FOOT = 6                              # AshfallUI's loot window: 6 under its last box (the view + 91 + 6)
BAND_Y, BAND_H = 47, 40                   # AshfallUI's look: the band under the header, left to AshfallUI's filter
                                          # row (ashScLinkedFilters, appended by its make_filters.py: 0,-47, 36 tall)
REF_W, REF_H = strips.REF_W, strips.REF_H
ASH_LOOT_W = 8 * CELL + 16                # 616: AshfallUI's windowLooting for an 8-wide crate (v2i_x(gridDimensions) * 75 + 16)
ASH_INSPECT_H = 787                       # mirrors (not read from) AshfallUI's INSPECT (itemInfoPanel / emptyInfoPanel): the row's tallest


def grid_w(look):
    """The content box: vanilla's v2i_x(gridDimensions) * 75 + 3 (678 / 603)."""
    return COLS[look] * CELL + 3


def win_w(look):
    """The window: vanilla's v2i_x(gridDimensions) * 75 + 16 (691 / 616)."""
    return COLS[look] * CELL + 16


def right(look):
    """The scroll bar's right edge (vanilla's "scrollbarborder,1,0"): 697 / 622."""
    return LEFT + grid_w(look) + BAR_W


def head_w(look):
    """The header and the actions bar, from the grid's left edge less 3 to the scroll bar's right edge: 694 / 619."""
    return right(look) - LEFT


def strip_w(look):
    """The linked strip's width: the header's, made even (a template's ${x/2} of an odd one is a half-unit position)."""
    return head_w(look) - head_w(look) % 2


def view_max(look):
    return VIEW_ROWS[look] * CELL + 3     # 753 / 528


def ash_below():
    """AshfallUI's look: the window's height less the content box's - its top, the strip and its gap, the foot: 141."""
    return CONTENT_Y["ash"] + STRIP_GAP["ash"] + ASH_STRIP_H + ASH_FOOT


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
    r = right(look) - (205 if look == "ash" else 170)
    return nest('<rect name="header" pos="%d,0" width="%d" height="43" depth="0">' % (LEFT, head_w(look)), [
        '<sprite depth="1" name="headerbackground" sprite="ui_game_panel_header" pos="0,0" width="%d" height="43"/>' % head_w(look),
        '<sprite depth="3" name="windowIcon" style="icon32px" pivot="center" pos="21,-21" sprite="ui_game_symbol_loot_sack"/>',
        '<label depth="2" name="lootName" pos="%d,-21" width="%d" height="38" text="{lootcontainer_name}" font_size="32" pivot="left"/>'
        % (45 - LEFT, r - 45)])


# ---------------------------------------------------------------- the actions (ContainerStandardControls)
FILTER_CATEGORY = ('<iconbutton name="btnFilterCategory" sprite="server_filter" pos="20,${ -Round(parentinnerheight/2,0) }" width="30" '
                   'height="30" selectable="true" visible="%s" tooltip_key="xuiInventoryFilterByCategory"/>')
CATEGORY_GRID = ['<grid name="categoryFiltersArea" pos="100,${ -Round(parentinnerheight/2,0) }" cols="10" rows="1" cell_width="34" '
                 'repeat_content="true" controller="CategoryList" allow_key_paging="false" visible="%s">',
                 '\t<category_icon size="30" collider_scale="1"/>',
                 '</grid>']
GRID8 = "{% v2i_x(gridDimensions) &gt;= 8 }"
FILTERING_OFF = 'enabled="{% !filtering_active }" disabledcolor="[disabledColor]" disabled_tooltip_key="xuiCanNotBeUsedWhileFiltering"'


def buttons(look, h):
    """vanilla's lock, sort, fill-and-smart and take-all, 162 wide at the scroll bar's right edge."""
    return nest('<rect pos="%d,0" width="162" height="%d" visible="{buttons_visible}">' % (right(look) - 162, h), [
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
    look = "van"
    box = 'pivot="topleft" type="sliced" globalopacitymod="0" ignoreparentpadding="true" pos="%d,0" width="%d" height="40"' % (LEFT, head_w(look))
    filters = nest('<rect name="filters" pos="2,0" width="600">', [
        FILTER_CATEGORY % GRID8,
        '<iconbutton name="btnFilterText" sprite="ui_game_symbol_search" pos="60,${ -Round(parentinnerheight/2,0) }" width="30" '
        'height="30" selectable="true" visible="%s" tooltip_key="xuiInventoryFilterByText"/>' % GRID8]
        + [CATEGORY_GRID[0] % "{% filter_categories }"] + CATEGORY_GRID[1:] + nest(
        '<rect name="textSearchArea" pos="80,-5" width="250" height="30" visible="{% filter_text_selected }">', [
            '<textfield depth="5" width="${parentinnerwidth}" height="${parentinnerheight}" name="txtFilterText" focus_on_open="false" '
            'search_field="false" close_group_on_tab="true" clear_button="true"/>']))
    return nest('<rect name="actions" pos="0,-46" width="%d" height="40" controller="ContainerStandardControls">' % right(look), [
        '<sprite depth="2" name="sprBorder" fillcenter="false" sprite="menu_empty2px" color="[black]" %s/>' % box,
        '<sprite depth="0" name="sprBackground" fillcenter="true" sprite="menu_empty" color="[darkGrey]" %s/>' % box]
        + filters + buttons(look, 40))


def actions_ashfall():
    """AshfallUI's: the actions bar moved into the header (pos 0,-2, 43 tall, no sprites), the search icon 202 left of the
    scroll bar's right edge with its box 205 left of that (over the title), the category filter hidden."""
    look = "ash"
    filters = nest('<rect name="filters" pos="%d,0" width="40" height="43">' % (right(look) - 202), [
        FILTER_CATEGORY % "false",
        '<iconbutton name="btnFilterText" depth="3" sprite="ui_game_symbol_search" pos="20,-20" width="30" height="30" '
        'selectable="true" visible="%s" tooltip_key="xuiInventoryFilterByText"/>' % GRID8]
        + [CATEGORY_GRID[0] % "false"] + CATEGORY_GRID[1:] + nest(
        '<rect name="textSearchArea" pos="-205,-6" width="200" height="30" visible="{% filter_text_selected }">', [
            '<textfield depth="5" width="${parentinnerwidth}" height="${parentinnerheight}" name="txtFilterText" focus_on_open="false" '
            'search_field="false" close_group_on_tab="true" clear_button="true" background_sprite="scui_row_thin" '
            'backgroundcolor="[white]" bordercolor="[transparent]" font_size="22"/>']))
    return nest('<rect name="actions" pos="0,-2" width="%d" height="43" controller="ContainerStandardControls">' % right(look),
                filters + buttons(look, 43))


# ---------------------------------------------------------------- the grid
def content(look):
    h = box_h(look)
    vmax = view_max(look)
    gw = grid_w(look)
    rect = ('<rect name="content" pos="%d,-%d" width="%d" height="%s" on_scroll="true" on_press="true" on_hover="true" snap="false" '
            'gamepad_selectable="false" controller="LootContainer">' % (LEFT, CONTENT_Y[look], gw, h))
    grid = ['<scrollview width="%d" height="%d" depth="%d" showcondition="%s" maxboundsdepth="3">'
            % (gw, vmax, 2 if look == "van" else 8, "always" if look == "van" else "OnlyIfNeeded"),
            '\t<grid pos="3,0" depth="12" name="queue" rows="%d" cols="%d" cell_width="%d" cell_height="%d" repeat_content="true">'
            % (ROWS[look], COLS[look], CELL, CELL),
            '\t\t<item_stack name="0"/>',
            '\t</grid>',
            '</scrollview>']
    if look == "van":
        box = 'pivot="topleft" type="sliced" globalopacitymod="0" ignoreparentpadding="true" width="%d" height="%s"' % (gw, h)
        body = ['<sprite depth="20" name="sprBorder" fillcenter="false" sprite="menu_empty3px" color="[black]" %s/>' % box,
                '<sprite depth="0" name="sprBackground" fillcenter="true" sprite="menu_empty" color="[darkGrey]" %s/>' % box,
                '<defaultscrollbar pos="%d,0" barheight="%s" thumbheight="%s" thumb_color_hover="[hoverColor]" thumb_color_active="[white]" '
                'thumb_color_inactive="[darkGrey]" border_color_active="[black]" border_color_inactive="[black]" '
                'background_color_active="[mediumGrey]" background_color_inactive="[mediumGrey]"/>' % (gw, h, box_h(look, -8))]
    else:
        body = ['<defaultscrollbar pos="%d,0" barheight="%s" thumbheight="%s" ash_narrow="true" ash_inset="true" ash_track_idle="0,0,0,0"/>'
                % (gw, h, box_h(look, -8))]
    return nest(rect, body + grid)


# ---------------------------------------------------------------- the linked strip (XUiC_ScLinkedStrip)
def under_box(look):
    """The strip's pos: STRIP_GAP under the content box, which grows with the view's rows (vanilla's own binding)."""
    return ("{%% v2i(%d, -%d - Min( v2i_y(gridDimensions) * %d + 3, %d )) }"
            % (LEFT, CONTENT_Y[look] + STRIP_GAP[look], CELL, view_max(look)))


def strip_vanilla():
    """generate_strips_ui's vanilla strip, 3 under the content box instead of 6 under the grid (which scrolls here)."""
    look = "van"
    lines, h = strips.linked(strips.LOOKS["van"])
    head = ('<rect name="scLinkedStrip" depth="14" controller="%s" visible="{sclvisible}" pos="%s" width="%d" height="%d">'
            % (strips.LINK_CONTROLLER, under_box(look), grid_w(look), h))
    return [head] + lines[1:]


def strip_ashfall():
    """The strip's text and Refresh 4 under the content box, on the window's plate (no plate of its own), as wide as the
    header. Round s1c: it sat in the band under the header before, which is AshfallUI's filter tab row's now."""
    look = strips.LOOKS["ash"]
    pad = 8
    head = ('<rect name="scLinkedStrip" depth="14" controller="%s" visible="{sclvisible}" pos="%s" width="%d" height="%d">'
            % (strips.LINK_CONTROLLER, under_box("ash"), strip_w("ash"), ASH_STRIP_H))
    body = ['<label depth="4" font_size="14" pivot="left" justify="left" text="{sclheader}" color="%s" %s/>'
            % (look["text"], strips.anchors("0", pad, "1", -(pad + REF_W + 8), 2, ASH_STRIP_H - 2))]
    ry = (ASH_STRIP_H - REF_H) // 2
    body += strips.button(look, "btnLinkedRefresh", strips.anchors("1", -(pad + REF_W), "1", -pad, ry, ry + REF_H),
                          "scStorageRefresh", font=look["font"])
    return nest(head, body)


# ---------------------------------------------------------------- the window
def window(look):
    if look == "van":
        win = ('<window name="%s" width="%d" height="378" controller="LootWindow" panel="Left" cursor_area="true">'
               % (WINDOW, win_w(look)))
        body = header(look) + actions_vanilla() + content(look) + strip_vanilla()
    else:
        win = ('<window name="%s" width="%d" height="%s" controller="LootWindow" panel="Left" cursor_area="true">'
               % (WINDOW, win_w(look), box_h(look, ash_below())))
        plate = ('<sprite name="scLinkedPlate" depth="-2" sprite="scui_window" type="sliced" color="[white]" pos="-8,8" '
                 'width="%d" height="%s"/>' % (win_w(look) + 16, box_h(look, ash_below() + 16)))
        body = [plate] + header(look) + actions_ashfall() + strip_ashfall() + content(look)
    return ['\t<append xpath="/windows">'] + ["\t\t" + line for line in nest(win, body, "</window>")] + ["\t</append>"]


def build():
    o = ['\t<!-- SC\'s own linked storage window (round s1), GENERATED by Tools/generate_linked_storage_ui.py - edit that, not this.',
         '\t     Vanilla\'s looting window with %d cells in a scrolling view (%d rows of %d; %d rows of %d with AshfallUI, the width'
         % (ROWS["van"] * COLS["van"], ROWS["van"], COLS["van"], ROWS["ash"], COLS["ash"]),
         '\t     of its loot window for the widest vanilla crate): the window group "sclinkedstorage" (xui.xml) has vanilla\'s',
         '\t     XUiC_LootWindowGroup, the window vanilla\'s loot controllers. ScStorage opens linked storage here when RE',
         '\t     Inventory is not loaded (vanilla\'s windowLooting has 90 cells) and sizes the view to this grid, its cells and its',
         '\t     columns. Two looks: vanilla\'s windowLooting, and AshfallUI\'s loot window when it is loaded. -->']
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
    print("generate_linked_storage_ui: wrote %s %d wide (%d with AshfallUI), %d cells (%d x %d of %d; %d x %d with AshfallUI), "
          "%d rows in view (%d with AshfallUI)"
          % (WINDOW, win_w("van"), win_w("ash"), ROWS["van"] * COLS["van"], COLS["van"], ROWS["van"], CELL,
             COLS["ash"], ROWS["ash"], VIEW_ROWS["van"], VIEW_ROWS["ash"]))
    # every fixed size a template could halve even (a template's ${x/2} of an odd one is a half-unit position); the
    # AshfallUI look's grid box and header are vanilla's own 603 and 619 for 8 columns, as on every 8-wide crate
    for v in (grid_w("van"), head_w("van"), strip_w("ash"), ASH_STRIP_H, REF_W, REF_H):
        assert v % 2 == 0, "odd size %d" % v
    for look in COLS:
        assert ROWS[look] * COLS[look] >= 300, "fewer cells than the sane cap the user asked for (300+)"
    assert ROWS["ash"] * COLS["ash"] == ROWS["van"] * COLS["van"], "the two looks must hold the same cells"
    assert view_max("van") == 753, "vanilla's look must not outgrow vanilla's 9 x 10 crate (753)"
    assert win_w("ash") == ASH_LOOT_W, "AshfallUI's look must be exactly AshfallUI's loot window for an 8-wide crate (616)"
    assert head_w("ash") <= strip_w("ash") + 1, "the strip must span the header"
    assert BAND_Y + BAND_H <= CONTENT_Y["ash"] - 4, "AshfallUI's band runs into the grid"
    assert CONTENT_Y["ash"] + CELL + 3 + STRIP_GAP["ash"] >= BAND_Y + BAND_H, "the strip reaches into AshfallUI's band"
    assert REF_H + 4 <= ASH_STRIP_H, "Refresh does not fit the strip"
    assert view_max("ash") + ash_below() <= ASH_INSPECT_H, \
        "the window at its tallest outgrows INSPECT, so the looting row would grow (left panel aligned to the bottom)"
    for look in COLS:
        assert 8 + REF_W + 8 + 300 <= head_w(look), "no room for the strip's text beside Refresh"


if __name__ == "__main__":
    main()
