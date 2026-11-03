"""SurvivorCommunity's voiced dialog lines, and a check of the voice folders (Resources/Voices).

A voiced line is something a survivor says in a talk:
- SC's own statements (Config/dialogs.xml, their words from Config/Localization.csv), keyed by the statement id;
- the 0-XNPCCore lines SC voices (EXTRA), keyed "<dialog>.<statement>" (ScVoice tries that form before the bare id);
- the enclave requests' lines, which ScEncRequests picks at runtime by kind and standing (RUNTIME_FAMILIES: LineKey's
  stem_kind_band, stem_kind, stem_band and stem; and the replies' RUNTIME_FIXED lines), keyed by their Localization
  key, which ScVoice tries first of all. The statements that only hold them ({reqask} and the like: RUNTIME_STATEMENTS)
  have no clip of their own.
A line with a name or a number in it ({tokens}) says only its token-free part (SPOKEN); SC's line panel shows the whole
line. Its clip is Resources/Voices/<male|female>_<Name>/<key>.ogg, mono Ogg Vorbis; a <key>.txt beside it holds the
words it says, for whoever records it.

    python Tools/voice_lines.py list        every line's key and the words it says
    python Tools/voice_lines.py check       the lines (a token with no spoken part), then per voice folder: lines with
                                            no clip yet, and clips no line uses
    python Tools/voice_lines.py sidecars    write <key>.txt (the words it says) for every line into every voice folder
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

# What a voice says of a statement whose words carry tokens (generated names and numbers can't be voiced): the
# token-free part. The panel shows the whole line.
SPOKEN = {
    "scEncAboutReply": "It's not much, but it keeps the dead out and the rain off. These people are mine to look after.",
    "scEncWhereReply": "You'll want to talk to the one in charge, not me.",
    "scEncEnlisted": "Right behind you. Say the word when you want me to head back.",
    "scEncEnlistConfirm": "I could come along and watch your back for a while.",
    "scEncRecruitConfirm": "Leave my people? I've thought about it more than once. If you'll have me, I'm in.",
}

# Statements that hold only a line picked at runtime ({reqask}, {reqwait}, {reqreply}, {reqnone}; E7's {giftreply}): no
# clip of their own.
RUNTIME_STATEMENTS = {"scEncReqNothing", "scEncReqOffer", "scEncReqWaiting", "scEncReqReply", "scEncGiftReply"}
# ScEncRequests' runtime lines: every Localization key of these stems (the stem itself, or stem_...) whose row is a dialog
# statement (a UI key named alike is no line), and the fixed ones.
RUNTIME_FAMILIES = ["scEncAsk", "scEncWait", "scEncAccepted", "scEncFound", "scEncThanks", "scEncDeclined", "scEncThink",
                    "scEncCantDo"]
RUNTIME_FIXED = ["scEncReqNothingLine", "scEncReqGone", "scEncReqNotCleared", "scEncReqNotCarried",
                 "scEncGiftRefuseCap", "scEncGiftRefuseSoon"]     # E7: a gift refused (the thanks are scEncThanks_gift_*)


def localization():
    """SC's Localization.csv: key -> (file, type, English text), in file order."""
    out = {}
    with io.open(os.path.join(MOD, "Config", "Localization.csv"), "r", encoding="utf-8", newline="") as f:
        rows = csv.reader(f)
        header = next(rows)
        col, fcol, tcol = header.index("english"), header.index("File"), header.index("Type")
        for r in rows:
            if r and len(r) > col:
                out[r[0]] = (r[fcol], r[tcol], r[col])
    return out


def english():
    """SC's Localization.csv: key -> English text, in file order."""
    return {k: v[2] for k, v in localization().items()}


def lines(problems):
    """key -> the words said, for every voiced line: XNPCCore's, the dialog file's statements in order, then the runtime
    lines. What can't be voiced goes to problems."""
    loc = localization()
    words = {k: v[2] for k, v in loc.items()}
    with io.open(os.path.join(MOD, "Config", "dialogs.xml"), "r", encoding="utf-8") as f:
        xml = re.sub(r"<!--.*?-->", "", f.read(), flags=re.S)
    out = dict(EXTRA)

    def add(key, said):
        if "{" in said:
            problems.append("%s has a token and no spoken part (SPOKEN): %s" % (key, said))
        elif key in out:
            problems.append("%s is two lines" % key)
        else:
            out[key] = said

    for m in re.finditer(r"<statement\b([^>]*)>", xml):
        attrs = dict(re.findall(r'(\w+)\s*=\s*"([^"]*)"', m.group(1)))
        sid, key = attrs.get("id"), attrs.get("text")
        if sid and key and sid not in RUNTIME_STATEMENTS:
            add(sid, SPOKEN.get(sid, words.get(key, key)))
    for stem in RUNTIME_FAMILIES:
        for k in sorted(words):
            if (k == stem or k.startswith(stem + "_")) and loc[k][:2] == ("Dialog", "Statement"):
                add(k, words[k])
    for k in RUNTIME_FIXED:
        if k in words:
            add(k, words[k])
        else:
            problems.append("no Localization row for " + k)
    return out


def voice_dirs():
    if not os.path.isdir(VOICES):
        return []
    return sorted(d for d in os.listdir(VOICES) if os.path.isdir(os.path.join(VOICES, d)))


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else "list"
    problems = []
    all_lines = lines(problems)
    if cmd == "list":
        for k, v in all_lines.items():
            print("%-34s %s" % (k, v))
        print("%d lines" % len(all_lines))
        for p in problems:
            print("PROBLEM " + p)
    elif cmd == "check":
        bad = len(problems)
        for p in problems:
            print("PROBLEM " + p)
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
        for p in problems:
            print("PROBLEM " + p)
    else:
        print(__doc__)
        sys.exit(2)


if __name__ == "__main__":
    main()
