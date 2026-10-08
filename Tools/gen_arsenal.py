# Generates SurvivorCommunity's raiders on 0-XNPCCore's player body (M10: scRaider{Male,Female}T{1,2,3}, hostile
# EntityAliveSDXV4 classes on XNPC's npcSdcsBase), their spawning and the spawning of the community's survivors -
# 0-XNPCCore's own npcSdcsSurvivor classes, which the SurvivorCommunity DLL takes over (XNPCCore migration; decisions
# D10 and D11: SC no longer defines survivor classes of its own) - plus the vanilla weapons the DLL arms both with.
# One table below is the single source of truth; rerunning is safe.
#
# Writes:
#   Config/entityclasses.xml   (whole file)   RAIDER_CLASSES: two templates + scRaider{Male,Female}T{1,2,3} (M10)
#                                             + XNPC_SURVIVOR_OPS: the hand-written ops that make 0-XNPCCore's own
#                                             survivors ours (XNPCCore migration M3), emitted after the classes
#   Config/entitygroups.xml    (whole file)   our own survivor groups (0-XNPCCore's survivors by band) and raider
#                                             groups (SC's raider classes by band); and the pack NPCs swapped out of the
#                                             framework's groups (W0)
#   Config/spawning.xml        (whole file)   our own biome spawn entries that call those groups; and the
#                                             framework's own NPC biome spawns removed (W0, D10)
#   Config/items.xml           (strip only)   nothing generated any more: the NPC weapon items of the OLD raiders
#                                             (meleeNPCSC*/gunNPCSC*, for scRaider<Sex><Weapon>) went in M10 round 2; a
#                                             run removes their old marked block if a file still has it
#   Config/blocks.xml          (marked block) XnpcWeapons: the vanilla weapons the DLL arms survivors and raiders with
#   Config/Localization.csv    (managed rows) the raider class names (the old scSurvivor, scRaider<Sex><Weapon> and
#                                             NPCSC item rows are dropped)
#
# Facts this relies on (all checked in the 3.2 decompile or the installed XML):
#  * entity classes: effect groups inherit down the extends chain (MinEffectController); ItemsOnEnterGame is a class
#    block, merged per key; Tags are restated on every leaf (whether they inherit or not, that is right)
#  * a hostile EntityAliveSDXV4 is made by class properties alone (DESIGN_M10_RAIDERS.md 1.2): Faction bandits,
#    IsEnemyEntity true, Hirable false, and AI packages that can fire a vanilla gun (XNPC's SdcsNpcRangedAdvanced)
import io, os, re, sys
from lxml import etree as ET

GAME = r"F:\SteamLibrary\steamapps\common\7 Days To Die"
# SC_GEN_CONFIG points the output at a staged COPY of Config (items.xml and Localization.csv are read back too)
MOD = os.environ.get("SC_GEN_CONFIG") or os.path.join(GAME, r"Mods\SurvivorCommunity\Config")   # blocks.xml too
# SC_GEN_XNPC reads the framework's Config from somewhere else (e.g. a new 0-XNPCCore download before installing it)
XNPC = os.environ.get("SC_GEN_XNPC") or os.path.join(GAME, r"Mods\0-XNPCCore\Config")
VAN = os.path.join(GAME, r"Data\Config")

# ------------------------------------------------------------------------------------------------------------
# The table.  token: the weapon's short name (the console accepts it).  cat: the framework weapon it maps to.
# tier: 0 early (melee, and the primitive ranged weapons - pipe guns and the primitive bow - which everyone may
# carry from day 1), 1 middle (wooden bow, iron crossbow, double barrel), 2 late.  vanilla: the vanilla item, for a
# weapon the framework has no NPC version of; None when the framework's NPC item names it (its CompatibleWeapon).
# The community's 0-XNPCCore survivors and, since M10, SC's raiders carry that vanilla weapon itself with the token's
# kind, family and tier (XNPC_WEAPONS). The framework templates (cat) are still how a pack NPC's weapon is recognised
# for W0, and its tier picks the class W0 swaps in. (Until M10 round 2 a sixth column named the framework NPC item the
# old raiders' NPC copy of each vanilla weapon was cloned from; those NPCSC items are gone.)
# ------------------------------------------------------------------------------------------------------------
WEAPONS = [
    # token              cat           kind      tier  vanilla item
    ("EmptyHand",        "EmptyHand",  "melee",  0,    None),
    ("Club",             "Club",       "melee",  0,    None),   # legacy name
    ("Knife",            "Knife",      "melee",  0,    None),
    ("Bat",              "Bat",        "melee",  0,    None),
    ("Axe",              "Axe",        "melee",  0,    None),
    ("Spear",            "Spear",      "melee",  0,    None),
    ("Machete",          "Machete",    "melee",  0,    None),
    ("PipePistol",       "PipePistol", "ranged", 0,    None),
    ("PipeRifle",        "PipeRifle",  "ranged", 0,    None),
    ("PipeShotgun",      "PipeShotgun","ranged", 0,    None),
    ("PipeMG",           "PipeMG",     "ranged", 0,    None),
    ("LBow",             "LBow",       "ranged", 1,    None),
    ("XBow",             "XBow",       "ranged", 1,    None),
    ("Pistol",           "Pistol",     "ranged", 2,    None),
    ("DPistol",          "DPistol",    "ranged", 2,    None),
    ("SMG",              "SMG",        "ranged", 2,    None),
    ("Rifle",            "AK47",       "ranged", 2,    None),   # legacy name, an AK-47
    ("TRifle",           "TRifle",     "ranged", 2,    None),
    ("HRifle",           "HRifle",     "ranged", 2,    None),
    ("SRifle",           "SRifle",     "ranged", 2,    None),
    ("PShotgun",         "PShotgun",   "ranged", 2,    None),
    ("AShotgun",         "AShotgun",   "ranged", 2,    None),
    ("M60",              "M60",        "ranged", 2,    None),
    ("RocketL",          "RocketL",    "ranged", 2,    None),
    # vanilla weapons the framework has no NPC version of
    ("BoneKnife",        "Knife",      "melee",  0,    "meleeWpnBladeT0BoneKnife"),
    ("LeatherKnuckles",  "Knife",      "melee",  0,    "meleeWpnKnucklesT0LeatherKnuckles"),
    ("IronKnuckles",     "Knife",      "melee",  0,    "meleeWpnKnucklesT1IronKnuckles"),
    ("SteelKnuckles",    "Knife",      "melee",  2,    "meleeWpnKnucklesT3SteelKnuckles"),
    ("PipeBaton",        "Club",       "melee",  0,    "meleeWpnBatonT0PipeBaton"),
    ("StunBaton",        "Club",       "melee",  1,    "meleeWpnBatonT2StunBaton"),
    ("SteelClub",        "Club",       "melee",  2,    "meleeWpnClubT3SteelClub"),
    ("StoneSledge",      "Club",       "melee",  0,    "meleeWpnSledgeT0StoneSledgehammer"),
    ("IronSledge",       "Club",       "melee",  0,    "meleeWpnSledgeT1IronSledgehammer"),
    ("SteelSledge",      "Club",       "melee",  2,    "meleeWpnSledgeT3SteelSledgehammer"),
    ("StoneSpear",       "Spear",      "melee",  0,    "meleeWpnSpearT0StoneSpear"),
    ("SteelSpear",       "Spear",      "melee",  2,    "meleeWpnSpearT3SteelSpear"),
    ("SteelAxe",         "Axe",        "melee",  1,    "meleeToolAxeT2SteelAxe"),
    ("Magnum",           "Pistol",     "ranged", 2,    "gunHandgunT2Magnum44"),
    ("DoubleBarrel",     "PShotgun",   "ranged", 1,    "gunShotgunT1DoubleBarrel"),
    ("LeverAction",      "HRifle",     "ranged", 2,    "gunRifleT2LeverActionRifle"),
    ("PrimitiveBow",     "LBow",       "ranged", 0,    "gunBowT0PrimitiveBow"),
    ("CompoundBow",      "LBow",       "ranged", 2,    "gunBowT3CompoundBow"),
    ("CompoundCrossbow", "XBow",       "ranged", 2,    "gunBowT3CompoundCrossbow"),
]

# The family of a weapon (the blocks.xml XnpcWeapons value the DLL's roll reads): melee, bow (bows and crossbows) or
# firearm. Firearms are rare (the user's call, 2026-09-30): the ranged roll picks a bow or crossbow unless
# SurvivorFirearmChance (a survivor) or RaiderFirearmChance (a raider) comes up. EmptyHand and RocketL are never
# rolled (left out of XnpcWeapons below).
BOWS = {"PrimitiveBow", "LBow", "XBow", "CompoundBow", "CompoundCrossbow"}

def family(token, kind):
    if token == "EmptyHand": return "unarmed"
    if kind == "melee": return "melee"
    if token in BOWS: return "bow"
    if token == "RocketL": return "launcher"
    return "firearm"

# Name pools: ("s", male) for the community's survivors (written onto 0-XNPCCore's survivor classes below), ("r", male)
# for the raiders.
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

# 0-XNPCCore 3.2's own player-body survivors that SurvivorCommunity takes over (XNPCCore migration M3, decisions D2
# and D7). Keep in step with ScXnpc.DefaultClasses in the DLL and XnpcSurvivorClasses in Config/blocks.xml.
XNPC_SURVIVORS = ["npcSdcsSurvivorMaleT1", "npcSdcsSurvivorMaleT2", "npcSdcsSurvivorMaleT3",
                  "npcSdcsSurvivorFemaleT1", "npcSdcsSurvivorFemaleT2", "npcSdcsSurvivorFemaleT3"]

# Hand-written patch ops for those six, emitted verbatim after the generated classes (this script rewrites the whole
# entityclasses.xml, and the game only reads Config/entityclasses.xml, so they live here). The %(...)s fields are
# filled from the tables in this file - the class selectors, the armor scale and the survivor name pools - so the
# XNPC survivors always get SurvivorCommunity's armor scale and names. XML comments may not contain two dashes.
XNPC_SURVIVOR_OPS = """
\t<!-- HAND-WRITTEN BLOCK, emitted verbatim by Tools/gen_arsenal.py (XNPC_SURVIVOR_OPS): edit it there, not here.

\t     0-XNPCCore 3.2's own survivors, npcSdcsSurvivor{Male,Female}T{1,2,3}, as SurvivorCommunity's survivors (XNPCCore
\t     migration M3; the user's decisions D2 and D7). The DLL owns the same six classes (ScXnpc, XnpcSurvivorClasses in
\t     blocks.xml) and dresses, names, heals and arms them; these ops take XNPC's own versions of that away. Their
\t     ItemsOnEnterGame is not edited here: the DLL rewrites each one's list to its own rolled pair of vanilla weapons
\t     in PostInit (ScLoadout, from the XnpcWeapons list in blocks.xml).

\t     XNPC's look applier (SdcsLookApplier) gives each of them a preset archetype, a whole vanilla armor set at full
\t     protection, a name from its pool and a rolled weapon with 60 rounds. Its only trigger is this effect group
\t     (onSelfFirstSpawn, which fires on every world entry), so without it the look is SurvivorCommunity's: the DLL
\t     builds the body from a rolled or remembered SC look (Patches_Xnpc, createModel). -->
\t<remove xpath="/entity_classes/entity_class[%(sel)s]/effect_group[@name='SDCS Look']"/>

\t<!-- Worn vanilla armor protects them at SurvivorCommunity's fraction, the same group the scRaider templates
\t     above carry (merged with the inherited effect groups; a perc_add suppresses nothing). -->
\t<append xpath="/entity_classes/entity_class[%(sel)s]">
\t\t<effect_group name="SurvivorCommunity worn armor scale">
\t\t\t<passive_effect name="PhysicalDamageResist" operation="perc_add" value="%(armor)s"/>
\t\t</effect_group>
\t</append>

\t<!-- No preset weapons in the bag. The property itself stays: SCore's EntityAliveSDXV4 builds the NPC's bag only when
\t     the class has one (SetupBagItems), and SCore reloads an NPC's gun from its inventory window only when it has a
\t     bag (ItemActionRangedHarvestAmmo). "none" is no item, so the bag is built empty. -->
\t<set xpath="/entity_classes/entity_class[%(sel)s]/property[@name='BagItems']/@value">none</set>

\t<!-- No preset medical stock: what they carry is SurvivorCommunity's roll (ScMedic: a few first aid bandages, now and
\t     then a kit), which they use with the bandage animation. -->
\t<set xpath="/entity_classes/entity_class[%(sel)s]/property[@name='HarvestItems']/@value">none</set>

\t<!-- SurvivorCommunity's survivor name pools (gen_arsenal.py NAMES), one per sex. EntityAliveSDXV4 picks a
\t     random one when the entity is made; a known survivor's body then gets the registry name (ScSpawn). -->
\t<set xpath="/entity_classes/entity_class[%(male)s]/property[@name='Names']/@value">%(male_names)s</set>
\t<set xpath="/entity_classes/entity_class[%(female)s]/property[@name='Names']/@value">%(female_names)s</set>
"""

def xnpc_survivor_ops():
    sel = lambda names: " or ".join("@name='%s'" % n for n in names)
    male = [n for n in XNPC_SURVIVORS if "Female" not in n]
    female = [n for n in XNPC_SURVIVORS if "Female" in n]
    if not male or not female: raise SystemExit("XNPC_SURVIVORS needs both sexes")
    return XNPC_SURVIVOR_OPS % {"sel": sel(XNPC_SURVIVORS), "male": sel(male), "female": sel(female),
                                "armor": "%g" % (WORN_ARMOR_SCALE - 1.0),
                                "male_names": NAMES[("s", True)], "female_names": NAMES[("s", False)]}

# M10 (DESIGN_M10_RAIDERS.md 2.1): SurvivorCommunity's raiders, hostile EntityAliveSDXV4 classes on 0-XNPCCore's player
# body. Keep in step with ScXnpc.DefaultRaiderClasses in the DLL and XnpcRaiderClasses in Config/blocks.xml. The tier
# (T1..T3) is the band the class stands for: it caps the weapons the DLL rolls (with the gamestage) and picks the
# raider's chance of leading with its ranged weapon (RaiderRangedShare; every raider carries both).
RAIDERS = ["scRaiderMaleT1", "scRaiderMaleT2", "scRaiderMaleT3",
           "scRaiderFemaleT1", "scRaiderFemaleT2", "scRaiderFemaleT3"]
# The XNPC bases they extend (one per sex: the female base carries the female mass, tags and sound bank) and the vanilla
# archetype each leaf names: EModelSdcsNpc.Init builds no model when the class archetype is missing, and the DLL swaps
# it for SC's own look before the first build anyway (Patches_Xnpc X1).
RAIDER_BASE = {True: "npcSdcsBase", False: "npcSdcsBaseFemale"}
RAIDER_TEMPLATE = {True: "scRaiderTemplate", False: "scRaiderFemaleTemplate"}
RAIDER_ARCHETYPE = {True: "Clark", False: "Anne"}
# npc and human make SCore's IsHuman true (its door opener), bandit matches SCore's HumanTags / faction tags, sdcsnpc is
# XNPC's own body tag, and no_voice keeps Voice Control's chat component (barks, hiring, its door flag) off them.
RAIDER_TAGS = "entity,%s,npc,human,bandit,sdcsnpc,no_voice"
# M10 round 3: the old raiders' map markers (XNPC's npcBandit templates; vanilla's zombies carry the same three). The DLL
# registers them for a raider body (Patches_Xnpc X11): SCore's V4 only ever registers a companion's marker. clear_sleeper
# is the red dot a sleeper shows inside a clear quest's bounds. Every name must be a vanilla nav_object_class.
RAIDER_NAV_OBJECT = "twitch_spawn,twitch_spawn_other,clear_sleeper"
# M10 round 3 (DESIGN_M10_RAIDERS.md 2.9): kill XP from the pair. The class's ExperienceGain (600, the old melee raider's)
# is raised by this cvar, which the DLL writes from the raider's ranged weapon (ScLoadout.RaiderXpCvar, SetRaiderXp:
# bow +33/+67%, firearm +50/+100/+133% by tier); vanilla evaluates the passive on the killed raider (EntityPlayer.AddKillXP).
RAIDER_XP_CVAR = "sc_raiderXp"

# The template, emitted once per sex. XML comments may not contain two dashes. Speeds are LITERAL m/s on a V4 body;
# the chase (run) uses MoveSpeedPanic, and XNPC runs at x1.6 above 2.75 (DESIGN_M10_RAIDERS.md 2.11, probe P9).
RAIDER_TEMPLATE_XML = """
\t\t<entity_class name="%(name)s" extends="%(base)s">
\t\t\t<property name="UserSpawnType" value="None"/>
\t\t\t<property name="Faction" value="bandits"/>
\t\t\t<property name="IsEnemyEntity" value="true"/>
\t\t\t<property name="Hirable" value="false"/>
\t\t\t<property name="IsQuestGiver" value="false"/>
\t\t\t<property name="AIPackages" value="NPCModCoreNoChat, SdcsNpcRangedAdvanced, SCRaider"/>
\t\t\t<property name="MoveSpeed" value="1.6"/>
\t\t\t<property name="MoveSpeedAggro" value="2.9, 3.1"/>
\t\t\t<property name="MoveSpeedPanic" value="2.9"/>
\t\t\t<property name="BagItems" value="none"/>
\t\t\t<property name="HarvestItems" value="none"/>
\t\t\t<property name="ExperienceGain" value="600"/>
\t\t\t<property name="Titles" value="Raider"/>
\t\t\t<property name="NavObject" value="%(nav)s"/>
\t\t\t<property class="ItemsOnEnterGame">
\t\t\t\t<property name="GameModeSurvival" value="meleeWpnClubT0WoodenClub"/>
\t\t\t\t<property name="GameModeSurvivalSP" value="meleeWpnClubT0WoodenClub"/>
\t\t\t\t<property name="GameModeSurvivalMP" value="meleeWpnClubT0WoodenClub"/>
\t\t\t</property>
\t\t\t<effect_group name="SurvivorCommunity worn armor scale">
\t\t\t\t<passive_effect name="PhysicalDamageResist" operation="perc_add" value="%(armor)s"/>
\t\t\t</effect_group>
\t\t\t<effect_group name="SurvivorCommunity raider">
\t\t\t\t<passive_effect name="LandMineImmunity" operation="base_set" value="0"/>
\t\t\t\t<passive_effect name="ExperienceGain" operation="perc_add" value="@%(xp)s"/>
\t\t\t</effect_group>
\t\t</entity_class>
"""
RAIDER_LEAF_XML = """\t\t<entity_class name="%(name)s" extends="%(template)s">
\t\t\t<property name="UserSpawnType" value="Menu"/>
\t\t\t<property name="Archetype" value="%(archetype)s"/>
\t\t\t<property name="IsMale" value="%(male)s"/>
\t\t\t<property name="Tags" value="%(tags)s"/>
\t\t\t<property name="Names" value="%(names)s"/>
\t\t</entity_class>
"""

def raider(male, tier):
    n = "scRaider%sT%d" % ("Male" if male else "Female", tier)
    if n not in RAIDERS: raise SystemExit("no raider class %s in RAIDERS" % n)
    return n

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
for token, cat, kind, tier, van in WEAPONS:
    for t in ("npcAdvanced%sTemplate" % cat, "npc%sTemplate" % cat):
        if cls(t) is None: problems.append("missing framework template " + t)
    if van and vitem(van) is None: problems.append("missing vanilla item " + van)
if problems:
    print("\n".join(problems)); sys.exit(1)

# ------------------------------------------------------------------------------------------------------------
# entity classes (M10): two raider templates (one per sex) and the six leaves, then the survivor ops
# ------------------------------------------------------------------------------------------------------------
for male in (True, False):                        # the bases and archetypes the classes name must exist
    if cls(RAIDER_BASE[male]) is None: raise SystemExit("raider base %s is not in 0-XNPCCore's entityclasses.xml" % RAIDER_BASE[male])
van_arch = ET.parse(os.path.join(VAN, "archetypes.xml")).getroot()
for male in (True, False):
    if not van_arch.xpath("//archetype[@name=$n]", n=RAIDER_ARCHETYPE[male]):
        raise SystemExit("raider archetype %s is not in vanilla archetypes.xml" % RAIDER_ARCHETYPE[male])
van_nav = ET.parse(os.path.join(VAN, "nav_objects.xml")).getroot()
for nav in RAIDER_NAV_OBJECT.split(","):         # NavObject.SetupNavObjectClass skips an unknown name silently
    if not van_nav.xpath("//nav_object_class[@name=$n]", n=nav):
        raise SystemExit("raider NavObject %s is not a vanilla nav_object_class" % nav)

out = []
out.append('<?xml version="1.0" encoding="UTF-8" ?>\n<configs>\n')
out.append("""\t<!-- GENERATED by Tools/gen_arsenal.py - edit RAIDERS and RAIDER_TEMPLATE_XML there, not this file.

\t     SurvivorCommunity's raiders (M10, DESIGN_M10_RAIDERS.md): hostile classes on 0-XNPCCore's player body, the body
\t     the community's survivors use. Each sex has a template on XNPC's base (npcSdcsBase, npcSdcsBaseFemale: Class
\t     EntityAliveSDXV4, the player body, NPCID npcSdcsTest) holding everything but the leaf's name, archetype, sex,
\t     tags and names; the leaves scRaider{Male,Female}T{1,2,3} are what spawns. The tier is the band the class stands
\t     for (T1 early, T2 middle, T3 late), as for the survivors.
\t     Hostile by class properties alone: Faction bandits (they hate players, survivors and zombies, never each other),
\t     IsEnemyEntity true (enemy spawn rules, EnemyCount, headshot rules, killall), Hirable and IsQuestGiver false. AI:
\t     XNPC's NPCModCoreNoChat (idle, wander) and SdcsNpcRangedAdvanced (the package that fires a vanilla gun on action 0
\t     and melees on class 0), plus SC's SCRaider (Config/utilityai.xml: a ranged approach and investigating noise).
\t     The SurvivorCommunity DLL owns the rest (ScXnpc's raider set, XnpcRaiderClasses in blocks.xml): SC's looks with
\t     raider gear, a pair of vanilla weapons rolled per raider and which one it leads with, a finite stack of ammo in the bag, no
\t     XNPC kill levels, items only healing, and a backpack on death. Their ItemsOnEnterGame is a wooden club only so
\t     that nothing hands them XNPC's endlessly refilled 9 mm if the DLL cannot arm them; the DLL rewrites it.
\t     no_voice keeps Voice Control off them; npc and human give them SCore's door opener (unlocked doors).
\t     BagItems none builds an empty bag (their ammo and meds live there); HarvestItems none gives them no inventory
\t     window. LandMineImmunity 0 undoes npcSdcsBase's 1. Speeds are literal m/s on this body.
\t     NavObject: the old raiders' map markers (the red dot of a sleeper inside a clear quest); the DLL registers them
\t     for a raider body, which SCore's EntityAliveSDXV4 does only for a companion. Kill XP: ExperienceGain 600 (the old
\t     melee raider's), raised by the cvar %s the DLL writes from the pair's ranged weapon: a bow
\t     798 or 1002, a firearm 900, 1200 or 1398 by tier. -->
\t<append xpath="/entity_classes">
""" % RAIDER_XP_CVAR)
for male in (True, False):
    out.append(RAIDER_TEMPLATE_XML % {"name": RAIDER_TEMPLATE[male], "base": RAIDER_BASE[male], "armor": "%g" % (WORN_ARMOR_SCALE - 1.0),
                                      "nav": RAIDER_NAV_OBJECT, "xp": RAIDER_XP_CVAR})
    for n in RAIDERS:
        if ("Female" not in n) != male: continue
        out.append(RAIDER_LEAF_XML % {"name": n, "template": RAIDER_TEMPLATE[male], "archetype": RAIDER_ARCHETYPE[male],
                                      "male": "true" if male else "false", "tags": RAIDER_TAGS % ("male" if male else "female"),
                                      "names": NAMES[("r", male)]})
out.append("\t</append>\n")
for n in XNPC_SURVIVORS:                          # the ops below would silently match nothing for a renamed class
    if cls(n) is None: raise SystemExit("XNPC survivor class %s is not in 0-XNPCCore's entityclasses.xml - update XNPC_SURVIVORS" % n)
out.append(xnpc_survivor_ops())
out.append("</configs>\n")
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
# spawns, so these are now the only wild NPCs. SURVIVOR_CHANCE and its spawn entries are SC's tuned rates, and since
# the XNPCCore migration they spawn 0-XNPCCore's survivors (decision D10).
SURVIVOR_CHANCE, SURVIVOR_RESPAWN = 0.06, 0.5
RAIDER_CHANCE, RAIDER_RESPAWN = 0.04, 1.0
# The survivors' groups (D10): each band holds the 0-XNPCCore survivor class of its tier, both sexes alike - T1 early,
# T2 middle, T3 late. The class's tier caps the weapons the DLL rolls for it, together with the gamestage (ScLoadout
# TierCap), so the place and time of day still decide how well armed a survivor can be. The raiders' groups (M10) are
# the same shape with SC's raider classes: no weapon mix here any more - the DLL rolls each raider's full pair (its
# ranged weapon a firearm at RaiderFirearmChance, 12%) and its lead: the ranged weapon at RaiderRangedShare by class
# tier (the old bands' 30/40/50%), else the melee weapon (Config/blocks.xml).
BAND_TIER = {"GS01": 1, "GS50": 2, "GS100": 3}
SURVIVOR_BAND_TIER = BAND_TIER

def xnpc_survivor(male, tier):
    n = "npcSdcsSurvivor%sT%d" % ("Male" if male else "Female", tier)
    if n not in XNPC_SURVIVORS: raise SystemExit("no 0-XNPCCore survivor class %s in XNPC_SURVIVORS" % n)
    return n

def survivor_band(band):
    """(class, weight) for a survivor band: its tier's two classes, one per sex, at equal weight."""
    return [(xnpc_survivor(True, BAND_TIER[band]), 0.5), (xnpc_survivor(False, BAND_TIER[band]), 0.5)]

def raider_band(band):
    """(class, weight) for a raider band: its tier's two raider classes, one per sex, at equal weight."""
    return [(raider(True, BAND_TIER[band]), 0.5), (raider(False, BAND_TIER[band]), 0.5)]

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
#
# 0-XNPCCore 3.2's own player-body survivors, npcSdcsSurvivor{Male,Female}T1-T3 (EntityAliveSDXV4, model
# EModelSdcsNpc), are the community's survivors since the XNPCCore migration (XNPC_SURVIVORS): ours, not pack NPCs.
# Its sleeper groups that list them keep them as they are. A human pack NPC becomes the class of the tier its framework
# weapon has (tier 0 -> T1, 1 -> T2, 2 -> T3), both sexes: in a friendly group the survivor class, in a hostile group
# SC's raider class (M10-D9; the raider's own weapons are then rolled by the DLL at that tier's cap). Its own biome
# spawns of them stay removed (D10): ours call them.
# Any other EntityAliveSDXV4 class in a group (none today) stops the generator, to be mapped by hand.
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

def is_ours(name):
    """One of the community's classes: our raiders, or the 0-XNPCCore survivors the community owns."""
    return bool(name) and (name in RAIDERS or name in XNPC_SURVIVORS)

def is_pack_npc(name):
    """A framework NPC (EntityAliveSDX / EntityAliveSDXV4 / EntityNPCBandit) that is not one of ours."""
    if not name or name == "none" or is_ours(name): return False
    c = class_prop(name, "Class") or ""
    return "EntityAliveSDX" in c or "EntityNPCBandit" in c

FRAMEWORK_CATS = set(cat for _, cat, _, _, van in WEAPONS if van is None)
BASE_TOKEN = dict((cat, tok) for tok, cat, _, _, van in WEAPONS if van is None)   # AK47 -> Rifle

def framework_cat(name):
    """The framework weapon a pack NPC carries, from its npc[Advanced|Bandit|BM]<Cat>Template ancestor, or None."""
    for n in class_chain(name):
        m = re.match(r"^npc(?:Advanced|Bandit|BM)?(\w+?)Template$", n)
        if m and m.group(1) in FRAMEWORK_CATS: return m.group(1)
    return None

TOKEN_TIER = dict((tok, tier) for tok, cat, kind, tier, van in WEAPONS)

def is_v4_npc(name):
    """An EntityAliveSDXV4 class of 0-XNPCCore that is not one of the six survivors we own (npcSdcsTest, npcSdcsMaleM60,
    ...). Our raiders are SC's classes, not in CLASS_INDEX, so they never answer here (and is_pack_npc skips them first)."""
    return (class_prop(name, "Class") or "").startswith("EntityAliveSDXV4")

def fmt_p(p):
    """A probability as plain decimals (the 0.001 sleeper weights split many ways would print as 1e-05 with %g)."""
    t = ("%.8f" % p).rstrip("0").rstrip(".")
    if t in ("", "0"): raise SystemExit("W0: probability %r rounds to 0" % p)
    return t

def is_pack_animal(name):
    """An NPC-framework animal (the NPC fox rides npcAnimalMeleeTemplate): dropped, never swapped."""
    return any(re.match(r"^npcAnimal", n) for n in class_chain(name))

def swap_kind(name):
    """'human' (swap for ours), 'animal' (drop), or stop: a human pack NPC with no known framework weapon, or an
    0-XNPCCore V4 class we do not own, would otherwise lose its share silently."""
    if is_v4_npc(name): raise SystemExit("W0: 0-XNPCCore class %s is in a spawn group and is not one of XNPC_SURVIVORS - map it by hand" % name)
    if framework_cat(name): return "human"
    if is_pack_animal(name): return "animal"
    raise SystemExit("W0: pack NPC %s has no framework weapon template (chain %s) - map it by hand" % (name, class_chain(name)))

def xpath_names(names):
    return " or ".join("@name='%s'" % n for n in names)

ours = set(RAIDERS) | set(XNPC_SURVIVORS)
xnpc_spawn_groups = set(e.get("entitygroup") for e in
                        ET.parse(os.path.join(XNPC, "spawning.xml")).getroot().iter("spawn") if e.get("entitygroup"))
w0_spawns, w0_swap, w0_drop = [], {}, {}
for grp in ET.parse(os.path.join(XNPC, "entitygroups.xml")).getroot().iter("entitygroup"):
    gname = grp.get("name")
    kids = [e for e in grp if isinstance(e.tag, str)]
    ents = [(e.get("n") or e.get("name"), float(e.get("p") or e.get("prob") or 1)) for e in kids]
    pack = [n for n, _ in ents if is_pack_npc(n)]
    # D10: the framework's own biome spawns of its survivors stay removed - ours call those classes now - while
    # the group itself is left as it is (nothing else spawns it)
    if gname in xnpc_spawn_groups and any(n in XNPC_SURVIVORS for n, _ in ents) and gname not in w0_spawns:
        w0_spawns.append(gname)
    if not pack: continue
    if (grp.text or "").strip(): raise SystemExit("W0: %s holds flat-text entries - not handled" % gname)
    if any(e.tag != "e" for e in kids): raise SystemExit("W0: %s has non-<e> entries - not handled" % gname)
    humans = [n for n in pack if swap_kind(n) == "human"]
    if not humans:                                # only pack animals (the NPC fox) among other members
        rest = [n for n, _ in ents if n not in pack]
        if not rest or rest[0] == "none": raise SystemExit("W0: dropping %s would leave %s empty or 'none'-first" % (pack, gname))
        w0_drop[gname] = tuple(sorted(set(pack)))
        continue                                  # its spawn entries stay: they still bring the vanilla animals
    if gname in xnpc_spawn_groups and gname not in w0_spawns:   # its biome groups: the <spawn> entries go, and the group is swapped
        w0_spawns.append(gname)
    # the group's side, not each NPC's: the framework files its one friendly Harley (npcHarleyKnife, an
    # EntityAliveSDX) among the bandits, and a survivor has no place in a bandit camp
    bandits = sum(1 for n in humans if "EntityNPCBandit" in (class_prop(n, "Class") or ""))
    hostile = bandits * 2 >= len(humans)
    if 0 < bandits < len(humans) and min(bandits, len(humans) - bandits) * 4 > len(humans):
        raise SystemExit("W0: %s mixes %d bandits with %d friendlies - pick its side by hand" % (gname, bandits, len(humans) - bandits))
    if hostile and any(n in XNPC_SURVIVORS for n, _ in ents):
        raise SystemExit("W0: 0-XNPCCore survivors in hostile group %s - pick its side by hand" % gname)
    new = []                                      # (name, p) in the framework's order, duplicates summed
    for n, p in ents:
        if not is_pack_npc(n):
            subs = [(n, p)]
        elif swap_kind(n) == "animal":
            subs = []                             # a pack animal inside a human group: dropped
        else:                                     # the class of its weapon's tier, the group's side (M10-D9)
            tier = TOKEN_TIER[BASE_TOKEN[framework_cat(n)]] + 1
            subs = [((raider if hostile else xnpc_survivor)(male, tier), p / 2.0) for male in (True, False)]
        for sn, sp in subs:
            if sn != "none" and is_pack_npc(sn): raise SystemExit("W0: %s still holds pack NPC %s" % (gname, sn))
            if sn.startswith(("scSurvivor", "scRaider", "npcSdcsSurvivor")) and sn not in ours: raise SystemExit("W0: no class " + sn)
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
g.append("""\t<!-- GENERATED by Tools/gen_arsenal.py - edit SURVIVOR_CHANCE and RAIDER_CHANCE there, not this file.

\t     Our own groups, called by our own spawn entries in spawning.xml, so our rate is independent of the NPC
\t     framework's and of whatever character packs are installed. Each group is one roll: "none" is the odds of
\t     that roll producing nobody, and the rest is split over the classes.
\t     Survivors are 0-XNPCCore's npcSdcsSurvivor classes (decision D10), both sexes, the tier of the band: T1 early,
\t     T2 middle, T3 late. They carry no weapon of their own: the SurvivorCommunity DLL rolls a melee weapon and a bow,
\t     crossbow or (at SurvivorFirearmChance) firearm for each, capped by the class's tier and the gamestage.
\t     Raiders are SurvivorCommunity's scRaider classes (M10), both sexes, the tier of the band, laid out the same
\t     way. They carry no weapon of their own either: the DLL rolls each one's pair (a firearm at RaiderFirearmChance,
\t     12%, capped by the class's tier and the gamestage) and its lead, the ranged weapon at RaiderRangedShare (by
\t     default 30% / 40% / 50% for T1 / T2 / T3), else the melee weapon. "none" must never be the first entry: EntityGroups.IsEnemyGroup reads
\t     entry 0 to decide whether the group is hostile, and a raider there makes the raider groups enemy groups. -->

\t<append xpath="/entitygroups">
""")
for side, tag, chance, _ in SIDES:
    for band in ("GS01", "GS50", "GS100"):
        entries = survivor_band(band) if side == "s" else raider_band(band)
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
\t     S_-Group_NPC_* POI volumes, and its biome groups (their spawn entries are removed in spawning.xml). A group may
\t     not be empty (the game throws at load) and "none" must not come first, so each pack NPC becomes one of ours at
\t     the same probability split over both sexes, the class of its weapon's tier (T1 for a tier 0 weapon, T2 for
\t     tier 1, T3 for tier 2): in a friendly group the 0-XNPCCore survivor class, in a hostile group SurvivorCommunity's
\t     raider class. 0-XNPCCore's own survivors are ours and stay where its groups list them. Groups that also hold
\t     vanilla animals only lose the pack NPC. %d groups swapped, %d trimmed. -->
""" % (len(w0_swap), len(w0_drop)))
for entries, names in sorted(w0_swap_sets.items(), key=lambda kv: kv[1][0]):
    sel = "/entitygroups/entitygroup[%s]" % xpath_names(names)
    g.append('\n\t<remove xpath="%s/*"/>\n' % sel)            # <e> and the older <entity> rows alike
    g.append('\t<append xpath="%s">\n' % sel)
    for n, p in entries:
        g.append('\t\t<e n="%s" p="%s"/>\n' % (n, fmt_p(p)))
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
\t<!-- W0 and D10: no new pack NPCs while SurvivorCommunity is installed, and 0-XNPCCore's survivors spawn only at
\t     SurvivorCommunity's rates. The NPC framework's own biome spawns call these groups of its sample cast and of its
\t     survivors; removing the entries leaves our spawns above as the only wild NPCs. -->
\t<remove xpath="/spawning/biome/spawn[%s]"/>
""" % " or ".join("@entitygroup='%s'" % n for n in sorted(w0_spawns)))
s.append("</configs>\n")
io.open(os.path.join(MOD, "spawning.xml"), "w", encoding="utf-8", newline="\n").write("".join(s))

# ------------------------------------------------------------------------------------------------------------
# items.xml: nothing generated since M10 round 2. The old raiders' NPC weapon copies (meleeNPCSC*/gunNPCSC*) lived in a
# marked block; a file that still has it loses it here, markers included, and a second run finds nothing to change.
# ------------------------------------------------------------------------------------------------------------
BEGIN, END = "<!-- BEGIN GENERATED: NPC weapons (Tools/gen_arsenal.py) -->", "<!-- END GENERATED: NPC weapons -->"
ip = os.path.join(MOD, "items.xml")
src = io.open(ip, encoding="utf-8").read()
old_items_block = BEGIN in src
src = re.sub(r"\n?\t*" + re.escape(BEGIN) + r".*?" + re.escape(END) + r"\n?", "\n", src, flags=re.S)
if "NPCSC" in src: raise SystemExit("items.xml still names an NPCSC item outside the old marked block - remove it by hand")
io.open(ip, "w", encoding="utf-8", newline="\n").write(src)

# ------------------------------------------------------------------------------------------------------------
# blocks.xml: XnpcWeapons, the vanilla weapons the DLL arms the community's 0-XNPCCore survivors with (migration M7,
# decision D4). 0-XNPCCore's survivors use real player weapons, so each token of the table becomes the vanilla item
# behind it - its vanilla column, or the framework NPC item's CompatibleWeapon - with the token's kind, family and tier,
# so the survivors' tiers and firearm odds are the raiders'. EmptyHand and RocketL are left out (no fists as a weapon,
# no rocket launcher outside its own class). A marked block inside the config block, replaced on every run.
# ------------------------------------------------------------------------------------------------------------
XW_BEGIN, XW_END = "<!-- BEGIN GENERATED: XnpcWeapons (Tools/gen_arsenal.py) -->", "<!-- END GENERATED: XnpcWeapons -->"
XNPC_WEAPONS = []                                 # (vanilla item, family, tier, token), the table's order
for token, cat, kind, tier, van in WEAPONS:
    if token in ("EmptyHand", "RocketL"): continue
    item = van
    if not item:
        npc = (class_items_on_enter("npc%sTemplate" % cat) or "").split(",")[0].strip()
        cw = one(xitem(npc), "property[@name='CompatibleWeapon']") if xitem(npc) is not None else None
        item = cw.get("value") if cw is not None else None
    if not item or vitem(item) is None: raise SystemExit("XnpcWeapons: %s has no vanilla weapon (framework item %s)" % (token, item))
    fam = family(token, kind)
    if fam not in ("melee", "bow", "firearm"): raise SystemExit("XnpcWeapons: %s is a %s" % (token, fam))
    if fam != "melee" and not vprop(vitem(item), "Magazine_items", "Action0"):
        raise SystemExit("XnpcWeapons: %s (%s) has no Action0 Magazine_items - 0-XNPCCore could not reload it" % (item, token))
    if any(x[0] == item for x in XNPC_WEAPONS): raise SystemExit("XnpcWeapons: %s twice in the table" % item)
    XNPC_WEAPONS.append((item, fam, tier, token))
bp = os.path.join(MOD, "blocks.xml")
src = io.open(bp, encoding="utf-8").read()
if src.count(XW_BEGIN) > 1 or src.count(XW_END) > 1: raise SystemExit("blocks.xml holds the XnpcWeapons markers more than once - fix it by hand")
if XW_BEGIN in src:
    src = re.sub(r"\t*" + re.escape(XW_BEGIN) + r".*?" + re.escape(XW_END) + r"\n", "", src, flags=re.S)
# after XnpcModelY (the last of the body switches) when it exists, else after XnpcBodyColliderLayers as before: the
# block then stays where it was, and the hand-written raider keys that follow it are never pulled above it
anchor = re.search(r'\n(\t*)<property name="XnpcModelY"[^\n]*\n', src) or re.search(r'\n(\t*)<property name="XnpcBodyColliderLayers"[^\n]*\n', src)
if anchor is None: raise SystemExit("blocks.xml: no XnpcModelY or XnpcBodyColliderLayers property to put XnpcWeapons after")
ind = anchor.group(1)
xw = [ind + XW_BEGIN + "\n",
      ind + "<!-- The vanilla weapons the community's 0-XNPCCore survivors and SurvivorCommunity's raiders carry, one melee\n"
      + ind + "     and one ranged each (a raider leads with one of them), rolled at or below the lower of their class's tier (T1 = 0,\n"
      + ind + "     T2 = 1, T3 = 2) and the gamestage tier; a ranged one is a firearm at SurvivorFirearmChance (a raider's at\n"
      + ind + "     RaiderFirearmChance), else a bow or crossbow. value = family (melee, bow, firearm), tier, and the short name\n"
      + ind + "     the console accepts. Written by Tools/gen_arsenal.py from its weapon table: edit it there. -->\n",
      ind + '<property class="XnpcWeapons">\n']
for item, fam, tier, token in XNPC_WEAPONS:
    xw.append(ind + '\t<property name="%s" value="%s,%d,%s" />\n' % (item, fam, tier, token))
xw.append(ind + "</property>\n")
xw.append(ind + XW_END + "\n")
src = src[:anchor.end()] + "".join(xw) + src[anchor.end():]
io.open(bp, "w", encoding="utf-8", newline="\n").write(src)

# ------------------------------------------------------------------------------------------------------------
# blocks.xml, optional: IZY Classic's guns (IGP_Classic, whose items only exist with Combat_Sandbox_FLTR) for any
# survivor or raider while that pack is installed. The user's rule (2026-10-08), by the tier in the gun's name (Tminus1
# counts as T0): T1 NPCs carry T0/T1 guns, T1 a little rarer; T2 NPCs T1/T2, T2 a little rarer; T3 NPCs T2/T3/T4, T4
# rarer. An NPC's tier is its effective band, the class band capped by the gamestage (ScLoadout.TierCap), as for every
# weapon. Each row's bands= field gives its group's weight at band 0/1/2 and group= names the group (T0..T4): the DLL
# picks a family's weapon by weight, the guns of one group splitting its weight, so "T1 a little rarer" holds for the
# tier as a whole however many T1 guns there are. The tier field is the first band it can turn up in (the console, and
# DLLs from before bands=). Left out (research 2026-10-08, scratch mig\mw\izy_weapons_research.md): IZY's melee weapons
# (the user's call), the duals (HoldType 67: no aim or reload pose on a body), the grenade launchers, the M134, the
# flamethrower, the Broken AR-15 (no ranged tag), the nailgun (nail ammo) and Combat Sandbox's not-ready items.
# Vanilla's own firearm rows above take the same rule while the pack is installed: Combat Sandbox names every gun with
# its tier ("Pipe Shotgun (T0)", and IZY's 13 replacers such as "AK-47 Machine Gun (T1)"), so a <set> per row gives each
# the bands and group of the tier in its item name. The bows are untouched. A top-level conditional, so without the pack
# none of it exists and nothing is logged.
# ------------------------------------------------------------------------------------------------------------
IZ_BEGIN, IZ_END = "<!-- BEGIN GENERATED: XnpcWeapons IZY Classic (Tools/gen_arsenal.py) -->", "<!-- END GENERATED: XnpcWeapons IZY Classic -->"
IZY_COND = "mod_loaded('IGP_Classic') and mod_loaded('Combat_Sandbox_FLTR')"
IZY_RARE, IZY_RARER = 0.65, 0.4
IZY_BANDS = {0: (1.0, 0.0, 0.0), 1: (IZY_RARE, 1.0, 0.0), 2: (0.0, IZY_RARE, 1.0), 3: (0.0, 0.0, 1.0), 4: (0.0, 0.0, IZY_RARER)}
IZY_WEAPONS = [   # (item, family, alias, display) - the pack tier is the T<n> in the item name (Tminus1 = T0)
    ("IZYgunTminus1MusketM1777Rifle", "firearm", "Musket", "Musket Model 1777 (TS)"),
    ("IZYgunT0PistolLiberatorImprovisedPistol", "firearm", "Liberator", "FP-45 Liberator Pistol (T0)"),
    ("IZYgunT0SMGPIPEsubmachinegun", "firearm", "PipeSMG", "Pipe SMG (T0)"),
    ("IZYgunT145TACSMGMAC10", "firearm", "MAC10", "MAC-10 SMG (T1)"),
    ("IZYgunT1ARCabineM608SurvivalRifle", "firearm", "M608", "Model 608 Survival Rifle (T1)"),
    ("IZYgunT1BattleRifleBARbrowningautomaticrifle", "firearm", "BAR", "Browning Automatic Rifle (T1)"),
    ("IZYgunT1Pistol45M1917", "firearm", "M1917", "M1917 Revolver (T1)"),
    ("IZYgunT1Pistol45Type17C96", "firearm", "Type17", "Shanxi Type 17 Pistol (T1)"),
    ("IZYgunT1Pistol9Makarov", "firearm", "Makarov", "Makarov Pistol (T1)"),
    ("IZYgunT1SMGTec9submachinegun", "firearm", "Tec9", "Tec-9 SMG (T1)"),
    ("IZYgunT1SNIPERRIFLE762Mosinnagant", "firearm", "Mosin", "Mosin Nagant Rifle (T1)"),
    ("IZYgunT1TACCompactshotgunSuperShortty", "firearm", "SuperShorty", "Super-Shorty Shotgun (T1)"),
    ("IZYgunT1TradshotgunDoubleBarrelOverUnder", "firearm", "OverUnder", "Over-Under Shotgun (T1)"),
    ("IZYgunT245SMGM3Greasegun", "firearm", "GreaseGun", "M3 Grease Gun SMG (T2)"),
    ("IZYgunT2ARCabineCAR15", "firearm", "CAR15", "CAR-15 Rifle (T2)"),
    ("IZYgunT2ARSTG44N", "firearm", "StG44", "StG 44 Rifle (T2)"),
    ("IZYgunT2BattleRifleFNFALRifle", "firearm", "FAL", "FNN FAL Rifle (T2)"),
    ("IZYgunT2LMGLMG36", "firearm", "LMG36", "MG36 Machine Gun (T2)"),
    ("IZYgunT2LMGlightmachinegunRPD", "firearm", "RPD", "RPD Machine Gun (T2)"),
    ("IZYgunT2MarksManRifle762M1garandNavyconverted", "firearm", "M1Garand", "M1N Reserve Rifle (T2)"),
    ("IZYgunT2Pistol45M1911A1", "firearm", "M1911", "M1911 Pistol (T2)"),
    ("IZYgunT2SMGUZIsubmachinegun", "firearm", "Uzi", "UZI SMG (T2)"),
    ("IZYgunT2TradshotgunLeveractionShotgunModel1887", "firearm", "Model1887", "Model 1887 Shotgun (T2)"),
    ("IZYgunT345SMGM1thompson", "firearm", "Thompson", "M1 Thompson SMG (T3)"),
    ("IZYgunT3ARAKM", "firearm", "AKM", "AKM Rifle (T3)"),
    ("IZYgunT3ARCabineM4Carbine", "firearm", "M4", "M4 Carbine (T3)"),
    ("IZYgunT3BULLPUPHellionVHS2", "firearm", "VHS2", "VHS-2 Assault Rifle (T3)"),
    ("IZYgunT3BattleRifleHKG3", "firearm", "G3", "G3 Rifle (T3)"),
    ("IZYgunT3LMGM249FNminimi", "firearm", "M249", "M249 Machine Gun (T3)"),
    ("IZYgunT3MarksManRifle556M16A2DMR", "firearm", "M16DMR", "M16A2 DMR (T3)"),
    ("IZYgunT3MarksManRifle762HKPSG1", "firearm", "PSG1", "PSG1 Rifle (T3)"),
    ("IZYgunT3MarksmanRifleMMRM1Carbine45", "firearm", "CampCarbine", "Camp Carbine 45 (T3)"),
    ("IZYgunT3Pistol45USP45", "firearm", "USP45", "USP45 Pistol (T3)"),
    ("IZYgunT3PistolMagnumAutomag44", "firearm", "AutoMag", ".44 AutoMag Heavy Pistol (T3)"),
    ("IZYgunT3SMGMP9submachinegun", "firearm", "MP9", "MP9 SMG (T3)"),
    ("IZYgunT3SNIPERRIFLE762LeeEnfieldRifle", "firearm", "Enfield", "Enfield Rifle (T3)"),
    ("IZYgunT3TACshotgunSPAS12G", "firearm", "SPAS12", "SPAS-12 Shotgun (T3)"),
    ("IZYgunT3TradshotgunTrenchGunM1897", "firearm", "TrenchGun", "Model 1897 Trench Gun (T3)"),
    ("IZYgunT445SMGM1928thompson", "firearm", "Thompson1928", "M1928 Thompson SMG (T4)"),
    ("IZYgunT445TACSMGKrissSuperV", "firearm", "Kriss", "KRISS Vector SMG (T4)"),
    ("IZYgunT4ARCabineHK416", "firearm", "HK416", "M416 Carbine (T4)"),
    ("IZYgunT4BULLPUPFNF2000", "firearm", "F2000", "FNN F2000 Assault Rifle (T4)"),
    ("IZYgunT4BattleRifleSCARH", "firearm", "SCARH", "SCAR-H Rifle (T4)"),
    ("IZYgunT4LMGlightmachinegunMG34R", "firearm", "MG34", "Antique Machine Gun (T4)"),
    ("IZYgunT4MarksManRifle762SVUprototypeRifle", "firearm", "SVU", "Prototype SVU Rifle (T4)"),
    ("IZYgunT4MarksManRifleM110SASS", "firearm", "M110", "M110 SASS Rifle (T4)"),
    ("IZYgunT4MarksmanRifle45MMRKrissVectorCarbine", "firearm", "VectorCarbine", "Vector Carbine (T4)"),
    ("IZYgunT4Pistol45STIEAGLE6", "firearm", "STIEagle", "STI Eagle 6 Pistol (T4)"),
    ("IZYgunT4SMGCZScorpionEVO3", "firearm", "Scorpion", "Scorpion EVO 3 SMG (T4)"),
    ("IZYgunT4SNIPERRIFLE762L96AWMRifle", "firearm", "L96", "L96 Rifle (T4)"),
    ("IZYgunT4TradshotgunBrowningAuto5", "firearm", "Auto5", "Browning Auto-5 Shotgun (T4)"),
    ("IZYgunT4BowVanhelsingAutomaticCrossbowVH", "bow", "VHCrossbow", "VH Automatic Crossbow (T4)"),
]


def izy_tier(item):
    m = re.match(r"IZYgunT(minus1|\d)", item)
    if not m: raise SystemExit("IZY_WEAPONS: %s has no T<n> in its name" % item)
    return 0 if m.group(1) == "minus1" else int(m.group(1))

def fmt_w(x):
    return ("%.2f" % x).rstrip("0").rstrip(".")

IGP = os.path.join(GAME, r"Mods\IGP_Classic\Config\IZY26513")
CSB = os.path.join(GAME, r"Mods\zzzCombatSandboxFLTR\Config\items.xml")
izy_items = {}
if os.path.isdir(IGP):
    for sub in sorted(os.listdir(IGP)):
        p = os.path.join(IGP, sub, "items.xml")
        if os.path.isfile(p):
            for it in ET.parse(p).getroot().iter("item"):
                izy_items[it.get("name")] = it          # the last definition wins, as in game
    not_ready = set()                               # Combat Sandbox's live CreativeMode Dev appends (not ready, no loot)
    if os.path.isfile(CSB):
        for ap in ET.parse(CSB).getroot().iter("append"):
            if ap.xpath("property[@name='CreativeMode' and @value='Dev']"):
                not_ready.update(re.findall(r"@name='(IZY\w+)'", ap.get("xpath", "")))
tokens = {x[3].lower() for x in XNPC_WEAPONS}
izy_rows = []
for item, fam, alias, disp in IZY_WEAPONS:
    t = izy_tier(item)
    if t not in IZY_BANDS: raise SystemExit("IZY_WEAPONS: %s is T%d, which no band takes" % (item, t))
    if fam not in ("bow", "firearm"): raise SystemExit("IZY_WEAPONS: %s is a %s (guns and bows only)" % (item, fam))
    if alias.lower() in tokens: raise SystemExit("IZY_WEAPONS: alias %s is taken" % alias)
    tokens.add(alias.lower())
    if izy_items:
        it = izy_items.get(item)
        if it is None: raise SystemExit("IZY_WEAPONS: %s is not in IGP_Classic" % item)
        tags = {x.strip().lower() for x in (one(it, "property[@name='Tags']").get("value") if one(it, "property[@name='Tags']") is not None else "").split(",")}
        if "ranged" not in tags: raise SystemExit("IZY_WEAPONS: %s has no ranged tag - 0-XNPCCore would fight with it as melee" % item)
        if item in not_ready: raise SystemExit("IZY_WEAPONS: %s is on Combat Sandbox's not-ready list" % item)
    bands = IZY_BANDS[t]
    tier = next(i for i, x in enumerate(bands) if x > 0)
    izy_rows.append((item, fam, tier, alias, "/".join(fmt_w(x) for x in bands), t))
# vanilla's firearm rows: the tier in the item name, which Combat Sandbox shows (gunMGT1AK47 "AK-47 Machine Gun (T1)")
rep_rows = []
for item, fam, tier0, token in XNPC_WEAPONS:
    if fam != "firearm": continue
    m = re.match(r"gun[A-Za-z]+?T(\d)", item)
    if not m or int(m.group(1)) not in IZY_BANDS: raise SystemExit("XnpcWeapons firearm %s has no usable T<n> in its name" % item)
    t = int(m.group(1))
    bands = IZY_BANDS[t]
    tier = next(i for i, x in enumerate(bands) if x > 0)
    rep_rows.append((item, fam, tier, token, "/".join(fmt_w(x) for x in bands), t))
bp = os.path.join(MOD, "blocks.xml")
src = io.open(bp, encoding="utf-8").read()
if src.count(IZ_BEGIN) > 1 or src.count(IZ_END) > 1: raise SystemExit("blocks.xml holds the IZY markers more than once - fix it by hand")
if IZ_BEGIN in src:
    src = re.sub(r"\t*" + re.escape(IZ_BEGIN) + r".*?" + re.escape(IZ_END) + r"\n", "", src, flags=re.S)
if src.count("</configs>") != 1: raise SystemExit("blocks.xml: expected one </configs>")
iz = ["\t" + IZ_BEGIN + "\n",
      "\t<!-- IZY Classic's guns for any survivor or raider, only while IZY Classic (with Combat Sandbox) is installed,\n"
      "\t     and every gun then follows the tier in its name: T1 NPCs carry T0/T1 guns (T1 a little rarer), T2 NPCs T1/T2\n"
      "\t     (T2 rarer), T3 NPCs T2/T3/T4 (T4 rarer). bands = the weight of the gun's group (group=, its name tier) at an\n"
      "\t     NPC's effective tier 0/1/2 (T1, T2, T3 NPCs, capped by the gamestage); the group's guns split it. Locked &\n"
      "\t     Loaded arms them with its IZY Classic magazines. Written by Tools/gen_arsenal.py (IZY_WEAPONS): edit it there. -->\n",
      "\t<conditional>\n", '\t\t<if cond="%s">\n' % IZY_COND,
      "\t\t\t<append xpath=\"/blocks/block[@name='ConfigSurvivorCommunity']/property[@class='XnpcWeapons']\">\n"]
for item, fam, tier, alias, bands, t in izy_rows:
    iz.append('\t\t\t\t<property name="%s" value="%s,%d,%s,bands=%s,group=T%d" />\n' % (item, fam, tier, alias, bands, t))
iz += ["\t\t\t</append>\n",
       "\t\t\t<!-- vanilla's guns (the pipe guns, and the 13 IZY replaces): the same rule by the tier in their name -->\n"]
for item, fam, tier, token, bands, t in rep_rows:
    iz.append("\t\t\t<set xpath=\"/blocks/block[@name='ConfigSurvivorCommunity']/property[@class='XnpcWeapons']"
              "/property[@name='%s']/@value\">%s,%d,%s,bands=%s,group=T%d</set>\n" % (item, fam, tier, token, bands, t))
iz += ["\t\t</if>\n", "\t</conditional>\n", "\t" + IZ_END + "\n"]
src = src.replace("</configs>", "".join(iz) + "</configs>")
io.open(bp, "w", encoding="utf-8", newline="\n").write(src)

# ------------------------------------------------------------------------------------------------------------
# Localization.csv: a row for every leaf class, managed by key
# ------------------------------------------------------------------------------------------------------------
lp = os.path.join(MOD, "Localization.csv")
raw = io.open(lp, encoding="utf-8").read()
nl = "\r\n" if "\r\n" in raw else "\n"
lines = raw.split(nl)
managed = set(RAIDERS)
# the rows of SC's old survivor classes go too (no longer generated, D11), those of the old per-weapon raider classes
# scRaider<Sex><Weapon> (M10: only the six T1-T3 classes are left), and those of the old raiders' NPC weapon copies
# meleeNPCSC*/gunNPCSC* (gone in M10 round 2)
def stale(key):
    return (bool(re.match(r"^scSurvivor(Male|Female)", key))
            or (bool(re.match(r"^scRaider(Male|Female)\w+$", key)) and key not in RAIDERS)
            or bool(re.match(r"^(melee|gun)NPCSC\w+$", key)))
kept = [ln for ln in lines if ln.split(",", 1)[0] not in managed and not stale(ln.split(",", 1)[0])]
while kept and kept[-1] == "": kept.pop()
rows = []
for n in RAIDERS:
    rows.append("%s,entityclasses,Entity,,,,Raider" % n)
io.open(lp, "w", encoding="utf-8", newline="").write(nl.join(kept + rows) + nl)

print("classes: %d raider leaves + %d templates (M10, on 0-XNPCCore's body)" % (len(RAIDERS), len(RAIDER_TEMPLATE)))
print("survivor weapons (XnpcWeapons): %d (%s)" % (len(XNPC_WEAPONS), ", ".join(
    "%s %d" % (f, sum(1 for x in XNPC_WEAPONS if x[1] == f)) for f in ("melee", "bow", "firearm"))))
print("IZY Classic weapons (optional block): %d new, %d vanilla guns re-tiered (%s)%s" % (len(izy_rows), len(rep_rows), ", ".join("T%d %d" % (k, sum(1 for r in izy_rows if r[5] == k)) for k in sorted(IZY_BANDS)),
      "" if izy_items else " - IGP_Classic not found, not checked against the pack"))
print("W0: %d groups swapped, %d trimmed, %d framework spawn groups removed" % (len(w0_swap), len(w0_drop), len(w0_spawns)))
print("items.xml: %s" % ("the old raiders' NPC weapon block removed (M10 round 2)" if old_items_block else "no generated block (none since M10 round 2)"))
