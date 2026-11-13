using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Builds sclandclaim, the asset bundle holding SC_LandClaim: the land claim block's model, a desk
// with a lamp and a claim ledger beside a tall steel locker.
//
// The geometry is four of the game's own block models baked into one mesh, so the inputs are NOT in
// the repo (they are The Fun Pimps' assets and this repo is public). Tools/build_landclaim_bundle.py
// copies them in from the working folder outside it, runs
//   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod BuildScLandClaim.Build
// and checks and copies the result. Nothing here writes into a mod folder.
//
// What the game needs of a block model (read off 3.4's own landClaimBlockPrefab and deskOfficePrefab,
// the block this replaces and vanilla's own multi-cell ModelEntity block):
//  - The root is at identity, tagged T_Block on layer 16 (TerrainCollision).
//  - NO collider on the root. A box or capsule there becomes the block's own bounds
//    (BlockShapeModelEntity.PoolCreateOnceToAllCallBack) and on a block of many cells EVERY cell
//    would get that one box. deskOfficePrefab, vanilla's 2-wide desk, has none for that reason and
//    puts a MeshCollider on a tagged child instead. This follows it.
//  - A ray that hits a T_Block collider is traced to its block by RootTransformRefParent.FindRoot,
//    which returns the collider's own transform unless a RootTransformRefParent sits on it or above
//    it, so the collider child carries one pointing at the root.
//  - Tags are stored by number, 20000 + their place in the PROJECT's tag list, and read back against
//    the GAME's list, so the project's list must start as the game's does (T_Block is fifth). The
//    tool sees to that and the build fails if the tag is missing.
//  - keystoneBlock carries MeshDamage "DMG0,7000,DMG1,1,", and
//    BlockShapeModelEntity.UpdateDamageState does transform.Find(name).gameObject with NO null check,
//    so a prefab without BOTH groups throws the moment the block takes damage. Transform.Find only
//    looks at direct children, so DMG0 and DMG1 must sit directly under the root. They hold the same
//    meshes here: there is no broken version of this model, and a damaged claim simply looks the same.
//  - The default ModelOffset is 0,0.5,0 and the game's own models are sunk half a block to match.
//    This one stands on y = 0 and is centred on its parent cell, so its block needs ModelOffset 0,0,0.
//    Placement works out as world = P + (0.5, 0, 0.5) for a 3 x 2 x 1 block; see the repo's
//    SC_LandClaim README for the derivation.
//
// The prefab:
//   SC_LandClaim            root: T_Block, layer 16, LODGroup
//     Collision             T_Block, layer 16, RootTransformRefParent: the locker's and desk's meshes
//     DMG0                  layer 0, active: the four renderers
//     DMG1                  layer 0, inactive: the same four again
public static class BuildScLandClaim
{
    const string SrcDir = "Assets/SurvivorCommunity/LandClaim";
    const string GenDir = "Assets/_gensclandclaim";
    const string OutDir = "BuiltScLandClaim";
    const string BundleName = "sclandclaim";
    const string PrefabName = "SC_LandClaim";
    const string BlockTag = "T_Block";      // Tags.BlocksMeshBlockEntity
    const int BlockLayer = 16;              // TerrainCollision

    // The baked model, in Unity metres with the origin at the parent cell's centre and the footprint's
    // floor. A 3 x 2 x 1 block, so 3 wide, 2 tall, 1 deep, and x runs -1.5 to 1.5.
    static readonly Vector3 WantMin = new Vector3(-1.5f, 0f, -0.5f);
    static readonly Vector3 WantSize = new Vector3(3f, 2f, 1f);
    const float Tol = 0.02f;

    // FBX object name -> material name. Each part carries exactly one material.
    static readonly Dictionary<string, string> PartMaterial = new Dictionary<string, string>
    {
        { "locker_ver4_loot", "locker" },
        { "deskOffice", "deskOffice" },
        { "lampDesk", "lampDesk" },
        { "bookScarletJournals", "Books" },
    };

    // Which parts are worth colliding with. The lamp and the ledger are small things sitting on the
    // desk; a mesh collider for each would cost more than it is worth.
    static readonly string[] CollidingParts = { "locker_ver4_loot", "deskOffice" };

    public static void Build()
    {
        try
        {
            Run();
        }
        catch (System.Exception e)
        {
            Log("ERROR: " + e);
            Exit(1);
        }
    }

    static void Run()
    {
        Log("start");
        string fbx = SrcDir + "/" + PrefabName + ".fbx";
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        if (!File.Exists(fbx)) { Log("ERROR: missing " + fbx); Exit(2); return; }

        if (AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.DeleteAsset(GenDir);
        Directory.CreateDirectory(GenDir);
        AssetDatabase.Refresh();

        SetModel(fbx);

        var materials = new Dictionary<string, Material>();
        foreach (var pair in PartMaterial)
        {
            var mat = MakeMaterial(pair.Value);
            if (mat == null) { Exit(3); return; }
            materials[pair.Key] = mat;
        }

        string prefab = MakePrefab(fbx, materials);
        if (prefab == null) { Exit(4); return; }

        // CreateAsset alone leaves the data unflushed in batchmode: the bundle then ships a container
        // entry pointing at an empty asset.
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        try { Preview(prefab); }
        catch (System.Exception e) { Log("WARN: no preview pictures: " + e.Message); }

        // The Standard shader goes into the bundle compiled for the graphics APIs of the build target.
        // Left to itself that is Direct3D only, and the model is pink for anyone on Linux. The list is
        // put back afterwards: the project is shared with other mods.
        var target = BuildTarget.StandaloneWindows64;
        bool wasDefault = PlayerSettings.GetUseDefaultGraphicsAPIs(target);
        var wasApis = PlayerSettings.GetGraphicsAPIs(target);
        AssetBundleManifest manifest;
        Directory.CreateDirectory(OutDir);
        try
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            PlayerSettings.SetGraphicsAPIs(target, new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Vulkan,
                                                           GraphicsDeviceType.OpenGLCore });
            manifest = BuildPipeline.BuildAssetBundles(OutDir,
                new[] { new AssetBundleBuild { assetBundleName = BundleName, assetNames = new[] { prefab } } },
                BuildAssetBundleOptions.ForceRebuildAssetBundle, target);
        }
        finally
        {
            PlayerSettings.SetGraphicsAPIs(target, wasApis);
            PlayerSettings.SetUseDefaultGraphicsAPIs(target, wasDefault);
        }
        if (manifest == null) { Log("ERROR: BuildAssetBundles returned null"); Exit(5); return; }

        string outFile = Path.GetFullPath(Path.Combine(OutDir, BundleName));
        var check = AssetBundle.LoadFromFile(outFile);
        if (check == null) { Log("ERROR: LOADBACK: the bundle failed to load"); Exit(6); return; }
        var names = check.GetAllAssetNames();
        Log("LOADBACK: " + names.Length + " asset(s): " + string.Join(", ", names));
        var loaded = check.LoadAsset<GameObject>(PrefabName);
        if (loaded == null) { Log("ERROR: LOADBACK: no asset named " + PrefabName); check.Unload(true); Exit(7); return; }
        Describe(loaded.transform, 0);
        check.Unload(true);
        Log("built " + outFile + " (" + new FileInfo(outFile).Length + " bytes)");
        Log("done");
        Exit(0);
    }

    // ---- inputs ----------------------------------------------------------------------------------

    static Material MakeMaterial(string name)
    {
        string albedo = SrcDir + "/" + name + "_d.png";
        string normal = SrcDir + "/" + name + "_n.png";
        string mask = SrcDir + "/" + name + "_mask.png";
        string emissive = SrcDir + "/" + name + "_e.png";
        foreach (string p in new[] { albedo, normal, mask })
            if (!File.Exists(p)) { Log("ERROR: missing " + p); return null; }

        SetTexture(albedo, false, true);
        SetTexture(normal, true, false);
        SetTexture(mask, false, false);

        var mat = new Material(Shader.Find("Standard")) { name = name };
        mat.color = Color.white;
        mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
        mat.EnableKeyword("_NORMALMAP");
        // make_maps.py packs one texture to serve as both: R metallic, G occlusion, A smoothness.
        var maskTex = AssetDatabase.LoadAssetAtPath<Texture2D>(mask);
        mat.SetTexture("_MetallicGlossMap", maskTex);
        mat.EnableKeyword("_METALLICGLOSSMAP");
        mat.SetFloat("_GlossMapScale", 1f);
        mat.SetFloat("_SmoothnessTextureChannel", 0f);      // smoothness from the metallic map's alpha
        mat.SetTexture("_OcclusionMap", maskTex);
        mat.SetFloat("_OcclusionStrength", 1f);

        if (File.Exists(emissive))
        {
            // The desk lamp: in game the block the model was taken from is a Light, so a lit shade is
            // what the arrangement actually looks like. It glows without lighting the room.
            SetTexture(emissive, false, true);
            mat.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(emissive));
            mat.SetColor("_EmissionColor", Color.white);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            Log("material " + name + ": emissive");
        }
        AssetDatabase.CreateAsset(mat, GenDir + "/" + name + ".mat");
        return mat;
    }

    static void SetTexture(string path, bool normalMap, bool srgb)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            ti = AssetImporter.GetAtPath(path) as TextureImporter;
        }
        if (ti == null) throw new System.Exception("no texture importer for " + path);
        ti.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (!normalMap) ti.sRGBTexture = srgb;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.alphaIsTransparency = false;
        ti.mipmapEnabled = true;
        // The model is furniture seen from a metre or two; 2048 would quadruple the bundle for nothing.
        ti.maxTextureSize = 1024;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.anisoLevel = 16;
        ti.filterMode = FilterMode.Trilinear;
        ti.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.SaveAndReimport();
    }

    static void SetModel(string path)
    {
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (mi == null) throw new System.Exception("no model importer for " + path);
        // The file's values taken as metres, the axis turn baked into the vertices so every transform
        // stays at identity, no materials from the file, and Read/Write on with welding off, which
        // keeps the UVs at full precision and each corner on its own UV.
        mi.useFileScale = false;
        mi.globalScale = 1f;
        mi.bakeAxisConversion = true;
        mi.importBlendShapes = false;
        mi.importAnimation = false;
        mi.importCameras = false;
        mi.importLights = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.CalculateMikk;
        mi.meshCompression = ModelImporterMeshCompression.Off;
        mi.isReadable = true;
        mi.weldVertices = false;
        mi.SaveAndReimport();
    }

    // ---- the prefab --------------------------------------------------------------------------------

    static string MakePrefab(string fbx, Dictionary<string, Material> materials)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (model == null) { Log("ERROR: could not load " + fbx); return null; }
        var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        root.name = PrefabName;

        // Collect the four parts wherever the importer put them (directly under the root, or under one
        // wrapper node named after the file).
        var parts = new Dictionary<string, Transform>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            if (PartMaterial.ContainsKey(mf.gameObject.name))
                parts[mf.gameObject.name] = mf.transform;
        foreach (var want in PartMaterial.Keys)
            if (!parts.ContainsKey(want))
            {
                Log("ERROR: the model has no part called " + want + "; it has:");
                Describe(root.transform, 0);
                Object.DestroyImmediate(root);
                return null;
            }

        var dmg0 = new GameObject("DMG0");
        dmg0.transform.SetParent(root.transform, false);
        foreach (var pair in parts)
        {
            if (!IsIdentity(pair.Value))
            {
                Log("ERROR: " + pair.Key + " is not at identity: " + Pose(pair.Value)
                    + " (the FBX must be exported axis_forward=Y, axis_up=Z so Unity bakes the turn)");
                Object.DestroyImmediate(root);
                return null;
            }
            pair.Value.SetParent(dmg0.transform, false);
            var r = pair.Value.GetComponent<Renderer>();
            r.sharedMaterials = new[] { materials[pair.Key] };
        }

        // Anything the importer left behind (an empty wrapper node) goes.
        var strays = new List<Transform>();
        foreach (Transform t in root.transform)
            if (t != dmg0.transform) strays.Add(t);
        foreach (var t in strays)
        {
            Log("dropping stray node " + t.name);
            Object.DestroyImmediate(t.gameObject);
        }

        var bounds = Measure(root);
        Log("size " + bounds.size.ToString("F3") + " m, from " + bounds.min.ToString("F3")
            + " to " + bounds.max.ToString("F3"));
        if ((bounds.min - WantMin).sqrMagnitude > Tol * Tol * 3f
            || (bounds.size - WantSize).sqrMagnitude > Tol * Tol * 3f)
        {
            Log("ERROR: that is not a 3 x 2 x 1 block standing on y = 0 centred on its parent cell"
                + " (wanted min " + WantMin.ToString("F3") + " size " + WantSize.ToString("F3") + ")");
            Object.DestroyImmediate(root);
            return null;
        }

        // The damaged state: keystoneBlock's MeshDamage names two groups and the game calls
        // Find() on both without a null check. There is no broken version of this model, so the
        // second group is the same meshes and a damaged claim looks the same.
        var dmg1 = (GameObject)Object.Instantiate(dmg0, root.transform);
        dmg1.name = "DMG1";
        dmg1.transform.localPosition = Vector3.zero;
        dmg1.transform.localRotation = Quaternion.identity;
        dmg1.transform.localScale = Vector3.one;
        foreach (Transform t in dmg1.transform) t.gameObject.name = t.gameObject.name.Replace("(Clone)", "");
        dmg1.SetActive(false);

        root.tag = BlockTag;
        root.layer = BlockLayer;
        foreach (var group in new[] { dmg0, dmg1 })
        {
            group.layer = 0;
            foreach (Transform t in group.transform) t.gameObject.layer = 0;
        }

        // Collision: the locker's and the desk's own meshes, on a child of their own so the block does
        // not take one box as the bounds of all six of its cells.
        var collision = new GameObject("Collision") { tag = BlockTag, layer = BlockLayer };
        collision.transform.SetParent(root.transform, false);
        collision.transform.SetAsFirstSibling();
        collision.AddComponent<RootTransformRefParent>().RootTransform = root.transform;
        foreach (string part in CollidingParts)
        {
            var mesh = parts[part].GetComponent<MeshFilter>().sharedMesh;
            var mc = collision.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = false;          // as deskOfficePrefab has it: a static, exact collider
            Log("collider: " + part + " (" + mesh.triangles.Length / 3 + " tris)");
        }

        // One level of detail holding everything: the game sets the distance it is culled at from the
        // group's size (BlockShapeModelEntity.PoolCreateCallBack, the block's LODCullScale).
        var lod = root.AddComponent<LODGroup>();
        lod.SetLODs(new[] { new LOD(0.02f, root.GetComponentsInChildren<Renderer>(true)) });
        lod.fadeMode = LODFadeMode.CrossFade;
        lod.animateCrossFading = true;
        lod.RecalculateBounds();

        string path = GenDir + "/" + PrefabName + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        // Force the prefab into the AssetDatabase before bundling, or batchmode writes no container
        // entry for it and the game rejects the bundle as "incompatible".
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        Log("prefab saved: " + path);
        return path;
    }

    static Bounds Measure(GameObject root)
    {
        var bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return bounds;
    }

    // ---- pictures ----------------------------------------------------------------------------------

    static void Preview(string prefabPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        bool wasAsync = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;       // or the first frame is drawn with the placeholder shader
        try
        {
            var inst = (GameObject)Object.Instantiate(prefab);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.LookRotation(new Vector3(-0.45f, -0.5f, 0.75f));
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.22f, 0.23f, 0.25f);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;
            // The block fronts onto +Z, so the viewer stands at +Z looking back.
            Shot(cam, new Vector3(-2.2f, 1.9f, 3.6f), new Vector3(0f, 0.85f, 0f), "preview_front");
            Shot(cam, new Vector3(2.6f, 1.6f, 2.8f), new Vector3(0.3f, 0.9f, 0f), "preview_locker");
            // The block's icon, square, on a flat magenta the tool takes out again.
            cam.backgroundColor = new Color(1f, 0f, 1f);
            cam.fieldOfView = 28f;
            RenderSettings.ambientLight = new Color(0.62f, 0.64f, 0.68f);
            Shot(cam, new Vector3(-3.3f, 2.8f, 5.4f), new Vector3(0f, 0.9f, 0f), "icon", 512, 512);
            Object.DestroyImmediate(inst);
        }
        finally
        {
            ShaderUtil.allowAsyncCompilation = wasAsync;
        }
    }

    static void Shot(Camera cam, Vector3 from, Vector3 at, string name, int w = 1280, int h = 720)
    {
        cam.transform.position = from;
        cam.transform.rotation = Quaternion.LookRotation(at - from, Vector3.up);
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        rt.antiAliasing = 4;
        cam.targetTexture = rt;
        cam.Render();
        var was = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = was;
        cam.targetTexture = null;
        Directory.CreateDirectory(OutDir);
        File.WriteAllBytes(Path.Combine(OutDir, name + "_" + w + "x" + h + ".rgb"), tex.GetRawTextureData());
        Object.DestroyImmediate(tex);
        rt.Release();
        Log("picture: " + name);
    }

    // ---- reporting ---------------------------------------------------------------------------------

    static bool IsIdentity(Transform t)
    {
        return t.localPosition.sqrMagnitude < 1e-8f
            && Quaternion.Angle(t.localRotation, Quaternion.identity) < 0.01f
            && (t.localScale - Vector3.one).sqrMagnitude < 1e-8f;
    }

    static string Pose(Transform t)
    {
        return t.localPosition.ToString("F4") + " " + t.localEulerAngles.ToString("F2") + " " + t.localScale.ToString("F4");
    }

    static void Describe(Transform t, int depth)
    {
        string pad = new string(' ', depth * 2);
        string s = pad + t.name + "  tag=" + t.gameObject.tag + " layer=" + t.gameObject.layer
                 + (t.gameObject.activeSelf ? "" : " INACTIVE");
        foreach (var c in t.GetComponents<Component>())
        {
            // A component whose script could not be resolved comes back null, and null is not a
            // Transform - without this the one diagnostic you need becomes a NullReferenceException.
            if (c == null) { s += " [(missing script)]"; continue; }
            if (c is Transform) continue;
            s += " [" + c.GetType().Name;
            if (c is MeshCollider) s += " convex=" + ((MeshCollider)c).convex;
            if (c is LODGroup) s += " lods=" + ((LODGroup)c).lodCount + " size=" + ((LODGroup)c).size.ToString("F2");
            if (c is MeshRenderer) s += " mat=" + ((MeshRenderer)c).sharedMaterial?.name;
            if (c is RootTransformRefParent) s += " root=" + ((RootTransformRefParent)c).RootTransform?.name;
            s += "]";
        }
        Log(s);
        foreach (Transform child in t) Describe(child, depth + 1);
    }

    static void Exit(int code)
    {
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    static void Log(string m) { Debug.Log("[ScLandClaim] " + m); }
}
