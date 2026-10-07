"""Copies the user's Guard Tower prefab from LocalPrefabs into the mod as Prefabs/scGuardTower.

The prefab is built by hand in the prefab editor (LocalPrefabs/Guard_Tower). The mod ships a copy under its own name,
so a LocalPrefabs file can never shadow it, with one change: the Watch post is turned to look away from the ladder
(the user's choice, 2026-10-06), so the ladder goes on the base side and the guard looks out. Run it again after
editing the tower in the editor.

    python import_guard_tower.py [source name] [mod folder]

Defaults: Guard_Tower from %APPDATA%/7DaysToDie/LocalPrefabs, and the mod folder this script sits in (Tools/..).
"""
import os
import pathlib
import struct
import sys

POST = "scWatchPost"
TARGET = "scGuardTower"
# ladder rotation -> the way it faces (away from its wall), and the post rotation that looks the other way:
# rotations 0-3 face +Z, +X, -Z, -X, so the opposite of r is (r + 2) & 3
YAW_ONLY = range(4)


def read_nim(path):
    data = path.read_bytes()
    _ver, count = struct.unpack_from("<ii", data, 0)
    off = 8
    names = {}
    for _ in range(count):
        bid = struct.unpack_from("<i", data, off)[0]
        off += 4
        n = 0
        shift = 0
        while True:
            b = data[off]
            off += 1
            n |= (b & 0x7F) << shift
            if not b & 0x80:
                break
            shift += 7
        names[bid] = data[off:off + n].decode("utf-8")
        off += n
    return names


def main():
    src_name = sys.argv[1] if len(sys.argv) > 1 else "Guard_Tower"
    mod = pathlib.Path(sys.argv[2]) if len(sys.argv) > 2 else pathlib.Path(__file__).resolve().parent.parent
    src = pathlib.Path(os.environ["APPDATA"]) / "7DaysToDie" / "LocalPrefabs" / src_name
    dst_dir = mod / "Prefabs"
    for ext in (".tts", ".blocks.nim", ".xml"):
        if not src.with_name(src.name + ext).exists():
            sys.exit("missing " + str(src) + ext)

    names = read_nim(src.with_name(src.name + ".blocks.nim"))
    post_ids = [i for i, n in names.items() if n == POST]
    ladder_ids = [i for i, n in names.items() if "ladder" in n.lower()]
    if not post_ids:
        sys.exit("no " + POST + " in the prefab")

    tts = bytearray(src.with_name(src.name + ".tts").read_bytes())
    if tts[:4] != b"tts\x00":
        sys.exit("not a .tts file")
    version = struct.unpack_from("<I", tts, 4)[0]
    if version < 18:
        sys.exit("prefab version %d: only 18+ (3.x) is supported" % version)
    sx, sy, sz = struct.unpack_from("<hhh", tts, 8)
    base = 14   # magic, version, size; v7+ has no header flags
    n = sx * sy * sz

    def cell(off):
        return struct.unpack_from("<I", tts, base + off * 4)[0]

    posts, ladders = [], []
    for off in range(n):
        raw = cell(off)
        t = raw & 0xFFFF
        if t in post_ids:
            posts.append(off)
        elif t in ladder_ids:
            ladders.append(off)
    if len(posts) != 1:
        sys.exit("expected one Watch post, found %d" % len(posts))
    rots = sorted({(cell(o) >> 16) & 0x1F for o in ladders})
    if len(rots) != 1 or rots[0] not in YAW_ONLY:
        sys.exit("the ladders must all face one way (rotations 0-3); found " + str(rots))
    want = (rots[0] + 2) & 3
    off = posts[0]
    raw = cell(off)
    old = (raw >> 16) & 0x1F
    raw = (raw & ~0x1F0000) | (want << 16)
    struct.pack_into("<I", tts, base + off * 4, raw)
    x, rem = off % sx, off // sx
    y, z = rem % sy, rem // sy

    xml = src.with_name(src.name + ".xml").read_bytes()
    if b'name="Tags"' in xml:
        sys.exit("the prefab has Tags - remove them in the editor (a tagged prefab can end up in random worlds)")

    dst_dir.mkdir(exist_ok=True)
    (dst_dir / (TARGET + ".tts")).write_bytes(bytes(tts))
    (dst_dir / (TARGET + ".blocks.nim")).write_bytes(src.with_name(src.name + ".blocks.nim").read_bytes())
    (dst_dir / (TARGET + ".xml")).write_bytes(xml)
    print("%s -> %s (size %dx%dx%d, version %d)" % (src, dst_dir / TARGET, sx, sy, sz, version))
    print("Watch post at (%d,%d,%d): rotation %d -> %d (the %d ladder cells face rotation %d)" % (x, y, z, old, want, len(ladders), rots[0]))


if __name__ == "__main__":
    main()
