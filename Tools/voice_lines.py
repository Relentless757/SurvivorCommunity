"""SurvivorCommunity's voiced dialog lines, and a check of the voice folders (Resources/Voices).

A voiced line is a dialog statement: SC's own (Config/dialogs.xml, its words from Config/Localization.csv) plus the
0-XNPCCore lines SC voices (EXTRA). Its clip is Resources/Voices/<male|female>_<Name>/<key>.ogg, mono Ogg Vorbis, where
<key> is the statement id, or "<dialog>.<statement>" for another mod's line (ScVoice tries that form first). A <key>.txt
beside the clip holds its words, for whoever records it.

    python Tools/voice_lines.py list        every line's key and words
    python Tools/voice_lines.py check       per voice folder: lines with no clip yet, and clips no line uses
    python Tools/voice_lines.py sidecars    write <key>.txt (the words) for every line into every voice folder
"""
import csv
import io
import os
import re
import sys

MOD = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VOICES = os.path.join(MOD, "Resources", "Voices")

# Lines of other mods that SC voices, keyed "<dialog>.<statement>": 0-XNPCCore's greeting, at the root of every talk
# (SdcsNPC_start in its Localization.csv).
EXTRA = {
    "SdcsNPC.start": "Hello friend, how can I help you?",
}


def english():
    """SC's Localization.csv: key -> English text."""
    out = {}
    with io.open(os.path.join(MOD, "Config", "Localization.csv"), "r", encoding="utf-8", newline="") as f:
        rows = csv.reader(f)
        header = next(rows)
        col = header.index("english")
        for r in rows:
            if r and len(r) > col:
                out[r[0]] = r[col]
    return out


def lines():
    """key -> words for every voiced line, in the order the dialog file has them."""
    words = english()
    with io.open(os.path.join(MOD, "Config", "dialogs.xml"), "r", encoding="utf-8") as f:
        xml = f.read()
    out = dict(EXTRA)
    for m in re.finditer(r"<statement\b([^>]*)>", xml):
        attrs = dict(re.findall(r'(\w+)\s*=\s*"([^"]*)"', m.group(1)))
        sid, key = attrs.get("id"), attrs.get("text")
        if sid and key:
            out[sid] = words.get(key, key)
    return out


def voice_dirs():
    if not os.path.isdir(VOICES):
        return []
    return sorted(d for d in os.listdir(VOICES) if os.path.isdir(os.path.join(VOICES, d)))


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else "list"
    all_lines = lines()
    if cmd == "list":
        for k, v in all_lines.items():
            print("%-20s %s" % (k, v))
        print("%d lines" % len(all_lines))
    elif cmd == "check":
        bad = 0
        for d in voice_dirs():
            if not re.match(r"(?i)^(male|female)_.+", d):
                print("%s: not named male_<Name> or female_<Name> - the game skips it" % d)
                bad += 1
                continue
            have = {os.path.splitext(f)[0] for f in os.listdir(os.path.join(VOICES, d)) if f.lower().endswith(".ogg")}
            missing = [k for k in all_lines if k not in have]
            unknown = sorted(k for k in have if k not in all_lines)
            print("%s: %d of %d lines%s%s" % (d, len(all_lines) - len(missing), len(all_lines),
                                              ("; no clip yet: " + ", ".join(missing)) if missing else "",
                                              ("; no such line: " + ", ".join(unknown)) if unknown else ""))
            bad += len(unknown)
        sys.exit(1 if bad else 0)
    elif cmd == "sidecars":
        for d in voice_dirs():
            for k, v in all_lines.items():
                with io.open(os.path.join(VOICES, d, k + ".txt"), "w", encoding="utf-8", newline="\n") as f:
                    f.write(v + "\n")
            print("%s: %d sidecars" % (d, len(all_lines)))
    else:
        print(__doc__)
        sys.exit(2)


if __name__ == "__main__":
    main()
