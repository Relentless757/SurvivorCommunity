"""Copies the user's Guard Tower prefab from LocalPrefabs into the mod as Prefabs/scGuardTower.

The prefab is built by hand in the prefab editor (LocalPrefabs/Guard_Tower). The mod ships a copy under its own name,
so a LocalPrefabs file can never shadow it, with the Watch post adjusted (the user's choices, 2026-10-06/07):
- it looks away from the ladder, so the ladder goes on the base side and the guard looks out;
- it matches the deck around it: the post becomes the Watch Post shape family's version of the deck's shape
  ("scWatchPostShapes:catwalkPlate" in a catwalk deck), in the deck's paint, turned the deck's way up (a deck of
  upside-down plates gets an upside-down post: its top is the walking surface the guard stands on).
Run it again after editing the tower in the editor (the post may be the old cube Watch Post or any of the shapes).

Paint saved with OcbCustomTextures installed (the user's 3.2 game) is turned back into vanilla's layout: that mod keeps a
face's paint in 10 bits instead of vanilla's 8, so vanilla reads it as other paints and logs "Missing paint ID XML entry"
(round tw1). A cell is converted when its top byte is set: vanilla keeps seven 8-bit faces (Middle is the 7th) and never
writes byte 7. The editor's brush changes one face's byte at a time and keeps the rest, so a marked cell can also be the
old paint with some faces painted again in 3.4. Each marked cell is read both ways - as OcbCustomTextures paint, and as
the prefab's most common marked value with the differing faces painted in 3.4 - and the reading whose faces are all
paints painting.xml defines wins (the game's painting.xml, through poi_gen's palette; without it, ids up to 255 count).
No reading, or two different ones, stops the script with the cell named.
--ocb converts every cell (an OcbCustomTextures cell whose last face has a low paint id leaves byte 7 empty), --no-ocb none.

    python import_guard_tower.py [--ocb | --no-ocb] [source name] [mod folder]

Defaults: Guard_Tower from %APPDATA%/7DaysToDie/LocalPrefabs, and the mod folder this script sits in (Tools/..).
Needs poi_gen's .tts reader/writer (it round-trips every vanilla prefab byte for byte).
"""
import collections
import os
import pathlib
import struct
import sys

POI_GEN = r"F:\Game Related Programs\Creation Stuff\poi_gen"
sys.path.insert(0, POI_GEN)
try:
    from tts.prefab import TTSPrefab  # noqa: E402
    from tts.blockvalue import unpack  # noqa: E402
except ImportError:
    sys.exit("poi_gen's tts package not found at " + POI_GEN)

POST_PREFIX = "scWatchPost"
FAMILY = "scWatchPostShapes"
TARGET = "scGuardTower"
# rotation -> facing (x, z): 0-3 upright, 4-7 upside down (BlockShapeNew.rotationsToQuats: Rx180 * Ry{180,270,360,450})
FACING = {0: (0, 1), 1: (1, 0), 2: (0, -1), 3: (-1, 0), 4: (0, 1), 5: (-1, 0), 6: (0, -1), 7: (1, 0)}


def read_nim(path):
    data = path.read_bytes()
    version, count = struct.unpack_from("<ii", data, 0)
    off = 8
    entries = []
    for _ in range(count):
        bid = struct.unpack_from("<i", data, off)[0]
        off += 4
        n = shift = 0
        while True:
            b = data[off]
            off += 1
            n |= (b & 0x7F) << shift
            if not b & 0x80:
                break
            shift += 7
        entries.append([bid, data[off:off + n].decode("utf-8")])
        off += n
    return version, entries


def write_nim(path, version, entries):
    out = bytearray(struct.pack("<ii", version, len(entries)))
    for bid, name in entries:
        raw = name.encode("utf-8")
        out += struct.pack("<i", bid)
        n = len(raw)
        while True:
            b = n & 0x7F
            n >>= 7
            out.append(b | (0x80 if n else 0))
            if not n:
                break
        out += raw
    path.write_bytes(bytes(out))


def defined_paints():
    """painting.xml's ids (the same in 3.2 and 3.4), or None when poi_gen's palette can't read the game's copy."""
    try:
        from palette.paints import load_paints  # noqa: E402
        return set(load_paints().by_id)
    except Exception as e:  # noqa: BLE001 - any failure just widens the test to "up to 255"
        print("note: painting.xml not read (%s) - any paint id up to 255 counts as defined" % e)
        return None


def ocb_to_vanilla(v):
    """OcbCustomTextures' six 10-bit faces -> vanilla's six 8-bit faces; None when a face names a paint above 255."""
    faces = [(v >> (f * 10)) & 0x3FF for f in range(6)]
    if any(t > 255 for t in faces):
        return None
    return sum(t << (f * 8) for f, t in enumerate(faces))


def readings(v, ref, defined):
    """The vanilla values a marked cell can mean: its own OcbCustomTextures paint, and ref (the prefab's most common
    marked value) with the faces that differ from it painted again in 3.4 (the editor's brush changes one face's byte and
    keeps the top byte). Only readings whose seven faces are all unpainted or defined paints count."""
    def good(n):
        faces = [(n >> (f * 8)) & 0xFF for f in range(7)]
        return all(t == 0 or defined is None or t in defined for t in faces)
    out = set()
    own = ocb_to_vanilla(v)
    if own is not None and good(own):
        out.add(own)
    if ref is not None and v != ref and v >> 56 == ref >> 56:
        base = ocb_to_vanilla(ref)
        if base is not None:
            n = 0
            for f in range(7):
                b = (v >> (f * 8)) & 0xFF
                n |= (b if b != (ref >> (f * 8)) & 0xFF else (base >> (f * 8)) & 0xFF) << (f * 8)
            if good(n):
                out.add(n)
    return out


def main():
    args = [a for a in sys.argv[1:] if a not in ("--ocb", "--no-ocb")]
    if "--ocb" in sys.argv and "--no-ocb" in sys.argv:
        sys.exit("--ocb and --no-ocb: pick one")
    ocb_flag = "--ocb" if "--ocb" in sys.argv else "--no-ocb" if "--no-ocb" in sys.argv else None
    src_name = args[0] if len(args) > 0 else "Guard_Tower"
    mod = pathlib.Path(args[1]) if len(args) > 1 else pathlib.Path(__file__).resolve().parent.parent
    src = pathlib.Path(os.environ["APPDATA"]) / "7DaysToDie" / "LocalPrefabs" / src_name
    for ext in (".tts", ".blocks.nim", ".xml"):
        if not pathlib.Path(str(src) + ext).exists():
            sys.exit("missing " + str(src) + ext)

    version, entries = read_nim(pathlib.Path(str(src) + ".blocks.nim"))
    names = {bid: name for bid, name in entries}
    p = TTSPrefab.load(str(src) + ".tts")
    if p.version < 18:
        sys.exit("prefab version %d: only 18+ (3.x) is supported" % p.version)
    marked = [off for off, v in p.textures.items() if v >> 56]
    convert = list(p.textures) if ocb_flag == "--ocb" else [] if ocb_flag == "--no-ocb" else marked
    defined = defined_paints() if convert else None
    ref = collections.Counter(p.textures[o] for o in marked).most_common(1)[0][0] if marked else None
    repainted = 0
    for off in convert:
        v = p.textures[off]
        found = readings(v, ref, defined)
        if len(found) != 1:
            sys.exit("cell %s: paint %s reads as %s - paint the whole block again in the editor%s"
                     % (p.offset_to_coord(off), hex(v),
                        "no paint the game defines" if not found else "two different paints (%s)" % ", ".join(hex(n) for n in sorted(found)),
                        "; painted in 3.4? leave out --ocb" if ocb_flag == "--ocb" else ""))
        n = found.pop()
        if n != ocb_to_vanilla(v):
            repainted += 1
        p.textures[off] = n
    if repainted:
        print("paint: %d cell(s) had faces painted again in 3.4 over the old paint - those faces kept their new paint"
              % repainted)
    if convert:
        print("paint: %d of %d painted cell(s) turned from OcbCustomTextures' 10-bit faces into vanilla's 8-bit ones (%s)"
              % (len(convert), len(p.textures), "--ocb" if ocb_flag else "their top byte was set"))
        if len(convert) < len(p.textures):
            print("note: the other %d kept their paint as it is; if the tower was painted only with OcbCustomTextures, "
                  "run with --ocb" % (len(p.textures) - len(convert)))
        print("note: %s itself still has the OcbCustomTextures paint (the 3.4 prefab editor shows it in other paints); this "
              "copy is converted" % src.name)
    elif marked:
        print("warning: %d cell(s) carry paint bits vanilla never sets (--no-ocb kept them)" % len(marked))
    sx, sy, sz = p.size

    cells = {}
    for off, raw in enumerate(p.blocks):
        d = unpack(raw, p.version)
        if d.type:
            cells[off] = d
    posts = [off for off, d in cells.items() if names.get(d.type, "").startswith(POST_PREFIX)]
    ladders = [off for off, d in cells.items() if "ladder" in names.get(d.type, "").lower()]
    if len(posts) != 1:
        sys.exit("expected one Watch post, found %d" % len(posts))
    lrots = sorted({cells[o].rotation for o in ladders})
    if len(lrots) != 1 or lrots[0] not in range(4):
        sys.exit("the ladders must all face one way (rotations 0-3); found " + str(lrots))
    lf = FACING[lrots[0]]
    want = (-lf[0], -lf[1])   # away from the ladder

    post = posts[0]
    px, py, pz = p.offset_to_coord(post)
    pd = cells[post]

    # the deck: the shape blocks on the post's layer ("<material>Shapes:<shape>"), their shape, rotation and paint
    deck = [off for off, d in cells.items() if p.offset_to_coord(off)[1] == py and off != post
            and ":" in names.get(d.type, "") and not names[d.type].startswith(POST_PREFIX)]
    by_name = collections.Counter(names[cells[o].type] for o in deck)
    if by_name:
        deck_name = by_name.most_common(1)[0][0]
        same = [o for o in deck if names[cells[o].type] == deck_name]
        deck_rot = collections.Counter(cells[o].rotation for o in same).most_common(1)[0][0]
        deck_tex = collections.Counter(p.textures.get(o, 0) for o in same).most_common(1)[0][0]
        new_name = FAMILY + ":" + deck_name.split(":", 1)[1]
        flipped = 4 <= deck_rot <= 7
    else:
        deck_name, deck_rot, deck_tex = None, None, 0
        new_name = names[pd.type]
        flipped = False
    rot = next(r for r in (range(4, 8) if flipped else range(4)) if FACING[r] == want)

    # the post's block: an existing entry for the new name, or its own entry renamed (it is the post's alone)
    new_id = next((bid for bid, name in entries if name == new_name), None)
    if new_id is None:
        if sum(1 for d in cells.values() if d.type == pd.type) == 1:
            new_id = pd.type
            for e in entries:
                if e[0] == pd.type:
                    e[1] = new_name
        else:
            new_id = max(bid for bid, _ in entries) + 1
            entries.append([new_id, new_name])
    raw = p.blocks[post]
    raw = (raw & ~0xFFFF & ~0x1F0000) | new_id | (rot << 16)
    p.blocks[post] = raw
    if deck_tex:
        p.textures[post] = deck_tex
    else:
        p.textures.pop(post, None)

    xml = pathlib.Path(str(src) + ".xml").read_bytes()
    if b'name="Tags"' in xml:
        sys.exit("the prefab has Tags - remove them in the editor (a tagged prefab can end up in random worlds)")

    dst = mod / "Prefabs"
    dst.mkdir(exist_ok=True)
    (dst / (TARGET + ".tts")).write_bytes(p.to_bytes())
    write_nim(dst / (TARGET + ".blocks.nim"), version, entries)
    (dst / (TARGET + ".xml")).write_bytes(xml)
    print("%s -> %s (size %dx%dx%d, version %d)" % (src, dst / TARGET, sx, sy, sz, p.version))
    print("Watch post at (%d,%d,%d): %s rotation %d -> %s rotation %d, paint %s (ladder rotation %d; deck %s rotation %s)"
          % (px, py, pz, names[pd.type], pd.rotation, new_name, rot, hex(deck_tex) if deck_tex else "none", lrots[0],
             deck_name, deck_rot))
    if deck_name and not flipped and not new_name.endswith(":cube"):
        print("warning: the deck's %s is the right way up, so the post is too - a guard can't stand on an upright %s "
              "(SC wants a flat top: turn the deck's plates upside down, or make the post a cube)" % (deck_name, new_name))
    if deck_rot is not None and deck_rot != rot:
        print("note: the post faces away from the ladder, so it is turned differently from the deck (rotation %d, not %d)"
              % (rot, deck_rot))


if __name__ == "__main__":
    main()
