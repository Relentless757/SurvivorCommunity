using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Rendering;

namespace SurvivorCommunity
{
    /// <summary>
    /// Head-and-shoulders pictures of community members for the community window (rowPortrait / selPortrait).
    ///
    /// Player-body survivors (Member.Look != null) are rebuilt from the Look alone - no entity needed - with the
    /// same SDCSUtils steps vanilla's UI previews use, in the ENTITY path's order (gear before hair) so helmets
    /// mask the hair exactly as they do on the live body. The community's 0-XNPCCore survivors wear an SC look as well
    /// (Patches_Xnpc builds them from it), so a member who joined as one has a Look and takes the same path. Pack NPCs
    /// (Look == null) can only be pictured while loaded: their live skinned meshes are baked into static copies on the
    /// stage (the entity is never touched).
    ///
    /// Everything renders on an isolated stage 1 km below the world origin, on layer 27 (no game camera renders
    /// it), with our own lights; sun, moon, ambient and fog are overridden only for the duration of Camera.Render.
    /// Each picture is rendered twice (black and white background) and the alpha is recovered from the
    /// difference, so it does not depend on what the deferred / hair shaders write into the alpha channel.
    ///
    /// Cached per member in memory; stored as &lt;save&gt;/survivor_portraits/&lt;id&gt;_&lt;born&gt;_&lt;hash&gt;.png so a relog, a
    /// supply run or a far-away member still has a picture. The hash covers the look and gear (or the class for
    /// pack NPCs) plus PortraitVersion, so a changed look re-shoots itself and old files are pruned.
    /// Main thread only (the PNG file write is the one background step). Host / single player only: the registry
    /// (and the save directory) exist only there.
    /// </summary>
    public static class ScPortraits
    {
        public const int Size = 256;                 // stored + displayed texture (72 and 190 px views sample its mips)
        private const int Super = 2;                 // render at 512 and box-downsample: deferred has no MSAA
        private const int StageLayer = 27;           // Constants.cLayerPlayerRagdollsOLD: unnamed, unused, culled by every game camera
        private static readonly Vector3 StagePos = new Vector3(0f, -1000f, 0f);
        private const int PortraitVersion = 1;       // bump when framing/lighting changes: every stored picture is re-shot
        private const string DirName = "survivor_portraits";
        // requestedMipmapLevel overrides streaming for a SHARED texture: a level above 0 would drop a live survivor
        // or the player of the same race/variant to a lower mip while the shot waits. 0 = the full texture.
        private const int MipLevelWanted = 0;
        private const int MipWaitFrames = 30;

        // framing - tune in game (a console verb can set these live)
        public static float Fov = 20f;               // vertical, degrees
        public static float Distance = 1.6f;         // camera to aim point, metres
        public static float Yaw = 20f;               // 3/4 view: camera swung this far round the face direction
        public static float EyeLift = 0.05f;         // camera height above the aim point
        public static float AimLift = 0.0f;          // aim point above the Head bone (the bone sits at the skull base)

        /// <summary>Bumped whenever any member's texture appears or changes: the window re-applies textures when it moves.</summary>
        public static int Version { get; private set; }

        /// <summary>
        /// Set by a community window that has portrait textures (rowPortrait / selPortrait). Until one exists no
        /// picture is shot: the original layout shows none, and a cold look build loads its assets synchronously.
        /// </summary>
        public static bool Wanted;

        /// <summary>
        /// Replaced / forgotten textures, destroyed a few frames later: a widget can still hold one for a frame or
        /// two (the window re-points it on the next Version poll, the view applies it on its next update).
        /// </summary>
        private static readonly List<KeyValuePair<Texture2D, int>> graveyard = new List<KeyValuePair<Texture2D, int>>();
        private const int GraveFrames = 3;

        private sealed class Entry
        {
            public string Want;          // key the member should have now
            public string Have;          // key of Tex (may be stale while a re-shoot runs)
            public Texture2D Tex;
            public bool Queued;
            public float RetryAt;        // live pack captures and failures wait for this
        }

        private static readonly Dictionary<int, Entry> cache = new Dictionary<int, Entry>();
        private static readonly List<int> queue = new List<int>();
        private static Coroutine pump;
        private static float busySince;               // when the current job started (watchdog)
        private const float StallSeconds = 20f;
        private static int generation;
        /// <summary>The rig and mip requests of the render in progress: Reset() unloads them before destroying the
        /// stage (StopCoroutine does not stop the nested render, and destroying the stage would null the rig first).</summary>
        private static GameObject inFlightRig;
        private static readonly List<Texture2D> inFlightMips = new List<Texture2D>();
        private static Stage stage;
        private static int lastDiskLoadFrame = -1;

        // ---------------------------------------------------------------- public API

        /// <summary>
        /// The picture for this member, or null (show the silhouette). Never blocks for a render: a missing or
        /// out-of-date picture is queued and <see cref="Version"/> moves when it arrives. A stale picture keeps
        /// showing until its replacement is ready.
        /// </summary>
        public static Texture2D Get(Member m)
        {
            if (m == null || GameManager.IsDedicatedServer || !Wanted) return null;
            try
            {
                DrainGraveyard(false);
                Entry e;
                if (!cache.TryGetValue(m.Id, out e)) { e = new Entry(); cache[m.Id] = e; }
                e.Want = KeyFor(m);
                if (e.Have != e.Want && !e.Queued && Time.unscaledTime >= e.RetryAt) Enqueue(m.Id, e, false);
                return e.Tex;
            }
            catch (Exception ex)
            {
                ScLog.Once("portrait-get", ex);
                return null;
            }
        }

        public static bool Has(Member m)
        {
            Entry e;
            return m != null && cache.TryGetValue(m.Id, out e) && e.Tex != null;
        }

        /// <summary>Ask for a (re)shoot now, ahead of the queue (the selected member, a member that just loaded in).</summary>
        public static void Request(Member m, bool urgent = false)
        {
            if (m == null || GameManager.IsDedicatedServer || !Wanted) return;
            Entry e;
            if (!cache.TryGetValue(m.Id, out e)) { e = new Entry(); cache[m.Id] = e; }
            e.Want = KeyFor(m);
            e.RetryAt = 0f;
            if (e.Have != e.Want) Enqueue(m.Id, e, urgent);
        }

        /// <summary>From the relink tick: a pack NPC that has no picture yet gets one while it is loaded and near.</summary>
        public static void OnMemberLoaded(Member m, EntityAlive npc)
        {
            if (m == null || m.Look != null || npc == null || !Wanted) return;
            Entry e;
            if (cache.TryGetValue(m.Id, out e) && e.Have == KeyFor(m)) return;
            Get(m);
        }

        /// <summary>Forget the picture (memory and disk). The next Get() shoots a new one.</summary>
        public static void Invalidate(int memberId)
        {
            Entry e;
            if (cache.TryGetValue(memberId, out e)) { Retire(e.Tex); e.Tex = null; e.Have = null; cache.Remove(memberId); Version++; }
            string dir = Dir();
            if (dir == null || !Directory.Exists(dir)) return;
            try { foreach (string f in Directory.GetFiles(dir, memberId + "_*.png")) File.Delete(f); }
            catch (Exception ex) { ScLog.Once("portrait-invalidate", ex); }
        }

        /// <summary>Delete stored pictures whose member is gone or whose look has changed (window open / world load).</summary>
        public static void Prune()
        {
            string dir = Dir();
            if (dir == null || !Directory.Exists(dir)) return;
            if (!ScRegistry.Loaded || ScRegistry.Degraded || ScRegistry.Members.Count == 0) return;   // never wipe on a bad load
            try
            {
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Member m in ScRegistry.Members.Values) keep.Add(KeyFor(m) + ".png");
                foreach (string f in Directory.GetFiles(dir, "*.png"))
                    if (!keep.Contains(Path.GetFileName(f))) File.Delete(f);
            }
            catch (Exception ex) { ScLog.Once("portrait-prune", ex); }
        }

        /// <summary>World start / shutdown: drop every texture, the queue, the stage (RTs, camera, lights) and any rig in flight.</summary>
        public static void Reset()
        {
            generation++;
            if (pump != null) { try { ThreadManager.StopCoroutine(pump); } catch (Exception) { } pump = null; }
            ClearMips(inFlightMips);
            UnloadRig(ref inFlightRig);
            queue.Clear();
            foreach (Entry e in cache.Values) DestroyTex(e);
            cache.Clear();
            DrainGraveyard(true);
            if (stage != null) { stage.Destroy(); stage = null; }
            Version++;
        }

        // ---------------------------------------------------------------- keys and files

        /// <summary>"&lt;id&gt;_&lt;born&gt;_&lt;hash&gt;": the hash covers what the picture shows, so it doubles as the staleness check.</summary>
        public static string KeyFor(Member m)
        {
            var sb = new StringBuilder(96);
            sb.Append('v').Append(PortraitVersion).Append('|');
            if (m.Look != null)
            {
                // no ScLooks.Validate here: it loads assets synchronously and this runs from binding code. The render
                // validates first and re-keys (a look with missing assets is rewritten to White/01 once, then stable).
                sb.Append("L|").Append(m.Look.Encode()).Append('|');
                for (int i = 0; i < 4; i++) sb.Append(m.Look.Gear[i] ?? "").Append(',');   // names only: quality does not show
            }
            else sb.Append("P|").Append(m.Class ?? "");
            return m.Id + "_" + m.Born + "_" + Fnv1a(sb.ToString()).ToString("x8");
        }

        private static uint Fnv1a(string s)
        {
            uint h = 2166136261;
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619; }
            return h;
        }

        private static string Dir()
        {
            string save = ScRegistry.SaveDir;
            return string.IsNullOrEmpty(save) ? null : Path.Combine(save, DirName);
        }

        private static Texture2D NewTexture()
        {
            // RGBA32, sRGB (linear:false) - the RT is sRGB too, so the bytes round-trip unchanged through PNG
            return new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = "scPortrait",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 0
            };
        }

        private static bool TryLoadFile(string key, out Texture2D tex)
        {
            tex = null;
            string dir = Dir();
            if (dir == null) return false;
            string path = Path.Combine(dir, key + ".png");
            if (!File.Exists(path)) return false;
            Texture2D tmp = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tmp.LoadImage(File.ReadAllBytes(path), false) || tmp.width != Size || tmp.height != Size)
                {
                    try { File.Delete(path); } catch (Exception) { }
                    return false;
                }
                tex = NewTexture();
                tex.SetPixels32(tmp.GetPixels32());          // copy so the display texture has a full mip chain
                tex.Apply(true, false);
                return true;
            }
            catch (Exception ex)
            {
                ScLog.Once("portrait-load", ex);
                return false;
            }
            finally { UnityEngine.Object.Destroy(tmp); }
        }

        private static void Deliver(int id, Entry e, string key, Color32[] px)
        {
            // the entry may have been replaced while the shot ran (Invalidate / "sc portrait"): hand the picture to
            // the current one, never to the orphan nothing can reach or free
            Entry cur;
            if (!cache.TryGetValue(id, out cur)) return;
            e = cur;
            string oldKey = e.Have;
            Texture2D tex = NewTexture();
            tex.SetPixels32(px);
            tex.Apply(true, false);
            SetTex(e, key, tex);
            string dir = Dir();
            if (dir == null) return;                        // client / no save yet: memory only
            byte[] png = tex.EncodeToPNG();                 // main thread; ImageConversion.EncodeArrayToPNG is the thread-safe alternative
            string path = Path.Combine(dir, key + ".png");
            // the member's previous picture is removed here, on the main thread: a background delete could race a
            // newer write for the same member and remove it. Anything else stale is Prune()'s job.
            if (!string.IsNullOrEmpty(oldKey) && oldKey != key)
            {
                try { string old = Path.Combine(dir, oldKey + ".png"); if (File.Exists(old)) File.Delete(old); }
                catch (Exception ex) { ScLog.Once("portrait-delete-old", ex); }
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string tmp = path + "." + Thread.CurrentThread.ManagedThreadId + ".tmp";
                    File.WriteAllBytes(tmp, png);
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmp, path);
                }
                catch (Exception ex) { Log.Warning("[SurvivorCommunity] portrait save failed: " + ex.Message); }
            });
        }

        private static void SetTex(Entry e, string key, Texture2D tex)
        {
            if (!ReferenceEquals(e.Tex, tex)) Retire(e.Tex);
            e.Tex = tex;
            e.Have = key;
            Version++;
        }

        private static void Retire(Texture2D tex)
        {
            if (tex != null) graveyard.Add(new KeyValuePair<Texture2D, int>(tex, Time.frameCount));
        }

        private static void DrainGraveyard(bool all)
        {
            for (int i = graveyard.Count - 1; i >= 0; i--)
            {
                if (!all && Time.frameCount - graveyard[i].Value < GraveFrames) continue;
                if (graveyard[i].Key != null) UnityEngine.Object.Destroy(graveyard[i].Key);
                graveyard.RemoveAt(i);
            }
        }

        private static void DestroyTex(Entry e)
        {
            if (e != null && e.Tex != null) UnityEngine.Object.Destroy(e.Tex);
            if (e != null) { e.Tex = null; e.Have = null; }
        }

        // ---------------------------------------------------------------- scheduler

        private static void Enqueue(int id, Entry e, bool urgent)
        {
            if (!e.Queued)
            {
                e.Queued = true;
                if (urgent) queue.Insert(0, id); else queue.Add(id);
            }
            else if (urgent) { queue.Remove(id); queue.Insert(0, id); }
            if (pump != null && busySince > 0f && Time.unscaledTime - busySince > StallSeconds)
            {
                // a job that never came back (an exception escaped a nested coroutine): start over
                ScLog.Warn("portrait queue stalled for " + StallSeconds + " s - restarting it");
                try { ThreadManager.StopCoroutine(pump); } catch (Exception) { }
                pump = null;
                generation++;                                // a still-suspended old render stands down instead of cleaning up ours
                ClearMips(inFlightMips);
                UnloadRig(ref inFlightRig);
            }
            if (pump == null) { busySince = Time.unscaledTime; pump = ThreadManager.StartCoroutine(Pump(generation)); }
        }

        /// <summary>One job at a time; a disk load costs one frame, a render a few; nothing runs faster than one per frame.</summary>
        private static IEnumerator Pump(int gen)
        {
            // never do the work inside the caller (Get() runs from binding / refresh code), and never finish
            // synchronously - Enqueue assigns 'pump' after StartCoroutine returns
            yield return null;
            while (gen == generation && queue.Count > 0)
            {
                busySince = Time.unscaledTime;
                DrainGraveyard(false);
                int id = queue[0];
                queue.RemoveAt(0);
                Entry e;
                if (!cache.TryGetValue(id, out e)) continue;
                e.Queued = false;                                                // even for a member who left: they may rejoin under this id
                Member m = ScRegistry.GetMember(id);
                if (m == null) continue;
                if (Time.unscaledTime < e.RetryAt) continue;                     // failed recently: Get() re-queues it later
                string key = null;
                bool loaded = false, render = false, look = false;
                if (lastDiskLoadFrame == Time.frameCount) yield return null;     // one PNG decode per frame
                try
                {
                    key = KeyFor(m);
                    e.Want = key;
                    if (e.Have != key)
                    {
                        Texture2D fromDisk;
                        if (TryLoadFile(key, out fromDisk))
                        {
                            lastDiskLoadFrame = Time.frameCount;
                            SetTex(e, key, fromDisk);
                            loaded = true;
                        }
                        else { render = true; look = m.Look != null; }
                    }
                }
                catch (Exception ex) { ScLog.Once("portrait-pump", ex); Fail(e, 30f); }
                if (loaded || !render) continue;
                if (look) yield return ThreadManager.StartCoroutine(RenderLook(id, e, key, gen));
                else yield return ThreadManager.StartCoroutine(RenderLive(id, e, key, gen));
                yield return null;
            }
            if (gen == generation) { pump = null; busySince = 0f; }
        }

        private static void Fail(Entry e, float retrySeconds)
        {
            e.RetryAt = Time.unscaledTime + retrySeconds;
        }

        // ---------------------------------------------------------------- player-body survivors: build from the Look

        /// <summary>
        /// The worn armor as SDCS slot data in equipment-slot order (what SDCSUtils.setupEquipment(entity) collects
        /// from a live entity, SDCSUtils.cs:1227-1249), plus the helmet's hair masks (:1242-1246).
        /// </summary>
        private static List<SDCSUtils.SlotData> Equipment(Look look, out SDCSUtils.SlotData.HairMaskTypes hair, out SDCSUtils.SlotData.HairMaskTypes facial)
        {
            hair = SDCSUtils.SlotData.HairMaskTypes.Full;
            facial = SDCSUtils.SlotData.HairMaskTypes.Full;
            var bySlot = new ItemClass[4];
            for (int i = 0; i < 4; i++)
            {
                if (string.IsNullOrEmpty(look.Gear[i])) continue;
                ItemClass ic = ItemClass.GetItemClass(look.Gear[i], true);
                if (ic == null) continue;
                ItemClassArmor armor = ic as ItemClassArmor;
                int slot = armor != null && (int)armor.EquipSlot >= 0 && (int)armor.EquipSlot < 4 ? (int)armor.EquipSlot : i;   // as ScLooks.SlotOf
                bySlot[slot] = ic;
            }
            List<SDCSUtils.SlotData> list = null;
            for (int s = 0; s < 4; s++)
            {
                ItemClass ic = bySlot[s];
                if (ic == null || ic.SDCSData == null) continue;
                if (list == null) list = new List<SDCSUtils.SlotData>();
                list.Add(ic.SDCSData);
                ItemClassArmor armor = ic as ItemClassArmor;
                if (armor != null && armor.EquipSlot == EquipmentSlots.Head)
                {
                    hair = ic.SDCSData.HairMaskType;
                    facial = ic.SDCSData.FacialHairMaskType;
                }
            }
            return list;
        }

        /// <summary>
        /// SDCSUtils.CreateVizUI(archetype, ref rig, ref catalog) (SDCSUtils.cs:887-913) re-ordered like the entity
        /// overload: gear first, then hair with the helmet's mask ("_hat" variant or none, :1437-1497). The
        /// no-entity overload builds hair first and unmasked, and blanking Archetype.Hair instead would also switch
        /// a {hair} helmet to its Bald variant (GetPathForSlotData, :1582-1585).
        /// </summary>
        private static void BuildLookRig(Look look, ref GameObject rig, ref SDCSUtils.TransformCatalog cat)
        {
            SDCSUtils.SlotData.HairMaskTypes hairMask, facialMask;
            Archetype arch = look.ToArchetype();
            arch.Equipment = Equipment(look, out hairMask, out facialMask);

            SDCSUtils.AssetRefs old = SDCSUtils.RemoveAssetOwnerForRig(rig);
            SDCSUtils.DestroyViz(rig);
            rig = null;
            SDCSUtils.tmpArchetype = arch;
            SDCSUtils.setupRig(ref rig, ref cat, SDCSUtils.baseRigPrefab, null, SDCSUtils.UIAnimController);
            SDCSUtils.tmpAssetsOwner = SDCSUtils.GetAssetOwnerForRig(rig);
            SDCSUtils.setupBase(rig, cat, SDCSUtils.baseParts, false);
            // NPC bodies are built with _ignoreDlcEntitlements = true (CreateVizTP passes !(entity is EntityPlayerLocal))
            SDCSUtils.setupEquipment(rig, cat, SDCSUtils.ignoredParts, true, arch.Equipment, true);
            if (hairMask == SDCSUtils.SlotData.HairMaskTypes.Hat && !string.IsNullOrEmpty(arch.Hair))
            {
                SDCSUtils.tmpArchetype = arch;               // baseHairLoc reads it
                SDCSUtils.setupHair(rig, cat, null, false, SDCSUtils.ignoredParts, true,
                    SDCSUtils.baseHairLoc + "/hair_" + arch.Hair + "_hat.asset", arch.Hair);
            }
            bool facial = facialMask != SDCSUtils.SlotData.HairMaskTypes.None;
            // emodel null = no mask suffix; also applies the hair colour swatch to the whole rig (the _hat hair included)
            SDCSUtils.setupHairObjects(rig, cat, null, false, SDCSUtils.ignoredParts, true,
                hairMask == SDCSUtils.SlotData.HairMaskTypes.Full ? arch.Hair : "",
                facial ? arch.MustacheName : "", facial ? arch.ChopsName : "", facial ? arch.BeardName : "");
            if (old != null) old.ReleaseAssets();

            Transform ik = rig.transform.Find("IKRig");
            Rig ikRig = ik != null ? ik.GetComponent<Rig>() : null;
            if (ikRig != null) ikRig.weight = 0f;
            foreach (HingeJoint j in rig.GetComponentsInChildren<HingeJoint>())
                if (j.connectedBody == null) j.gameObject.SetActive(false);
            SDCSUtils.fixCloths(rig);
        }

        private static void UnloadRig(ref GameObject rig)
        {
            if (rig == null) return;
            try
            {
                SDCSUtils.AssetRefs refs = SDCSUtils.RemoveAssetOwnerForRig(rig);
                SDCSUtils.DestroyViz(rig);                   // MeshMorph instances + material instances, then DestroyImmediate
                if (refs != null) refs.ReleaseAssets();
            }
            catch (Exception ex) { ScLog.Once("portrait-unload", ex); }
            rig = null;
        }

        private static bool PrepareLook(int id, out GameObject rig, out Transform head, List<Texture2D> mips)
        {
            rig = null;
            head = null;
            SDCSUtils.TransformCatalog cat = null;
            try
            {
                Member m = ScRegistry.GetMember(id);
                if (m == null || m.Look == null) return false;
                // the live body takes its sex from the entity (ScLooks.Sync); a record rolled before that fix could
                // disagree, and the survivor classes - the community's 0-XNPCCore survivors (npcSdcsSurvivorFemaleT2 and
                // so on) - carry the sex in their name
                if (ScXnpc.IsManagedClass(m.Class)) m.Look.Male = ScLooks.ClassIsMale(m.Class);
                ScLooks.Validate(m.Look);                    // base body/head assets exist, else White/01 (ScLooks.cs:499-514)
                BuildLookRig(m.Look, ref rig, ref cat);
                Stage st = EnsureStage();
                rig.transform.SetParent(st.Root, false);
                rig.transform.localPosition = Vector3.zero;
                rig.transform.localRotation = Quaternion.identity;       // the rig faces +Z (vanilla turns it 180 to face its camera)
                Utils.SetLayerRecursively(rig, StageLayer);
                Animator anim = rig.GetComponent<Animator>();
                if (anim != null)
                {
                    anim.applyRootMotion = false;
                    anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    anim.Update(0f);                                     // idle pose now (XUiC_SDCSPreviewWindow.cs:254)
                }
                if (!cat.TryGetValue("Head", out head) || head == null) { UnloadRig(ref rig); return false; }
                st.Frame(head.position, rig.transform.forward);
                CharacterGazeController gaze = head.GetComponent<CharacterGazeController>();
                if (gaze != null)
                {
                    gaze.LookAtTransformOverride = st.Cam.transform;    // look into the lens (as XUiC_CharacterFrameWindow.cs:466)
                    gaze.SnapNextUpdate();
                }
                RequestMips(rig, mips);
                return true;
            }
            catch (Exception ex)
            {
                ScLog.Once("portrait-build", ex);
                UnloadRig(ref rig);
                return false;
            }
        }

        private static IEnumerator RenderLook(int id, Entry e, string key, int gen)
        {
            var sw = Stopwatch.StartNew();
            GameObject rig;
            Transform head;
            ClearMips(inFlightMips);
            UnloadRig(ref inFlightRig);
            if (!PrepareLook(id, out rig, out head, inFlightMips)) { Fail(e, 30f); yield break; }
            inFlightRig = rig;
            long buildMs = sw.ElapsedMilliseconds;
            try
            {
                Member vm = ScRegistry.GetMember(id);
                if (vm != null) key = KeyFor(vm);            // Validate / the sex check may have rewritten the look
            }
            catch (Exception ex) { ScLog.Once("portrait-rekey", ex); }
            try
            {
                // at least one full frame: gaze Start + LateUpdate turn the head and eyes to the lens, cloth re-enables
                yield return null;                           // Start() of the gaze and eyelid controllers has run by now
                if (gen != generation || inFlightRig == null) yield break;
                try
                {
                    EyeLidController lid = head != null ? head.GetComponent<EyeLidController>() : null;
                    if (lid != null) lid.nextBlinkTime = Time.time + 60f;   // Start scheduled the first blink >= 1 s out (EyeLidController.cs:79)
                }
                catch (Exception ex) { ScLog.Once("portrait-lid", ex); }
                int frames = 1;
                while (gen == generation && !MipsLoaded(inFlightMips) && frames < MipWaitFrames) { yield return null; frames++; }
                yield return new WaitForEndOfFrame();
                if (gen != generation || inFlightRig == null || head == null || stage == null) yield break;
                try
                {
                    sw.Reset(); sw.Start();
                    stage.Frame(head.position, inFlightRig.transform.forward);
                    Color32[] px = stage.Capture();
                    long renderMs = sw.ElapsedMilliseconds;
                    if (!HasSubject(px)) { ScLog.Warn("portrait of member " + id + " came out empty"); Fail(e, 60f); }
                    else
                    {
                        Deliver(id, e, key, px);
                        ScLog.Verbose("portrait " + key + ": build " + buildMs + " ms, render+read+matte " + renderMs + " ms, " + frames + " wait frame(s)");
                    }
                }
                catch (Exception ex) { ScLog.Once("portrait-capture", ex); Fail(e, 60f); }
            }
            finally
            {
                if (gen == generation)
                {
                    ClearMips(inFlightMips);
                    UnloadRig(ref inFlightRig);
                }
            }
        }

        private static readonly List<Renderer> rendererBuf = new List<Renderer>();

        private static void RequestMips(GameObject root, List<Texture2D> into)
        {
            // Texture streaming is on and only cameras with a StreamingController drive it (quality setting
            // streamingMipmapsAddAllCameras = false); SDCS head textures are 2048 with streaming enabled.
            root.GetComponentsInChildren(true, rendererBuf);
            foreach (Renderer r in rendererBuf)
            {
                foreach (Material mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    foreach (int pid in mat.GetTexturePropertyNameIDs())
                    {
                        Texture2D t = mat.GetTexture(pid) as Texture2D;
                        if (t == null || !t.streamingMipmaps || into.Contains(t)) continue;
                        t.requestedMipmapLevel = MipLevelWanted;
                        into.Add(t);
                    }
                }
            }
            rendererBuf.Clear();
        }

        private static bool MipsLoaded(List<Texture2D> mips)
        {
            foreach (Texture2D t in mips) if (t != null && !t.IsRequestedMipmapLevelLoaded()) return false;
            return true;
        }

        private static void ClearMips(List<Texture2D> mips)
        {
            foreach (Texture2D t in mips) if (t != null) t.ClearRequestedMipmapLevel();
            mips.Clear();
        }

        // ---------------------------------------------------------------- pack NPCs: bake the live body

        private static bool CanShootLive(Member m, out EntityAlive npc)
        {
            npc = ScCommunity.FindNpc(m.Id);
            if (npc == null || npc.IsDead() || !npc.enabled || m.Status == MemberStatus.OnRun) return false;
            if (m.Born != 0 && npc.WorldTimeBorn != 0 && npc.WorldTimeBorn != m.Born) return false;   // reused id
            EModelBase em = npc.emodel;
            if (em == null || !em.visible || em.IsRagdollActive || em.GetModelTransform() == null) return false;
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (p == null || (p.position - npc.position).sqrMagnitude > 40f * 40f) return false;
            // drawn by some camera last frame, so its animator has posed it (culled animators freeze)
            em.GetModelTransform().GetComponentsInChildren(false, rendererBuf);
            bool seen = false;
            foreach (Renderer r in rendererBuf) if (r is SkinnedMeshRenderer && r.enabled && r.isVisible) { seen = true; break; }
            rendererBuf.Clear();
            return seen;
        }

        private static IEnumerator RenderLive(int id, Entry e, string key, int gen)
        {
            yield return new WaitForEndOfFrame();          // this frame's animation + LateUpdate are done
            if (gen != generation) yield break;
            Member m = ScRegistry.GetMember(id);
            EntityAlive npc;
            if (m == null || !CanShootLive(m, out npc)) { Fail(e, 5f); yield break; }
            var made = new List<GameObject>();
            var baked = new List<Mesh>();
            Color32[] px = null;
            try
            {
                Stage st = EnsureStage();
                Transform head = npc.emodel.GetHeadTransform();          // tag E_BP_Head = bone "Head" on the Xyth rigs
                Vector3 origin = head.position;
                BakeProxies(npc.emodel.GetModelTransform(), origin, st.Root, made, baked);
                st.Frame(st.Root.position, FaceDirection(npc, origin));
                px = st.Capture();
            }
            catch (Exception ex) { ScLog.Once("portrait-live", ex); }
            finally
            {
                foreach (GameObject go in made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
                foreach (Mesh mesh in baked) if (mesh != null) UnityEngine.Object.Destroy(mesh);
            }
            if (px == null || !HasSubject(px)) { Fail(e, 30f); yield break; }
            try { Deliver(id, e, key, px); }
            catch (Exception ex) { ScLog.Once("portrait-deliver", ex); Fail(e, 60f); }
        }

        /// <summary>Static copies of every drawn renderer of the model, placed relative to the head (head = stage origin).</summary>
        private static void BakeProxies(Transform model, Vector3 origin, Transform stageRoot, List<GameObject> made, List<Mesh> baked)
        {
            var skip = new HashSet<Renderer>();
            foreach (LODGroup g in model.GetComponentsInChildren<LODGroup>())
            {
                LOD[] lods = g.GetLODs();
                for (int i = 1; i < lods.Length; i++)
                    foreach (Renderer r in lods[i].renderers) if (r != null) skip.Add(r);
            }
            model.GetComponentsInChildren(false, rendererBuf);
            var mpb = new MaterialPropertyBlock();
            foreach (Renderer r in rendererBuf)
            {
                if (!r.enabled || skip.Contains(r)) continue;
                Mesh mesh;
                bool isBaked = false;
                SkinnedMeshRenderer smr = r as SkinnedMeshRenderer;
                if (smr != null)
                {
                    if (smr.sharedMesh == null) continue;
                    mesh = new Mesh();
                    smr.BakeMesh(mesh, true);          // current pose, blend shapes included; scale baked in
                    isBaked = true;
                }
                else if (r is MeshRenderer)
                {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    mesh = mf.sharedMesh;
                }
                else continue;
                var go = new GameObject("scPortraitProxy") { layer = StageLayer };
                go.transform.SetParent(stageRoot, false);
                go.transform.SetPositionAndRotation(stageRoot.position + (r.transform.position - origin), r.transform.rotation);
                go.transform.localScale = isBaked ? Vector3.one : r.transform.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = r.sharedMaterials;
                mr.shadowCastingMode = r.shadowCastingMode;
                mr.receiveShadows = r.receiveShadows;
                if (r.HasPropertyBlock()) { r.GetPropertyBlock(mpb); mr.SetPropertyBlock(mpb); mpb.Clear(); }
                made.Add(go);
                if (isBaked) baked.Add(mesh);
            }
            rendererBuf.Clear();
        }

        /// <summary>The body's facing, or toward the player when they are roughly in front (the head is usually turned to them).</summary>
        private static Vector3 FaceDirection(EntityAlive npc, Vector3 head)
        {
            Vector3 f = npc.transform.forward;
            f.y = 0f;
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (p != null && p.cameraTransform != null)
            {
                Vector3 to = p.cameraTransform.position - head;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f && Vector3.Angle(f, to) < 60f) f = to;
            }
            return f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
        }

        // ---------------------------------------------------------------- the stage

        private static Stage EnsureStage()
        {
            if (stage == null || stage.Root == null) { if (stage != null) stage.Destroy(); stage = new Stage(); }
            return stage;
        }

        private static bool HasSubject(Color32[] px)
        {
            int solid = 0;
            for (int i = 0; i < px.Length; i += 7) if (px[i].a > 128) solid++;
            return solid > px.Length / 7 / 50;              // at least ~2% of the frame
        }

        private sealed class Stage
        {
            public readonly Transform Root;
            public readonly Camera Cam;
            private readonly Light key, fill, rim;
            private readonly RenderTexture big, small;
            private readonly Texture2D read;
            private Color32[] black, white;

            private static readonly float[] toLinear = new float[256];
            private static readonly byte[] toSrgb = new byte[4096];

            static Stage()
            {
                for (int i = 0; i < 256; i++) toLinear[i] = Mathf.GammaToLinearSpace(i / 255f);
                for (int i = 0; i < 4096; i++) toSrgb[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.LinearToGammaSpace(i / 4095f) * 255f), 0, 255);
            }

            public Stage()
            {
                var root = new GameObject("scPortraitStage");
                root.layer = StageLayer;
                Root = root.transform;
                Root.SetPositionAndRotation(StagePos, Quaternion.identity);

                var camGo = new GameObject("scPortraitCamera") { layer = StageLayer };
                camGo.transform.SetParent(Root, false);
                Cam = camGo.AddComponent<Camera>();
                Cam.enabled = false;                                   // rendered by hand only; never tagged MainCamera
                Cam.cullingMask = 1 << StageLayer;
                Cam.clearFlags = CameraClearFlags.SolidColor;
                Cam.renderingPath = RenderingPath.DeferredShading;      // like the vanilla SDCS previews and the player camera (tier setting 3)
                Cam.allowHDR = true;
                Cam.allowMSAA = false;
                Cam.useOcclusionCulling = false;
                Cam.nearClipPlane = 0.05f;
                Cam.farClipPlane = 8f;
                Cam.fieldOfView = Fov;

                // colours and intensities from XUiC_CharacterFrameWindow.OnOpen (cs:342-393); positions set per shot
                key = MakeLight("scPortraitKey", new Color(0.9f, 0.8f, 0.7f), 1.5f, true);
                fill = MakeLight("scPortraitFill", Color.white, 0.6f, false);
                rim = MakeLight("scPortraitRim", new Color(0.4f, 0.75f, 1f), 1.2f, false);

                big = new RenderTexture(Size * Super, Size * Super, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "scPortraitBig" };
                small = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "scPortraitSmall", filterMode = FilterMode.Bilinear };
                big.filterMode = FilterMode.Bilinear;
                big.Create();
                small.Create();
                read = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                Cam.targetTexture = big;
            }

            private Light MakeLight(string name, Color color, float intensity, bool shadows)
            {
                var go = new GameObject(name) { layer = StageLayer };
                go.transform.SetParent(Root, false);
                Light l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = 60f;
                l.range = 8f;
                l.color = color;
                l.intensity = intensity;
                // default culling mask: deferred shading only partly honours light masks, and the lights are on only
                // inside Capture(), 1 km under the world
                l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
                l.shadowStrength = 0.3f;
                l.shadowBias = 0.005f;
                l.enabled = false;                                      // only on inside Capture()
                return l;
            }

            /// <summary>Aim at a head (world position) seen from faceDir, swung by Yaw; lights follow the camera.</summary>
            public void Frame(Vector3 head, Vector3 faceDir)
            {
                Vector3 f = faceDir;
                f.y = 0f;
                f = f.sqrMagnitude < 1e-4f ? Vector3.forward : f.normalized;
                Vector3 dir = Quaternion.AngleAxis(Yaw, Vector3.up) * f;
                Vector3 target = head + Vector3.up * AimLift;
                Cam.fieldOfView = Fov;
                Cam.transform.position = target + dir * Distance + Vector3.up * EyeLift;
                Cam.transform.LookAt(target);
                Place(key, target, Quaternion.AngleAxis(-40f, Vector3.up) * dir, 1.3f, 0.9f);
                Place(fill, target, Quaternion.AngleAxis(55f, Vector3.up) * dir, 1.5f, 0.3f);
                Place(rim, target, Quaternion.AngleAxis(160f, Vector3.up) * dir, 1.4f, 1.0f);
            }

            private static void Place(Light l, Vector3 target, Vector3 dir, float dist, float up)
            {
                l.transform.position = target + dir * dist + Vector3.up * up;
                l.transform.LookAt(target);
            }

            /// <summary>
            /// Two renders (black / white background), each box-downsampled to Size and read back; returns straight
            /// (un-premultiplied) sRGB colour with the alpha recovered from the difference, in linear light.
            /// </summary>
            public Color32[] Capture()
            {
                Light sun = SkyManager.sunLight, moon = SkyManager.moonLight;
                bool sunOn = sun != null && sun.enabled, moonOn = moon != null && moon.enabled;
                Color sky = RenderSettings.ambientSkyColor, equator = RenderSettings.ambientEquatorColor, ground = RenderSettings.ambientGroundColor;
                bool fog = RenderSettings.fog;
                Vector4 fogParams = Shader.GetGlobalVector("_FogParams");
                RenderTexture prevActive = RenderTexture.active;
                try
                {
                    if (sunOn) sun.enabled = false;                   // time of day must not change the picture
                    if (moonOn) moon.enabled = false;
                    RenderSettings.ambientSkyColor = new Color(0.42f, 0.42f, 0.44f);
                    RenderSettings.ambientEquatorColor = new Color(0.30f, 0.30f, 0.30f);
                    RenderSettings.ambientGroundColor = new Color(0.16f, 0.15f, 0.14f);
                    RenderSettings.fog = false;
                    Shader.SetGlobalVector("_FogParams", new Vector4(0f, fogParams.y, fogParams.z, fogParams.w));
                    key.enabled = fill.enabled = rim.enabled = true;
                    Cam.targetTexture = big;
                    black = Shot(new Color(0f, 0f, 0f, 0f), black);
                    white = Shot(new Color(1f, 1f, 1f, 0f), white);
                }
                finally
                {
                    key.enabled = fill.enabled = rim.enabled = false;
                    if (sunOn) sun.enabled = true;
                    if (moonOn) moon.enabled = true;
                    RenderSettings.ambientSkyColor = sky;
                    RenderSettings.ambientEquatorColor = equator;
                    RenderSettings.ambientGroundColor = ground;
                    RenderSettings.fog = fog;
                    Shader.SetGlobalVector("_FogParams", fogParams);
                    RenderTexture.active = prevActive;
                }
                return Matte(black, white);
            }

            private Color32[] Shot(Color background, Color32[] reuse)
            {
                Cam.backgroundColor = background;
                Cam.Render();
                Graphics.Blit(big, small);                               // 2x2 box filter, in linear (sRGB RTs)
                RenderTexture.active = small;
                read.ReadPixels(new Rect(0f, 0f, Size, Size), 0, 0, false);   // CPU copy only: no Apply needed for GetPixels32
                return read.GetPixels32();
            }

            private static Color32[] Matte(Color32[] b, Color32[] w)
            {
                var o = new Color32[b.Length];
                for (int i = 0; i < b.Length; i++)
                {
                    float br = toLinear[b[i].r], bg = toLinear[b[i].g], bb = toLinear[b[i].b];
                    float a = 1f - ((toLinear[w[i].r] - br) + (toLinear[w[i].g] - bg) + (toLinear[w[i].b] - bb)) * (1f / 3f);
                    if (a <= 0.004f) { o[i] = new Color32(0, 0, 0, 0); continue; }
                    if (a > 1f) a = 1f;
                    float inv = 1f / a;
                    o[i] = new Color32(Enc(br * inv), Enc(bg * inv), Enc(bb * inv), (byte)(a * 255f + 0.5f));
                }
                Bleed(o, Size, 3);
                return o;
            }

            /// <summary>
            /// Gives fully transparent pixels next to the subject the colour of their visible neighbours (alpha stays
            /// 0), a few passes out. The display texture's mips average neighbours together; without this the
            /// transparent black would darken every hair edge in the small row portrait.
            /// </summary>
            private static void Bleed(Color32[] px, int size, int passes)
            {
                var src = new Color32[px.Length];
                var filled = new bool[px.Length];
                for (int i = 0; i < px.Length; i++) filled[i] = px[i].a > 0;
                for (int p = 0; p < passes; p++)
                {
                    Array.Copy(px, src, px.Length);
                    var grew = new List<int>();
                    for (int y = 0; y < size; y++)
                    {
                        for (int x = 0; x < size; x++)
                        {
                            int i = y * size + x;
                            if (filled[i]) continue;
                            int r = 0, g = 0, b = 0, n = 0;
                            if (x > 0 && filled[i - 1]) { r += src[i - 1].r; g += src[i - 1].g; b += src[i - 1].b; n++; }
                            if (x < size - 1 && filled[i + 1]) { r += src[i + 1].r; g += src[i + 1].g; b += src[i + 1].b; n++; }
                            if (y > 0 && filled[i - size]) { r += src[i - size].r; g += src[i - size].g; b += src[i - size].b; n++; }
                            if (y < size - 1 && filled[i + size]) { r += src[i + size].r; g += src[i + size].g; b += src[i + size].b; n++; }
                            if (n == 0) continue;
                            px[i] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 0);
                            grew.Add(i);
                        }
                    }
                    if (grew.Count == 0) break;
                    for (int k = 0; k < grew.Count; k++) filled[grew[k]] = true;
                }
            }

            private static byte Enc(float linear)
            {
                int k = (int)(linear * 4095f + 0.5f);
                return toSrgb[k < 0 ? 0 : (k > 4095 ? 4095 : k)];
            }

            public void Destroy()
            {
                if (Cam != null) Cam.targetTexture = null;
                if (big != null) { big.Release(); UnityEngine.Object.Destroy(big); }
                if (small != null) { small.Release(); UnityEngine.Object.Destroy(small); }
                if (read != null) UnityEngine.Object.Destroy(read);
                if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
            }
        }
    }
}
