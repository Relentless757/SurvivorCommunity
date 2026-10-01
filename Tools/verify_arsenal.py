import io, os, csv, re, ast
from lxml import etree as ET
G = r"F:\SteamLibrary\steamapps\common\7 Days To Die"
# SC_GEN_CONFIG checks a staged copy of Config (the same switch gen_arsenal.py writes to)
SC = os.environ.get("SC_GEN_CONFIG") or G + r"\Mods\SurvivorCommunity\Config"
# SC_GEN_XNPC replays against another 0-XNPCCore Config (the same switch gen_arsenal.py reads)
XNPC = os.environ.get("SC_GEN_XNPC") or G + r"\Mods\0-XNPCCore\Config"
def load(p): return ET.parse(p)
def apply(doc, patchfile, label):
    p = ET.parse(patchfile).getroot(); ok = miss = 0
    def walk(node):
        nonlocal ok, miss
        for op in node:
            if not isinstance(op.tag, str): continue
            if op.tag in ("conditional", "if", "else"):
                walk(op); continue
            xp = op.get("xpath")
            if not xp: continue
            try: targets = doc.xpath(xp)
            except Exception: targets = []
            if not targets: miss += 1; continue
            ok += 1
            kids = [c for c in op if isinstance(c.tag, str)]
            for t in targets:
                if not hasattr(t, "getparent"): continue
                if op.tag == "set":                   # attribute targets only: that is all SC's ops use
                    if isinstance(t, str) and getattr(t, "is_attribute", False): t.getparent().set(t.attrname, op.text or "")
                    continue
                if op.tag == "remove":
                    if isinstance(t, str):            # an attribute or text match: the game's RemoveByXPath throws
                        print("  !! %s: <remove> matches an attribute/text node: %s" % (label, xp)); continue
                    t.getparent().remove(t)
                elif op.tag == "append":
                    for c in kids: t.append(ET.fromstring(ET.tostring(c)))
                elif op.tag == "insertAfter":
                    i = t.getparent().index(t)
                    for k, c in enumerate(kids): t.getparent().insert(i + 1 + k, ET.fromstring(ET.tostring(c)))
                elif op.tag == "insertBefore":
                    i = t.getparent().index(t)
                    for k, c in enumerate(kids): t.getparent().insert(i + k, ET.fromstring(ET.tostring(c)))
    walk(p)
    print("  %-40s applied %d missed %d" % (label, ok, miss))

print("entity classes")
ec = load(G + r"\Data\Config\entityclasses.xml")
apply(ec, XNPC + r"\entityclasses.xml", "0-XNPCCore")
apply(ec, SC + r"\entityclasses.xml", "SurvivorCommunity")
names = {e.get("name"): e for e in ec.getroot().iter("entity_class")}
print("items")
it = load(G + r"\Data\Config\items.xml")
apply(it, XNPC + r"\items.xml", "0-XNPCCore")
apply(it, SC + r"\items.xml", "SurvivorCommunity")
items = set(e.get("name") for e in it.getroot().iter("item"))

errors = []
ours = [n for n in names if n.startswith("scSurvivor") or n.startswith("scRaider")]
leaves = [n for n in ours if "Template" not in n]
def chain(n):
    seen = []
    while n:
        if n in seen: errors.append("extends loop at " + n); break
        seen.append(n)
        e = names.get(n)
        if e is None: errors.append("unknown class in chain: " + n); break
        n = e.get("extends")
    return seen
def resolved(n, prop, klass=None):
    for c in chain(n):
        e = names[c]
        base = e if klass is None else (e.xpath("property[@class=$k]", k=klass) or [None])[0]
        if base is None: continue
        ign = (e.get("ignore") or "").split(",")
        p = base.xpath("property[@name=$p]", p=prop)
        if p: return p[0].get("value")
    return None
from collections import Counter
cnt = Counter()
for n in leaves:
    ch = chain(n)
    cls = resolved(n, "Class"); fac = resolved(n, "Faction"); mt = resolved(n, "ModelType")
    loadout = resolved(n, "GameModeSurvival", "ItemsOnEnterGame") or ""
    for x in [s.strip() for s in loadout.split(",") if s.strip()]:
        if x not in items: errors.append("%s: loadout item %s does not exist" % (n, x))
    bag = resolved(n, "BagItems")
    if bag and bag not in items: errors.append("%s: BagItems %s does not exist" % (n, bag))
    if mt != "SDCS": errors.append("%s: ModelType %s" % (n, mt))
    # Mesh must be dropped by the template's ignore
    tmpl = names[ch[1]]
    if "Mesh" not in (tmpl.get("ignore") or ""): errors.append("%s: template does not ignore Mesh" % n)
    side = "friendly" if n.startswith("scSurvivor") else "hostile"
    if side == "friendly" and (fac != "whiteriver" or "EntityAliveSDX" not in (cls or "")): errors.append("%s: friendly but %s / %s" % (n, fac, cls))
    if side == "hostile" and (fac != "bandits" or "EntityNPCBandit" not in (cls or "")): errors.append("%s: hostile but %s / %s" % (n, fac, cls))
    if side == "friendly" and resolved(n, "Hirable") != "true": errors.append(n + ": friendly not hirable")
    if side == "hostile" and resolved(n, "Hirable") == "true": errors.append(n + ": hostile is hirable")
    cnt[(side, (loadout.split(",")[0] if loadout else "-"))] += 1
    # W2: the DLL's second-weapon roll reads these; a missing or contradictory pair would leave an NPC one-armed
    kind, fam = resolved(n, "ScWeaponKind"), resolved(n, "ScWeaponFamily")
    if kind not in ("melee", "ranged"): errors.append("%s: ScWeaponKind %s" % (n, kind))
    elif fam not in (("melee", "unarmed") if kind == "melee" else ("bow", "firearm", "launcher")):
        errors.append("%s: ScWeaponFamily %s does not fit kind %s" % (n, fam, kind))
# XNPCCore migration M3: 0-XNPCCore's own survivors carry SC's look, armor scale, names and no preset bag or stock
# (gen_arsenal.py XNPC_SURVIVOR_OPS), every one of them, exactly once
_gen_src = io.open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "gen_arsenal.py"), encoding="utf-8").read()
def _gen_const(name):
    """A literal table from gen_arsenal.py, read without running it (running it would regenerate the Config)."""
    m = re.search(r"^%s = (.*?)\n(?=[^\s}\])])" % name, _gen_src, flags=re.S | re.M)   # up to the next top-level line
    return ast.literal_eval(m.group(1).strip()) if m else None
xnpc_survivors = _gen_const("XNPC_SURVIVORS") or []
gen_names = _gen_const("NAMES") or {}
if len(xnpc_survivors) != 6: errors.append("gen_arsenal.py XNPC_SURVIVORS: %d classes, want 6" % len(xnpc_survivors))
for n in xnpc_survivors:
    e = names.get(n)
    if e is None: errors.append("XNPC survivor %s does not exist after the replay" % n); continue
    if e.xpath("effect_group[@name='SDCS Look']"): errors.append(n + ": still has XNPC's 'SDCS Look' effect group")
    groups_ = e.xpath("effect_group[@name='SurvivorCommunity worn armor scale']")
    if len(groups_) != 1: errors.append("%s: %d 'SurvivorCommunity worn armor scale' groups, want 1" % (n, len(groups_)))
    for prop in ("BagItems", "HarvestItems"):
        v = e.xpath("property[@name=$p]/@value", p=prop)
        if v != ["none"]: errors.append("%s: %s is %s, want none (the property must stay)" % (n, prop, v))
    want = gen_names.get(("s", "Female" not in n))
    if e.xpath("property[@name='Names']/@value") != [want]: errors.append(n + ": Names is not SC's survivor pool")
print("  XNPC survivors checked: %d" % len(xnpc_survivors))
# duplicate definitions (a rerun of the generator must replace its block, never append a second)
for label, root, tag in (("item", it.getroot(), "item"), ("entity class", ec.getroot(), "entity_class")):
    c = Counter(e.get("name") for e in root.iter(tag))
    d = [k for k, v in c.items() if v > 1 and k and ("NPCSC" in k or k.startswith("scSurvivor") or k.startswith("scRaider"))]
    if d: errors.append("duplicate %s definitions: %s" % (label, d[:5]))
# new items: CompatibleWeapon must be a vanilla item, effect groups present
van = set(e.get("name") for e in load(G + r"\Data\Config\items.xml").getroot().iter("item"))
for e in it.getroot().iter("item"):
    n = e.get("name") or ""
    if "NPCSC" not in n: continue
    cw = e.xpath("property[@name='CompatibleWeapon']/@value")
    if not cw or cw[0] not in van: errors.append("%s: CompatibleWeapon %s" % (n, cw))
    if not e.xpath("effect_group"): errors.append(n + ": no effect groups")
    if not e.xpath("property[@name='Meshfile']/@value"): errors.append(n + ": no Meshfile")
print("entity groups")
eg = load(G + r"\Data\Config\entitygroups.xml")
apply(eg, XNPC + r"\entitygroups.xml", "0-XNPCCore")
apply(eg, SC + r"\entitygroups.xml", "SurvivorCommunity")
for grp in eg.getroot().iter("entitygroup"):
    for e in grp.findall("e"):
        n = e.get("n")
        if n and (n.startswith("scSurvivor") or n.startswith("scRaider")) and n not in names:
            errors.append("group %s: unknown class %s" % (grp.get("name"), n))
groups = {g.get("name"): g for g in eg.getroot().iter("entitygroup")}
placed = set()
for tag in ("Survivor", "Raider"):
    for band in ("GS01", "GS50", "GS100"):
        gn = "sc%sGroup%s" % (tag, band)
        grp = groups.get(gn)
        if grp is None: errors.append("missing group " + gn); continue
        es = grp.findall("e")
        if es and es[0].get("n") == "none": errors.append(gn + ": 'none' is entry 0, IsEnemyGroup reads that one")
        nones = [e for e in es if e.get("n") == "none"]
        if len(nones) != 1: errors.append("%s: %d 'none' entries, want exactly 1" % (gn, len(nones)))
        hit = sum(float(e.get("p")) for e in es if e.get("n") != "none")
        tot = hit + sum(float(e.get("p")) for e in nones)
        print("  %-22s %3d entries  %4.1f%% per roll" % (gn, len(es) - len(nones), 100.0 * hit / tot))
        placed.update(e.get("n") for e in es if e.get("n") != "none")
# W2: firearms are rare - in every band they are the same share of the ranged class weapons as the DLL's ranged
# roll uses for the second weapon (SurvivorFirearmChance in blocks.xml), so either way an NPC's one ranged weapon is
# a firearm at that rate
fc = None
try:
    bx = load(os.path.join(SC, "blocks.xml")).getroot().xpath("//property[@name='SurvivorFirearmChance']/@value")
    fc = float(bx[0]) if bx else None
except Exception as ex: errors.append("blocks.xml: " + str(ex))
if fc is None: errors.append("blocks.xml has no SurvivorFirearmChance")
for gn, grp in sorted(groups.items()):
    if not gn.startswith(("scSurvivorGroup", "scRaiderGroup")): continue
    share = {}
    for e in grp.findall("e"):
        if e.get("n") == "none": continue
        f = resolved(e.get("n"), "ScWeaponFamily")
        share[f] = share.get(f, 0.0) + float(e.get("p"))
    ranged = sum(v for k, v in share.items() if k in ("bow", "firearm", "launcher"))
    fire = share.get("firearm", 0.0) + share.get("launcher", 0.0)
    pct = fire / ranged if ranged else 0.0
    print("  %-22s ranged %4.1f%%, firearms %4.1f%% of those" % (gn, 100.0 * ranged / sum(share.values()), 100.0 * pct))
    if fc is not None and abs(pct - fc) > 0.005:
        errors.append("%s: firearms are %.1f%% of its ranged weapons, SurvivorFirearmChance is %.1f%%" % (gn, 100 * pct, 100 * fc))
missing_leaf = [n for n in leaves if n not in placed]
if missing_leaf: errors.append("%d leaves are in no group e.g. %s" % (len(missing_leaf), missing_leaf[:3]))

print("biome spawning")
sp = load(G + r"\Data\Config\spawning.xml")
apply(sp, XNPC + r"\spawning.xml", "0-XNPCCore")
apply(sp, SC + r"\spawning.xml", "SurvivorCommunity")
covered = set()
for biome in sp.getroot().iter("biome"):
    bn = biome.get("name")
    ids = [s.get("id") for s in biome.findall("spawn")]
    for s in biome.findall("spawn"):
        gn = s.get("entitygroup")
        if gn not in groups: errors.append("%s: spawn %s calls unknown group %s" % (bn, s.get("id"), gn))
        elif gn.startswith(("scSurvivorGroup", "scRaiderGroup")): covered.add(bn)
    if not all(ids): errors.append(bn + ": a spawn has no id (the game hashes it and would collide)")
    dup = [i for i, c in Counter(ids).items() if c > 1]
    if dup: errors.append("%s: duplicate spawn ids %s - the game throws on this" % (bn, dup[:3]))
bare = [b.get("name") for b in sp.getroot().iter("biome") if b.get("name") not in covered]
if bare: errors.append("no spawns of ours in biomes: %s" % bare)
print("  our spawn entries in %d biomes, %d total" % (
    len(covered), len(sp.getroot().xpath("//spawn[starts-with(@entitygroup,'scSurvivorGroup') or starts-with(@entitygroup,'scRaiderGroup')]"))))
biome_groups = set(s.get("entitygroup") for s in sp.getroot().iter("spawn"))
# biome rate: ours only through our own groups (one rate knob; installing packs must not dilute us). The framework's
# SLEEPER groups do carry ours since W0, by design - they feed POI volumes, not the biome rate.
for gn in sorted(biome_groups):
    if gn.startswith(("scSurvivorGroup", "scRaiderGroup")) or gn not in groups: continue
    stray = [e.get("n") for e in groups[gn].findall("e") if (e.get("n") or "").startswith(("scSurvivor", "scRaider"))]
    if stray: errors.append("biome group %s carries %d of ours e.g. %s" % (gn, len(stray), stray[0]))

# W0: no NEW pack NPCs (framework NPC classes that are not ours) from any spawner while this mod is installed
# Any group can be called by something - a biome <spawn>, a gamestage spawner (sleeper volumes, hordes), a sleeper
# block's SpawnGroup, an <entityspawner>, an SCore spawn block - so after W0 NO group may hold a pack NPC.
print("W0: pack NPCs in entity groups")
def is_pack(n):
    if not n or n == "none" or n.startswith(("scSurvivor", "scRaider")) or "Template" in n or n not in names: return False
    c = resolved(n, "Class") or ""
    return "EntityAliveSDX" in c or "EntityNPCBandit" in c
pack_classes = sorted(n for n in names if is_pack(n))
holding, swapped, mixed = [], [], []
for gn, grp in groups.items():
    es = [e for e in grp if isinstance(e.tag, str) and e.tag in ("e", "entity")]
    if not es and not (grp.text or "").strip(): errors.append("group %s is empty - the game throws at load" % gn)
    ns = [e.get("n") or e.get("name") for e in es]
    for n in ns:
        if n != "none" and n not in names: errors.append("group %s: unknown class %s - the game throws at load" % (gn, n))
    if gn.startswith("npc") and ns and ns[0] == "none":
        errors.append("%s: 'none' is entry 0, IsEnemyGroup reads that one" % gn)
    packs = [n for n in ns if is_pack(n)]
    if packs: holding.append(gn); errors.append("group %s still holds %d pack NPC(s) e.g. %s" % (gn, len(packs), packs[0]))
    sides = set("survivor" if n.startswith("scSurvivor") else "raider" for n in ns if n and n.startswith(("scSurvivor", "scRaider")))
    if gn.startswith("npc") and sides:
        swapped.append(gn)
        if len(sides) > 1: mixed.append(gn)
if mixed: errors.append("framework groups mixing survivors and raiders: %s" % mixed[:5])
print("  %d pack NPC classes; %d framework groups now carry ours; %d groups still hold pack NPCs" % (
    len(pack_classes), len(swapped), len(holding)))
# localization
with io.open(SC + r"\Localization.csv", encoding="utf-8") as f:
    rows = list(csv.reader(f))
keys = [r[0] for r in rows if r]
dups = [k for k, c in Counter(keys).items() if c > 1]
if dups: errors.append("duplicate localization keys: %s" % dups[:5])
missing_loc = [n for n in leaves if n not in keys]
if missing_loc: errors.append("no localization for %d leaves e.g. %s" % (len(missing_loc), missing_loc[:3]))
print()
print("leaves:", len(leaves), " friendly:", sum(1 for n in leaves if n.startswith("scSurvivor")), " hostile:", sum(1 for n in leaves if n.startswith("scRaider")))
print("distinct starting weapons:", len(set(k[1] for k in cnt)))
print("ERRORS: %d" % len(errors))
for e in errors[:30]: print("  " + e)
