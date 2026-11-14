using System;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Makes the land claim block light like a block.
    ///
    /// The model ships in SC's own AssetBundle, and a mod Unity project only has Unity's `Standard`
    /// shader. The game draws its own blocks with "Game/Entity Tint Mask" and lights them by pushing a
    /// float into it: UpdateLight.UpdateLighting reads the sun level of the chunk the block stands in
    /// and calls SetFloatProp("_MacroAO", lit, renderers), through the UpdateLightOnAllMaterials
    /// component that every vanilla block prefab carries. Standard has no _MacroAO, so that write goes
    /// nowhere and the block sits at one brightness while the props beside it change with the light.
    ///
    /// Both halves are needed: the game's shader so the property exists, and the component so something
    /// drives it. The shader cannot be referenced when the bundle is built - it lives in the game's own
    /// CAB archives (shaders_assets_all.bundle) and no Shader.Find in the game names it - so it is
    /// swapped in the first time the prefab comes out of the bundle. Materials are shared assets, so
    /// doing it once covers every claim in the world.
    ///
    /// If the shader cannot be found the block keeps Standard, but the _rmom map is taken back off it:
    /// that map is roughness/metallic/occlusion, and Standard would read its red channel as metallic and
    /// draw a shiny metal desk. Flat and matte is the better way to be wrong.
    /// </summary>
    public static class Patches_LandClaimModel
    {
        /// <summary>The prefab inside SC's bundle; blocks.xml points keystoneBlock's Model at it.</summary>
        public const string ModelName = "SC_LandClaim";

        /// <summary>
        /// The shader every vanilla block material uses (pathID 9077681516710986715 in
        /// Data/Addressables/Standalone/shaders_assets_all.bundle). The only one in the game with
        /// _MacroAO or _RMOM.
        /// </summary>
        public const string GameShaderName = "Game/Entity Tint Mask";

        [HarmonyPatch(typeof(BlockShapeModelEntity), "PoolLoadCallback")]
        public static class Patch_PoolLoadCallback
        {
            [HarmonyPostfix]
            public static void Postfix(BlockShapeModelEntity __instance, Transform __result)
            {
                try
                {
                    if (__result == null || __instance == null) return;
                    if (!string.Equals(__instance.modelName, ModelName, StringComparison.OrdinalIgnoreCase)) return;
                    Apply(__result);
                }
                catch (Exception e)
                {
                    ScLog.Once("land claim model shader", e);
                }
            }
        }

        private static Shader cached;

        private static Shader FindGameShader()
        {
            // The game's own route: GlobalAssets.FindShader looks the name up in its addressables
            // shader map and falls back to Shader.Find. GameObjectPool.Init calls it with this exact
            // name at app start and holds the result in tintMaskShader, so the shader is resident
            // long before any block's Init runs. (The != null goes through Unity's overloaded check,
            // so a shader destroyed by a bundle reload is correctly looked up again.)
            if (cached != null) return cached;
            cached = GlobalAssets.FindShader(GameShaderName);
            return cached;
        }

        private static void Apply(Transform prefab)
        {
            Shader game = FindGameShader();
            int swapped = 0, cleared = 0, already = 0;

            // One pass per MATERIAL, not per renderer. DMG1 is a copy of DMG0, so the eight
            // renderers share these same four materials and walking renderers would "swap" four and
            // then find four "already done", which reads like something half-worked in the log.
            // includeInactive: DMG1, the damaged state's group, is inactive in the prefab.
            var seen = new System.Collections.Generic.HashSet<Material>();
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                foreach (Material m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null) continue;
                    if (!seen.Add(m)) continue;
                    if (m.shader.name == GameShaderName) { already++; continue; }

                    // Read the maps off the Standard slots they were built into BEFORE the shader
                    // changes, or the properties are gone with it.
                    Texture albedo = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                    Texture normal = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
                    Texture rmom = m.HasProperty("_MetallicGlossMap") ? m.GetTexture("_MetallicGlossMap") : null;
                    Texture emissive = m.HasProperty("_EmissionMap") ? m.GetTexture("_EmissionMap") : null;

                    if (game == null)
                    {
                        if (rmom != null)
                        {
                            m.SetTexture("_MetallicGlossMap", null);
                            m.DisableKeyword("_METALLICGLOSSMAP");
                            m.SetFloat("_Metallic", 0f);
                            m.SetFloat("_Glossiness", 0.15f);
                            cleared++;
                        }
                        continue;
                    }

                    m.shader = game;
                    if (albedo != null) m.SetTexture("_MainTex", albedo);      // same name in both
                    if (normal != null) m.SetTexture("_Normal", normal);
                    if (rmom != null) m.SetTexture("_RMOM", rmom);
                    if (emissive != null) m.SetTexture("_Emissive", emissive);

                    // Floats the game's shader has and Standard does not, so without setting them
                    // the material takes the SHADER's defaults rather than a block's.
                    //
                    // _Tint is the one that matters: this is the Tint Mask shader, its default is
                    // 1 (tinting ON) and every vanilla block material sets it to 0. Left at 1 the
                    // block is painted flat _Color and renders pure white with no texture at all -
                    // which is exactly what the first build of this did.
                    m.SetFloat("_Tint", 0f);
                    m.SetColor("_Color", Color.white);
                    // Starting value only: UpdateLight overrides it per renderer through a
                    // MaterialPropertyBlock. 1 = fully lit, so a stuck block is bright, never black.
                    m.SetFloat("_MacroAO", 1f);
                    // Standard's _Cutoff default of 0.5 is serialized into these materials and the
                    // name exists on the game's shader too, so it carries over. Vanilla blocks run
                    // 0.5 happily, but only because their albedos are DXT1 and alpha reads 1.0; 0
                    // takes the dependency away for the day one of these gains an alpha channel.
                    m.SetFloat("_Cutoff", 0f);
                    swapped++;
                }
            }

            // What actually drives _MacroAO. Vanilla block prefabs carry this; ours cannot, because the
            // class is the game's. IsDynamicObject stays false and appliedLit -1, as the vanilla land
            // claim prefab has them.
            bool addedLight = false;
            if (prefab.GetComponent<UpdateLightOnAllMaterials>() == null)
            {
                prefab.gameObject.AddComponent<UpdateLightOnAllMaterials>();
                addedLight = true;
            }

            if (game == null)
            {
                ScLog.Warn("land claim model: shader \"" + GameShaderName + "\" not found, so the block keeps "
                           + "Unity's Standard and will not light with the world. Cleared the _rmom map off "
                           + cleared + " material(s) so it does not read as metal.");
                return;
            }
            if (swapped > 0 || addedLight)
            {
                ScLog.Out("land claim model: " + swapped + " material(s) moved to \"" + GameShaderName + "\""
                          + (already > 0 ? " (" + already + " already on it)" : "")
                          + (addedLight ? ", UpdateLightOnAllMaterials added" : "") + " - it lights with the world now.");
            }
        }
    }
}
