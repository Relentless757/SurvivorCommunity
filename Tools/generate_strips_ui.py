#!/usr/bin/env python3
"""
Generates the two strips SC hangs under vanilla's loot grid (windowLooting) into Config/XUi_InGame/windows.xml, between
its GENERATED markers at the top of the file. Run from anywhere:  python Tools/generate_strips_ui.py

    scPackingStrip (XUiC_ScPackingStrip, shown while the open box is a packing station):
        Pack  [Food][Water][Meds][Ammo][Mats][Fuel][Lux]
        Take  [Food][Water][Meds][Ammo][Mats][Fuel][Lux]
        [Deposit bundles]  Stock: food 0, water 0, meds 0, ammo 0, mats 0, fuel 0, lux 0   (wraps to a second line)
        the last action's message                                                          (two lines)
    scLinkedStrip (XUiC_ScLinkedStrip, shown while the open box is the linked storage):
        12 boxes in the base - 340 stacks - 120 free slots (two lines)       [Refresh]

Round 32b (U32, the user: "the packing station window needs a touch up"). Before it, the strip was laid out for a loot
window 603 wide (vanilla's 8 columns of 75): its fourteen 72-wide buttons ran past the right edge of RE Inventory's
grid (8 columns of 55, 443 wide) onto INSPECT, and the stock line ran off. Now:

- Its width is the grid's, whatever that is: the strip is anchored to the grid ("queue", whose size changes re-anchor
  it), and its seven category buttons share the width by relative anchors (#parent, i/7 of the width plus a fixed
  offset; NGUI re-anchors every frame), so they fit 443 (RE Inventory: the narrowest grid, its minCols 8 x 55) as well
  as 603 (vanilla) or any other. At 443 a button is about 51 wide, so the labels are the short words of the stock line
  (Food, Water, Meds, Ammo, Mats, Fuel, Lux); each button's tooltip says what it does in full.
- The stock line has a row of its own beside Deposit and wraps to a second line; the message two lines under it.
- Two looks in one <conditional>:
    AshfallUI loaded: a plate of AshfallUI's own (scui_window, which it ships premium art under) as wide as the loot
        window's plate (AshfallUI's ashfallPlate stands 8 outside the window, the window 10 right of the grid's right
        edge and 6 left of its left edge: so -14 / +18) and below it (its plate ends 8 to 17 under the grid's last row:
        the strip starts 21 under it), scui_btn buttons, AshfallUI's text colors.
    otherwise: vanilla's content box (windowLooting's own: menu_empty [darkGrey] with a menu_empty3px [black] rim),
        3 under the grid's box and exactly its width, vanilla's button (simplebutton's clickable: menu_empty
        [mediumGrey], ui_game_select_row under the pointer, a menu_empty2px [black] rim), white outlined labels.
- Control names are the controllers' (XUiC_ScPackingStrip / XUiC_ScLinkedStrip hook each <button>'s OnPress by name).
"""

import io
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
MOD = os.path.dirname(HERE)
TARGET = os.path.join(MOD, "Config", "XUi_InGame", "windows.xml")
BEGIN = "\t<!-- GENERATED:BEGIN scstrips (Tools/generate_strips_ui.py) -->"
END = "\t<!-- GENERATED:END scstrips -->"
# the hand-written strips this replaced the first time (their comment banners, appends and closes)
LEGACY_PACKING = re.compile(r"\t<!-- The packing strip: .*?\t</append>\n", re.S)
LEGACY_LINKED = re.compile(r"\t<!-- The linked storage strip: .*?\t</append>\n\n?", re.S)

PACK_CONTROLLER = "SurvivorCommunity.ScPackingStrip, SurvivorCommunity"
LINK_CONTROLLER = "SurvivorCommunity.ScLinkedStrip, SurvivorCommunity"

# (button suffix, short label key) - the order of the stock line; the tooltips are scPack<suffix> / scTake<suffix>
CATEGORIES = (("Food", "scStripFood"), ("Water", "scStripWater"), ("Meds", "scStripMeds"), ("Ammo", "scStripAmmo"),
              ("Materials", "scStripMats"), ("Fuel", "scStripFuel"), ("Luxury", "scStripLux"))
NARROWEST = 440                  # RE Inventory's grid ("queue"): minCols 8 x 55

LBL_W = 46                       # the Pack / Take column
GAP = 4                          # between buttons
ROW_H = 30                       # a row of category buttons
DEP_W, DEP_H = 132, 32           # Deposit, and the stock line's row
MSG_H = 32                       # the message: two 14-point lines
REF_W, REF_H = 100, 28           # the linked strip's Refresh
HEAD_H = 36                      # its header: two 14-point lines

LOOKS = {
    "ash": dict(left=-14, right=18, top=21, pad_x=16, pad_y=14,
                text="226,220,208,255", dim="190,182,166,255", gold="206,178,120,255",
                btn=("scui_btn", "scui_btn_hover", "[white]", "[white]"), rim=None,
                font=14, upper=True, effect=False, row_font=16),
    "van": dict(left=-3, right=0, top=6, pad_x=8, pad_y=8,
                text="255,255,255,255", dim="180,180,180,255", gold="222,206,163,255",
                btn=("menu_empty", "ui_game_select_row", "96,96,96,255", "255,255,255,255"), rim=("menu_empty2px", "0,0,0,255"),
                font=16, upper=False, effect=True, row_font=18),
}


def frac(n, d):
    if n == 0:
        return "0"
    if n == d:
        return "1"
    return ("%.6f" % (n / float(d))).rstrip("0")


def anchors(l_rel, l_abs, r_rel, r_abs, top, bottom):
    """NGUI anchors on the strip (#parent): x from its left edge at l_rel of its width, y down from its top."""
    return ('anchor_left="#parent,%s,%d" anchor_right="#parent,%s,%d" anchor_top="#parent,1,%d" anchor_bottom="#parent,1,%d"'
            % (l_rel, l_abs, r_rel, r_abs, -top, -bottom))


def plate(look):
    fill = 'anchor_left="#parent,0,0" anchor_right="#parent,1,0" anchor_top="#parent,1,0" anchor_bottom="#parent,0,0"'
    if look is LOOKS["ash"]:
        return ['<sprite depth="0" name="bg" sprite="scui_window" type="sliced" color="[white]" %s/>' % fill]
    return ['<sprite depth="0" name="bg" sprite="menu_empty" type="sliced" color="64,64,64,255" globalopacitymod="0" %s/>' % fill,
            '<sprite depth="1" name="rim" sprite="menu_empty3px" type="sliced" fillcenter="false" color="0,0,0,255" '
            'globalopacitymod="0" %s/>' % fill]


def button(look, name, where, key, tip=None, font=None):
    """A <button> on anchors `where`, its rim (vanilla) and its label centered on it."""
    sprite, hover, color, hcolor = look["btn"]
    tip = (' tooltip_key="%s"' % tip) if tip else ""
    o = ['<button depth="2" name="%s" sprite="%s" hoversprite="%s" defaultcolor="%s" hovercolor="%s" type="sliced" '
         'hoverscale="1.0" sound="[default_click]"%s %s/>' % (name, sprite, hover, color, hcolor, tip, where)]
    if look["rim"]:
        o.append('<sprite depth="3" sprite="%s" type="sliced" fillcenter="false" color="%s" %s/>' % (look["rim"] + (where,)))
    extra = (' upper_case="true"' if look["upper"] else "") + (' effect="outline"' if look["effect"] else "")
    inset = re.sub(r'(anchor_left="#parent,[^,]+,)(-?\d+)', lambda m: m.group(1) + str(int(m.group(2)) + 2), where)
    inset = re.sub(r'(anchor_right="#parent,[^,]+,)(-?\d+)', lambda m: m.group(1) + str(int(m.group(2)) - 2), inset)
    o.append('<label depth="4" font_size="%d" justify="center" pivot="center" text_key="%s" color="%s"%s %s/>'
             % (font or look["font"], key, look["text"], extra, inset))
    return o


def category_row(look, verb, y):
    """The row label and seven buttons sharing the strip's width: button i spans [x0 + i*u, x0 + (i+1)*u - GAP] with
    u = (W - pad - x0 + GAP) / 7, i.e. i/7 of W plus round(x0 + i*(GAP - pad - x0)/7)."""
    pad = look["pad_x"]
    x0 = pad + LBL_W + GAP
    n = len(CATEGORIES)
    k = (GAP - pad - x0) / float(n)
    o = ['<label depth="4" pos="%d,%d" width="%d" height="%d" font_size="%d" pivot="left" justify="left" text_key="scStrip%s" '
         'color="%s"%s/>' % (pad, -(y + ROW_H // 2), LBL_W, ROW_H, look["row_font"], verb, look["dim"],
                             ' upper_case="true"' if look["upper"] else "")]
    for i, (suffix, key) in enumerate(CATEGORIES):
        where = anchors(frac(i, n), int(round(x0 + i * k)), frac(i + 1, n), int(round(x0 + (i + 1) * k - GAP)), y, y + ROW_H)
        o += button(look, "btn%s%s" % (verb, suffix), where, key, tip="sc%s%s" % (verb, suffix))
    return o


def packing(look):
    pad, pt = look["pad_x"], look["pad_y"]
    y1 = pt
    y2 = y1 + ROW_H + GAP
    y3 = y2 + ROW_H + 6
    y4 = y3 + DEP_H + GAP
    h = y4 + MSG_H + pt
    top = look["top"]
    o = ['<rect name="scPackingStrip" depth="14" controller="%s" visible="{scvisible}" anchor_left="queue,0,%d" '
         'anchor_right="queue,1,%d" anchor_top="queue,0,%d" anchor_bottom="queue,0,%d">'
         % (PACK_CONTROLLER, look["left"], look["right"], -top, -(top + h))]
    body = plate(look) + category_row(look, "Pack", y1) + category_row(look, "Take", y2)
    body += button(look, "btnDeposit", anchors("0", pad, "0", pad + DEP_W, y3, y3 + DEP_H), "scDeposit", tip="scDepositTip")
    body += ['<label depth="4" font_size="14" pivot="left" justify="left" text="{scstock}" color="%s" %s/>'
             % (look["text"], anchors("0", pad + DEP_W + 8, "1", -pad, y3, y3 + DEP_H)),
             '<label depth="4" font_size="14" pivot="left" justify="left" text="{scmessage}" color="%s" %s/>'
             % (look["gold"], anchors("0", pad, "1", -pad, y4, y4 + MSG_H))]
    return o + ["\t" + b for b in body] + ["</rect>"], h


def linked(look):
    pad, pt = look["pad_x"], look["pad_y"]
    h = pt + HEAD_H + pt
    top = look["top"]
    o = ['<rect name="scLinkedStrip" depth="14" controller="%s" visible="{sclvisible}" anchor_left="queue,0,%d" '
         'anchor_right="queue,1,%d" anchor_top="queue,0,%d" anchor_bottom="queue,0,%d">'
         % (LINK_CONTROLLER, look["left"], look["right"], -top, -(top + h))]
    body = plate(look)
    body += ['<label depth="4" font_size="14" pivot="left" justify="left" text="{sclheader}" color="%s" %s/>'
             % (look["text"], anchors("0", pad, "1", -(pad + REF_W + 8), pt, pt + HEAD_H))]
    ry = pt + (HEAD_H - REF_H) // 2
    body += button(look, "btnLinkedRefresh", anchors("1", -(pad + REF_W), "1", -pad, ry, ry + REF_H), "scStorageRefresh",
                   font=look["font"])
    return o + ["\t" + b for b in body] + ["</rect>"], h


def appends(look):
    p, ph = packing(look)
    l, lh = linked(look)
    o = ['<append xpath="/windows/window[@name=\'windowLooting\']">'] + ["\t" + x for x in p] + ["</append>",
         '<append xpath="/windows/window[@name=\'windowLooting\']">'] + ["\t" + x for x in l] + ["</append>"]
    return o, ph, lh


def build():
    ash, ph_a, lh_a = appends(LOOKS["ash"])
    van, ph_v, lh_v = appends(LOOKS["van"])
    o = ["\t<!-- The packing strip and the linked storage strip under vanilla's loot grid, GENERATED by",
         "\t     Tools/generate_strips_ui.py - edit that, not this. Each controller shows its strip only while the open box",
         "\t     is its own (a packing station / the linked storage), so every other crate is untouched. Both fit any grid",
         "\t     width (anchored to the grid, the category buttons by fractions of it), in AshfallUI's look when it is",
         "\t     loaded and vanilla's otherwise (round 32b). -->",
         "\t<conditional>",
         "\t\t<if cond=\"mod_loaded('zzzzz_AshfallUI')\">"]
    o += ["\t\t\t" + x for x in ash]
    o += ["\t\t</if>", "\t\t<else>"]
    o += ["\t\t\t" + x for x in van]
    o += ["\t\t</else>", "\t</conditional>"]
    return "\n".join(o) + "\n", (ph_a, ph_v, lh_a, lh_v)


def main():
    with io.open(TARGET, "r", encoding="utf-8", newline="") as f:
        text = f.read()
    if "\r\n" in text:
        raise SystemExit("REFUSING: %s has CRLF lines; this generator writes LF like the rest of the file." % TARGET)
    out, heights = build()
    block = BEGIN + "\n" + out + END + "\n"
    if BEGIN in text and END in text:
        text = text.split(BEGIN)[0] + block + text.split(END)[1].lstrip("\n")
    else:
        if len(LEGACY_PACKING.findall(text)) != 1 or len(LEGACY_LINKED.findall(text)) != 1:
            raise SystemExit("REFUSING: no GENERATED markers and not exactly one hand-written packing and linked strip in %s."
                             % TARGET)
        text = LEGACY_LINKED.sub("", text)
        text = LEGACY_PACKING.sub(lambda _m: block, text)
    with io.open(TARGET, "w", encoding="utf-8", newline="") as f:
        f.write(text)
    # the narrowest grid still gives every category button room for its short label (about 40 units at 16 points)
    for name, look in LOOKS.items():
        width = NARROWEST + look["right"] - look["left"]
        x0 = look["pad_x"] + LBL_W + GAP
        each = (width - look["pad_x"] - x0 + GAP) / float(len(CATEGORIES)) - GAP
        assert each >= 46, "%s: a category button is %.1f wide at the narrowest grid" % (name, each)
        assert width - 2 * look["pad_x"] - DEP_W - 8 >= 250, "%s: no room for the stock line beside Deposit" % name
    print("generate_strips_ui: wrote scPackingStrip (%d tall with AshfallUI, %d without) and scLinkedStrip (%d / %d)"
          % heights)


if __name__ == "__main__":
    main()
