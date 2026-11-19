using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round fx1 (2026-11-19, the user's ask: "It's hard to tell what direction a member will face when placing the watch
    /// post block. Is there a way to visually show which direction a posted member will face?"). A flat arrow lying where the
    /// guard stands (on top of the post), pointing the way they will look - ScWatch.Facing, the same direction the guard is
    /// turned to, so the arrow can't disagree with them. Shown, one at a time, by ScTower.Patch_FocusPreview's postfix on
    /// vanilla's focus cube update (every frame the player looks at something):
    ///   - holding a Watch post (any shape) with vanilla's placement ghost up: on the ghost, at the rotation it would get;
    ///   - holding the Guard Tower kit while its ghost shows: over the tower's post;
    ///   - looking at a placed Watch post within ArrowRange.
    /// Hidden the frame none of these holds (the keeper's LateUpdate). WatchPostArrow false = never. Local player only, no
    /// world change; a plain unlit mesh, rebuilt after a world change (Reset).
    /// </summary>
    public static class ScPostArrow
    {
        /// <summary>Meters: a placed post shows its arrow while looked at from this close.</summary>
        private const float ArrowRange = 8f;

        private static GameObject root;
        private static Material mat;
        private static Mesh mesh;
        private static bool shown, failed;
        private static int shownFrame = -10;

        private static readonly Color Tint = new Color(1f, 0.78f, 0.15f, 0.85f);

        public static void Reset()
        {
            try
            {
                if (mat != null) UnityEngine.Object.Destroy(mat);
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
                if (root != null) UnityEngine.Object.Destroy(root);
            }
            catch (Exception e)
            {
                ScLog.Once("post-arrow-reset", e);
            }
            mat = null;
            mesh = null;
            root = null;
            shown = false;
            failed = false;
        }

        /// <summary>
        /// The focus-cube postfix: decides whether an arrow shows this frame and where. towerPost: the tower kit's post, when its
        /// ghost showed this frame (ScTower).
        /// </summary>
        internal static void Focus(RenderDisplacedCube rdc, World world, WorldRayHitInfo hit, EntityAlive player, bool towerPostShown, Vector3i towerPost, Vector3 towerFacing)
        {
            if (!ScConfig.WatchPostArrow || !ScConfig.WatchPosts || world == null || player == null) return;
            if (towerPostShown)
            {
                Show(towerPost, towerFacing);
                return;
            }
            Inventory inv = player.inventory;
            if (inv != null && rdc != null && rdc.transformBlockPreview != null && inv.holdingItemItemValue.ToBlockValue().Block is BlockScWatchPost)
            {
                BlockValue bv = rdc.focusTransformBlockValue;
                if (bv.Block is BlockScWatchPost)
                {
                    Show(rdc.focusTransformPosition, ScWatch.Facing(bv));
                    return;
                }
            }
            if (hit == null || !hit.bHitValid || !GameUtils.IsBlockOrTerrain(hit.tag) || hit.hit.distanceSq > ArrowRange * ArrowRange) return;
            BlockValue looked = hit.hit.blockValue;
            if (looked.Block is BlockScWatchPost && !looked.ischild) Show(hit.hit.blockPos, ScWatch.Facing(looked));
        }

        private static void Show(Vector3i post, Vector3 facing)
        {
            if (failed) return;
            if (root == null) Build();
            if (root == null) return;
            shownFrame = Time.frameCount;
            if (!shown)
            {
                shown = true;
                root.SetActive(true);
            }
            // where the guard's feet go (the post's top, ScWatch's Room check stands them at post.y + 1), a hair above it
            root.transform.SetPositionAndRotation(new Vector3(post.x + 0.5f, post.y + 1.04f, post.z + 0.5f) - Origin.position,
                                                  Quaternion.LookRotation(facing.sqrMagnitude > 0.01f ? facing : Vector3.forward, Vector3.up));
        }

        public static void Hide()
        {
            if (!shown) return;
            shown = false;
            if (root != null) root.SetActive(false);
        }

        /// <summary>The keeper's LateUpdate: hidden once nobody asked for it this frame or the last.</summary>
        internal static void Tick()
        {
            if (shown && Time.frameCount - shownFrame > 1) Hide();
        }

        private static void Build()
        {
            try
            {
                Shader s = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Transparent/Diffuse");
                if (s == null)
                {
                    ScLog.Warn("watch post arrow: no unlit shader found - the arrow is off this game");
                    failed = true;
                    return;
                }
                mat = new Material(s);
                mat.color = Tint;
                if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", (int)CullMode.Off);
                if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 0);
                mat.renderQueue = 3100;

                root = new GameObject("ScWatchPostArrow");
                root.AddComponent<ScPostArrowKeeper>();
                var mf = root.AddComponent<MeshFilter>();
                var mr = root.AddComponent<MeshRenderer>();
                mesh = ArrowMesh();
                mf.sharedMesh = mesh;
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                int layer = LayerMask.NameToLayer("NotInReflections");
                if (layer >= 0) root.layer = layer;
                root.SetActive(false);
                shown = false;
                ScLog.Out("watch post arrow built (shader " + s.name + ")");
            }
            catch (Exception e)
            {
                ScLog.Once("post-arrow-build", e);
                Reset();
                failed = true;   // not rebuilt every frame after a failure; a new game tries again
            }
        }

        /// <summary>A flat arrow in the XZ plane pointing +Z, about 0.85 m long: a shaft and a head, both sides drawn.</summary>
        private static Mesh ArrowMesh()
        {
            float shaftW = 0.08f, headW = 0.27f, tail = -0.4f, neck = 0.05f, tip = 0.45f;
            var v = new[]
            {
                new Vector3(-shaftW, 0f, tail), new Vector3(shaftW, 0f, tail), new Vector3(shaftW, 0f, neck), new Vector3(-shaftW, 0f, neck),
                new Vector3(-headW, 0f, neck), new Vector3(headW, 0f, neck), new Vector3(0f, 0f, tip)
            };
            var m = new Mesh { name = "ScWatchPostArrow" };
            m.vertices = v;
            m.triangles = new[] { 0, 3, 2, 0, 2, 1, 4, 6, 5 };
            var white = new Color[v.Length];
            for (int i = 0; i < white.Length; i++) white[i] = Color.white;
            m.colors = white;   // Hidden/Internal-Colored multiplies the vertex color by _Color
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>Hides the watch post arrow the frame nothing shows it.</summary>
    public class ScPostArrowKeeper : MonoBehaviour
    {
        private void LateUpdate()
        {
            ScPostArrow.Tick();
        }
    }
}
