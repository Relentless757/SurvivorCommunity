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
    /// Both halves are needed: a material on the game's shader so the property exists, and the
    /// component so something drives it.
    ///
    /// Rather than move the bundle's own materials onto that shader and set its values by hand, each
    /// part BORROWS the material off the vanilla block its mesh came from. Same meshes, same UVs, same
    /// maps, so the vanilla material is simply the right answer, and everything on it comes along -
    /// shader, tint, keywords, GI flags, instancing, render queue - with nothing to get wrong.
    ///
    /// Setting the values by hand failed twice, and the reason is in PartDonor: the four materials do
    /// not agree with each other, so there is no single set of values that is correct for all of them.
    /// Vanilla materials come out of the game's bundles with positive instance IDs, so
    /// Utils.CleanupMaterials (which destroys only runtime-created ones) leaves them alone.
    ///
    /// If a vanilla prefab cannot be loaded, that part keeps the bundle's own Standard material: it
    /// carries the same maps, so the part still looks like itself and only misses the world's light.
    /// </summary>
    public static class Patches_LandClaimModel
    {
        /// <summary>The prefab inside SC's bundle; blocks.xml points keystoneBlock's Model at it.</summary>
        public const string ModelName = "SC_LandClaim";

        /// <summary>
        /// The shader every vanilla block material uses (pathID 9077681516710986715 in
        /// Data/Addressables/Standalone/shaders_assets_all.bundle), and the only one in the game with
        /// _MacroAO or _RMOM. Nothing here names it any more - borrowing a vanilla material brings it
        /// along - but it is the thing to grep for if this ever needs picking apart again.
        /// </summary>
        public const string GameShaderName = "Game/Entity Tint Mask";

        /// <summary>
        /// Each part of the baked model, and the vanilla block prefab its mesh and textures were
        /// taken from. The whole material is borrowed from there rather than rebuilt here: these are
        /// the same meshes with the same UVs and the same maps, so the vanilla material IS the right
        /// answer, and every value on it comes along - shader, tint, keywords, GI flags, instancing.
        ///
        /// Setting those by hand is how this went wrong twice. The four materials do NOT agree: the
        /// desk and the book pile run _Tint 0 with a white _Color, while the locker runs _Tint 1 with
        /// (0.80, 0.80, 0.77) and the lamp _Tint 1 with (0.19, 0.19, 0.19) - the lamp body is MEANT to
        /// be dark. There is no single set of values to apply.
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, string> PartDonor =
            new System.Collections.Generic.Dictionary<string, string>
            {
                { "locker_ver4_loot", "@:Entities/LootContainers/locker_ver4_lootPrefab.prefab" },
                { "deskOffice", "@:Entities/Furniture/deskOfficePrefab.prefab" },
                { "lampDesk", "@:Entities/Lighting/lampDeskPrefab.prefab" },
                { "bookScarletJournals", "@:Entities/Commercial/bookScarletJournalsPrefab.prefab" },
            };

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

        private static readonly System.Collections.Generic.Dictionary<string, Material> donorCache =
            new System.Collections.Generic.Dictionary<string, Material>();

        private static Material DonorMaterial(string assetPath)
        {
            Material cached;
            if (donorCache.TryGetValue(assetPath, out cached) && cached != null) return cached;
            GameObject prefab = DataLoader.LoadAsset<GameObject>(assetPath);
            if (prefab == null) return null;
            Renderer r = prefab.GetComponentInChildren<Renderer>(true);
            Material m = r != null ? r.sharedMaterial : null;
            if (m != null) donorCache[assetPath] = m;
            return m;
        }

        private static void Apply(Transform prefab)
        {
            // Added BEFORE the loop on purpose. If a donor load throws, the loop unwinds to the
            // postfix's catch, and with this below it the prefab would be left half-borrowed AND with
            // nothing driving _MacroAO - the worst of both. The component is harmless on its own.
            bool addedLight = false;
            if (prefab.GetComponent<UpdateLightOnAllMaterials>() == null)
            {
                prefab.gameObject.AddComponent<UpdateLightOnAllMaterials>();
                addedLight = true;
            }

            int borrowed = 0;
            var missing = new System.Collections.Generic.List<string>();

            // includeInactive: DMG1, the damaged state's group, is inactive in the prefab. Its four
            // renderers are copies of DMG0's and want the same materials, so both groups are walked.
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                string part = r.gameObject.name;
                string assetPath;
                if (!PartDonor.TryGetValue(part, out assetPath)) continue;
                try
                {
                    Material donor = DonorMaterial(assetPath);
                    if (donor == null)
                    {
                        // Leave the bundle's own Standard material alone: it carries the same maps, so
                        // the part still looks like itself, it just will not light with the world.
                        if (!missing.Contains(part)) missing.Add(part);
                        continue;
                    }
                    if (r.sharedMaterial == donor) continue;
                    r.sharedMaterials = new[] { donor };
                    borrowed++;
                }
                catch (Exception e)
                {
                    // One bad part must not cost the others theirs.
                    if (!missing.Contains(part)) missing.Add(part);
                    ScLog.Once("land claim donor " + part, e);
                }
            }

            if (missing.Count > 0)
            {
                ScLog.Warn("land claim model: could not load the vanilla prefab for " + string.Join(", ", missing.ToArray())
                           + ", so " + missing.Count + " part(s) keep the bundle's own material and will not light with "
                           + "the world.");
            }
            // Gated on borrowed, not on addedLight: adding the component while every part still sits on
            // Unity's Standard achieves nothing (Standard has no _MacroAO), and saying "it lights with
            // the world now" there would make a total failure read as a pass.
            if (borrowed > 0)
            {
                ScLog.Out("land claim model: " + borrowed + " renderer(s) now use the vanilla block materials"
                          + (addedLight ? ", UpdateLightOnAllMaterials added" : "") + " - it lights with the world now.");
            }
        }

    }
}
