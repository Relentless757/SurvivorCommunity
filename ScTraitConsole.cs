#if SC_V34
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round m1t (2026-11-17): the console for traits and life skills, to test what new recruits roll on members who were
    /// recruited before it (they keep what they had):
    ///   sc traits                                     the trait table (keys, roll chance, live or coming later), the excluded pairs
    ///   sc traits sim [n]                             n trait rolls: each trait's share, and any excluded pair rolled together (none)
    ///   sc traits &lt;id|name&gt;                          her traits and what they do now
    ///   sc traits &lt;id|name&gt; set &lt;A&gt; [B ...] [force]   her traits replaced (an excluded pair, or Squeamish on a medic, needs force)
    ///   sc traits &lt;id|name&gt; add &lt;A&gt; [force] | remove &lt;A&gt;
    ///   sc spec                                       the life skills (roll weight and chance, live or coming later), the keys
    ///   sc spec sim [n]                               n new T1 survivors rolled: each life skill's share (and a squeamish medic: none)
    ///   sc spec &lt;id|name&gt;                            her life skill and what it does now
    ///   sc spec &lt;id|name&gt; set &lt;Specialty&gt; [stars] [force]   her life skill (and stars) set (Medicine on a squeamish one needs force)
    /// Host only; members only (a stranger's profile is not changed here).
    /// </summary>
    public static class ScTraitConsole
    {
        public static void TraitsCmd(List<string> p, Action<string> output)
        {
            try
            {
                ScConfig.EnsureParsed();
                if (p.Count < 2) { TraitTable(output); return; }
                if (p[1].Equals("sim", StringComparison.OrdinalIgnoreCase)) { TraitSim(p, output); return; }
                if (!ScRegistry.Loaded) { output("[SC] the registry is not loaded"); return; }
                Member m = Find(p[1]);
                if (m == null) { output("[SC] no member '" + p[1] + "' - sc members lists them"); return; }
                if (p.Count >= 3)
                {
                    string verb = p[2].ToLowerInvariant();
                    if (verb == "set" || verb == "add" || verb == "remove")
                    {
                        Change(m, verb, p, output);
                        return;
                    }
                    output("[SC] sc traits <id|name> [set <trait> [trait ...] [force] | add <trait> [force] | remove <trait>]");
                    return;
                }
                foreach (string line in ScTraitEffects.Lines(m)) output("[SC] " + m.Name + ": " + line);
            }
            catch (Exception e)
            {
                output("[SC] traits failed: " + e.Message);
                ScLog.Once("traits-console", e);
            }
        }

        private static void TraitTable(Action<string> output)
        {
            int total = 0;
            foreach (TraitDef t in ScConfig.Traits) total += Math.Max(0, t.Weight);
            output("[SC] traits (" + ScConfig.Traits.Count + ", " + ScConfig.TraitsPerRecruit + " a new recruit; existing survivors keep theirs):");
            foreach (TraitDef t in ScConfig.Traits)
                output("[SC]   " + t.Name + " (" + ScTraits.Display(t.Name) + ") " + (total > 0 ? (100f * t.Weight / total).ToString("0.0", CultureInfo.InvariantCulture) : "0")
                       + "% a draw: " + ScTraitEffects.KeysText(t) + " - " + ScTraits.Describe(t.Name));
            output("[SC] excluded pairs: " + ScTraits.ExclusionPairs());
            output("[SC] counters: " + ScTraitEffects.Report());
            if (ScRegistry.Loaded)
                foreach (Member m in ScRegistry.Members.Values)
                    output("[SC]   " + m.Name + " (" + m.Id + "): " + (m.Traits.Count > 0 ? string.Join(", ", m.Traits.ToArray()) : "none"));
            output("[SC] sc traits sim [n] | sc traits <id|name> [set <trait> [trait ...] [force] | add <trait> [force] | remove <trait>]");
        }

        private static void TraitSim(List<string> p, Action<string> output)
        {
            int n = 1000;
            if (p.Count >= 3 && (!int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 1)) { output("[SC] sc traits sim [n]"); return; }
            n = Math.Min(n, 200000);
            GameRandom rnd = ScCommunity.World != null ? ScCommunity.World.GetGameRandom() : null;
            if (rnd == null) { output("[SC] no world"); return; }
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int bad = 0, sets = 0;
            string firstBad = null;
            for (int i = 0; i < n; i++)
            {
                List<string> roll = ScTraits.Roll(ScConfig.TraitsPerRecruit, rnd);
                sets++;
                foreach (string t in roll) { int c; counts.TryGetValue(t, out c); counts[t] = c + 1; }
                string conflict = ScTraits.FirstConflict(roll);
                if (conflict != null) { bad++; if (firstBad == null) firstBad = conflict; }
            }
            var sb = new StringBuilder();
            foreach (TraitDef t in ScConfig.Traits)
            {
                int c;
                counts.TryGetValue(t.Name, out c);
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(t.Name).Append(' ').Append((100.0 * c / Math.Max(1, sets)).ToString("0.0", CultureInfo.InvariantCulture)).Append('%');
            }
            output("[SC] " + sets + " trait rolls of " + ScConfig.TraitsPerRecruit + ": a survivor has " + sb);
            output("[SC] excluded pairs rolled together: " + bad + (firstBad != null ? " (first: " + firstBad + ")" : " (as it should be)"));
        }

        private static void Change(Member m, string verb, List<string> p, Action<string> output)
        {
            bool force = false;
            var names = new List<string>();
            for (int i = 3; i < p.Count; i++)
            {
                if (p[i].Equals("force", StringComparison.OrdinalIgnoreCase)) { force = true; continue; }
                TraitDef t = ScTraits.Find(p[i]);
                if (t == null) { output("[SC] no trait '" + p[i] + "' - sc traits lists them"); return; }
                if (!names.Contains(t.Name)) names.Add(t.Name);
            }
            if (names.Count == 0 && verb != "set") { output("[SC] sc traits <id|name> " + verb + " <trait>"); return; }
            var next = new List<string>();
            if (verb == "set") next.AddRange(names);
            else
            {
                next.AddRange(m.Traits);
                foreach (string n in names)
                {
                    int at = next.FindIndex(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
                    if (verb == "add" && at < 0) next.Add(n);
                    if (verb == "remove" && at >= 0) next.RemoveAt(at);
                }
            }
            if (next.Count > 5) { output("[SC] at most 5 traits (the profile keeps 5; the card shows 3)"); return; }
            string conflict = ScTraits.FirstConflict(next);
            if (conflict != null && !force) { output("[SC] refused: " + conflict + " exclude each other (add force to do it anyway)"); return; }
            var probe = new Member();
            probe.Traits.AddRange(next);
            if (m.Specialty == Specialty.Medicine && !ScTraitEffects.CanBeMedic(probe) && !force)
            {
                output("[SC] refused: " + m.Name + " is a medic (Medicine) - give her another life skill first (sc spec " + m.Id + " set <Specialty>), or add force");
                return;
            }
            EntityAlive npc = ScCommunity.FindNpc(m.Id);
            if (npc != null) ScTraits.RemoveBuffs(m, npc);
            m.Traits.Clear();
            m.Traits.AddRange(next);
            if (npc != null)
            {
                ScTraits.ApplyBuffs(m, npc);
                ScSkills.StampCvars(m, npc);
            }
            ScMorale.Recompute();
            ScRegistry.SaveAtomic();
            output("[SC] " + m.Name + "'s traits: " + (next.Count > 0 ? string.Join(", ", next.ToArray()) : "none") + (conflict != null ? " (forced: " + conflict + ")" : "")
                   + (next.Count > 3 ? " - the card shows the first 3" : "") + (npc == null ? " (not loaded: the buffs follow when she is)" : ""));
            foreach (string line in ScTraitEffects.Lines(m)) output("[SC]   " + line);
        }

        // ------------------------------------------------------------------ sc spec ----

        public static void SpecCmd(List<string> p, Action<string> output)
        {
            try
            {
                ScConfig.EnsureParsed();
                if (p.Count < 2) { SpecTable(output); return; }
                if (p[1].Equals("sim", StringComparison.OrdinalIgnoreCase)) { SpecSim(p, output); return; }
                if (!ScRegistry.Loaded) { output("[SC] the registry is not loaded"); return; }
                Member m = Find(p[1]);
                if (m == null) { output("[SC] no member '" + p[1] + "' - sc members lists them"); return; }
                if (p.Count >= 4 && p[2].Equals("set", StringComparison.OrdinalIgnoreCase))
                {
                    Set(m, p, output);
                    return;
                }
                if (p.Count >= 3) { output("[SC] sc spec <id|name> [set <Specialty> [stars] [force]]"); return; }
                output("[SC] " + ScLifeSkills.Line(m));
            }
            catch (Exception e)
            {
                output("[SC] spec failed: " + e.Message);
                ScLog.Once("spec-console", e);
            }
        }

        private static void SpecTable(Action<string> output)
        {
            output("[SC] life skills (one a survivor; existing survivors keep theirs):");
            foreach (Specialty s in Enum.GetValues(typeof(Specialty)))
            {
                if (s == Specialty.None) continue;
                output("[SC]   " + s + " (" + ScLifeSkills.Name(s) + ", role " + Localization.Get("scRole" + s) + ", icon " + XUiC_ScCommunity.SpecIcon(s) + ") weight "
                       + ScConfig.SpecialtyWeights[(int)s].ToString("0.##", CultureInfo.InvariantCulture) + " = "
                       + ScLifeSkills.ChancePercent(s).ToString("0.0", CultureInfo.InvariantCulture) + "% of new recruits"
                       + (ScLifeSkills.Pending(s) ? " - waits for the " + ScLifeSkills.Needs(s) : "") + " - " + ScLifeSkills.Describe(s));
            }
            output("[SC] keys: " + ScConfig.LifeSkillsLine());
            output("[SC] counters: " + ScLifeSkills.Report());
            if (ScRegistry.Loaded)
                foreach (Member m in ScRegistry.Members.Values)
                    output("[SC]   " + m.Name + " (" + m.Id + "): " + m.Specialty + " " + m.SpecLevel);
            output("[SC] sc spec sim [n] | sc spec <id|name> [set <Specialty> [stars] [force]]");
        }

        private static void SpecSim(List<string> p, Action<string> output)
        {
            int n = 1000;
            if (p.Count >= 3 && (!int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 1)) { output("[SC] sc spec sim [n]"); return; }
            n = Math.Min(n, 200000);
            GameRandom rnd = ScCommunity.World != null ? ScCommunity.World.GetGameRandom() : null;
            if (rnd == null) { output("[SC] no world"); return; }
            var counts = new int[ScConfig.SpecialtyWeights.Length];
            int squeamish = 0, squeamishMedics = 0;
            for (int i = 0; i < n; i++)
            {
                var m = new Member();
                m.Traits.AddRange(ScTraits.Roll(ScConfig.TraitsPerRecruit, rnd));
                ScSkills.RollStart(m, rnd, 0);
                int k = (int)m.Specialty;
                if (k >= 0 && k < counts.Length) counts[k]++;
                if (!ScTraitEffects.CanBeMedic(m))
                {
                    squeamish++;
                    if (m.Specialty == Specialty.Medicine) squeamishMedics++;
                }
            }
            var sb = new StringBuilder();
            for (int i = 1; i < counts.Length; i++)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append((Specialty)i).Append(' ').Append((100.0 * counts[i] / n).ToString("0.0", CultureInfo.InvariantCulture)).Append('%');
            }
            output("[SC] " + n + " new T1 survivors (traits and life skill as a new recruit rolls them): " + sb);
            output("[SC] squeamish " + squeamish + ", of them with Medicine " + squeamishMedics + (squeamishMedics == 0 ? " (as it should be)" : " (WRONG)"));
        }

        private static void Set(Member m, List<string> p, Action<string> output)
        {
            Specialty sp;
            if (!ScNames.TrySpecialty(p[3], out sp) || sp == Specialty.None) { output("[SC] unknown life skill '" + p[3] + "' - sc spec lists them"); return; }
            int stars = -1;
            bool force = false;
            for (int i = 4; i < p.Count; i++)
            {
                if (p[i].Equals("force", StringComparison.OrdinalIgnoreCase)) { force = true; continue; }
                int x;
                if (!int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out x) || x < 1 || x > Member.MaxStars)
                {
                    output("[SC] stars must be 1-" + Member.MaxStars + ": '" + p[i] + "'");
                    return;
                }
                stars = x;
            }
            if (sp == Specialty.Medicine && !ScTraitEffects.CanBeMedic(m) && !force)
            {
                output("[SC] refused: " + m.Name + " is squeamish and never takes the medic role (add force to do it anyway: she still won't act as one)");
                return;
            }
            Specialty was = m.Specialty;
            m.Specialty = sp;
            if (stars > 0)
            {
                m.SpecLevel = stars;
                m.SpecXp = ScSkills.NeededFor(stars);
            }
            EntityAlive npc = ScCommunity.FindNpc(m.Id);
            if (npc != null) ScSkills.StampCvars(m, npc);
            ScMorale.Recompute();
            ScRegistry.SaveAtomic();
            output("[SC] " + m.Name + ": " + was + " -> " + sp + " " + m.SpecLevel + " star(s)" + (npc == null ? " (not loaded)" : ""));
            output("[SC] " + ScLifeSkills.Line(m));
        }

        private static Member Find(string key)
        {
            int id;
            if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return ScRegistry.GetMember(id);
            foreach (Member m in ScRegistry.Members.Values)
                if (m.Name != null && m.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return m;
            return null;
        }
    }
}
#endif
