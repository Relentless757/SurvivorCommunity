#!/usr/bin/env python3
"""
The vanilla look of SC's generated windows (round 32b, U32): the community window, the radio and the survivor snapshot
are written twice by their generators, inside one <conditional>:

    <if cond="mod_loaded('zzzzz_AshfallUI')">  the window as it always was: scui_* plates handed [white], which
                                               AshfallUI ships its premium art under, and SC's warm text colors
    <else>                                     the same window run through vanilla() below: vanilla's sprites and
                                               colors, as vanilla's own windows draw them

so AshfallUI keeps exactly the look it had, and without it the windows look like the game's. vanilla() works on the
generated lines (one element per line) and keeps every position, size, depth, name and binding; it only swaps sprites
and colors and adds a border sprite beside the plates and buttons that need one. What it copies, from vanilla's
Data/Config/XUi_Common/templates.xml (boxbg, headerbg, simplebutton, labeledbutton) and XUi_InGame (windowLooting's
content box, recipe_entry, skill_entry, the HUD stat bars):

    a window             messagebox's box: menu_empty 16,16,16 with a menu_empty2px [darkGrey] border
    a panel in it        windowLooting's content box: menu_empty [darkGrey] with a menu_empty3px [black] border
    a header             headerbg: ui_game_panel_header in [lightGrey]
    a button             simplebutton's clickable: menu_empty [mediumGrey], ui_game_select_row [white] under the
                         pointer, a menu_empty2px [black] border
    a row                recipe_entry / skill_entry: menu_empty [darkGrey] with a menu_empty3px [black] border,
                         [mediumGrey] under the pointer, ui_game_select_row when selected
    a bar                the HUD stat bars: a black groove, a flat colored fill, a black rim
    text                 [labelColor] white; captions light gray; the gold accents [selectedColor] (222,206,163)

Bound colors are written as numbers, never [tokens]: a {# ...} value is parsed as a color string.
"""

import re

WHITE = "255,255,255,255"
BLACK = "0,0,0,255"
DARKEST = "32,32,32,255"            # [darkestGrey]
DARK = "64,64,64,255"               # [darkGrey]
MEDIUM = "96,96,96,255"             # [mediumGrey]
LIGHT = "128,128,128,255"           # [lightGrey]
BEIGE = "222,206,163,255"           # [selectedColor]
WINDOW = "16,16,16,255"             # the messagebox template's backcolor
DANGER_FILL = "72,28,28,255"
DANGER_BTN = "96,40,40,255"
DANGER_HOVER = "140,56,56,255"
DANGER_RIM = "228,18,21,255"        # vanilla's accent red (messagebox, list rows)

# SC's palette (the generators' TITLE/BODY/DIM/GOLD/OFF/RULE) -> vanilla's. GOOD, BAD and the danger text keep their hue.
COLORS = (("226,219,203,255", WHITE),
          ("226,220,208,255", WHITE),
          ("190,182,166,255", "180,180,180,255"),
          ("206,178,120,255", BEIGE),
          ("130,124,112,255", LIGHT),
          ("140,134,124,255", LIGHT),
          ("196,168,112,70", "128,128,128,128"))

# a plate (a sliced <sprite>): its fill, and its border (sprite, color) or None
PLATES = {
    "scui_window": (("menu_empty", DARK), ("menu_empty3px", BLACK)),
    "scui_row": (("menu_empty", DARK), ("menu_empty3px", BLACK)),
    "scui_row_hover": (("menu_empty", MEDIUM), ("menu_empty3px", BLACK)),
    "scui_row_selected": (("ui_game_select_row", WHITE), None),
    "scui_tab_selected": (("ui_game_select_row", WHITE), None),
    "scui_row_thin": (("menu_empty", DARKEST), None),
    "scui_portrait": (("menu_empty", BLACK), None),
    "scui_btn": (("menu_empty", MEDIUM), ("menu_empty2px", BLACK)),
    "scui_btn_danger": (("menu_empty", DANGER_FILL), ("menu_empty2px", DANGER_RIM)),
    "scui_bar_track": (("menu_empty", BLACK), None),
    "scui_bar_frame": (("menu_empty2px", BLACK), None),
    "scui_divider": (("menu_empty", LIGHT), None),
    "scui_fill_skill": (("menu_empty", BEIGE), None),
    "scui_fill_health": (("menu_empty", "200,40,40,255"), None),
    "scui_fill_good": (("menu_empty", "120,180,90,255"), None),
}
# a morale fill whose sprite AshfallUI's look picks by band: here a flat fill in the band's color (the word's own)
BOUND_FILLS = {"{moralefillsprite}": "{moralewordcolor}", "{selmoodfillsprite}": "{selmoodcolor}"}

# a <button>'s state: (sprite, color)
BUTTON = {
    "scui_btn": ("menu_empty", MEDIUM),
    "scui_btn_hover": ("ui_game_select_row", WHITE),
    "scui_btn_danger": ("menu_empty", DANGER_BTN),
    "scui_row": ("menu_empty", DARK),
    "scui_row_hover": ("menu_empty", MEDIUM),
}
BUTTON_RIM = {"scui_btn": ("menu_empty2px", BLACK), "scui_btn_danger": ("menu_empty2px", DANGER_RIM),
              "scui_row": ("menu_empty3px", BLACK)}

READY = re.compile(r"^\{# (\w+) \? '(\w+)' : '(\w+)' \}$")
ROW = re.compile(r"^\{# (\w+) \? 'scui_row_selected' : \((\w+) \? 'scui_row_hover' : 'scui_row'\) \}$")


def get(line, name):
    m = re.search(r'\s%s="([^"]*)"' % name, line)
    return m.group(1) if m else None


def put(line, name, value):
    """Sets an attribute, adding it before the element's end when it is not there."""
    if get(line, name) is not None:
        return re.sub(r'(\s%s=")[^"]*(")' % name, lambda m: m.group(1) + value + m.group(2), line, count=1)
    end = line.rfind("/>")
    if end < 0:
        end = line.rfind(">")
    return line[:end] + ' %s="%s"' % (name, value) + line[end:]


def rim(indent, depth, sprite, color, src, opacity=False):
    """A border over a plate or a button: the same box, no fill, its visible= with it."""
    parts = ['<sprite depth="%d" sprite="%s" type="sliced" fillcenter="false" color="%s"' % (depth, sprite, color)]
    for a in ("pos", "width", "height", "visible"):
        v = get(src, a)
        if v is not None:
            parts.append(' %s="%s"' % (a, v))
    if opacity:
        parts.append(' globalopacitymod="0"')
    return indent + "".join(parts) + "/>"


def recolor(line):
    for old, new in COLORS:
        line = line.replace(old, new)
    return line


def _plate(line, indent):
    sprite = get(line, "sprite")
    if sprite in BOUND_FILLS:
        return [recolor(put(put(line, "sprite", "menu_empty"), "color", BOUND_FILLS[sprite]))]
    m = ROW.match(sprite or "")
    if m:                                              # a selectable row: selected, hovered or at rest
        sel, hov = m.groups()
        out = put(line, "sprite", "{# %s ? 'ui_game_select_row' : 'menu_empty' }" % sel)
        out = put(out, "color", "{# %s ? '%s' : (%s ? '%s' : '%s') }" % (sel, WHITE, hov, MEDIUM, DARK))
        depth = int(get(line, "depth") or 0)
        return [out, rim(indent, depth + 1, "menu_empty3px", BLACK, line)]
    if sprite not in PLATES:
        return [recolor(line)]
    (fill, color), border = PLATES[sprite]
    out = put(put(line, "sprite", fill), "color", color)
    panel = sprite == "scui_window"
    if panel:
        out = put(out, "globalopacitymod", "0")
    lines = [recolor(out)]
    if border:
        depth = int(get(line, "depth") or 0)
        lines.append(rim(indent, depth + 1, border[0], border[1], line, opacity=panel))
    return lines


def _button(line, indent):
    s = get(line, "sprite")
    h = get(line, "hoversprite")
    if s not in BUTTON:
        return [recolor(line)]
    sprite, color = BUTTON[s]
    out = put(put(line, "sprite", sprite), "defaultcolor", color)
    m = READY.match(h or "")
    if m:                                              # hover lit only while it can act
        ready, a, b = m.groups()
        sa, ca = BUTTON[a]
        sb, cb = BUTTON[b]
        out = put(out, "hoversprite", "{# %s ? '%s' : '%s' }" % (ready, sa, sb))
        out = put(out, "hovercolor", "{# %s ? '%s' : '%s' }" % (ready, ca, cb))
    else:
        if h == s == "scui_btn_danger":
            hs, hc = "menu_empty", DANGER_HOVER
        else:
            hs, hc = BUTTON[h]
        out = put(put(out, "hoversprite", hs), "hovercolor", hc)
    lines = [recolor(out)]
    if s in BUTTON_RIM:
        depth = int(get(line, "depth") or 0)
        bs, bc = BUTTON_RIM[s]
        lines.append(rim(indent, depth + 1, bs, bc, line))
    return lines


def vanilla(lines, overrides=None, after=None):
    """The vanilla look of a generated window's lines. overrides: {exact line: [lines]} replaces a line outright;
    after: {exact line: [lines]} adds lines after a line's own (a header bar after the window's plate)."""
    overrides = overrides or {}
    after = after or {}
    out = []
    for line in lines:
        indent = line[:len(line) - len(line.lstrip("\t"))]
        body = line.lstrip("\t")
        if line in overrides:
            res = list(overrides[line])
        elif body.startswith("<button ") and get(line, "sprite"):
            res = _button(line, indent)
        elif body.startswith("<sprite ") and get(line, "sprite"):
            res = _plate(line, indent)
        else:
            res = [recolor(line)]
        out += res
        if line in after:
            out += list(after[line])
    return out


def window_plate(indent, w, h):
    """The window's own plate: vanilla's message box (a near-black fill, a gray rim). The fill sits at depth -1, under
    the panels drawn straight on the window at depth 0 (NGUI orders equal depths as it likes)."""
    return [indent + '<sprite depth="-1" sprite="menu_empty" type="sliced" color="%s" pos="0,0" width="%d" height="%d" '
                     'globalopacitymod="0"/>' % (WINDOW, w, h),
            indent + '<sprite depth="1" sprite="menu_empty2px" type="sliced" fillcenter="false" color="%s" pos="0,0" '
                     'width="%d" height="%d" globalopacitymod="0"/>' % (DARK, w, h)]


def header_bar(indent, w, h, depth=1):
    """vanilla's headerbg (ui_game_panel_header in [lightGrey]) across a window's top, inside its rim."""
    return [indent + '<sprite depth="%d" sprite="ui_game_panel_header" type="sliced" color="%s" pos="3,-3" '
                     'width="%d" height="%d" globalopacitymod="0"/>' % (depth, LIGHT, w - 6, h)]


def conditional(ashfall_lines, vanilla_lines, indent="\t"):
    """Both looks of one window: AshfallUI's when it is loaded, vanilla's otherwise."""
    return ([indent + "<conditional>",
             indent + "\t<if cond=\"mod_loaded('zzzzz_AshfallUI')\">"]
            + ["\t\t" + l if l else l for l in ashfall_lines]
            + [indent + "\t</if>", indent + "\t<else>"]
            + ["\t\t" + l if l else l for l in vanilla_lines]
            + [indent + "\t</else>", indent + "</conditional>"])


def sprites(lines):
    """Every literal sprite name the lines use (bound names inside {# ... } quotes too)."""
    names = set()
    for l in lines:
        for attr in ("sprite", "hoversprite"):
            v = get(l, attr)
            if v is None:
                continue
            if v.startswith("{#"):
                names.update(re.findall(r"'([A-Za-z_][\w ]*)'", v))
            elif not v.startswith("{"):
                names.add(v)
    return names
