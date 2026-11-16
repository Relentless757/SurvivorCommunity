"""SurvivorCommunity's small talk (round U3, 2026-11-16): the pairs of lines two residents say to each other at home (and
an enclave's people in their own building), in their own recorded voices.

PAIRS below is the one source. Each pair is (number, tags, opener, answer):
  - the clip keys are scTalkOpen_<nn> and scTalkReply_<nn>: Resources/Voices/<voice>/<key>.ogg, as every SC line;
  - the words (the caption, and the text a recording says) are Config/Localization.csv's <key>Said rows. gen writes them
    as one block right after the horde warnings (vo4); Tools/voice_lines.py reads them as its family "smalltalk";
  - ScSocialLines.cs (gen) is the table the game picks from: each pair's number and tags.
Until a voice has the clips, the game shows the words as captions only (ScSocial); a recorded clip plays as soon as it is
in the voice's folder, with no code change.

Tags (comma separated; none: anyone, anywhere):
  home    only at your base, never in an enclave (it is about you, the runs, or your walls);
  fire    only during a campfire gathering, and much likelier there;
  up      fits a good mood: likelier for an Optimist, Cheerful, People Person or Hardy opener or one in a good mood, rare for
          a Pessimist or one in a low mood;
  down    fits a low mood: the other way round (Pessimist, Insomniac, Hoarder, Cowardly);
  <Trait> a trait name from Config/blocks.xml's Traits (Homesteader, Tinkerer, ...): much likelier when the opener has it,
          rare otherwise.
No names and no tokens: every voice says every line, and either side of a pair can be anyone.

    python -I Tools/smalltalk_lines.py list
        the pairs, their tags and words
    python -I Tools/smalltalk_lines.py gen
        write the Localization rows, ScSocialLines.cs and the recording manifest Tools/smalltalk_jobs.json
    python -I Tools/smalltalk_lines.py check
        whether those three are in step with PAIRS (exit 1 when not)
    python -I Tools/smalltalk_lines.py jobs <ids.json> <out.json>
        the recording manifest with each voice's ElevenLabs voice id filled in from the jobs.json of an earlier recording
        round (<ids.json>: a list of {"voice", "voice_id"}). The ids stay out of this repo; write <out.json> outside it.
"""
import csv
import io
import json
import os
import re
import sys

MOD = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOC = os.path.join(MOD, "Config", "Localization.csv")
CS = os.path.join(MOD, "ScSocialLines.cs")
JOBS = os.path.join(MOD, "Tools", "smalltalk_jobs.json")
ANCHOR = "scHordeOverrun_2Said"            # the block goes right after the horde warnings' last row
ROW_KEY = re.compile(r"^scTalk(Open|Reply)_\d+Said$")

DEFAULT_VOICES = [
    "male_White_Vane", "male_White_David", "female_White_Jessica", "female_White_Beth",
    "male_Black_Nate", "male_Black_Desmond", "female_Black_Tiffany", "female_Black_Rene",
    "male_Asian_Jasper", "male_Asian_Julian", "female_Asian_Layla", "female_Asian_Jess",
    "male_Native_NativeM1", "male_Native_NativeM2", "female_Native_NativeF1", "female_Native_NativeF2",
]

TRAITS = {"EatsLikeABird", "BottomlessStomach", "Optimist", "Pessimist", "Loner", "PeoplePerson", "Veteran",
          "Sharpshooter", "MarathonRunner", "QuickStudy", "Cowardly", "Hardy", "MedicTraining", "Homesteader",
          "Tinkerer", "Hoarder", "Cheerful", "Insomniac"}
PLAIN_TAGS = {"home", "fire", "up", "down"}

# (number, tags, opener, answer). Life at the base: the stockpile, the weather, a run, the walls, the night, food, jokes,
# worries, the leader (you: "the boss"). The user's brief, 2026-11-16 (D11): 30-40 pairs. The Fable review: no line ties
# itself to a time of day (they are said morning, noon and night), and a "home" line about the boss fits the boss being in earshot.
PAIRS = [
    # anyone, anywhere
    (1, "", "Stockpile's looking better than it did last week.", "Don't say that out loud. You'll jinx it."),
    (2, "", "Smell that? Rain's coming.", "Good. The barrels could use it."),
    (3, "home", "You hear how the last run went?", "Everyone came back. That's a good run."),
    (4, "", "The walls are holding up fine.", "Walls are only as good as whoever's watching them."),
    (5, "", "Nights are getting louder out there.", "Yeah. I sleep with my boots on now."),
    (6, "", "What's on the menu?", "Same as yesterday. Be grateful."),
    (7, "", "Knock knock.", "If it's a zombie, I'm not opening."),
    (8, "down", "You ever think about leaving?", "Every day. Then I remember what's out there."),
    (9, "home", "The boss seems to know what they're doing.", "Better than the last place I was at."),
    (10, "home", "Somebody should tell the boss to get some rest.", "Good luck with that. I've tried."),
    (11, "", "We're low on something again.", "We're always low on something."),
    (12, "", "Saw a deer by the fence this morning.", "Next time, wake me. I'll bring the rifle."),
    (13, "", "How's the arm?", "Still attached. I'll call that a win."),
    (14, "", "Found a deck of cards in the last crate.", "Missing half the hearts, I bet."),
    (15, "", "You ever miss your old job?", "I miss getting paid for it."),
    (16, "", "Somebody left the gate open again.", "Wasn't me. Probably."),
    (17, "", "Heard engines out east earlier.", "Raiders, maybe. Keep your eyes open."),
    # a good mood
    (18, "up", "This place is starting to feel like home.", "Don't get too comfortable."),
    (19, "up", "We're doing alright, you know that?", "Yeah. Better than alright, some days."),
    (20, "up", "One more wall and this place is a fortress.", "Then we'll want a moat."),
    (21, "up", "Thanks for covering my watch the other night.", "Anytime. You'd do the same for me."),
    # a low mood
    (22, "down", "Sometimes I don't see the point anymore.", "Eat something. Everything looks better on a full stomach."),
    (23, "down", "How long can we really keep this up?", "As long as we have to. One day at a time."),
    # a trait
    (24, "Homesteader", "The garden's coming in nicely.", "Good. I'm sick of canned food."),
    (25, "Tinkerer", "I almost have that old generator running.", "Almost is what you said yesterday."),
    (26, "MedicTraining", "Let me look at that cut before it gets infected.", "It's fine. It's just a scratch."),
    (27, "Veteran,Sharpshooter", "Clean your rifle tonight. It'll save your life.", "It's cleaner than your boots, at least."),
    (28, "Insomniac", "Couldn't sleep again. It was too quiet.", "Too quiet is the good kind of night."),
    (29, "BottomlessStomach", "Is there any more of that stew?", "You've had three bowls already."),
    (30, "Hoarder", "Don't touch that pile. I have a system.", "Your system is a pile."),
    (31, "Loner", "I'm going to sit on the wall for a while. Alone.", "Suit yourself. Holler if you see anything."),
    (32, "Cowardly", "I don't like how quiet it is out there.", "Quiet's good. Quiet means nothing's hungry."),
    (33, "PeoplePerson", "We should all eat together tonight.", "I'll bring the good cans."),
    (34, "Optimist,Cheerful", "I've got a good feeling about tomorrow.", "You say that every day."),
    (35, "Pessimist", "Mark my words, those walls won't hold forever.", "Nothing does. Doesn't mean we stop."),
    # a campfire gathering
    (36, "fire", "Nothing beats sitting round a fire.", "Except a fire and a cold drink."),
    (37, "fire", "To the ones we lost.", "And to the ones still here."),
    (38, "fire", "Remember telling ghost stories around a fire?", "Now we just keep watching the dark."),
]


def keys(n):
    return "scTalkOpen_%02d" % n, "scTalkReply_%02d" % n


def problems():
    out = []
    seen = set()
    for n, tags, a, b in PAIRS:
        if n in seen:
            out.append("pair %d is listed twice" % n)
        seen.add(n)
        for t in [t.strip() for t in tags.split(",") if t.strip()]:
            if t not in PLAIN_TAGS and t not in TRAITS:
                out.append("pair %d: unknown tag %s" % (n, t))
        for w in (a, b):
            if "{" in w or "}" in w:
                out.append("pair %d has a token: %s" % (n, w))
            if not w.strip():
                out.append("pair %d has an empty line" % n)
    return out


def lines():
    """[(key, words)] in order: each pair's opener, then its answer."""
    out = []
    for n, _, a, b in PAIRS:
        ko, kr = keys(n)
        out.append((ko, a))
        out.append((kr, b))
    return out


# ------------------------------------------------------------------------------------------------- Localization ----

def read_loc():
    with io.open(LOC, "r", encoding="utf-8-sig", newline="") as f:
        text = f.read()
    return text


def loc_rows():
    """The block gen writes, as CSV text (LF line ends, QUOTE_MINIMAL as the rest of the file)."""
    buf = io.StringIO()
    w = csv.writer(buf, lineterminator="\n", quoting=csv.QUOTE_MINIMAL)
    for k, t in lines():
        w.writerow([k + "Said", "UI", "Tooltip", "", "", "", t])
    return buf.getvalue()


def loc_with_block(text):
    """Localization.csv's text with every scTalk... row removed and the block put right after ANCHOR's row."""
    out, anchored = [], False
    for row in text.split("\n"):
        key = row.split(",", 1)[0]
        if ROW_KEY.match(key):
            continue
        out.append(row)
        if key == ANCHOR:
            out.append(loc_rows().rstrip("\n"))
            anchored = True
    if not anchored:
        raise SystemExit("no row %s in Config/Localization.csv - nowhere to put the block" % ANCHOR)
    return "\n".join(out)


# ------------------------------------------------------------------------------------------------- ScSocialLines ----

def cs_string(s):
    out = []
    for ch in s:
        if ch == "\\":
            out.append("\\\\")
        elif ch == '"':
            out.append('\\"')
        elif ord(ch) < 32 or ord(ch) > 126:
            out.append("\\u%04x" % ord(ch))
        else:
            out.append(ch)
    return '"' + "".join(out) + '"'


def cs_table():
    o = []
    o.append("// <auto-generated>")
    o.append("// Written by Tools/smalltalk_lines.py gen from its PAIRS. Edit the pairs there and run it again;")
    o.append("// \"python -I Tools/smalltalk_lines.py check\" says when this file is stale.")
    o.append("// </auto-generated>")
    o.append("#if SC_V34")
    o.append("namespace SurvivorCommunity")
    o.append("{")
    o.append("    /// <summary>")
    o.append("    /// Round U3: the small-talk pairs, as number, tags, number, tags, ... A pair's clips are scTalkOpen_&lt;nn&gt; and")
    o.append("    /// scTalkReply_&lt;nn&gt;; their words are Localization's &lt;key&gt;Said rows. Tags: home, fire, up, down or a trait name")
    o.append("    /// (comma separated, empty for anyone anywhere); ScSocialVoice.PickPair weighs them.")
    o.append("    /// </summary>")
    o.append("    public static class ScSocialLines")
    o.append("    {")
    o.append("        public static readonly string[] Pairs =")
    o.append("        {")
    for n, tags, a, b in PAIRS:
        o.append("            %s, %s,   // %s / %s" % (cs_string("%02d" % n), cs_string(tags), a.replace("*/", ""), b.replace("*/", "")))
    o.append("        };")
    o.append("    }")
    o.append("}")
    o.append("#endif")
    return "\n".join(o) + "\n"


# ------------------------------------------------------------------------------------------------------ manifest ----

def voice_table():
    try:
        with io.open(os.path.join(MOD, "Config", "blocks.xml"), "r", encoding="utf-8") as f:
            xml = re.sub(r"<!--.*?-->", "", f.read(), flags=re.S)
        m = re.search(r'<property\s+name="Voices"\s+value="([^"]*)"', xml)
        if m:
            return [v.strip() for v in m.group(1).split(",") if v.strip()]
    except (IOError, OSError):
        pass
    return list(DEFAULT_VOICES)


def manifest():
    """Per voice: every new line's key and words (what each voice records - both sides, since anyone can open or answer)."""
    ls = [{"key": k, "text": t} for k, t in lines()]
    return [{"voice": v, "lines": ls} for v in voice_table()]


def manifest_text():
    return json.dumps(manifest(), indent=1, ensure_ascii=False) + "\n"


# ------------------------------------------------------------------------------------------------------ commands ----

def write(path, text, bom=False):
    with io.open(path, "w", encoding="utf-8-sig" if bom else "utf-8", newline="") as f:
        f.write(text)


def state():
    out = {}
    loc = read_loc()
    out["Config/Localization.csv"] = loc_with_block(loc) == loc
    out["ScSocialLines.cs"] = os.path.isfile(CS) and io.open(CS, "r", encoding="utf-8", newline="").read() == cs_table()
    out["Tools/smalltalk_jobs.json"] = os.path.isfile(JOBS) and io.open(JOBS, "r", encoding="utf-8", newline="").read() == manifest_text()
    return out


def main():
    args = sys.argv[1:]
    cmd = args.pop(0) if args else "list"
    bad = problems()
    for p in bad:
        print("PROBLEM " + p)
    if cmd == "list":
        for n, tags, a, b in PAIRS:
            ko, kr = keys(n)
            print("%-14s %-22s %s" % (ko, tags or "-", a))
            print("%-14s %-22s %s" % (kr, "", b))
        ls = lines()
        print("%d pairs, %d lines per voice, %d characters per voice" % (len(PAIRS), len(ls), sum(len(t) for _, t in ls)))
    elif cmd == "gen":
        if bad:
            sys.exit(1)
        write(LOC, loc_with_block(read_loc()), bom=True)
        write(CS, cs_table())
        write(JOBS, manifest_text())
        ls = lines()
        print("wrote %d Localization rows, ScSocialLines.cs (%d pairs) and the manifest (%d voices x %d lines, %d characters per voice)"
              % (len(ls), len(PAIRS), len(voice_table()), len(ls), sum(len(t) for _, t in ls)))
    elif cmd == "check":
        st = state()
        for name, ok in st.items():
            print("%s: %s" % (name, "in step" if ok else "STALE - run gen"))
        sys.exit(1 if bad or not all(st.values()) else 0)
    elif cmd == "jobs":
        if len(args) != 2:
            raise SystemExit("usage: smalltalk_lines.py jobs <ids.json> <out.json>")
        with io.open(args[0], "r", encoding="utf-8") as f:
            ids = {j["voice"]: j["voice_id"] for j in json.load(f)}
        jobs = []
        for j in manifest():
            if j["voice"] not in ids:
                raise SystemExit("no voice id for %s in %s" % (j["voice"], args[0]))
            jobs.append({"voice": j["voice"], "voice_id": ids[j["voice"]], "lines": j["lines"]})
        out = os.path.abspath(args[1])
        if out.lower().startswith(MOD.lower() + os.sep):
            raise SystemExit("write the manifest with the voice ids outside the repo: " + out)
        write(out, json.dumps(jobs, indent=1, ensure_ascii=False) + "\n")
        print("wrote %s: %d voices x %d lines" % (out, len(jobs), len(lines())))
    else:
        raise SystemExit(__doc__)


if __name__ == "__main__":
    main()
