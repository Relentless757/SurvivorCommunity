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
# XNPCCore migration (decision D11): SurvivorCommunity defines raiders only; its survivors are 0-XNPCCore's own
# npcSdcsSurvivor classes (XNPC_SURVIVORS in gen_arsenal.py), checked further down
leaves = [n for n in names if n.startswith("scRaider") and "Template" not in n]
legacy = [n for n in names if n.startswith("scSurvivor")]
if legacy: errors.append("D11: %d scSurvivor classes still defined e.g. %s" % (len(legacy), legacy[0]))
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
    if fac != "bandits" or "EntityNPCBandit" not in (cls or ""): errors.append("%s: hostile but %s / %s" % (n, fac, cls))
    if resolved(n, "Hirable") == "true": errors.append(n + ": hostile is hirable")
    cnt[("hostile", (loadout.split(",")[0] if loadout else "-"))] += 1
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
    if "EntityAliveSDXV4" not in (resolved(n, "Class") or ""): errors.append("%s: Class %s, want EntityAliveSDXV4" % (n, resolved(n, "Class")))
    if not re.search(r"T[123]$", n): errors.append(n + ": no T1-T3 tier in the name - the DLL's weapon tier cap reads it")
print("  XNPC survivors checked: %d" % len(xnpc_survivors))
# duplicate definitions (a rerun of the generator must replace its block, never append a second)
for label, root, tag in (("item", it.getroot(), "item"), ("entity class", ec.getroot(), "entity_class")):
    c = Counter(e.get("name") for e in root.iter(tag))
    d = [k for k, v in c.items() if v > 1 and k and ("NPCSC" in k or k.startswith("scSurvivor") or k.startswith("scRaider"))]
    if d: errors.append("duplicate %s definitions: %s" % (label, d[:5]))
# new items: CompatibleWeapon must be a vanilla item, effect groups present
van_root = load(G + r"\Data\Config\items.xml").getroot()
van = set(e.get("name") for e in van_root.iter("item"))
for e in it.getroot().iter("item"):
    n = e.get("name") or ""
    if "NPCSC" not in n: continue
    cw = e.xpath("property[@name='CompatibleWeapon']/@value")
    if not cw or cw[0] not in van: errors.append("%s: CompatibleWeapon %s" % (n, cw))
    if not e.xpath("effect_group"): errors.append(n + ": no effect groups")
    if not e.xpath("property[@name='Meshfile']/@value"): errors.append(n + ": no Meshfile")

# XNPCCore migration M7: the survivors' weapons (blocks.xml XnpcWeapons). Their classes carry none: the DLL rolls a
# melee weapon and a ranged one per NPC, at or below a tier cap of 0, 1 or 2, a firearm at SurvivorFirearmChance. So
# every entry must be a real vanilla player weapon that 0-XNPCCore can use (its reload and dry-gun failover read
# Action0's Magazine_items), and every cap needs a melee, a bow and a firearm entry - an empty pool would make the
# roll fall back to the other family and drift off the configured firearm share.
print("survivor weapons (blocks.xml XnpcWeapons)")
def vitem(n):
    r = van_root.xpath("//item[@name=$n]", n=n); return r[0] if r else None
def vprop(item, name, klass=None):
    hops = 0
    while item is not None and hops < 6:
        base = item if klass is None else (item.xpath("property[@class=$c]", c=klass) or [None])[0]
        if base is not None:
            p = base.xpath("property[@name=$n]", n=name)
            if p: return p[0].get("value")
        ext = item.xpath("property[@name='Extends']")
        item = vitem(ext[0].get("value")) if ext else None
        hops += 1
    return None
blocks = load(os.path.join(SC, "blocks.xml")).getroot()
cfg = (blocks.xpath("//block[@name='ConfigSurvivorCommunity']") or [None])[0]
if cfg is None: errors.append("blocks.xml: no ConfigSurvivorCommunity block")
xw = cfg.xpath("property[@class='XnpcWeapons']/property") if cfg is not None else []
if not xw: errors.append("blocks.xml: no XnpcWeapons entries")
cat = []
swap_tags = set()
base_tags = names.get("npcSdcsBase")
if base_tags is not None:
    swap_tags = set(t[len("sdcsSwap_"):] for t in (resolved("npcSdcsBase", "Tags") or "").split(",") if t.startswith("sdcsSwap_"))
for p in xw:
    item, val = p.get("name"), (p.get("value") or "").split(",")
    if len(val) < 2 or val[0] not in ("melee", "bow", "firearm") or not val[1].strip().isdigit() or int(val[1]) > 2:
        errors.append("XnpcWeapons %s: value %r, want family,tier[,alias]" % (item, p.get("value"))); continue
    fam, tier = val[0], int(val[1])
    v = vitem(item)
    if v is None: errors.append("XnpcWeapons %s: not a vanilla item" % item); continue
    tags = set((vprop(v, "Tags") or "").split(","))
    if fam == "melee" and "melee" not in tags: errors.append("XnpcWeapons %s: family melee, but the item has no melee tag" % item)
    if fam != "melee":
        if not vprop(v, "Magazine_items", "Action0"): errors.append("XnpcWeapons %s: no Action0 Magazine_items - 0-XNPCCore could not reload it" % item)
        if fam == "bow" and not (tags & {"bow", "crossbow"}): errors.append("XnpcWeapons %s: family bow, but the item is no bow or crossbow" % item)
        if fam == "firearm" and "gun" not in tags: errors.append("XnpcWeapons %s: family firearm, but the item has no gun tag" % item)
    if swap_tags and item not in swap_tags: print("  note: %s has no sdcsSwap_ tag on npcSdcsBase - a voice 'use' refuses it" % item)
    cat.append((item, fam, tier))
dups = [k for k, c in Counter(x[0] for x in cat).items() if c > 1]
if dups: errors.append("XnpcWeapons: listed twice: %s" % dups)
fc = None
try:
    bx = blocks.xpath("//property[@name='SurvivorFirearmChance']/@value")
    fc = float(bx[0]) if bx else None
except Exception as ex: errors.append("blocks.xml: " + str(ex))
if fc is None: errors.append("blocks.xml has no SurvivorFirearmChance")
for cap in (0, 1, 2):
    pools = dict((f, [x[0] for x in cat if x[1] == f and x[2] <= cap]) for f in ("melee", "bow", "firearm"))
    empty = [f for f, l in pools.items() if not l]
    if empty: errors.append("XnpcWeapons: no %s weapon at tier cap %d - the roll would drift off SurvivorFirearmChance" % ("/".join(empty), cap))
    print("  tier cap %d: melee %d, bow %d, firearm %d -> a survivor's ranged weapon is a firearm %s%%" % (
        cap, len(pools["melee"]), len(pools["bow"]), len(pools["firearm"]), "?" if fc is None or empty else "%.1f" % (100 * fc)))
xsc = (cfg.xpath("property[@name='XnpcSurvivorClasses']/@value") if cfg is not None else []) or [""]
if [x.strip() for x in xsc[0].split(",") if x.strip()] != list(xnpc_survivors):
    errors.append("blocks.xml XnpcSurvivorClasses %s is not gen_arsenal.py's XNPC_SURVIVORS" % xsc[0])

print("entity groups")
eg = load(G + r"\Data\Config\entitygroups.xml")
apply(eg, XNPC + r"\entitygroups.xml", "0-XNPCCore")
apply(eg, SC + r"\entitygroups.xml", "SurvivorCommunity")
def is_ours(n):
    return bool(n) and (n.startswith("scRaider") or n in xnpc_survivors)
for grp in eg.getroot().iter("entitygroup"):
    for e in grp.findall("e"):
        n = e.get("n")
        if n and (n.startswith(("scSurvivor", "scRaider")) or n in xnpc_survivors) and n not in names:
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
        if tag == "Survivor":
            # D10: SC's tuned rates spawn 0-XNPCCore's survivors - its classes only, both sexes, the band's tier, none last
            members = [e.get("n") for e in es if e.get("n") != "none"]
            if es and es[-1].get("n") != "none": errors.append(gn + ": 'none' is not the last entry")
            stray = [n for n in members if n not in xnpc_survivors]
            if stray: errors.append("%s: holds %s - want only 0-XNPCCore's survivor classes" % (gn, stray))
            want_t = {"GS01": "T1", "GS50": "T2", "GS100": "T3"}[band]
            if any(not n.endswith(want_t) for n in members): errors.append("%s: a class of another tier than %s" % (gn, want_t))
            if not any("Female" in n for n in members) or not any("Female" not in n for n in members): errors.append(gn + ": not both sexes")
        hit = sum(float(e.get("p")) for e in es if e.get("n") != "none")
        tot = hit + sum(float(e.get("p")) for e in nones)
        print("  %-22s %3d entries  %4.1f%% per roll" % (gn, len(es) - len(nones), 100.0 * hit / tot))
        placed.update(e.get("n") for e in es if e.get("n") != "none")
# W2: a raider's firearms are rare - in every band they are the same share of the ranged class weapons as the DLL's
# ranged roll uses for the second weapon (SurvivorFirearmChance in blocks.xml), so either way a raider's one ranged
# weapon is a firearm at that rate. The survivors' groups carry no weapons: their share is the DLL's roll (above).
for gn, grp in sorted(groups.items()):
    if not gn.startswith("scRaiderGroup"): continue
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
missing_leaf = [n for n in leaves + list(xnpc_survivors) if n not in placed]
if missing_leaf: errors.append("%d classes are in none of our groups e.g. %s" % (len(missing_leaf), missing_leaf[:3]))

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
# biome rate: ours only through our own groups (one rate knob; installing packs must not dilute us, and D10: XNPC's
# own survivor spawn entries stay removed). The framework's SLEEPER groups do carry ours since W0, by design - they
# feed POI volumes, not the biome rate.
for gn in sorted(biome_groups):
    if gn.startswith(("scSurvivorGroup", "scRaiderGroup")) or gn not in groups: continue
    stray = [e.get("n") for e in groups[gn].findall("e") if is_ours(e.get("n"))]
    if stray: errors.append("biome group %s carries %d of ours e.g. %s" % (gn, len(stray), stray[0]))

# W0: no NEW pack NPCs (framework NPC classes that are not ours) from any spawner while this mod is installed
# Any group can be called by something - a biome <spawn>, a gamestage spawner (sleeper volumes, hordes), a sleeper
# block's SpawnGroup, an <entityspawner>, an SCore spawn block - so after W0 NO group may hold a pack NPC. Since the
# XNPCCore migration 0-XNPCCore's six survivor classes are ours; its other EntityAliveSDXV4 classes still count as pack.
print("W0: pack NPCs in entity groups")
def is_pack(n):
    if not n or n == "none" or is_ours(n) or "Template" in n or n not in names: return False
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
    if gn.startswith(("npc", "sc")) and ns and ns[0] == "none":
        errors.append("%s: 'none' is entry 0, IsEnemyGroup reads that one" % gn)
    packs = [n for n in ns if is_pack(n)]
    if packs: holding.append(gn); errors.append("group %s still holds %d pack NPC(s) e.g. %s" % (gn, len(packs), packs[0]))
    sides = set("survivor" if n in xnpc_survivors else "raider" for n in ns if is_ours(n))
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
stale_loc = [k for k in keys if re.match(r"^scSurvivor(Male|Female)", k)]
if stale_loc: errors.append("D11: %d localization rows of removed scSurvivor classes e.g. %s" % (len(stale_loc), stale_loc[0]))
print()
print("raider leaves:", len(leaves), " survivor classes (0-XNPCCore):", len(xnpc_survivors), " survivor weapons:", len(cat))
print("distinct raider starting weapons:", len(set(k[1] for k in cnt)))
print("ERRORS: %d" % len(errors))
for e in errors[:30]: print("  " + e)
