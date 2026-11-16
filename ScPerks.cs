using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NPCSdcsCharacters.Pure;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round U5 (2026-11-16, decision D10; research/npc306/FEATURES_306.md section 5): 0-XNPCCore 3.4.06's weapon-class perks for
    /// community members, from SC's stars. 0-XNPCCore raises a hire's perks with her level (SdcsLevelTick), which SC skips on its
    /// bodies (decision D8 of round X3: SC's skills only), so its perk growth never reached SC's people.
    ///
    /// Here a member's Shooting stars give the ranged classes' perk (pistols: Gunslinger, rifles: Dead Eye, machine guns: Machine
    /// Gunner, shotguns: Boomstick, archery: Archery) and her Fighting stars the melee classes' (blades: Deep Cuts, clubs: Pummel
    /// Pete, sledgehammers: Skull Crusher, knuckles: Brawler, spears: Javelin Master, batons: Electrocutioner), at
    /// rank = clamp((stars - 1) / 2, 0, 3): 1-2 stars 0, 3-4 stars 1, 5-6 stars 2, 7 stars 3. Only the perks in 0-XNPCCore's
    /// Settings.xml Progression/PerkList, and never past its PerkRankCap (0 turns them all off). A perk the list names that is no
    /// weapon class 0-XNPCCore knows (a modded one) is left out, with one log line.
    ///
    /// The raise is 0-XNPCCore's own rule (AvatarSdcsNpcController.SdcsApplyLevelPerks, mirrored with its public pieces): the
    /// perk's attribute goes to NpcLevelRules.AttributeLevelForRank (1, 3, 5 for ranks 1-3) and the perk to the rank, each only
    /// raised, never lowered (CraftSkillRules.Raised). So a rank a member already has - from her time as a paid hire, from a
    /// higher star count - stays: the larger one wins, never the sum.
    ///
    /// Perk books do not fight it. A weapon perk book (Pistol Pete and the rest) sets its own book perk (perkPistolPeteTakeAim,
    /// ...), a separate progression entry of its book group, not the class perk this file raises: books add their bonuses on top
    /// of the rank, by design (vanilla's own split; the Progress page counts them as "books n/m"). Nothing here reads or writes
    /// a book perk.
    ///
    /// Stacking with SC's per-star damage (the user's decision: start untrimmed and measure): RangedPerStar / MeleePerStar (0.10)
    /// give +10% damage per star above the first through buffSCSkills, a perc_add on EntityDamage; a class perk adds its own
    /// perc_add (vanilla: +10% EntityDamage per rank, plus its other effects). perc_add values add up: a 7-star shooter with a
    /// pistol does 1 + 0.6 + 0.3 = 1.9x (was 1.6x), a 3-star one 1 + 0.2 + 0.1 = 1.3x (was 1.2x).
    ///
    /// When: every ScSkills.StampCvars (join, each relink second, a new star, sc xp), applied only when her ranks, her Progression
    /// (a new body after a load) or the perk list changed. A weapon change needs nothing: every class is raised at once. Her own
    /// body on 0-XNPCCore's body only; a stand-in gets none (a short-lived copy with no Progression). MemberWeaponPerks false: no
    /// raise (ranks already given stay, as 0-XNPCCore's own never drop). Every member keeps a Progression for it
    /// (ScCraft.OwnsProgression, round U5: crafting on or off).
    /// </summary>
    public static class ScPerks
    {
        public static readonly string[] RangedClasses = { "pistols", "rifles", "machineguns", "shotguns", "archery" };
        public static readonly string[] MeleeClasses = { "blades", "clubs", "sledgehammers", "knuckles", "spears", "batons" };

        private struct Stamp
        {
            public Progression Prog;
            public int Ranged, Melee;
            public string[] List;
        }

        private static readonly Dictionary<int, Stamp> stamped = new Dictionary<int, Stamp>();
        private static readonly HashSet<string> warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool bound;
        private static MethodInfo listGetter, capGetter;
        private static int applies, raises;

        public static void Reset()
        {
            stamped.Clear();
            warned.Clear();
            applies = 0;
            raises = 0;
        }

        /// <summary>The user's rank: clamp((stars - 1) / 2, 0, 3).</summary>
        public static int RankFor(int stars)
        {
            return Mathf.Clamp((stars - 1) / 2, 0, 3);
        }

        /// <summary>ScSkills.StampCvars: her ranks into her Progression when anything they depend on changed. A ticker path.</summary>
        public static void Apply(Member m, EntityAlive npc)
        {
            if (!ScConfig.MemberWeaponPerks || m == null || npc == null || npc.isEntityRemote || npc.Buffs == null) return;
            if (!ReferenceEquals(ScRegistry.GetMember(npc.entityId), m) || !ScXnpc.HasXnpcLevels(npc)) return;
            try
            {
                if (npc.Progression == null && !ScCraft.OwnsProgression(npc)) return;     // U5: every member keeps one (made when missing)
                if (npc.Progression == null) return;
                string[] list = PerkList();
                int cap = RankCap();
                int r = Math.Min(RankFor(m.Stars(Skill.Shooting)), cap), f = Math.Min(RankFor(m.Stars(Skill.Fighting)), cap);
                Stamp s;
                if (stamped.TryGetValue(npc.entityId, out s) && ReferenceEquals(s.Prog, npc.Progression) && s.Ranged == r && s.Melee == f && ReferenceEquals(s.List, list)) return;
                var done = new List<string>();
                int n = Raise(npc, r, f, list, done);
                stamped[npc.entityId] = new Stamp { Prog = npc.Progression, Ranged = r, Melee = f, List = list };
                applies++;
                raises += n;
                string line = m.Name + ": weapon perks from " + ScMedic.Her(m) + " stars - Shooting " + m.Stars(Skill.Shooting) + " rank " + r + ", Fighting "
                              + m.Stars(Skill.Fighting) + " rank " + f + " (0-XNPCCore's PerkRankCap " + cap + ")";
                if (n > 0) ScLog.Out(line + ": " + string.Join(", ", done.ToArray()) + " (round U5)");
                else ScLog.Verbose(line + ": nothing to raise");
            }
            catch (Exception e)
            {
                ScLog.Once("perks-apply", e);
            }
        }

        /// <summary>0-XNPCCore's own raise, per perk of its list, at the rank of the perk's side. Returns the levels written.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Raise(EntityAlive npc, int ranged, int melee, string[] list, List<string> done)
        {
            int written = 0;
            Dictionary<string, ProgressionClass> classes = Progression.ProgressionClasses;
            if (classes == null || list == null) return 0;
            foreach (string perk in list)
            {
                string cls = ClassOf(perk);
                if (cls == null)
                {
                    if (warned.Add("class:" + perk)) ScLog.Out("weapon perks: '" + perk + "' in 0-XNPCCore's PerkList is no weapon class SC can tell ranged from melee - left out for members");
                    continue;
                }
                int rank = Array.IndexOf(RangedClasses, cls) >= 0 ? ranged : melee;
                if (rank <= 0) continue;
                ProgressionClass pc;
                if (!classes.TryGetValue(perk, out pc) || pc == null || !pc.IsPerk)
                {
                    if (warned.Add("perk:" + perk)) ScLog.Out("weapon perks: '" + perk + "' is not a perk in this game - skipped");
                    continue;
                }
                string att = NpcLevelRules.PerkAttributeFor(pc.Name);
                if (att != null)
                {
                    int attLevel = NpcLevelRules.AttributeLevelForRank(pc.Name, rank);
                    ProgressionValue av = npc.Progression.GetProgressionValue(att);
                    ProgressionClass ac;
                    if (attLevel > 0 && av != null && classes.TryGetValue(att, out ac) && ac != null)
                    {
                        int to = CraftSkillRules.Raised(av.Level, attLevel, ac.MaxLevel);
                        if (to != av.Level) { av.Level = to; written++; done.Add(att + " " + to); }
                    }
                }
                ProgressionValue pv = npc.Progression.GetProgressionValue(pc.Name);
                if (pv == null) continue;
                int lv = CraftSkillRules.Raised(pv.Level, rank, pc.MaxLevel);
                if (lv == pv.Level) continue;
                pv.Level = lv;
                written++;
                done.Add(pc.Name + " " + lv);
            }
            return written;
        }

        /// <summary>The weapon class 0-XNPCCore gives a perk (NpcLevelRules.PerkForClass, reversed), or null.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string ClassOf(string perk)
        {
            if (string.IsNullOrEmpty(perk)) return null;
            foreach (string c in NpcLevelRules.Phase1ClassIds)
                if (string.Equals(NpcLevelRules.PerkForClass(c), perk, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        private static void Bind()
        {
            if (bound) return;
            bound = true;
            listGetter = AccessTools.PropertyGetter(typeof(AvatarSdcsNpcController), "SdcsLevelPerkList");
            capGetter = AccessTools.PropertyGetter(typeof(AvatarSdcsNpcController), "SdcsLevelPerkRankCap");
            if (listGetter == null || !listGetter.IsStatic || listGetter.ReturnType != typeof(string[])) listGetter = null;
            if (capGetter == null || !capGetter.IsStatic || capGetter.ReturnType != typeof(int)) capGetter = null;
            if (listGetter == null || capGetter == null)
                ScLog.Warn("weapon perks: 0-XNPCCore's SdcsLevelPerkList / SdcsLevelPerkRankCap not found (0-XNPCCore older than 3.4.06, or an update?) - "
                           + "members get its default perk list at rank cap 3");
        }

        /// <summary>0-XNPCCore's Progression/PerkList (its parsed, cached array), else its default list.</summary>
        public static string[] PerkList()
        {
            Bind();
            string[] l = listGetter != null ? listGetter.Invoke(null, null) as string[] : null;
            return l ?? Fallback();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string[] Fallback() { return NpcLevelRules.Phase1PerkNames; }

        /// <summary>0-XNPCCore's Progression/PerkRankCap (0-5), else 3; never above the user's 3.</summary>
        public static int RankCap()
        {
            Bind();
            int cap = 3;
            if (capGetter != null)
            {
                object o = capGetter.Invoke(null, null);
                if (o is int) cap = (int)o;
            }
            return Mathf.Clamp(cap, 0, 3);
        }

        /// <summary>sc perks [id|name] [apply]: each member's ranks and the class perks on her body; apply forces a re-raise.</summary>
        public static void ConsoleCmd(List<string> p, Action<string> output)
        {
            ScConfig.EnsureParsed();
            if (!ScRegistry.Loaded) { output("[SC] the registry is not loaded"); return; }
            string[] list = PerkList();
            output("[SC] weapon perks (round U5): " + (ScConfig.MemberWeaponPerks ? "on" : "OFF (MemberWeaponPerks false)") + ", rank = (stars - 1) / 2 within 0-XNPCCore's cap "
                   + RankCap() + "; its PerkList: " + string.Join(", ", list) + "; applied " + applies + " time(s), " + raises + " level(s) raised this session");
            foreach (Member m in ScRegistry.Members.Values)
            {
                if (p.Count >= 2 && !Matches(m, p[1])) continue;
                EntityAlive e = ScCommunity.FindNpc(m.Id);
                if (p.Count >= 3 && p[2].Equals("apply", StringComparison.OrdinalIgnoreCase) && e != null)
                {
                    stamped.Remove(e.entityId);
                    Apply(m, e);
                }
                int r = RankFor(m.Stars(Skill.Shooting)), f = RankFor(m.Stars(Skill.Fighting));
                string body;
                if (e == null) body = "not loaded";
                else if (!ScXnpc.HasXnpcLevels(e)) body = "not 0-XNPCCore's body (no perks)";
                else if (e.Progression == null) body = "NO Progression";
                else
                {
                    var parts = new List<string>();
                    foreach (string perk in list)
                    {
                        ProgressionValue pv = e.Progression.GetProgressionValue(perk);
                        string cls = ClassOf(perk);
                        if (pv != null && pv.Level > 0) parts.Add((cls ?? "?") + " " + perk + " " + pv.Level);
                    }
                    body = parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "no class perk raised";
                }
                output("[SC]   " + m.Name + " (" + m.Id + "): Shooting " + m.Stars(Skill.Shooting) + " -> ranged rank " + r + ", Fighting " + m.Stars(Skill.Fighting)
                       + " -> melee rank " + f + "; on " + ScMedic.Her(m) + " body: " + body);
            }
        }

        private static bool Matches(Member m, string key)
        {
            int id;
            if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return m.Id == id;
            return m.Name != null && m.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
