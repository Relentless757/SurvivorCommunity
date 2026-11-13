"""Build Resources/SCLandClaim.unity3d, the asset bundle holding the land claim block's model.

    python -I Tools/build_landclaim_bundle.py [--no-copy] [--inputs <folder>]
                                              [--unity <Unity.exe>] [--project <Unity project>]

The model is four of the game's own block models (a tall locker, an office desk, a desk lamp and a
book pile) baked into one mesh, so ITS INPUTS ARE NOT IN THIS REPO - they are The Fun Pimps' assets
and this repo is public. They live in a working folder outside it, --inputs, which by default is
    F:\\Game Related Programs\\Creation Stuff\\SC_LandClaim_assets\\unity_in
and holds SC_LandClaim.fbx plus <material>_d/_n/_mask(/_e).png, written by make_maps.py beside it.
The built bundle and the rendered icon are TFP geometry too, so .gitignore keeps both out.

What it does, in order:
 1. Copies the inputs into the Unity project (the one shared with the other mods), along with
    art/landclaim/BuildScLandClaim.cs from this repo.
 2. Makes sure the project's tag list starts as the game's does. A prefab stores its tag as a number,
    20000 + the tag's place in the project's list, and the game reads the number against its own
    list: T_Block must be fifth here as it is there. An empty list is filled with the game's; a list
    that disagrees stops the build.
 3. Runs Unity in batch mode (BuildScLandClaim.Build) and reads its log.
 4. Checks the built bundle with UnityPy: the prefab is in it, the root is T_Block on layer 16 with
    NO collider of its own, DMG0 and DMG1 are both there (keystoneBlock's MeshDamage names them and
    the game calls Find() on both without a null check), the collider child carries
    "Assembly-CSharp, RootTransformRefParent" as the game's own bundles name it, the textures decode,
    and the Standard shader is compiled for more than Direct3D.
 5. Turns the build's pictures into PNGs: art/landclaim/preview_*.png (kept, they are renders of the
    build, small) and the block's icon, UIAtlases/ItemIconAtlas/scLandClaim.png.
 6. Copies the bundle to Resources/SCLandClaim.unity3d, unless --no-copy.
The Unity project's ProjectSettings.asset is put back as it was afterwards (the build changes the
graphics API list while it runs).
"""
import argparse
import os
import shutil
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UNITY = r"C:\Program Files\Unity\Hub\Editor\2022.3.62f2\Editor\Unity.exe"
PROJECT = r"F:\SpikeZombieUnity"
INPUTS = r"F:\Game Related Programs\Creation Stuff\SC_LandClaim_assets\unity_in"
BUNDLE = "sclandclaim"
BUILT = "BuiltScLandClaim"
DEPLOYED = os.path.join(REPO, "Resources", "SCLandClaim.unity3d")
ASSET_SUB = "Assets/SurvivorCommunity/LandClaim"

MATERIALS = ["locker", "deskOffice", "lampDesk", "Books"]
PARTS = ["locker_ver4_loot", "deskOffice", "lampDesk", "bookScarletJournals"]

# 7 Days to Die 3.4's tag list, in order (TagManager in the game's data.unity3d). T_Block is 20004.
GAME_TAGS = """Item T_Mesh B_Mesh T_Mesh_B T_Block SB_Prefabs SB_StartPoint SB_EntitySpawner SB_TraderTeleport E_Player
E_BP_Body E_BP_Head E_Car E_BP_LArm E_Animal E_Enemy E_BP_RArm E_BP_LLeg E_BP_RLeg Sky E_BP_Spotlight E_BP_BipedRoot
L_LeftUpperArmGore L_RightUpperArmGore L_HeadGore L_LeftLowerArmGore L_RightLowerArmGore L_LeftUpperLegGore
L_LeftLowerLegGore L_RightUpperLegGore L_RightLowerLegGore E_BP_LLowerArm E_BP_RLowerArm E_BP_LLowerLeg
E_BP_RLowerLeg SB_SleeperVolume SB_SleeperSpawn LargeEntityBlocker T_Deco E_Vehicle TE_PrefabSub Sun Moon Physics
E_BP_Special E_Mesh SupplyLit SupplySmoke E_BP_Eye NoOcclude VisibleOnZoom SB_TriggerVolume SB_InfoVolume
SB_WallVolume LOD E_VehicleStrong E_BP_Temp AudioRigidBody ModMesh ModOn D_Accessory GroundProxy""".split()
T_BLOCK = 20000 + GAME_TAGS.index("T_Block")
BLOCK_LAYER = 16


def say(text):
    print(text, flush=True)


def copy_inputs(project, inputs):
    if not os.path.isdir(inputs):
        sys.exit("no input folder %s - run make_maps.py in the working folder first" % inputs)
    wanted = ["SC_LandClaim.fbx"]
    for mat in MATERIALS:
        wanted += [mat + "_d.png", mat + "_n.png", mat + "_mask.png"]
    optional = [mat + "_e.png" for mat in MATERIALS]

    dst_dir = os.path.join(project, *ASSET_SUB.split("/"))
    os.makedirs(dst_dir, exist_ok=True)
    for name in wanted:
        src = os.path.join(inputs, name)
        if not os.path.isfile(src):
            sys.exit("missing input: " + src)
    for name in wanted + optional:
        src = os.path.join(inputs, name)
        if not os.path.isfile(src):
            continue
        dst = os.path.join(dst_dir, name)
        if not os.path.isfile(dst) or open(src, "rb").read() != open(dst, "rb").read():
            shutil.copyfile(src, dst)
            say("  copied " + name)

    script = os.path.join(REPO, "art", "landclaim", "BuildScLandClaim.cs")
    if not os.path.isfile(script):
        sys.exit("missing " + script)
    editor = os.path.join(project, "Assets", "Editor")
    os.makedirs(editor, exist_ok=True)
    dst = os.path.join(editor, "BuildScLandClaim.cs")
    if not os.path.isfile(dst) or open(script, "rb").read() != open(dst, "rb").read():
        shutil.copyfile(script, dst)
        say("  copied BuildScLandClaim.cs -> Assets/Editor")

    # The RootTransformRefParent stand-in. Roadworthy put one in this shared project; if it is gone,
    # write it again - the bundle only stores "Assembly-CSharp, RootTransformRefParent" and the field
    # by name, and the game supplies the real class. The whole Assets tree is searched first: these
    # classes are in the global namespace, so a second copy anywhere is CS0101 and breaks every mod's
    # build in this shared project until someone deletes one by hand.
    have = []
    for where, _dirs, files in os.walk(os.path.join(project, "Assets")):
        if "RootTransformRefParent.cs" in files:
            have.append(where)
    if have:
        say("  RootTransformRef stubs already in " + ", ".join(have))
    else:
        stubs = os.path.join(project, "Assets", "SurvivorCommunity", "GameStubs")
        os.makedirs(stubs, exist_ok=True)
        with open(os.path.join(stubs, "RootTransformRef.cs"), "w", encoding="utf-8", newline="\n") as fh:
            fh.write("using UnityEngine;\n\n"
                     "// A stand-in for the game's own class of this name, so a prefab built here can carry the\n"
                     "// component. Keep the name, the base class and the field exactly as the game has them.\n"
                     "public class RootTransformRef : MonoBehaviour\n{\n    public Transform RootTransform;\n}\n")
        with open(os.path.join(stubs, "RootTransformRefParent.cs"), "w", encoding="utf-8", newline="\n") as fh:
            fh.write("// A stand-in for the game's own class of this name (see RootTransformRef.cs).\n"
                     "public class RootTransformRefParent : RootTransformRef\n{\n}\n")
        say("  wrote the RootTransformRef stubs into " + stubs)


def ensure_tags(project):
    path = os.path.join(project, "ProjectSettings", "TagManager.asset")
    text = open(path, encoding="utf-8").read()
    lines = text.split("\n")
    at = next(i for i, line in enumerate(lines) if line.startswith("  tags:"))
    have, end = [], at + 1
    if lines[at].strip() != "tags: []":
        while end < len(lines) and lines[end].startswith("  - "):
            have.append(lines[end][4:].strip())
            end += 1
    if have[:len(GAME_TAGS)] == GAME_TAGS:
        say("  the project's tag list starts as the game's does")
        return
    if have and have != GAME_TAGS[:len(have)]:
        sys.exit("the Unity project's tag list disagrees with the game's (%s ...): put the game's tags first, in "
                 "the game's order, or T_Block will not be T_Block in the game" % ", ".join(have[:6]))
    backup = path + ".before_sclandclaim"
    if not os.path.exists(backup):
        shutil.copyfile(path, backup)
    lines[at:end] = ["  tags:"] + ["  - " + tag for tag in GAME_TAGS]
    open(path, "w", encoding="utf-8", newline="\n").write("\n".join(lines))
    say("  wrote the game's %d tags into the project's tag list (the old file is kept as %s)"
        % (len(GAME_TAGS), os.path.basename(backup)))


def run_unity(unity, project):
    log = os.path.join(project, "build_sclandclaim.log")
    settings = os.path.join(project, "ProjectSettings", "ProjectSettings.asset")
    before = open(settings, "rb").read()
    if os.path.exists(log):
        os.remove(log)
    built = os.path.join(project, BUILT)
    out = os.path.join(built, BUNDLE)
    if os.path.exists(out):
        os.remove(out)
    if os.path.isdir(built):
        for name in os.listdir(built):
            if name.endswith(".rgb"):
                os.remove(os.path.join(built, name))      # no picture of an earlier run is taken for this one's
    say("  running Unity (a few minutes the first time)...")
    try:
        code = subprocess.call([unity, "-batchmode", "-quit", "-projectPath", project, "-executeMethod",
                                "BuildScLandClaim.Build", "-buildTarget", "Win64", "-logFile", log])
    finally:
        if open(settings, "rb").read() != before:
            open(settings, "wb").write(before)
            say("  ProjectSettings.asset put back as it was")
    text = open(log, encoding="utf-8", errors="replace").read() if os.path.exists(log) else ""
    ours = [line for line in text.splitlines() if "[ScLandClaim]" in line]
    seen = set()
    for line in ours:
        if line not in seen:                    # Unity writes each log line twice
            seen.add(line)
            say("    " + line.split("[ScLandClaim] ", 1)[1])
    trouble = [line for line in text.splitlines()
               if "not supported because" in line or "error CS" in line or "is not defined" in line]
    for line in sorted(set(trouble))[:20]:
        say("  LOG: " + line.strip())
    if code != 0 or trouble or not os.path.isfile(out) or not any("done" in line for line in ours):
        sys.exit("the Unity build failed (exit code %s): see %s" % (code, log))
    return out


def check_bundle(path):
    import UnityPy
    env = UnityPy.load(path)
    kinds = {}
    for obj in env.objects:
        kinds[obj.type.name] = kinds.get(obj.type.name, 0) + 1
    say("  objects: " + ", ".join("%s %d" % kv for kv in sorted(kinds.items())))
    problems = []
    if not kinds.get("AssetBundle"):
        problems.append("no AssetBundle object: the AssetBundle module was missing from the build")
    names = [p.lower() for p in env.container]
    if not any(p.endswith("sc_landclaim.prefab") for p in names):
        problems.append("SC_LandClaim.prefab is not in the container: " + ", ".join(names))

    tagged, layered, scripts = {}, {}, []
    root_obj = None
    for obj in env.objects:
        if obj.type.name == "GameObject":
            go = obj.read()
            tagged[go.m_Name] = tagged.get(go.m_Name, []) + [go.m_Tag]
            layered[go.m_Name] = layered.get(go.m_Name, []) + [go.m_Layer]
            if go.m_Name == "SC_LandClaim":
                root_obj = obj
        elif obj.type.name == "MonoScript":
            ms = obj.read()
            scripts.append("%s, %s" % (ms.m_AssemblyName, ms.m_ClassName))
    say("  tags: " + ", ".join("%s %s" % (n, t) for n, t in sorted(tagged.items())))
    say("  layers: " + ", ".join("%s %s" % (n, t) for n, t in sorted(layered.items())))
    say("  scripts: " + "; ".join(scripts) if scripts else "  scripts: none")

    if tagged.get("SC_LandClaim") != [T_BLOCK] or layered.get("SC_LandClaim") != [BLOCK_LAYER]:
        problems.append("the root is not tagged T_Block (%d) on layer %d" % (T_BLOCK, BLOCK_LAYER))
    if tagged.get("Collision") != [T_BLOCK] or layered.get("Collision") != [BLOCK_LAYER]:
        problems.append("the Collision child is not T_Block on layer %d" % BLOCK_LAYER)
    for group in ("DMG0", "DMG1"):
        # keystoneBlock's MeshDamage names both; UpdateDamageState calls Find() with no null check.
        if tagged.get(group) != [0] or layered.get(group) != [0]:
            problems.append("%s is missing, or is not untagged on layer 0" % group)
    for part in PARTS:
        if len(tagged.get(part, [])) != 2:
            problems.append("%s should appear twice, once in DMG0 and once in DMG1" % part)
        elif set(tagged[part]) != {0} or set(layered.get(part, [])) != {0}:
            problems.append("%s should be untagged on layer 0" % part)
    if "Assembly-CSharp, RootTransformRefParent" not in scripts:        # as the game's own bundles name it
        problems.append("no RootTransformRefParent script from Assembly-CSharp")
    if kinds.get("MeshCollider", 0) != 2:
        problems.append("expected 2 mesh colliders (the locker and the desk), found %s" % kinds.get("MeshCollider", 0))
    if kinds.get("BoxCollider", 0) or kinds.get("CapsuleCollider", 0):
        problems.append("a box or capsule collider is in the bundle; on a block of many cells the game "
                        "would take it for the bounds of every cell")
    if root_obj is not None:
        root = root_obj.read()
        on_root = [c.component.deref().type.name for c in root.m_Component]
        say("  root components: " + ", ".join(on_root))
        if any(n.endswith("Collider") for n in on_root):
            problems.append("a collider sits on the prefab's root (the game would take it for the block's bounds)")
        if "LODGroup" not in on_root:
            problems.append("no LODGroup on the root")

    shaders = []
    for obj in env.objects:
        if obj.type.name == "Texture2D":
            tex = obj.read()
            img = tex.image                         # decoding it proves the pixel data came along
            say("  texture %s %dx%d %s, %d mip level(s)" % (tex.m_Name, tex.m_Width, tex.m_Height,
                                                            getattr(tex.m_TextureFormat, "name", tex.m_TextureFormat),
                                                            tex.m_MipCount))
            if max(tex.m_Width, tex.m_Height) > 1024 or img is None or tex.m_MipCount < 8:
                problems.append("texture %s is bigger than 1024 or has no mip chain" % tex.m_Name)
        elif obj.type.name == "Mesh":
            tree = obj.read_typetree()
            channels = tree["m_VertexData"]["m_Channels"]
            uv = channels[4] if len(channels) > 4 else {}
            agree = winding_agrees(obj)
            say("  mesh %s: %d vertices, UV format %s x%s, winding agrees with normals %.0f%%"
                % (tree["m_Name"], tree["m_VertexData"]["m_VertexCount"],
                   uv.get("format"), uv.get("dimension"), 100.0 * agree))
            if uv.get("format") != 0:               # 0 is 32-bit float
                problems.append("mesh %s has compressed UVs" % tree["m_Name"])
            # Every step that negates an axis mirrors the mesh and reverses what winding means.
            # Unity culls by winding, so if it disagrees with the authored normals the model renders
            # inside-out - you see through the locker doors and into the lamp shade. Blender does not
            # cull backfaces, so this is invisible until it reaches the game.
            if agree < 0.9:
                problems.append("mesh %s is inside-out: its winding agrees with its normals only %.0f%% "
                                "of the time (reverse the triangle winding wherever an axis is negated)"
                                % (tree["m_Name"], 100.0 * agree))
        elif obj.type.name == "Material":
            tree = obj.read_typetree()
            say("  material %s: keywords %s" % (tree["m_Name"], tree.get("m_ValidKeywords") or tree.get("m_ShaderKeywords")))
        elif obj.type.name == "Shader":
            tree = obj.read_typetree()
            name = tree["m_ParsedForm"]["m_Name"]
            platforms = tree.get("platforms") or tree.get("m_Platforms") or []
            say("  shader %s, compiled for platform ids %s" % (name, list(platforms)))
            shaders.append(name)
            if name != "Standard":
                problems.append("unexpected shader " + name)
            if len(platforms) < 2:
                problems.append("the shader %s is compiled for one graphics API only" % name)
    if shaders != ["Standard"]:
        problems.append("expected the Standard shader alone, found %s" % shaders)
    for p in problems:
        say("  PROBLEM: " + p)
    return not problems


def winding_agrees(mesh_obj):
    """What fraction of a mesh's triangles wind the same way their stored normals point."""
    import numpy as np
    from UnityPy.helpers.MeshHelper import MeshHandler
    h = MeshHandler(mesh_obj.read())
    h.process()
    if not h.m_Normals:
        return 1.0
    tris = []
    for sub in h.get_triangles():
        for f in sub:
            tris += list(f)
    v = np.asarray(h.m_Vertices, dtype=np.float64)
    t = np.asarray(tris, dtype=np.int64).reshape(-1, 3)
    a, b, c = v[t[:, 0]], v[t[:, 1]], v[t[:, 2]]
    gn = np.cross(b - a, c - a)
    ln = np.linalg.norm(gn, axis=1)
    ok = ln > 1e-12
    if not ok.any():
        return 1.0
    gn = gn[ok] / ln[ok, None]
    sn = np.asarray([n[:3] for n in h.m_Normals], dtype=np.float64)[t[ok, 0]]
    sl = np.linalg.norm(sn, axis=1)
    good = sl > 1e-9
    if not good.any():
        return 1.0
    dots = (gn[good] * (sn[good] / sl[good, None])).sum(axis=1)
    return float((dots > 0).mean())


def previews(project):
    from PIL import Image
    made = []
    built = os.path.join(project, BUILT)
    art = os.path.join(REPO, "art", "landclaim")
    for name in sorted(os.listdir(built)):
        if not name.endswith(".rgb"):
            continue
        stem, size = name[:-4].rsplit("_", 1)
        w, h = (int(v) for v in size.split("x"))
        data = open(os.path.join(built, name), "rb").read()
        if len(data) != w * h * 3:
            say("  preview %s has the wrong size, skipped" % name)
            continue
        img = Image.frombytes("RGB", (w, h), data).transpose(Image.FLIP_TOP_BOTTOM)
        if stem == "icon":
            out = os.path.join(REPO, "UIAtlases", "ItemIconAtlas", "scLandClaim.png")
            os.makedirs(os.path.dirname(out), exist_ok=True)
            icon(img).save(out)
        else:
            os.makedirs(art, exist_ok=True)
            out = os.path.join(art, stem + ".png")
            img.save(out)
        made.append(out)
    # The editor script swallows a failed Preview() as a WARN, which the log scan does not treat as
    # trouble, so without this a run can finish "clean" having written no icon at all - leaving
    # blocks.xml pointing CustomIcon at a file that is not there, or at the previous build's picture.
    if not any(os.path.basename(p) == "scLandClaim.png" for p in made):
        sys.exit("no icon came back from the build (look for 'WARN: no preview pictures' in the Unity log)")
    return made


def icon(img, size=160, margin=6):
    """The block's picture on flat magenta as an item icon: the magenta taken out (and out of the soft
    edges), cropped to the model, fitted into a square with a little room around it."""
    import numpy as np
    from PIL import Image
    a = np.asarray(img, dtype=np.float32) / 255.0
    pink = np.clip(np.minimum(a[..., 0], a[..., 2]) - a[..., 1], 0.0, 1.0)      # how much background is in a pixel
    alpha = 1.0 - pink
    bg = np.array([1.0, 0.0, 1.0], np.float32)
    rgb = np.clip((a - pink[..., None] * bg) / np.maximum(alpha[..., None], 0.05), 0.0, 1.0)
    rgba = np.dstack([rgb, np.where(alpha > 0.04, alpha, 0.0)])
    ys, xs = np.nonzero(rgba[..., 3] > 0.1)
    cut = Image.fromarray((rgba * 255.0 + 0.5).astype(np.uint8), "RGBA").crop(
        (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
    room = size - 2 * margin
    scale = min(room / cut.width, room / cut.height)
    cut = cut.resize((max(1, round(cut.width * scale)), max(1, round(cut.height * scale))), Image.LANCZOS)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(cut, ((size - cut.width) // 2, (size - cut.height) // 2), cut)
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--no-copy", action="store_true", help="build and check, but leave Resources/ alone")
    ap.add_argument("--inputs", default=INPUTS, help="the folder with the FBX and the textures (not in this repo)")
    ap.add_argument("--unity", default=UNITY)
    ap.add_argument("--project", default=PROJECT)
    args = ap.parse_args()

    if not os.path.isfile(args.unity):
        sys.exit("no Unity at " + args.unity)
    if not os.path.isdir(args.project):
        sys.exit("no Unity project at " + args.project)

    say("inputs")
    copy_inputs(args.project, args.inputs)
    say("tags")
    ensure_tags(args.project)
    say("build")
    out = run_unity(args.unity, args.project)
    say("check")
    ok = check_bundle(out)
    # The pictures come AFTER the gate: they overwrite the icon this mod ships, and a bundle that
    # failed its checks is not copied, so writing its icon would leave Resources/ and UIAtlases/
    # showing two different builds.
    if not ok:
        sys.exit("the bundle has problems (above): neither it nor its icon were written")
    say("pictures")
    for made in previews(args.project):
        say("  wrote " + made)
    if args.no_copy:
        say("built %s (%d bytes); --no-copy, so Resources/ is untouched" % (out, os.path.getsize(out)))
        return
    os.makedirs(os.path.dirname(DEPLOYED), exist_ok=True)
    shutil.copyfile(out, DEPLOYED)
    say("copied -> %s (%d bytes)" % (DEPLOYED, os.path.getsize(DEPLOYED)))


main()
