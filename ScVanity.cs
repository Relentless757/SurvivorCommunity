using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Vanity Vault's body paints, face paints and tattoos on survivors and raiders (the user's picks of 2026-10-07, ticked
    /// in SurvivorPaints_Choices.md). Rolled with the rest of a new look (ScLooks.Roll, host only), kept in the look
    /// (Look.BodyPaint / FacePaint / Tattoo) and put on after every body build (Patches_Vanity), through Vanity Vault's own
    /// public helpers - bound by reflection, so SC carries no reference to it and simply goes without paints when it is
    /// not installed (a look keeps its ids either way, and they come back with Vanity Vault).
    ///
    /// Vanity Vault's player code gates on EntityPlayer and never touches an NPC; the helpers used here only look at
    /// renderer and material names, and cache what they load per paint, shared with the player. A body paint is a whole
    /// skin with light skin baked in, so it comes with its face paint as a set ("body:face" in the config lists); a face
    /// paint can also come alone; one tattoo at most (Vanity Vault cannot stack them), applied last so it sits on top of a
    /// paint. Never loads Vanity Vault's bundles itself: a bundle held open would break Vanity Vault's own loads.
    /// Research: scratchpad mig\nf\vv_paint_research.md.
    /// </summary>
    public static class ScVanity
    {
        private static bool tried, ok;
        private static Action<SkinnedMeshRenderer[], string, bool> applyFace, applyBody;
        private static Action<SkinnedMeshRenderer[], string, string, string, string, bool> applyCosmetics;
        private static IDictionary bodyGroups, faceGroups, tattoos;
        private static string version = "?";
        /// <summary>
        /// Bodies whose last build ran while they were out of view (the rig root is off then, and Vanity Vault skips
        /// renderers that are not active): painted when they show again (Patches_Vanity.Patch_SetVisible).
        /// </summary>
        private static readonly HashSet<int> pending = new HashSet<int>();

        public static void Reset()
        {
            pending.Clear();   // the binding stays: Vanity Vault's registries live as long as the process
        }

        /// <summary>The body is gone (ScLooks.Forget): no paint waits for it any more.</summary>
        public static void Forget(int entityId)
        {
            pending.Remove(entityId);
        }

        /// <summary>Vanity Vault is loaded and its helpers and registries were found (bound on first use).</summary>
        public static bool Ready { get { Bind(); return ok; } }

        private static void Bind()
        {
            if (tried) return;
            tried = true;
            try
            {
                Type fm = AccessTools.TypeByName("Sonja.VanityVault.FacepaintManager");
                Type bm = AccessTools.TypeByName("Sonja.VanityVault.BodypaintManager");
                Type tm = AccessTools.TypeByName("Sonja.VanityVault.TattooMakeupManager");
                if (fm == null || bm == null || tm == null) { ScLog.Out("Vanity Vault not loaded - no paints or tattoos on survivors"); return; }
                Type smrs = typeof(SkinnedMeshRenderer[]), s = typeof(string), b = typeof(bool);
                MethodInfo mf = AccessTools.DeclaredMethod(fm, "ApplyFacepaint", new[] { smrs, s, b });
                MethodInfo mb = AccessTools.DeclaredMethod(bm, "ApplyBodypaint", new[] { smrs, s, b });
                MethodInfo mt = AccessTools.DeclaredMethod(tm, "ApplyCosmetics", new[] { smrs, s, s, s, s, b });
                if (mf != null && mf.IsStatic) applyFace = (Action<SkinnedMeshRenderer[], string, bool>)Delegate.CreateDelegate(typeof(Action<SkinnedMeshRenderer[], string, bool>), mf);
                if (mb != null && mb.IsStatic) applyBody = (Action<SkinnedMeshRenderer[], string, bool>)Delegate.CreateDelegate(typeof(Action<SkinnedMeshRenderer[], string, bool>), mb);
                if (mt != null && mt.IsStatic)
                    applyCosmetics = (Action<SkinnedMeshRenderer[], string, string, string, string, bool>)Delegate.CreateDelegate(
                        typeof(Action<SkinnedMeshRenderer[], string, string, string, string, bool>), mt);
                bodyGroups = Registry("Sonja.VanityVault.BodypaintRegistry", "KnownGroups");
                faceGroups = Registry("Sonja.VanityVault.FacepaintRegistry", "KnownGroups");
                tattoos = Registry("Sonja.VanityVault.TattooMakeupRegistry", "KnownTattoos");
                Mod vv = ModManager.GetModForAssembly(fm.Assembly);   // its ModInfo version (the assembly says 1.0.0.0)
                version = vv != null && !string.IsNullOrEmpty(vv.VersionString) ? vv.VersionString : fm.Assembly.GetName().Version.ToString();
                ok = applyFace != null && applyBody != null && applyCosmetics != null && bodyGroups != null && faceGroups != null && tattoos != null;
                if (!ok) { ScLog.Warn("Vanity Vault " + version + " is loaded but its paint code has moved - no paints or tattoos on survivors (Vanity Vault update?)"); return; }
                ScLog.Out("Vanity Vault " + version + ": paints and tattoos on survivors - it knows " + bodyGroups.Count + " body paints, " + faceGroups.Count
                          + " face paints, " + tattoos.Count + " tattoo variants" + Unknown());
            }
            catch (Exception e)
            {
                ok = false;
                ScLog.Once("vanity-bind", e);
            }
        }

        private static IDictionary Registry(string type, string field)
        {
            Type t = AccessTools.TypeByName(type);
            FieldInfo f = t != null ? AccessTools.Field(t, field) : null;
            return f != null && f.IsStatic ? f.GetValue(null) as IDictionary : null;
        }

        /// <summary>The configured names Vanity Vault does not know (renamed or removed there): logged once at bind.</summary>
        private static string Unknown()
        {
            var miss = new List<string>();
            foreach (List<string> sets in new[] { ScConfig.SurvivorBodyPaints, ScConfig.RaiderBodyPaints })
                foreach (string set in sets)
                {
                    string body, face;
                    SplitSet(set, out body, out face);
                    if (Key(bodyGroups, body) == null && !miss.Contains(body)) miss.Add(body);
                    if (face != null && Key(faceGroups, face) == null && !miss.Contains(face)) miss.Add(face);
                }
            foreach (List<string> faces in new[] { ScConfig.SurvivorFacePaints, ScConfig.RaiderFacePaints })
                foreach (string f in faces) if (Key(faceGroups, f) == null && !miss.Contains(f)) miss.Add(f);
            foreach (List<string> shapes in new[] { ScConfig.SurvivorTattoos, ScConfig.RaiderTattoos })
                foreach (string sh in shapes) if (TattooId(sh, "No Color", ScConfig.TattooMode) == null && !miss.Contains(sh)) miss.Add(sh);
            return miss.Count == 0 ? "" : "; not found there (skipped): " + string.Join(", ", miss.ToArray());
        }

        // ---------------------------------------------------------------- rolling (ScLooks.Roll)

        /// <summary>
        /// Host: paints for a new look - a body paint set, else maybe a face paint alone, and maybe a tattoo, each from the
        /// survivor or raider lists and chances in Config/blocks.xml, kept to what fits the look's sex.
        /// </summary>
        public static void Roll(Look look, GameRandom rnd, bool raider)
        {
            if (look == null || !ScCommunity.IsHost || !Ready) return;
            try
            {
                look.BodyPaint = look.FacePaint = look.Tattoo = "";
                if (Chance(rnd) < (raider ? ScConfig.RaiderBodyPaintChance : ScConfig.SurvivorBodyPaintChance))
                {
                    List<KeyValuePair<string, string>> sets = BodySets(raider ? ScConfig.RaiderBodyPaints : ScConfig.SurvivorBodyPaints, look.Male);
                    if (sets.Count > 0)
                    {
                        KeyValuePair<string, string> set = sets[Index(rnd, sets.Count)];
                        look.BodyPaint = set.Key;
                        look.FacePaint = set.Value ?? "";
                    }
                }
                // a face paint alone only on an unpainted body (a set brings its own, or keeps the face as it is)
                if (look.BodyPaint.Length == 0 && Chance(rnd) < (raider ? ScConfig.RaiderFacePaintChance : ScConfig.SurvivorFacePaintChance))
                {
                    List<string> faces = Faces(raider ? ScConfig.RaiderFacePaints : ScConfig.SurvivorFacePaints, look.Male);
                    if (faces.Count > 0) look.FacePaint = faces[Index(rnd, faces.Count)];
                }
                if (Chance(rnd) < (raider ? ScConfig.RaiderTattooChance : ScConfig.SurvivorTattooChance))
                {
                    List<string> inks = raider ? ScConfig.RaiderTattooInks : ScConfig.SurvivorTattooInks;
                    string ink = inks.Count > 0 ? inks[Index(rnd, inks.Count)] : "No Color";
                    List<string> shapes = Shapes(raider ? ScConfig.RaiderTattoos : ScConfig.SurvivorTattoos);
                    if (shapes.Count > 0) look.Tattoo = TattooId(shapes[Index(rnd, shapes.Count)], ink, ScConfig.TattooMode) ?? "";
                }
            }
            catch (Exception e)
            {
                ScLog.Once("vanity-roll", e);
            }
        }

        /// <summary>The usable body paint sets of a list for this sex: Vanity Vault's real body id, and its face id or null.</summary>
        public static List<KeyValuePair<string, string>> BodySets(List<string> list, bool male)
        {
            var sets = new List<KeyValuePair<string, string>>();
            if (!Ready || list == null) return sets;
            foreach (string entry in list)
            {
                string body, face;
                SplitSet(entry, out body, out face);
                string bk = Key(bodyGroups, body);
                if (bk == null || !Safe(bk) || !Fits(bodyGroups[bk], male)) continue;
                string fk = null;
                if (face != null)
                {
                    fk = Key(faceGroups, face);
                    if (fk == null || !Safe(fk) || !Fits(faceGroups[fk], male)) continue;   // a painted body under its own face only
                }
                sets.Add(new KeyValuePair<string, string>(bk, fk));
            }
            return sets;
        }

        /// <summary>The usable face paints of a list for this sex (Vanity Vault's real ids).</summary>
        public static List<string> Faces(List<string> list, bool male)
        {
            var faces = new List<string>();
            if (!Ready || list == null) return faces;
            foreach (string f in list)
            {
                string fk = Key(faceGroups, f);
                if (fk != null && Safe(fk) && Fits(faceGroups[fk], male)) faces.Add(fk);
            }
            return faces;
        }

        /// <summary>The tattoo shapes of a list Vanity Vault has (tattoos are for both sexes).</summary>
        public static List<string> Shapes(List<string> list)
        {
            var shapes = new List<string>();
            if (!Ready || list == null) return shapes;
            foreach (string sh in list) if (TattooId(sh, "No Color", ScConfig.TattooMode) != null) shapes.Add(sh);
            return shapes;
        }

        /// <summary>
        /// Vanity Vault's id for a tattoo shape in an ink: "Maori 03 (Black Orchid) [Blend]"; a shape without colours has
        /// only "(No Color)" (or no colour part at all); a shape that lacks this ink takes its first one. Null when the
        /// shape is unknown.
        /// </summary>
        public static string TattooId(string shape, string ink, string mode)
        {
            if (tattoos == null || string.IsNullOrEmpty(shape)) return null;
            string m = "[" + (string.Equals(mode, "Vivid", StringComparison.OrdinalIgnoreCase) ? "Vivid" : "Blend") + "]";
            string[] tries = { shape + " (" + (ink ?? "") + ") " + m, shape + " (No Color) " + m, shape + " " + m };
            foreach (string t in tries)
            {
                string k = Key(tattoos, t);
                if (k != null && Safe(k)) return k;
            }
            string withInk = shape + " (", bare = shape + " [";
            foreach (object o in tattoos.Keys)
            {
                string k = o as string;
                if (k != null && Safe(k) && k.EndsWith(m, StringComparison.Ordinal)
                    && (k.StartsWith(withInk, StringComparison.OrdinalIgnoreCase) || k.StartsWith(bare, StringComparison.OrdinalIgnoreCase))) return k;
            }
            return null;
        }

        // ---------------------------------------------------------------- applying

        /// <summary>
        /// Puts the look's paints on a freshly built rig: the face paint, the body paint, then the tattoo on top (Vanity
        /// Vault's own order for the player). Unknown ids are skipped and kept in the look.
        /// </summary>
        public static void Apply(GameObject rig, Look look, bool male)
        {
            if (rig == null || look == null || !look.HasPaint || !Ready) return;
            try
            {
                SkinnedMeshRenderer[] smrs = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs.Length == 0) return;
                string face = Key(faceGroups, look.FacePaint), body = Key(bodyGroups, look.BodyPaint), tattoo = StoredTattoo(look.Tattoo);
                if (face != null) applyFace(smrs, face, male);
                if (body != null) applyBody(smrs, body, male);
                if (tattoo != null) applyCosmetics(smrs, "None", "None", tattoo, "None", male);
            }
            catch (Exception e)
            {
                ScLog.Once("vanity-apply", e);
            }
        }

        /// <summary>A body build (Patches_Vanity.Patch_CreateVizTP): paint it now, or when it shows if it is out of view.</summary>
        public static void Built(EntityAlive entity, GameObject rig, Look look, bool male)
        {
            if (!rig.activeInHierarchy)
            {
                pending.Add(entity.entityId);
                Patches_Xnpc.Count("paints-hidden");
                return;
            }
            pending.Remove(entity.entityId);
            Patches_Xnpc.Count("paints");
            Apply(rig, look, male);
        }

        /// <summary>A body came into view (Patches_Vanity.Patch_SetVisible): the paints its last build could not put on.</summary>
        public static void Shown(EModelSDCS m)
        {
            if (pending.Count == 0 || m == null) return;
            Entity e = m.entity;
            if ((object)e == null || !pending.Contains(e.entityId)) return;
            GameObject rig = m.baseRig;
            if (rig == null || !rig.activeInHierarchy) return;   // still not drawn: wait for the next showing
            pending.Remove(e.entityId);
            if (!ScXnpc.IsBodyModel(m)) return;
            Look look = ScLooks.Get(e as EntityAlive);
            if (look == null || !look.HasPaint) return;
            Patches_Xnpc.Count("paints-late");
            Apply(rig, look, m.isMale);
        }

        /// <summary>A stored tattoo id, or - when Vanity Vault renamed its variants - the same shape, ink and mode found again.</summary>
        private static string StoredTattoo(string id)
        {
            if (string.IsNullOrEmpty(id) || tattoos == null) return null;
            string k = Key(tattoos, id);
            if (k != null) return k;
            string mode = id.IndexOf("[Vivid]", StringComparison.OrdinalIgnoreCase) >= 0 ? "Vivid" : "Blend";
            int cut = id.LastIndexOf(" (", StringComparison.Ordinal);
            if (cut < 0) cut = id.LastIndexOf(" [", StringComparison.Ordinal);
            if (cut <= 0) return null;
            string ink = "No Color";
            int close = id.IndexOf(')', cut);
            if (id[cut + 1] == '(' && close > cut) ink = id.Substring(cut + 2, close - cut - 2);
            return TattooId(id.Substring(0, cut), ink, mode);
        }

        // ---------------------------------------------------------------- console (sc paint)

        /// <summary>A body paint name as configured or typed: the set's face paint from the lists when it is one of them.</summary>
        public static string FaceFor(string body)
        {
            foreach (List<string> sets in new[] { ScConfig.SurvivorBodyPaints, ScConfig.RaiderBodyPaints })
                foreach (string set in sets)
                {
                    string b, f;
                    SplitSet(set, out b, out f);
                    if (string.Equals(b, body, StringComparison.OrdinalIgnoreCase)) return f != null ? Key(faceGroups, f) : null;
                }
            return null;
        }

        /// <summary>The lists name this body paint with no face paint (Clean: the survivor keeps their own face).</summary>
        public static bool ListedAlone(string body)
        {
            foreach (List<string> sets in new[] { ScConfig.SurvivorBodyPaints, ScConfig.RaiderBodyPaints })
                foreach (string set in sets)
                {
                    string b, f;
                    SplitSet(set, out b, out f);
                    if (f == null && string.Equals(b, body, StringComparison.OrdinalIgnoreCase)) return true;
                }
            return false;
        }

        public static string BodyKey(string name) { return Ready ? Key(bodyGroups, name) : null; }
        public static string FaceKey(string name) { return Ready ? Key(faceGroups, name) : null; }

        /// <summary>The paint has a version for this sex (Vanity Vault silently skips one that has not).</summary>
        public static bool BodyFits(string key, bool male) { return Ready && key != null && bodyGroups.Contains(key) && Fits(bodyGroups[key], male); }
        public static bool FaceFits(string key, bool male) { return Ready && key != null && faceGroups.Contains(key) && Fits(faceGroups[key], male); }

        /// <summary>sc paint: what the lists give a survivor and a raider of this sex, as Vanity Vault knows them.</summary>
        public static string Report(bool male)
        {
            if (!Ready) return "Vanity Vault is not loaded (or its paint code moved) - no paints";
            var sb = new StringBuilder();
            sb.Append("Vanity Vault ").Append(version).Append(", tattoo mode ").Append(ScConfig.TattooMode).Append('\n');
            foreach (bool raider in new[] { false, true })
            {
                string who = raider ? "raiders" : "survivors";
                sb.Append("  ").Append(who).Append(" (").Append(male ? "male" : "female").Append("): body ")
                  .Append(Pct(raider ? ScConfig.RaiderBodyPaintChance : ScConfig.SurvivorBodyPaintChance)).Append(" [");
                var names = new List<string>();
                foreach (var set in BodySets(raider ? ScConfig.RaiderBodyPaints : ScConfig.SurvivorBodyPaints, male)) names.Add(set.Value != null ? set.Key + ":" + set.Value : set.Key);
                sb.Append(string.Join(", ", names.ToArray())).Append("], face ").Append(Pct(raider ? ScConfig.RaiderFacePaintChance : ScConfig.SurvivorFacePaintChance))
                  .Append(" [").Append(string.Join(", ", Faces(raider ? ScConfig.RaiderFacePaints : ScConfig.SurvivorFacePaints, male).ToArray()))
                  .Append("], tattoo ").Append(Pct(raider ? ScConfig.RaiderTattooChance : ScConfig.SurvivorTattooChance))
                  .Append(" [").Append(string.Join(", ", Shapes(raider ? ScConfig.RaiderTattoos : ScConfig.SurvivorTattoos).ToArray())).Append(']');
                if (!raider) sb.Append('\n');
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>"body:face" or "body".</summary>
        private static void SplitSet(string entry, out string body, out string face)
        {
            entry = (entry ?? "").Trim();
            int c = entry.IndexOf(':');
            body = c >= 0 ? entry.Substring(0, c).Trim() : entry;
            face = c >= 0 ? entry.Substring(c + 1).Trim() : null;
            if (face != null && face.Length == 0) face = null;
        }

        /// <summary>The registry's own spelling of a name (case-insensitive), or null.</summary>
        private static string Key(IDictionary d, string name)
        {
            if (d == null || string.IsNullOrEmpty(name)) return null;
            if (d.Contains(name)) return name;
            foreach (object o in d.Keys)
            {
                string k = o as string;
                if (k != null && string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) return k;
            }
            return null;
        }

        /// <summary>A group's HasMale / HasFemale (true when the group type has no such property).</summary>
        private static bool Fits(object group, bool male)
        {
            if (group == null) return false;
            PropertyInfo p = AccessTools.Property(group.GetType(), male ? "HasMale" : "HasFemale");
            object v = p != null ? p.GetValue(group, null) : null;
            return !(v is bool) || (bool)v;
        }

        /// <summary>Never store an id the look string or the registry file would split or rewrite.</summary>
        private static bool Safe(string id)
        {
            return id.IndexOfAny(new[] { '|', '=', ';', '\n', '\r' }) < 0;
        }

        private static float Chance(GameRandom rnd) { return rnd != null ? rnd.RandomFloat : UnityEngine.Random.value; }

        private static int Index(GameRandom rnd, int count) { return rnd != null ? rnd.RandomRange(0, count) : UnityEngine.Random.Range(0, count); }

        private static string Pct(float f) { return Mathf.RoundToInt(f * 100f) + "%"; }
    }

    /// <summary>The body build of our XNPC bodies gets the look's paints (ScVanity), or gets them when it shows.</summary>
    public static class Patches_Vanity
    {
        /// <summary>
        /// After every third-person build of one of our bodies (spawn, gear change, ScLooks.Rebuild, XNPC's model switch):
        /// the look's paints. Here and not after EModelSdcsNpc.GenerateMeshes: XNPC hides the rig right after the build
        /// while the body is out of view (always on the first build), and Vanity Vault skips inactive renderers.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_CreateVizTP
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(SDCSUtils), "CreateVizTP", new[] { typeof(Archetype), typeof(GameObject).MakeByRefType(),
                    typeof(SDCSUtils.TransformCatalog).MakeByRefType(), typeof(EntityAlive), typeof(bool) });
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (Target() != null) return true;
                ScLog.Warn("SDCSUtils.CreateVizTP not found - no paints or tattoos on survivors (game update?)");
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPostfix]
            public static void Postfix(Archetype _archetype, ref GameObject baseRig, EntityAlive entity, bool isFPV)
            {
                try
                {
                    if (isFPV || baseRig == null || GameManager.IsDedicatedServer || !ScXnpc.IsBody(entity)) return;
                    Look look = ScLooks.Get(entity);
                    if (look == null || !look.HasPaint) return;
                    ScVanity.Built(entity, baseRig, look, _archetype != null ? _archetype.IsMale : look.Male);
                }
                catch (Exception e)
                {
                    ScLog.Once("vanity-createviztp", e);
                }
            }
        }

        /// <summary>
        /// A body coming into view: paints a build out of view could not put on (ScVanity.Shown). EModelSdcsNpc does not
        /// override SetVisible, so this is the call that turns the rig back on. Called often; returns at once unless a
        /// body is waiting.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_SetVisible
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(EModelSDCS), "SetVisible", new[] { typeof(bool), typeof(bool) });
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                if (Target() != null) return true;
                ScLog.Warn("EModelSDCS.SetVisible not found - a survivor rebuilt out of view keeps no paint (game update?)");
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPostfix]
            public static void Postfix(EModelSDCS __instance, bool _bVisible)
            {
                if (!_bVisible) return;
                try
                {
                    ScVanity.Shown(__instance);
                }
                catch (Exception e)
                {
                    ScLog.Once("vanity-setvisible", e);
                }
            }
        }
    }
}
