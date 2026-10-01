# Generates the player-body survivors for every weapon, friendly and hostile, plus NPC versions of the vanilla
# weapons the NPC framework has none for. One table below is the single source of truth; rerunning is safe.
#
# Writes:
#   Config/entityclasses.xml   (whole file)   scSurvivor<Weapon>Template + scSurvivor<Sex><Weapon>   (friendly)
#                                             scRaider<Weapon>Template   + scRaider<Sex><Weapon>     (hostile)
#   Config/entitygroups.xml    (whole file)   our own survivor and raider groups, tiered; and the pack NPCs
#                                             swapped out of the framework's sleeper groups (W0, below)
#   Config/spawning.xml        (whole file)   our own biome spawn entries that call those groups; and the
#                                             framework's own pack-NPC biome spawns removed (W0)
#   Config/items.xml           (marked block) the new NPC weapon items, cloned from the framework's closest one
#   Config/Localization.csv    (managed rows) entity and item names
#
# Facts this relies on (all checked in the 3.2 decompile or the installed XML):
#  * item Extends copies properties but NOT effect groups (ItemClassesFromXml parses Effects from the child node
#    only), so a new NPC weapon is a full clone of the framework item, not an Extends of it
#  * entity classes: Tags do not inherit; ItemsOnEnterGame is a class block, merged per key
#  * the framework's hostile pattern (npcHarleyAK47): the plain npc<W>Template + Class EntityNPCBandit + Faction
#    bandits + AIPackages NPCModCoreNoChat, NPCModHostileMeleeBasic, NPCModHostileRangedBasic
#  * the four legacy class names (scSurvivor{Male,Female}{Rifle,Club}) are in saves and must not change
import io, os, re, sys, csv
from lxml import etree as ET

GAME = r"F:\SteamLibrary\steamapps\common\7 Days To Die"
# SC_GEN_CONFIG points the output at a staged COPY of Config (items.xml and Localization.csv are read back too)
MOD = os.environ.get("SC_GEN_CONFIG") or os.path.join(GAME, r"Mods\SurvivorCommunity\Config")
XNPC = os.path.join(GAME, r"Mods\0-XNPCCore\Config")
VAN = os.path.join(GAME, r"Data\Config")

# ------------------------------------------------------------------------------------------------------------
# The table.  token: class-name weapon token.  cat: the framework weapon whose templates this rides on.
# tier: 0 early (melee, and the primitive ranged weapons - pipe guns and the primitive bow - which everyone may
# carry from day 1), 1 middle (wooden bow, iron crossbow, double barrel), 2 late.  new: vanilla item that needs a
# new NPC weapon (cloned from base), or None when the framework already has one.
# ------------------------------------------------------------------------------------------------------------
WEAPONS = [
    # token              cat           kind      tier  new vanilla item                         base NPC item
    ("EmptyHand",        "EmptyHand",  "melee",  0,    None,                                    None),
    ("Club",             "Club",       "melee",  0,    None,                                    None),   # legacy name
    ("Knife",            "Knife",      "melee",  0,    None,                                    None),
    ("Bat",              "Bat",        "melee",  0,    None,                                    None),
    ("Axe",              "Axe",        "melee",  0,    None,                                    None),
    ("Spear",            "Spear",      "melee",  0,    None,                                    None),
    ("Machete",          "Machete",    "melee",  0,    None,                                    None),
    ("PipePistol",       "PipePistol", "ranged", 0,    None,                                    None),
    ("PipeRifle",        "PipeRifle",  "ranged", 0,    None,                                    None),
    ("PipeShotgun",      "PipeShotgun","ranged", 0,    None,                                    None),
    ("PipeMG",           "PipeMG",     "ranged", 0,    None,                                    None),
    ("LBow",             "LBow",       "ranged", 1,    None,                                    None),
    ("XBow",             "XBow",       "ranged", 1,    None,                                    None),
    ("Pistol",           "Pistol",     "ranged", 2,    None,                                    None),
    ("DPistol",          "DPistol",    "ranged", 2,    None,                                    None),
    ("SMG",              "SMG",        "ranged", 2,    None,                                    None),
    ("Rifle",            "AK47",       "ranged", 2,    None,                                    None),   # legacy name, an AK-47
    ("TRifle",           "TRifle",     "ranged", 2,    None,                                    None),
    ("HRifle",           "HRifle",     "ranged", 2,    None,                                    None),
    ("SRifle",           "SRifle",     "ranged", 2,    None,                                    None),
    ("PShotgun",         "PShotgun",   "ranged", 2,    None,                                    None),
    ("AShotgun",         "AShotgun",   "ranged", 2,    None,                                    None),
    ("M60",              "M60",        "ranged", 2,    None,                                    None),
    ("RocketL",          "RocketL",    "ranged", 2,    None,                                    None),
    # vanilla weapons the framework has no NPC version of
    ("BoneKnife",        "Knife",      "melee",  0,    "meleeWpnBladeT0BoneKnife",              "meleeNPCKnife"),
    ("LeatherKnuckles",  "Knife",      "melee",  0,    "meleeWpnKnucklesT0LeatherKnuckles",     "meleeNPCKnife"),
    ("IronKnuckles",     "Knife",      "melee",  0,    "meleeWpnKnucklesT1IronKnuckles",        "meleeNPCKnife"),
    ("SteelKnuckles",    "Knife",      "melee",  2,    "meleeWpnKnucklesT3SteelKnuckles",       "meleeNPCKnife"),
    ("PipeBaton",        "Club",       "melee",  0,    "meleeWpnBatonT0PipeBaton",              "meleeNPCClubWood"),
    ("StunBaton",        "Club",       "melee",  1,    "meleeWpnBatonT2StunBaton",              "meleeNPCClubWood"),
    ("SteelClub",        "Club",       "melee",  2,    "meleeWpnClubT3SteelClub",               "meleeNPCClubWood"),
    ("StoneSledge",      "Club",       "melee",  0,    "meleeWpnSledgeT0StoneSledgehammer",     "meleeNPCClubWood"),
    ("IronSledge",       "Club",       "melee",  0,    "meleeWpnSledgeT1IronSledgehammer",      "meleeNPCClubWood"),
    ("SteelSledge",      "Club",       "melee",  2,    "meleeWpnSledgeT3SteelSledgehammer",     "meleeNPCClubWood"),
    ("StoneSpear",       "Spear",      "melee",  0,    "meleeWpnSpearT0StoneSpear",             "meleeNPCSpear"),
    ("SteelSpear",       "Spear",      "melee",  2,    "meleeWpnSpearT3SteelSpear",             "meleeNPCSpear"),
    ("SteelAxe",         "Axe",        "melee",  1,    "meleeToolAxeT2SteelAxe",                "meleeNPCAxe"),
    ("Magnum",           "Pistol",     "ranged", 2,    "gunHandgunT2Magnum44",                  "gunNPCPistol"),
    ("DoubleBarrel",     "PShotgun",   "ranged", 1,    "gunShotgunT1DoubleBarrel",              "gunNPCPShotgun"),
    ("LeverAction",      "HRifle",     "ranged", 2,    "gunRifleT2LeverActionRifle",            "gunNPCHRifle"),
    ("PrimitiveBow",     "LBow",       "ranged", 0,    "gunBowT0PrimitiveBow",                  "gunNPCLBow"),
    ("CompoundBow",      "LBow",       "ranged", 2,    "gunBowT3CompoundBow",                   "gunNPCLBow"),
    ("CompoundCrossbow", "XBow",       "ranged", 2,    "gunBowT3CompoundCrossbow",              "gunNPCXBow"),
]

# W2: what the DLL's second-weapon roll needs to know about a weapon (ScWeaponKind / ScWeaponFamily on every
# template). Every NPC carries one weapon of each kind: the class weapon, plus one of the other kind rolled per
# entity. "unarmed" counts as melee but is never rolled as a second weapon; neither is the "launcher" (its attack
# cadence is bound to its class). Firearms are rare (the user's call, 2026-09-30): the ranged roll picks a bow or
# crossbow unless SurvivorFirearmChance comes up.
BOWS = {"PrimitiveBow", "LBow", "XBow", "CompoundBow", "CompoundCrossbow"}

def family(token, kind):
    if token == "EmptyHand": return "unarmed"
    if kind == "melee": return "melee"
    if token in BOWS: return "bow"
    if token == "RocketL": return "launcher"
    return "firearm"

# No DRLow/DRMid/DRHigh here: those tags buy the framework's flat +10/+20/+30 PhysicalDamageResist, which it
# balanced for NPCs that wear no armor. These wear real vanilla armor, so the tag stacked on top of it - a raider in
# three q1-2 Nerd pieces measured 84.5% armor rating (10 template + 20 DRMid + 54.5 armor) and shrugged off melee.
SWAP_TAGS = "Knife,Machete,ClubWood,BatWood,Axe,Spear,SMG,PipePistol,PipeShotgun,PipeRifle,PipeMG,M60,Pistol,DPistol,Ak47,TRifle,HRifle,SRifle,LBow,XBow,PShotgun,AShotgun,RocketL"
NAMES = {
    ("s", True):  "Alex,Ben,Carl,Dan,Eli,Frank,Gus,Hank,Ivan,Jack,Kyle,Leo,Mike,Nate,Owen,Pete,Ray,Sam,Tom,Vic,Walt,Zack,Abe,Bill,Cole,Dave,Ed,Fred,Glen,Hal,Ike,Joe,Ken,Luke,Max,Ned,Otis,Paul,Rob,Seth,Ty,Vern,Wes,Zeke",
    ("s", False): "Amy,Beth,Cara,Dana,Erin,Faye,Gina,Holly,Iris,Jess,Kate,Lena,Mia,Nora,Opal,Pam,Rose,Sara,Tess,Val,Wren,Zoe",
    ("r", True):  "Rook,Scab,Vex,Dusty,Knox,Gutter,Rafe,Brick,Colt,Dregs,Hatch,Jax,Lash,Mace,Pike,Rust,Shiv,Slate,Stitch,Tank,Wolfe,Zane",
    ("r", False): "Ash,Blaze,Crow,Echo,Fang,Grit,Havoc,Jinx,Kestrel,Lynx,Mercy,Nyx,Raven,Riot,Sable,Scar,Siren,Spike,Thorn,Venom,Vixen,Wraith",
}
# Worn armor protects these NPCs, but at this fraction of what it gives a player. The template's own 10 counts too;
# the percentage multiplies the whole rating (EffectManager sums every base first, then applies perc_add). 0.5 puts
# an unarmored NPC at 5%, three medium pieces around 30%, a full top set under 50% - never near-immune.
WORN_ARMOR_SCALE = 0.5

FEMALE_SOUNDS = [("SoundHurt","player2painlg"),("SoundHurtSmall","player2painsm"),("SoundDeath","player2death"),
    ("SoundStamina","player2stamina"),("SoundDrownPain","player2drownpain"),("SoundWaterSurface","player2drownsurface"),
    ("SoundDrownDeath","player2drowndeath"),("SoundJump","player2Jump"),("SoundLandSoft","player2LandSoft"),
    ("SoundLandHard","player2LandHard"),("SoundPlayerLandThump","PlayerLandThump"),("SoundRandom","player2stamina"),
    ("SoundAlert","player2stamina"),("SoundAttack","player2stamina"),("SoundSense","player2stamina")]

def npc_item_name(token, vanilla):
    kind = "melee" if vanilla.startswith("melee") else "gun"
    return kind + "NPCSC" + token          # SC marks them as ours; the framework uses meleeNPC*/gunNPC*

# ------------------------------------------------------------------------------------------------------------
xnpc_classes = ET.parse(os.path.join(XNPC, "entityclasses.xml")).getroot()
xnpc_items = ET.parse(os.path.join(XNPC, "items.xml")).getroot()
van_items = ET.parse(os.path.join(VAN, "items.xml")).getroot()

def one(root, xp, **kw):
    r = root.xpath(xp, **kw)
    return r[0] if r else None

def cls(name): return one(xnpc_classes, "//entity_class[@name=$n]", n=name)
def xitem(name): return one(xnpc_items, "//item[@name=$n]", n=name)
def vitem(name): return one(van_items, "//item[@name=$n]", n=name)

def vprop(item, name, klass=None):
    """A vanilla item property, following vanilla Extends for the value."""
    seen = 0
    while item is not None and seen < 5:
        base = item if klass is None else one(item, "property[@class=$c]", c=klass)
        if base is not None:
            p = one(base, "property[@name=$n]", n=name)
            if p is not None: return p.get("value")
        ext = one(item, "property[@name='Extends']")
        item = vitem(ext.get("value")) if ext is not None else None
        seen += 1
    return None

def class_items_on_enter(template_name):
    """ItemsOnEnterGame of a framework template, walking extends."""
    c = cls(template_name); hops = 0
    while c is not None and hops < 8:
        b = one(c, "property[@class='ItemsOnEnterGame']/property[@name='GameModeSurvival']")
        if b is not None: return b.get("value")
        c = cls(c.get("extends")) if c.get("extends") else None; hops += 1
    return None

problems = []
for token, cat, kind, tier, van, base in WEAPONS:
    for t in ("npcAdvanced%sTemplate" % cat, "npc%sTemplate" % cat):
        if cls(t) is None: problems.append("missing framework template " + t)
    if van:
        if vitem(van) is None: problems.append("missing vanilla item " + van)
        if xitem(base) is None: problems.append("missing framework NPC item " + base)
if problems:
    print("\n".join(problems)); sys.exit(1)

# ------------------------------------------------------------------------------------------------------------
# items
# ------------------------------------------------------------------------------------------------------------
def set_prop(parent, name, value):
    p = one(parent, "property[@name=$n]", n=name)
    if p is None:
        p = ET.SubElement(parent, "property"); p.set("name", name)
    p.set("value", value)

new_items = []
for token, cat, kind, tier, van, base in WEAPONS:
    if not van: continue
    v = vitem(van)
    it = ET.fromstring(ET.tostring(xitem(base)))
    it.tail = None
    name = npc_item_name(token, van)
    it.set("name", name)
    set_prop(it, "CompatibleWeapon", van)
    for pn in ("Meshfile", "Material"):
        val = vprop(v, pn)
        if val: set_prop(it, pn, val)
    set_prop(it, "CustomIcon", vprop(v, "CustomIcon") or van)
    if kind == "melee":
        light, heavy = vprop(v, "Sound_start", "Action0"), vprop(v, "Sound_start", "Action1")
        for aname, snd in (("Action0", light), ("Action1", heavy or light)):
            a = one(it, "property[@class=$c]", c=aname)
            if a is not None and snd: set_prop(a, "Sound_start", snd)
    else:
        fire = vprop(v, "Sound_start", "Action0")
        a = one(it, "property[@class='Action1']")
        if a is not None and fire:
            set_prop(a, "Sound_start", fire)
            if one(a, "property[@name='Sound_loop']") is not None: set_prop(a, "Sound_loop", fire)
    new_items.append((name, van, it))

# ------------------------------------------------------------------------------------------------------------
# entity classes
# ------------------------------------------------------------------------------------------------------------
def P(name, value): return '\t\t\t<property name="%s" value="%s"/>\n' % (name, value)

BODY = (P("ModelType", "SDCS") + P("PhysicsBody", "PlayerSDCS") + P("AvatarController", "AvatarSDCSController")
        + P("RootMotion", "false"))

out = []
out.append('<?xml version="1.0" encoding="UTF-8" ?>\n<configs>\n')
out.append("""\t<!-- GENERATED by Tools/gen_arsenal.py - edit the table there, not this file.

\t     Survivors that wear the PLAYER'S body (ModelType SDCS) and real vanilla armor, one class per weapon and
\t     sex, in two allegiances:
\t       scSurvivor<Sex><Weapon>   friendly, hireable, recruitable into the community (whiteriver)
\t       scRaider<Sex><Weapon>     hostile raiders (bandits faction), the NPC framework's own hostile pattern:
\t                                 EntityNPCBandit with the hostile melee and ranged AI packages
\t     Each weapon has a template holding everything but the sex (scSurvivor<Weapon>Template,
\t     scRaider<Weapon>Template); the SurvivorCommunity DLL skips any class whose name contains "Template".
\t     The rig is generated at runtime by SurvivorCommunity.dll (Patches_Sdcs.cs) from a rolled face plus armor
\t     in the equipment slots. The pack's mesh and its rig-specific hand joint are dropped (ignore=); the
\t     player rig's weapon hand is RightWeapon and vanilla picks it. Tags do not inherit, so every leaf class
\t     restates them. RootMotion is off: the player's animator has none.

\t     Speeds, measured live on this body: about 4.2 m/s per unit, tailing off near 6. MoveSpeed is the roaming
\t     pace. A friendly follower does NOT use MoveSpeedAggro: SCore's EntityAliveSDX derives aggro and panic
\t     from MoveSpeedPanic scaled by the player's own RunSpeed modifiers (0.45 arrived as 1.12). A raider is an
\t     EntityNPCBandit, whose chase speed IS MoveSpeedAggro (RunSpeed-scaled), so theirs is set to a jog you
\t     can outrun.

\t     ScWeapon / ScWeaponTier / ScAllegiance / ScWeaponKind / ScWeaponFamily are read by the DLL (console spawns,
\t     radio picks by gamestage, the second weapon every NPC rolls). Tier 0 early melee plus the pipe guns and the
\t     primitive bow, 1 wooden bow, iron crossbow and double barrel, 2 late guns. -->
\t<append xpath="/entity_classes">
""")

leaf_names = []
for side in ("s", "r"):
    for token, cat, kind, tier, van, base in WEAPONS:
        prefix = "scSurvivor" if side == "s" else "scRaider"
        tmpl = "%s%sTemplate" % (prefix, token)
        parent = ("npcAdvanced%sTemplate" if side == "s" else "npc%sTemplate") % cat
        t = []
        t.append('\n\t\t<entity_class name="%s" extends="%s" ignore="Mesh,RightHandJointName">\n' % (tmpl, parent))
        t.append(P("UserSpawnType", "None"))
        t.append(P("ScWeapon", token) + P("ScWeaponTier", str(tier)) + P("ScAllegiance", "friendly" if side == "s" else "hostile"))
        t.append(P("ScWeaponKind", kind) + P("ScWeaponFamily", family(token, kind)))
        if side == "s":
            t.append(P("Faction", "whiteriver"))
        else:
            t.append(P("Class", "EntityNPCBandit, SCore") + P("Faction", "bandits") + P("IsEnemyEntity", "true")
                     + P("AIPackages", "NPCModCoreNoChat, NPCModHostileMeleeBasic, NPCModHostileRangedBasic")
                     + P("LootDropProb", "0.2"))
        t.append(BODY)
        if side == "s":
            t.append(P("MoveSpeed", "0.35") + P("MoveSpeedAggro", "0.45, 0.45") + P("MoveSpeedPanic", "0.45") + P("MoveSpeedRand", "-0.05, 0.05"))
        else:
            t.append(P("MoveSpeed", "0.35") + P("MoveSpeedAggro", "0.8, 0.9") + P("MoveSpeedPanic", "0.9") + P("MoveSpeedRand", "-0.05, 0.05"))
        if van:
            framework_items = class_items_on_enter("npc%sTemplate" % cat) or ""
            rest = framework_items.split(",")[1:]
            loadout = ",".join([npc_item_name(token, van)] + [x.strip() for x in rest if x.strip()])
            t.append('\t\t\t<property class="ItemsOnEnterGame">\n')
            for gm in ("GameModeSurvival", "GameModeSurvivalSP", "GameModeSurvivalMP"):
                t.append('\t\t\t\t<property name="%s" value="%s"/>\n' % (gm, loadout))
            t.append('\t\t\t</property>\n')
            t.append(P("BagItems", van))
        # merged with, never replacing, the framework's inherited effect groups (a perc_add suppresses nothing)
        t.append('\t\t\t<effect_group name="SurvivorCommunity worn armor scale">\n'
                 '\t\t\t\t<passive_effect name="PhysicalDamageResist" operation="perc_add" value="%s"/>\n'
                 '\t\t\t</effect_group>\n' % ("%g" % (WORN_ARMOR_SCALE - 1.0)))
        t.append('\t\t</entity_class>\n')
        out.append("".join(t))
        for male in (True, False):
            sex = "Male" if male else "Female"
            name = "%s%s%s" % (prefix, sex, token)
            leaf_names.append((name, side, token, tier))
            tags = "entity,%s,npc,walker,%s,%s%s" % ("male" if male else "female", kind, SWAP_TAGS, ",bandit" if side == "r" else "")
            l = ['\t\t<entity_class name="%s" extends="%s">\n' % (name, tmpl)]
            l.append(P("UserSpawnType", "Menu") + P("Tags", tags) + P("IsMale", "true" if male else "false")
                     + P("Names", NAMES[(side, male)]))
            if not male:
                l.append("".join(P(k, v) for k, v in FEMALE_SOUNDS))
            l.append('\t\t</entity_class>\n')
            out.append("".join(l))
out.append("\t</append>\n</configs>\n")
io.open(os.path.join(MOD, "entityclasses.xml"), "w", encoding="utf-8", newline="\n").write("".join(out))

# ------------------------------------------------------------------------------------------------------------
# entity groups + biome spawning: our own layer, which is what the framework's own guide asks add-ons to do
# ------------------------------------------------------------------------------------------------------------
# These two used to be one thing: our leaves were appended into the framework's own groups, sharing the 3 points
# of NPC against 97 points of "none" that its groups carry. That worked out at 0.4% per roll, which is roaming
# for an hour and meeting nobody. They now have their own groups and their own <spawn> entries - "layer on top,
# rather than replace", per the guide at the top of 0-XNPCCore/Config/spawning.xml - so the rate below is ours
# alone and does not move when NPC packs are installed or removed.
#
# What CHANCE actually is (SpawnManagerBiomes.SpawnUpdate + ChunkAreaBiomeSpawnData, 3.2 decompile): the world is
# diced into 80x80 m areas, and an area rolls one <spawn> entry at a time against its entity group. A "none" hit
# burns that entry for that area until respawndelay has passed (DecMaxCount), so each area gives one real roll
# per entry per respawndelay - and CHANCE is exactly the odds of that roll being one of ours. RESPAWN is in game
# days. For scale, the framework's own sample cast rolled about 1-4% per area (by band) before W0 removed its biome
# spawns, so these are now the only wild NPCs.
SURVIVOR_CHANCE, SURVIVOR_RESPAWN = 0.06, 0.5
RAIDER_CHANCE, RAIDER_RESPAWN = 0.04, 1.0
# How each band picks the CLASS weapon. Since W2 every NPC also rolls a second weapon of the other kind in the DLL,
# so this only decides which of the two is the class one. Per band: RANGED is the share of ranged class weapons,
# and the tier weights spread each family over its tiers. FIREARM_SHARE is the share of those ranged class weapons
# that are firearms (the rest are bows and crossbows); keep it equal to SurvivorFirearmChance in Config/blocks.xml,
# the odds the DLL's ranged roll uses, so an NPC's one ranged weapon is a firearm at that rate whether it is the
# class weapon or the rolled one. RocketL counts as a firearm here (it is never rolled as a second weapon).
FIREARM_SHARE = 0.12
MIX = {"GS01": (0.30, {0: 1.0}), "GS50": (0.40, {0: 1.0, 1: 1.0}), "GS100": (0.50, {0: 0.5, 1: 1.0, 2: 1.0})}
FAMILY_GROUP = {"melee": "melee", "unarmed": "melee", "bow": "bow", "firearm": "firearm", "launcher": "firearm"}
TOKEN_GROUP = dict((tok, FAMILY_GROUP[family(tok, kind)]) for tok, cat, kind, tier, van, base in WEAPONS)

def band_weights(side, band):
    """(leaf, weight) for a band: the group shares, each spread over its entries by tier weight."""
    ranged, tiers = MIX[band]
    share = {"melee": 1.0 - ranged, "bow": ranged * (1.0 - FIREARM_SHARE), "firearm": ranged * FIREARM_SHARE}
    entries = [(n, TOKEN_GROUP[tok], tiers[t]) for (n, sd, tok, t) in leaf_names if sd == side and t in tiers]
    out = []
    for grp, sh in share.items():
        mine = [(n, w) for n, g, w in entries if g == grp]
        rel = sum(w for _, w in mine)
        if not mine or rel <= 0: raise SystemExit("band %s has no %s weapons" % (band, grp))
        out.extend((n, sh * w / rel) for n, w in mine)
    order = dict((n, i) for i, (n, _, _) in enumerate(entries))
    out.sort(key=lambda nw: order[nw[0]])                      # the table's order, as before
    return out
SIDES = (("s", "Survivor", SURVIVOR_CHANCE, SURVIVOR_RESPAWN),
         ("r", "Raider", RAIDER_CHANCE, RAIDER_RESPAWN))
BIOMES = ["pine_forest", "burnt_forest", "desert", "snow", "wasteland"]
# one <spawn> per place and time of day, same layout the framework uses: place, time, band, tags, notags
PLACES = [("Wild", "Day",   "GS01",  None,                    "commercial,industrial,downtown"),
          ("Wild", "Night", "GS50",  None,                    "commercial,industrial,downtown"),
          ("Town", "Day",   "GS01",  "commercial,industrial", "downtown"),
          ("Town", "Night", "GS100", "commercial,industrial", "downtown"),
          ("City", "Day",   "GS50",  "downtown",              None),
          ("City", "Night", "GS100", "downtown",              None)]

# ------------------------------------------------------------------------------------------------------------
# W0: no NEW pack NPCs while this mod is installed (the user's call, 2026-09-30). Existing ones stay and keep their
# behaviour; the class definitions are untouched, so saves holding them still load. The framework's sample cast
# (Nurse, Baker, Harley, the NPC fox - and any character pack that inserts next to them) spawns from:
#  * its own biome <spawn> entries (0-XNPCCore/Config/spawning.xml) - removed;
#  * the sleeper groups behind its S_-Group_NPC_* POI volumes (gamestages.xml spawners NPC*Horde). No installed
#    POI uses them today (only its own testonly prefabs), an NPC POI pack would.
# A group cannot just be emptied (EntityGroupsFromXml throws "Empty entity groups not allowed!") and "none" must
# never be entry 0 (see below), so in EVERY framework group (the now-uncalled biome ones too, in case a later mod
# calls them again) each human pack NPC becomes our player-body class with the same framework weapon and the
# group's side, at the same probability split over both sexes. All element children are replaced (<e> and the
# older <entity>), which also drops whatever a character pack inserted there. Groups whose other members are
# vanilla animals only lose the pack NPC. Rerun after a 0-XNPCCore update: the lists come from its files.
# ------------------------------------------------------------------------------------------------------------
CLASS_INDEX = {}                                  # name -> element; the framework's definition wins over vanilla's
for root in (xnpc_classes, ET.parse(os.path.join(VAN, "entityclasses.xml")).getroot()):
    for e in root.iter("entity_class"):
        CLASS_INDEX.setdefault(e.get("name"), e)

def any_class(name):
    return CLASS_INDEX.get(name)

def class_chain(name):
    out = []
    while name and len(out) < 40:
        out.append(name)
        e = any_class(name)
        if e is None: break
        name = e.get("extends")
    return out

def class_prop(name, prop):
    for n in class_chain(name):
        e = any_class(n)
        p = one(e, "property[@name=$p]", p=prop) if e is not None else None
        if p is not None: return p.get("value")
    return None

def is_pack_npc(name):
    """A framework NPC (EntityAliveSDX / EntityNPCBandit) that is not one of ours."""
    if not name or name == "none" or name.startswith(("scSurvivor", "scRaider")): return False
    c = class_prop(name, "Class") or ""
    return "EntityAliveSDX" in c or "EntityNPCBandit" in c

FRAMEWORK_CATS = set(cat for _, cat, _, _, van, _ in WEAPONS if van is None)
BASE_TOKEN = dict((cat, tok) for tok, cat, _, _, van, _ in WEAPONS if van is None)   # AK47 -> Rifle

def framework_cat(name):
    """The framework weapon a pack NPC carries, from its npc[Advanced|Bandit|BM]<Cat>Template ancestor, or None."""
    for n in class_chain(name):
        m = re.match(r"^npc(?:Advanced|Bandit|BM)?(\w+?)Template$", n)
        if m and m.group(1) in FRAMEWORK_CATS: return m.group(1)
    return None

def is_pack_animal(name):
    """An NPC-framework animal (the NPC fox rides npcAnimalMeleeTemplate): dropped, never swapped."""
    return any(re.match(r"^npcAnimal", n) for n in class_chain(name))

def swap_kind(name):
    """'human' (swap for ours), 'animal' (drop), or stop: a human pack NPC with no known framework weapon would
    otherwise lose its share silently."""
    if framework_cat(name): return "human"
    if is_pack_animal(name): return "animal"
    raise SystemExit("W0: pack NPC %s has no framework weapon template (chain %s) - map it by hand" % (name, class_chain(name)))

def xpath_names(names):
    return " or ".join("@name='%s'" % n for n in names)

ours = set(n for n, _, _, _ in leaf_names)
xnpc_spawn_groups = set(e.get("entitygroup") for e in
                        ET.parse(os.path.join(XNPC, "spawning.xml")).getroot().iter("spawn") if e.get("entitygroup"))
w0_spawns, w0_swap, w0_drop = [], {}, {}
for grp in ET.parse(os.path.join(XNPC, "entitygroups.xml")).getroot().iter("entitygroup"):
    gname = grp.get("name")
    kids = [e for e in grp if isinstance(e.tag, str)]
    ents = [(e.get("n") or e.get("name"), float(e.get("p") or e.get("prob") or 1)) for e in kids]
    pack = [n for n, _ in ents if is_pack_npc(n)]
    if not pack: continue
    if gname in xnpc_spawn_groups:                # its biome groups: the <spawn> entries go, and the group is swapped
        w0_spawns.append(gname)
    if (grp.text or "").strip(): raise SystemExit("W0: %s holds flat-text entries - not handled" % gname)
    if any(e.tag != "e" for e in kids): raise SystemExit("W0: %s has non-<e> entries - not handled" % gname)
    humans = [n for n in pack if swap_kind(n) == "human"]
    if not humans:                                # only pack animals (the NPC fox) among other members
        rest = [n for n, _ in ents if n not in pack]
        if not rest or rest[0] == "none": raise SystemExit("W0: dropping %s would leave %s empty or 'none'-first" % (pack, gname))
        w0_drop[gname] = tuple(sorted(set(pack)))
        continue
    # the group's side, not each NPC's: the framework files its one friendly Harley (npcHarleyKnife, an
    # EntityAliveSDX) among the bandits, and a survivor has no place in a bandit camp
    bandits = sum(1 for n in humans if "EntityNPCBandit" in (class_prop(n, "Class") or ""))
    prefix = "scRaider" if bandits * 2 >= len(humans) else "scSurvivor"
    if 0 < bandits < len(humans) and min(bandits, len(humans) - bandits) * 4 > len(humans):
        raise SystemExit("W0: %s mixes %d bandits with %d friendlies - pick its side by hand" % (gname, bandits, len(humans) - bandits))
    new = []                                      # (name, p) in the framework's order, duplicates summed
    for n, p in ents:
        if not is_pack_npc(n):
            subs = [(n, p)]
        elif swap_kind(n) == "animal":
            subs = []                             # a pack animal inside a human group: dropped
        else:
            subs = [("%s%s%s" % (prefix, sex, BASE_TOKEN[framework_cat(n)]), p / 2.0) for sex in ("Male", "Female")]
        for sn, sp in subs:
            if sn != "none" and is_pack_npc(sn): raise SystemExit("W0: %s still holds pack NPC %s" % (gname, sn))
            if sn.startswith(("scSurvivor", "scRaider")) and sn not in ours: raise SystemExit("W0: no class " + sn)
            for i, (en, ep) in enumerate(new):
                if en == sn: new[i] = (en, ep + sp); break
            else:
                new.append((sn, sp))
    if not new or new[0][0] == "none": raise SystemExit("W0: %s would be empty or 'none'-first" % gname)
    w0_swap[gname] = tuple(new)
if not w0_spawns or not w0_swap: raise SystemExit("W0: found no framework pack groups - did 0-XNPCCore change?")
w0_swap_sets = {}                                 # identical replacement lists share one remove + append
for gname in sorted(w0_swap): w0_swap_sets.setdefault(w0_swap[gname], []).append(gname)
w0_drop_sets = {}
for gname in sorted(w0_drop): w0_drop_sets.setdefault(w0_drop[gname], []).append(gname)

g = ['<?xml version="1.0" encoding="UTF-8" ?>\n<configs>\n']
g.append("""\t<!-- GENERATED by Tools/gen_arsenal.py - edit SURVIVOR_CHANCE, RAIDER_CHANCE and MIX there, not this file.

\t     Our own groups, called by our own spawn entries in spawning.xml, so our rate is independent of the NPC
\t     framework's and of whatever character packs are installed. Each group is one roll: "none" is the odds of
\t     that roll producing nobody, and the rest is split over the weapons and both sexes, so a given survivor
\t     is rare but survivors as a whole are not. The class weapon is melee or ranged by a set share per band
\t     (%d%% / %d%% / %d%% ranged), and only %d%% of the ranged ones are firearms - the rest are bows and
\t     crossbows. The early band has the primitive weapons only, the middle adds the wooden bow, iron crossbow
\t     and double barrel, the late one everything. Every NPC also rolls a second weapon of the other kind in the
\t     DLL. "none" must never be the first entry - EntityGroups.IsEnemyGroup reads entry 0 to decide whether the
\t     group is hostile. -->

\t<append xpath="/entitygroups">
""" % (100 * MIX["GS01"][0], 100 * MIX["GS50"][0], 100 * MIX["GS100"][0], 100 * FIREARM_SHARE))
for side, tag, chance, _ in SIDES:
    for band in ("GS01", "GS50", "GS100"):
        entries = band_weights(side, band)
        rel = sum(w for _, w in entries)
        total = 100.0 * chance
        g.append('\n\t\t<!-- %d entries sharing %.2f in 100 -->\n' % (len(entries), total))
        g.append('\t\t<entitygroup name="sc%sGroup%s">\n' % (tag, band))
        for n, w in entries:
            g.append('\t\t\t<e n="%s" p="%.5f"/>\n' % (n, total * w / rel))
        g.append('\t\t\t<e n="none" p="%.2f"/>\n' % (100.0 - total))
        g.append('\t\t</entitygroup>\n')
g.append("\t</append>\n")
g.append("""
\t<!-- W0: no new pack NPCs while SurvivorCommunity is installed (Nurse, Baker, Harley, the NPC fox, and whatever a
\t     character pack inserts next to them). These are the framework's own groups: the sleeper groups behind its
\t     S_-Group_NPC_* POI volumes, and its biome groups (their spawn entries are removed in spawning.xml). A group
\t     may not be empty (the game throws at load) and "none" must not come first, so each pack NPC becomes our
\t     player-body class with the same framework weapon and the group's side, at the same probability split over
\t     both sexes. Groups that also hold vanilla animals only lose the pack NPC. %d groups swapped, %d trimmed. -->
""" % (len(w0_swap), len(w0_drop)))
for entries, names in sorted(w0_swap_sets.items(), key=lambda kv: kv[1][0]):
    sel = "/entitygroups/entitygroup[%s]" % xpath_names(names)
    g.append('\n\t<remove xpath="%s/*"/>\n' % sel)            # <e> and the older <entity> rows alike
    g.append('\t<append xpath="%s">\n' % sel)
    for n, p in entries:
        g.append('\t\t<e n="%s" p="%s"/>\n' % (n, "%g" % round(p, 6)))
    g.append('\t</append>\n')
for packs, names in sorted(w0_drop_sets.items(), key=lambda kv: kv[1][0]):
    g.append('\n\t<remove xpath="/entitygroups/entitygroup[%s]/e[%s]"/>\n' % (
        xpath_names(names), " or ".join("@n='%s'" % n for n in packs)))
g.append("</configs>\n")
io.open(os.path.join(MOD, "entitygroups.xml"), "w", encoding="utf-8", newline="\n").write("".join(g))

s = ['<?xml version="1.0" encoding="UTF-8" ?>\n<configs>\n']
s.append("""\t<!-- GENERATED by Tools/gen_arsenal.py - edit SURVIVOR_RESPAWN, RAIDER_RESPAWN and PLACES there.

\t     Where our people turn up, mirroring the framework's own layout: wilderness carries the early band by day
\t     and the middle band at night, towns the early band by day and the late one at night, city centres the
\t     middle band by day and the late one at night. maxcount is 1 because two at a time visibly floods an area,
\t     and respawndelay is in game days. Every id must be unique within its biome - the game hashes it for the
\t     per-area spawn counter and throws on a collision. -->
""")
for biome in BIOMES:
    s.append('\n\t<append xpath="/spawning/biome[@name=\'%s\']">\n' % biome)
    for side, tag, _, delay in SIDES:
        for place, when, band, tags, notags in PLACES:
            a = 'id="sc%s%s%s" maxcount="1" respawndelay="%g" time="%s" entitygroup="sc%sGroup%s"' % (
                tag, place, when, delay, when, tag, band)
            if tags: a += ' tags="%s"' % tags
            if notags: a += ' notags="%s"' % notags
            s.append('\t\t<spawn %s/>\n' % a)
    s.append('\t</append>\n')
s.append("""
\t<!-- W0: no new pack NPCs while SurvivorCommunity is installed. The NPC framework's own biome spawns call these
\t     groups of its sample cast; removing the entries leaves our spawns above as the only wild NPCs. -->
\t<remove xpath="/spawning/biome/spawn[%s]"/>
""" % " or ".join("@entitygroup='%s'" % n for n in sorted(w0_spawns)))
s.append("</configs>\n")
io.open(os.path.join(MOD, "spawning.xml"), "w", encoding="utf-8", newline="\n").write("".join(s))

# ------------------------------------------------------------------------------------------------------------
# items.xml: a marked block, replaced on every run
# ------------------------------------------------------------------------------------------------------------
BEGIN, END = "<!-- BEGIN GENERATED: NPC weapons (Tools/gen_arsenal.py) -->", "<!-- END GENERATED: NPC weapons -->"
ip = os.path.join(MOD, "items.xml")
src = io.open(ip, encoding="utf-8").read()
src = re.sub(r"\n?\t*" + re.escape(BEGIN) + r".*?" + re.escape(END) + r"\n?", "\n", src, flags=re.S)
block = ["\t\t" + BEGIN + "\n",
         "\t\t<!-- NPC versions of vanilla weapons the NPC framework has none of. Each is a full copy of the framework's\n"
         "\t\t     closest NPC weapon (effect groups do not inherit through Extends) with the vanilla weapon's model,\n"
         "\t\t     material, icon and sounds, and CompatibleWeapon pointing at it - which is what lets a player-body\n"
         "\t\t     survivor animate it with the vanilla weapon's own hold type. Damage and timing are the base weapon's. -->\n"]
for name, van, it in new_items:
    txt = ET.tostring(it, encoding="unicode")
    block.append("\t\t<!-- %s -->\n\t\t%s\n" % (van, txt.strip()))
block.append("\t\t" + END + "\n")
last = src.rfind("</append>")
src = src[:last] + "".join(block) + "\t" + src[last:]
io.open(ip, "w", encoding="utf-8", newline="\n").write(src)

# ------------------------------------------------------------------------------------------------------------
# Localization.csv: rows for every leaf class and every new item, managed by key
# ------------------------------------------------------------------------------------------------------------
lp = os.path.join(MOD, "Localization.csv")
raw = io.open(lp, encoding="utf-8").read()
nl = "\r\n" if "\r\n" in raw else "\n"
lines = raw.split(nl)
managed = set(n for n, _, _, _ in leaf_names) | set(n for n, _, _ in new_items)
kept = [ln for ln in lines if ln.split(",", 1)[0] not in managed]
while kept and kept[-1] == "": kept.pop()
van_loc = {}
with io.open(os.path.join(VAN, "Localization.csv"), encoding="utf-8") as f:
    rd = csv.reader(f)
    hdr = next(rd); ei = hdr.index("english")
    for row in rd:
        if row and len(row) > ei: van_loc[row[0]] = row[ei]
rows = []
for n, side, tok, t in leaf_names:
    rows.append("%s,entityclasses,Entity,,,,%s" % (n, "Survivor" if side == "s" else "Raider"))
for n, van, _ in new_items:
    label = van_loc.get(van, van).replace('"', "'")
    rows.append('%s,items,Item,,,,"%s"' % (n, label))
io.open(lp, "w", encoding="utf-8", newline="").write(nl.join(kept + rows) + nl)

print("classes: %d leaves (%d friendly, %d hostile) + %d templates" % (
    len(leaf_names), sum(1 for x in leaf_names if x[1] == "s"), sum(1 for x in leaf_names if x[1] == "r"), 2 * len(WEAPONS)))
print("new NPC weapons: %d" % len(new_items))
for n, van, it in new_items:
    print("   %-28s <- %-36s mesh=%s" % (n, van, (one(it, "property[@name='Meshfile']").get("value"))[-48:]))
