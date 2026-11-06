using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NPCSdcsCharacters.Pure;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// C1: the stars-to-level rule and the defaults, with no game type in them, so a harness outside the game can call the
    /// built DLL's own function (research: the 1/3/5/7-star table). Specialty order is the enum's (Medicine = 1 ...).
    /// </summary>
    public static class ScCraftMath
    {
        /// <summary>The specialties in Specialty enum order (index = (int)Specialty - 1); the config keys end in these.</summary>
        public static readonly string[] SpecialtyNames = { "Medicine", "Cooking", "Mechanics", "Gardening", "Chemistry", "Craftsmanship" };

        /// <summary>Each specialty's crafting skills (progression.xml names), the user's mapping (2026-11-05, decision 1).</summary>
        public static readonly string[] DefaultSkills =
        {
            "craftingMedical",
            "craftingFood",
            "craftingWorkstations,craftingVehicles,craftingTraps,craftingRobotics",
            "craftingSeeds",
            "craftingExplosives,craftingMedical,craftingElectrician",
            "craftingArmor,craftingBackpack,craftingBlades,craftingBows,craftingClubs,craftingHandguns,craftingHarvestingTools,craftingKnuckles,"
            + "craftingMachineGuns,craftingRepairTools,craftingRifles,craftingSalvageTools,craftingShotguns,craftingSledgehammers,craftingSpears"
        };

        /// <summary>Each specialty's 0-XNPCCore roles (the user's Q2: Mechanics Blacksmith + Crafter + Chemist; Chemistry Chemist + Crafter).</summary>
        public static readonly string[] DefaultRoles = { "cook,chemist", "cook", "blacksmith,crafter,chemist", "", "chemist,crafter", "crafter" };

        /// <summary>
        /// A crafting skill's level for a star count: round(maxLevel x (stars / maxStars) ^ curve), half up, within 0..maxLevel
        /// (decision 2: proportional at SC's cap with curve 1). 0 for no stars or no max.
        /// </summary>
        public static int Level(int maxLevel, int stars, int maxStars, float curve)
        {
            if (maxLevel <= 0 || stars <= 0 || maxStars <= 0) return 0;
            double f = Math.Min(1.0, (double)stars / maxStars);
            if (curve > 0f && Math.Abs(curve - 1f) > 1e-6f) f = Math.Pow(f, curve);
            int lv = (int)Math.Floor(maxLevel * f + 0.5 + 1e-9);
            return Math.Max(0, Math.Min(maxLevel, lv));
        }
    }

    /// <summary>
    /// C1 (round 22, 3.4): community members craft through 0-XNPCCore 3.4.02's crafting, their specialty stars deciding their
    /// skills (design DESIGN_C1_CRAFTING.md sections 3 and 9.6).
    ///
    ///  - Levels. A member's crafting skills live in an SC-owned Progression: ScXnpc.StripLevels keeps a member's (OwnsProgression)
    ///    where it nulls everyone else's, and Apply (the end of ScSkills.StampCvars: every relink second, join, star change)
    ///    writes each skill of her specialty at ScCraftMath.Level(max level, stars) and every other skill SC manages at 0 (the game
    ///    reads 0 as the skill's min level 1, so she keeps exactly the level-1 recipes a fresh player has). Exact set: whatever
    ///    0-XNPCCore or SCore raised is lowered again. 0-XNPCCore's own learning and XP are off for members (Patches_Craft C1a/C1b).
    ///  - Roles. 0-XNPCCore's four role cvars (SdcsRoleCook ...) are stamped once per body from the specialty's defaults (or the
    ///    registry's mask), marked with sc_craftRoles; after that the body is the truth (the order wheel, voice, the Community tab)
    ///    and its mask is mirrored into the registry (craftroles=), so a re-created body gets the player's choice back.
    ///  - XP. A counted craft (her recipe belongs to a skill of her specialty: its tags meet a RecipeTagUnlocked or CraftingTier
    ///    passive of that skill, as the game's own unlock and tier checks read it) pays CraftSpecXpPerCraft specialty XP, whole points through ScSkills.AddSpecialtyXp from a saved
    ///    fractional carry (craftxp=), no daily cap. Fed by Patches_Craft's backpack, station and station-sweep hooks.
    ///
    /// Non-members are never touched: every hook asks the registry first.
    /// </summary>
    public static class ScCraft
    {
        /// <summary>The body was stamped with its roles by SC (1; a 0 cvar is not saved, so "absent" = never stamped).</summary>
        public const string RolesMarkCvar = "sc_craftRoles";

        /// <summary>The four roles in mask bit order (1 cook, 2 blacksmith, 4 chemist, 8 crafter): 0-XNPCCore's names and cvars.</summary>
        public static readonly string[] RoleNames = { CraftRoleRules.Cook, CraftRoleRules.Blacksmith, CraftRoleRules.Chemist, CraftRoleRules.Crafter };
        public static readonly string[] RoleCvars = { CraftRoleRules.CookCVar, CraftRoleRules.BlacksmithCVar, CraftRoleRules.ChemistCVar, CraftRoleRules.CrafterCVar };
        /// <summary>The Community tab's labels for them.</summary>
        public static readonly string[] RoleLocKeys = { "scUiRoleCook", "scUiRoleSmith", "scUiRoleChemist", "scUiRoleCrafter" };

        /// <summary>0-XNPCCore's learn-by-crafting counters (CraftSkillRules.*CraftsCVar), shown by sc craft: they stay 0 on members.</summary>
        private static readonly string[] NpcCoreCraftsCvars = { "SdcsCraftsCook", "SdcsCraftsBlacksmith", "SdcsCraftsChemist", "SdcsCraftsCrafter" };

        // ---- resolved per world (and per config parse) ----
        private static int resolvedStamp = int.MinValue;
        private static bool resolvedOk;
        private static readonly List<ProgressionClass> managed = new List<ProgressionClass>();
        private static readonly List<ProgressionClass>[] specSkills = { new List<ProgressionClass>(), new List<ProgressionClass>(), new List<ProgressionClass>(),
                                                                         new List<ProgressionClass>(), new List<ProgressionClass>(), new List<ProgressionClass>() };
        private static readonly ulong[] specMask = new ulong[6];
        /// <summary>recipe name -> the managed skills (bits by index in managed) whose unlock or tier passives name it; built lazily.</summary>
        private static Dictionary<string, ulong> recipeIndex;
        private static int indexedRecipes;

        /// <summary>Session bookkeeping per member id, for sc craft.</summary>
        private sealed class Stats
        {
            public int Crafts, Counted, NotCounted, XpPaid, Backpack, Station, Swept, Stars;
        }
        private static readonly Dictionary<int, Stats> stats = new Dictionary<int, Stats>();
        private static int levelWrites, progressionsMade, roleStamps, roleMirrors, xpSaves;
        /// <summary>Review #6: craft XP waiting for a registry save (a star saves at once; else FlushXp on the ticker's cadence).</summary>
        private static bool xpDirty;
        private static float xpSavedAt;
        private const float FlushSeconds = 30f;

        public static void Reset()
        {
            resolvedStamp = int.MinValue;
            resolvedOk = false;
            managed.Clear();
            for (int i = 0; i < specSkills.Length; i++) { specSkills[i].Clear(); specMask[i] = 0UL; }
            recipeIndex = null;
            indexedRecipes = 0;
            stats.Clear();
            levelWrites = progressionsMade = roleStamps = roleMirrors = xpSaves = 0;
            xpDirty = false;
            xpSavedAt = 0f;
        }

        private static int SpecIndex(Specialty s)
        {
            int i = (int)s - 1;
            return i >= 0 && i < ScCraftMath.SpecialtyNames.Length ? i : -1;
        }

        private static Stats StatsOf(int id)
        {
            Stats s;
            if (!stats.TryGetValue(id, out s)) { s = new Stats(); stats[id] = s; }
            return s;
        }

        // ------------------------------------------------------------------ resolve ----

        /// <summary>The config's skill lists against the loaded progression (once per world and config parse). False until it is loaded.</summary>
        private static bool Resolve()
        {
            if (resolvedOk && resolvedStamp == ScConfig.CraftStamp) return true;
            if (Progression.ProgressionClasses == null || Progression.ProgressionClasses.Count == 0) return false;
            managed.Clear();
            recipeIndex = null;
            var bad = new List<string>();
            for (int i = 0; i < specSkills.Length; i++)
            {
                specSkills[i].Clear();
                specMask[i] = 0UL;
                if (!string.Equals(Enum.GetName(typeof(Specialty), i + 1), ScCraftMath.SpecialtyNames[i], StringComparison.Ordinal))
                    ScLog.Once("craft-spec-order", "crafting: the specialty table is out of step with the Specialty enum at " + (i + 1) + " - fix ScCraftMath.SpecialtyNames");
                foreach (string name in ScConfig.CraftSkills[i])
                {
                    ProgressionClass pc;
                    if (!Progression.ProgressionClasses.TryGetValue(name, out pc) || pc == null || !pc.IsCrafting)
                    {
                        bad.Add("CraftSkills" + ScCraftMath.SpecialtyNames[i] + ":" + name);
                        continue;
                    }
                    int idx = managed.IndexOf(pc);
                    if (idx < 0) { managed.Add(pc); idx = managed.Count - 1; }
                    if (!specSkills[i].Contains(pc)) specSkills[i].Add(pc);
                    if (idx < 64) specMask[i] |= 1UL << idx;
                }
            }
            if (managed.Count > 64) ScLog.Warn("crafting: " + managed.Count + " skills managed - only the first 64 count crafts for specialty XP");
            if (bad.Count > 0) ScLog.Warn("crafting: not a crafting skill in progression.xml, skipped - " + string.Join(", ", bad.ToArray()));
            resolvedStamp = ScConfig.CraftStamp;
            resolvedOk = true;
            ScLog.Out("crafting: " + managed.Count + " crafting skills managed on members (" + Table(false) + ")");
            return true;
        }

        /// <summary>
        /// Which managed skills each recipe belongs to: its tags (vanilla's Recipe.tags, which hold the recipe's own name whenever it
        /// has a tags attribute - what the game's own unlock and tier checks test) against each skill's RecipeTagUnlocked and
        /// CraftingTier passives. A recipe without tags is open to everyone and belongs to no skill.
        /// </summary>
        private static void BuildIndex()
        {
            recipeIndex = new Dictionary<string, ulong>(StringComparer.Ordinal);
            indexedRecipes = 0;
            int n = Math.Min(64, managed.Count);
            var passives = new List<PassiveEffect>[n];
            for (int k = 0; k < n; k++)
            {
                passives[k] = new List<PassiveEffect>();
                MinEffectController fx = managed[k].Effects;
                if (fx == null || fx.EffectGroups == null) continue;
                foreach (MinEffectGroup g in fx.EffectGroups)
                {
                    if (g == null || g.PassiveEffects == null) continue;
                    foreach (PassiveEffect pe in g.PassiveEffects)
                    {
                        if (pe == null || pe.Tags.IsEmpty || pe.InvertTagCheck) continue;   // an untagged or inverted passive names no recipe
                        if (pe.Type == PassiveEffects.RecipeTagUnlocked || pe.Type == PassiveEffects.CraftingTier) passives[k].Add(pe);
                    }
                }
            }
            List<Recipe> all = CraftingManager.GetAllRecipes();
            if (all == null) return;
            foreach (Recipe r in all)
            {
                if (r == null) continue;
                string name = r.GetName();
                if (string.IsNullOrEmpty(name)) continue;
                FastTags<TagGroup.Global> tags = r.tags;
                if (tags.IsEmpty) continue;
                ulong mask = 0UL;
                for (int k = 0; k < n; k++)
                {
                    foreach (PassiveEffect pe in passives[k])
                        if (pe.hasMatchingTag(tags)) { mask |= 1UL << k; break; }
                }
                if (mask == 0UL) continue;
                ulong old;
                recipeIndex.TryGetValue(name, out old);
                if (old == 0UL) indexedRecipes++;
                recipeIndex[name] = old | mask;
            }
            ScLog.Out("crafting: " + indexedRecipes + " recipes belong to a managed crafting skill (the crafts that train a specialty)");
        }

        /// <summary>The managed skills a recipe belongs to (0: none - an open recipe or another mod's skill).</summary>
        private static ulong SkillsOf(string recipeName)
        {
            if (string.IsNullOrEmpty(recipeName) || !Resolve()) return 0UL;
            if (recipeIndex == null) BuildIndex();
            ulong m;
            return recipeIndex != null && recipeIndex.TryGetValue(recipeName, out m) ? m : 0UL;
        }

        /// <summary>Does a craft of this recipe train this specialty (decision 3: its recipe belongs to one of the specialty's skills)?</summary>
        public static bool Counts(Specialty s, string recipeName)
        {
            int si = SpecIndex(s);
            return si >= 0 && (SkillsOf(recipeName) & specMask[si]) != 0UL;
        }

        /// <summary>A managed skill's level at this many stars (the loaded max level, the config's curve).</summary>
        public static int LevelFor(ProgressionClass pc, int stars)
        {
            return pc != null ? ScCraftMath.Level(pc.MaxLevel, stars, Member.MaxStars, ScConfig.CraftLevelCurve) : 0;
        }

        // ------------------------------------------------------------------ levels ----

        /// <summary>
        /// ScXnpc.StripLevels' question: does SC own this body's Progression (a member, crafting on)? Then it is kept - one is made
        /// when there is none - instead of nulled. Stand-ins, raiders and strangers: false, nulled as before.
        /// </summary>
        public static bool OwnsProgression(EntityAlive e)
        {
            if (e == null || !ScConfig.CraftSkillsEnabled || ScRegistry.GetMember(e.entityId) == null) return false;
            if (e.Progression == null)
            {
                e.Progression = new Progression(e);
                progressionsMade++;
            }
            return true;
        }

        /// <summary>
        /// Every ScSkills.StampCvars (each relink second, join, a star, sc xp): her skills from her stars, then her roles. A member's
        /// own body on 0-XNPCCore's body only (its crafting needs its controller): StampCvars also dresses outpost and distress
        /// stand-ins, enclave people and band people in a record's skills - none of them is the registry's member under that id,
        /// and none gets a Progression or roles here (StripLevels keeps nulling theirs).
        /// </summary>
        public static void Apply(Member m, EntityAlive npc)
        {
            if (m == null || npc == null || npc.Buffs == null || !ScConfig.CraftSkillsEnabled || !ScXnpc.HasXnpcLevels(npc)) return;
            if (!ReferenceEquals(ScRegistry.GetMember(npc.entityId), m)) return;
            try
            {
                if (!Resolve()) return;
                if (npc.Progression == null)
                {
                    npc.Progression = new Progression(npc);
                    progressionsMade++;
                }
                SetLevels(m, npc);
                SyncRoles(m, npc);
            }
            catch (Exception e)
            {
                ScLog.Once("craft-apply", e);
            }
        }

        private static void SetLevels(Member m, EntityAlive npc)
        {
            int si = SpecIndex(m.Specialty);
            int changed = 0;
            for (int k = 0; k < managed.Count; k++)
            {
                ProgressionClass pc = managed[k];
                int target = si >= 0 && specSkills[si].Contains(pc) ? LevelFor(pc, m.SpecLevel) : 0;
                ProgressionValue pv = npc.Progression.GetProgressionValue(pc.Name);
                if (pv == null || pv.Level == target) continue;
                pv.Level = target;
                changed++;
            }
            if (changed == 0) return;
            levelWrites += changed;
            ScLog.Verbose(m.Name + ": crafting skills set from " + m.Specialty + " " + m.SpecLevel + " stars (" + changed + " written)");
        }

        // ------------------------------------------------------------------ roles ----

        public static int DefaultMask(Specialty s)
        {
            int si = SpecIndex(s);
            if (si < 0) return 0;
            int mask = 0;
            foreach (string r in ScConfig.CraftRoles[si])
            {
                int i = RoleIndex(r);
                if (i >= 0) mask |= 1 << i;
                else ScLog.Once("craft-role-" + r, "crafting: unknown role '" + r + "' in CraftRoles" + ScCraftMath.SpecialtyNames[si] + " (cook, blacksmith, chemist, crafter)");
            }
            return mask;
        }

        private static int RoleIndex(string name)
        {
            string p = CraftRoleRules.Parse(name);
            for (int i = 0; i < RoleNames.Length; i++)
                if (string.Equals(RoleNames[i], p ?? name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public static int ReadMask(EntityAlive npc)
        {
            int mask = 0;
            if (npc == null || npc.Buffs == null) return 0;
            for (int i = 0; i < RoleCvars.Length; i++)
                if (CraftRoleRules.IsOn(npc.Buffs.GetCustomVar(RoleCvars[i]))) mask |= 1 << i;
            return mask;
        }

        /// <summary>Writes the role cvars as Voice Control's MutateRole does: 1 on (synced, forced to clients), 0 off.</summary>
        private static void WriteMask(EntityAlive npc, int mask)
        {
            for (int i = 0; i < RoleCvars.Length; i++)
            {
                bool want = (mask & (1 << i)) != 0;
                bool on = CraftRoleRules.IsOn(npc.Buffs.GetCustomVar(RoleCvars[i]));
                if (want == on) continue;
                npc.Buffs.SetCustomVar(RoleCvars[i], want ? 1f : 0f, true, CVarOperation.set, true);
            }
        }

        public static string MaskText(int mask)
        {
            if (mask < 0) return "unknown";
            var parts = new List<string>();
            for (int i = 0; i < RoleNames.Length; i++) if ((mask & (1 << i)) != 0) parts.Add(RoleNames[i]);
            return parts.Count > 0 ? string.Join("+", parts.ToArray()) : "none";
        }

        /// <summary>
        /// Decision 4: a body SC has not stamped (no sc_craftRoles: a new member, the first load after the update, a re-created body)
        /// gets the registry's mask, or the specialty's defaults; a stamped body is the truth (the player's toggles) and is mirrored
        /// into the registry. A specialty change re-applies that specialty's defaults once.
        /// </summary>
        public static void SyncRoles(Member m, EntityAlive npc)
        {
            bool marked = npc.Buffs.HasCustomVar(RolesMarkCvar);
            bool specChanged = m.CraftRoles >= 0 && m.CraftRolesFor != m.Specialty;
            if (!marked || specChanged)
            {
                int mask = !specChanged && m.CraftRoles >= 0 ? m.CraftRoles : DefaultMask(m.Specialty);
                WriteMask(npc, mask);
                npc.Buffs.SetCustomVar(RolesMarkCvar, 1f);
                bool changed = m.CraftRoles != mask || m.CraftRolesFor != m.Specialty;
                m.CraftRoles = mask;
                m.CraftRolesFor = m.Specialty;
                roleStamps++;
                ScLog.Out(m.Name + ": crafting roles " + MaskText(mask) + (specChanged ? " (" + m.Specialty + "'s defaults)" : " (stamped on this body)"));
                if (changed) ScRegistry.SaveAtomic();
                return;
            }
            int now = ReadMask(npc);
            if (now == m.CraftRoles && m.CraftRolesFor == m.Specialty) return;
            ScLog.Out(m.Name + ": crafting roles now " + MaskText(now) + " (changed in game - kept)");
            m.CraftRoles = now;
            m.CraftRolesFor = m.Specialty;
            roleMirrors++;
            ScRegistry.SaveAtomic();
        }

        /// <summary>The Community tab's role toggle (any member with a body here): the cvar flips as the wheel's would, the registry follows.</summary>
        public static bool ToggleRole(Member m, int role, out string message)
        {
            message = null;
            if (m == null || role < 0 || role >= RoleCvars.Length) return false;
            EntityAlive npc = ScCommunity.FindNpc(m.Id);
            if (npc == null || npc.IsDead() || npc.Buffs == null) { message = Localization.Get("scNotLoaded"); return false; }
            int mask = ReadMask(npc) ^ (1 << role);
            WriteMask(npc, mask);
            npc.Buffs.SetCustomVar(RolesMarkCvar, 1f);
            m.CraftRoles = mask;
            m.CraftRolesFor = m.Specialty;
            ScRegistry.SaveAtomic();
            bool on = (mask & (1 << role)) != 0;
            ScLog.Out(m.Name + ": crafting role " + RoleNames[role] + (on ? " on" : " off") + " (the Community tab) - now " + MaskText(mask));
            message = string.Format(Localization.Get(on ? "scRoleOn" : "scRoleOff"), m.Name, Localization.Get(RoleLocKeys[role]));
            return true;
        }

        // ------------------------------------------------------------------ XP ----

        /// <summary>
        /// One or more items made (Patches_Craft): where = "backpack", "station" or "sweep" (the station's list when the player
        /// opened it). A counted craft pays CraftSpecXpPerCraft each, whole points through AddSpecialtyXp from the carry. npc may be
        /// null (a member who is not loaded, from the sweep).
        /// </summary>
        public static void OnCrafted(Member m, EntityAlive npc, string recipeName, int crafts, string where)
        {
            if (m == null || crafts <= 0 || !ScConfig.CraftSkillsEnabled) return;
            try
            {
                Stats st = StatsOf(m.Id);
                st.Crafts += crafts;
                if (where == "backpack") st.Backpack += crafts;
                else if (where == "station") st.Station += crafts;
                else st.Swept += crafts;
                if (!Counts(m.Specialty, recipeName))
                {
                    st.NotCounted += crafts;
                    ScLog.Verbose(m.Name + " made " + crafts + " x " + recipeName + " (" + where + ") - not a " + m.Specialty + " recipe, no specialty XP");
                    return;
                }
                st.Counted += crafts;
                float add = ScConfig.CraftSpecXpPerCraft * crafts;
                if (add <= 0f) return;
                m.CraftXpCarry += add;
                int whole = (int)Math.Floor(m.CraftXpCarry + 0.0001f);
                ScLog.Verbose(m.Name + " made " + crafts + " x " + recipeName + " (" + where + "): +" + add.ToString("0.##", CultureInfo.InvariantCulture)
                              + " " + m.Specialty + " XP, carry " + m.CraftXpCarry.ToString("0.##", CultureInfo.InvariantCulture));
                if (whole <= 0) { xpDirty = true; return; }     // the carry: saved with the next flush
                m.CraftXpCarry = Math.Max(0f, m.CraftXpCarry - whole);
                int before = m.SpecXp;
                bool gained = ScSkills.AddSpecialtyXp(m, whole);
                st.XpPaid += m.SpecXp - before;
                if (gained)
                {
                    st.Stars++;
                    if (npc != null && !npc.IsDead()) ScSkills.StampCvars(m, npc);     // the new levels at once
                    ScCommunity.Tell(ScCommunity.LocalPlayer, string.Format(Localization.Get("scCraftStar"), m.Name,
                                     Localization.Get(ScNames.LocKey(m.Specialty)), m.SpecLevel), "");
                }
                xpDirty = true;
                FlushXp(gained);                  // a star at once, else at most every FlushSeconds (a 30-item queue was 15 writes)
            }
            catch (Exception e)
            {
                ScLog.Once("craft-xp", e);
            }
        }

        /// <summary>Saves the registry when craft XP is waiting: now, or when FlushSeconds have passed (ScWork.Sweep, 1 Hz).</summary>
        public static void FlushXp(bool now)
        {
            if (!xpDirty) return;
            float t = Time.unscaledTime;
            if (!now && t - xpSavedAt < FlushSeconds) return;
            xpDirty = false;
            xpSavedAt = t;
            xpSaves++;
            ScRegistry.SaveAtomic();
        }

        // ------------------------------------------------------------------ leaving, ammo ----

        /// <summary>ScCommunity.CleanupStale: a former member gets 0-XNPCCore's own Progression when someone next hires her.</summary>
        public static void OnLeft(EntityAlive npc)
        {
            if (npc == null || npc.Buffs == null) return;
            try
            {
                ScWork.DropJob(npc, "left the community");     // review #1: CleanupStale takes SdcsLootMode off, the order would stay
                if (ScXnpc.HasXnpcLevels(npc)) npc.Progression = null;
                npc.Buffs.RemoveCustomVar(RolesMarkCvar);
                ScWork.Forget(npc.entityId, npc);
            }
            catch (Exception e)
            {
                ScLog.Once("craft-left", e);
            }
        }

        /// <summary>
        /// ScAmmo.MoveWindowAmmoToBag (design 3.6): only ammo her own weapons fire - her pair's gun or bow, the gun in her hand -
        /// moves from her inventory window to her bag. Anything else (rounds she crafted, ammo a loot job found) stays in the
        /// window, where the player can take it.
        /// </summary>
        public static bool MovesToBag(EntityAlive e, ItemClass ammo)
        {
            if (e == null || ammo == null) return true;
            if (ScRegistry.GetMember(e.entityId) == null) return true;       // members only: stand-ins, raiders keep the old rule
            if (Fires(ScAmmo.PairGun(e), ammo)) return true;
            ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
            if (held != null && !held.IsEmpty() && Fires(held.ItemClass, ammo)) return true;
            Patches_Craft.Count("c1-window-ammo-kept");
            return false;
        }

        private static bool Fires(ItemClass gun, ItemClass ammo)
        {
            ItemActionRanged r = ScAmmo.RangedAction(gun);
            if (r == null) return false;
            foreach (string n in r.MagazineItemNames)
            {
                ItemClass a = ItemClass.GetItemClass(n, true);
                if (a != null && a.Id == ammo.Id) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>The 1/3/5/7-star levels of each specialty's skills, from ScCraftMath.Level and the loaded max levels.</summary>
        public static string Table(bool full)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < specSkills.Length; i++)
            {
                if (i > 0) sb.Append(full ? "\n" : "; ");
                sb.Append(ScCraftMath.SpecialtyNames[i]).Append(':');
                if (specSkills[i].Count == 0) { sb.Append(" -"); continue; }
                foreach (ProgressionClass pc in specSkills[i])
                {
                    sb.Append(' ').Append(full ? pc.Name : pc.Name.Replace("crafting", "")).Append('/').Append(pc.MaxLevel);
                    if (!full) continue;
                    sb.Append(" [");
                    for (int s = 1; s <= Member.MaxStars; s += 2) sb.Append(s > 1 ? " " : "").Append(s).Append("*=").Append(LevelFor(pc, s));
                    sb.Append(']');
                }
            }
            return sb.ToString();
        }

        private static Member Find(string key)
        {
            int id;
            if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return ScRegistry.GetMember(id);
            foreach (Member m in ScRegistry.Members.Values)
                if (string.Equals(m.Name, key, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        /// <summary>sc craft [table | &lt;id|name&gt; [spec &lt;Specialty&gt; | end]].</summary>
        public static void ConsoleCmd(List<string> p, Action<string> output)
        {
            try
            {
                ScConfig.EnsureParsed();
                if (p.Count < 2)
                {
                    output("[SC] craft table | craft <id|name> [spec <Specialty> | end] - " + ScConfig_Line());
                    output("[SC] crafting hooks: " + Patches_Craft.Report() + "; levels written " + levelWrites + ", Progressions made " + progressionsMade
                           + ", role stamps " + roleStamps + ", role changes kept " + roleMirrors + ", XP saves " + xpSaves + "; " + ScWork.Summary());
                    return;
                }
                if (p[1].Equals("table", StringComparison.OrdinalIgnoreCase))
                {
                    if (!Resolve()) { output("[SC] the progression is not loaded yet"); return; }
                    output("[SC] crafting levels by stars (ScCraftMath.Level, max levels as loaded, curve " + ScConfig.CraftLevelCurve.ToString("0.##", CultureInfo.InvariantCulture) + "):");
                    foreach (string line in Table(true).Split('\n')) output("[SC]   " + line);
                    return;
                }
                Member m = Find(p[1]);
                if (m == null) { output("[SC] no member '" + p[1] + "'"); return; }
                EntityAlive npc = ScCommunity.FindNpc(m.Id);
                if (p.Count >= 4 && p[2].Equals("spec", StringComparison.OrdinalIgnoreCase))
                {
                    Specialty sp;
                    if (!ScNames.TrySpecialty(p[3], out sp) || sp == Specialty.None) { output("[SC] unknown specialty '" + p[3] + "'"); return; }
                    m.Specialty = sp;
                    if (npc != null) ScSkills.StampCvars(m, npc);
                    ScRegistry.SaveAtomic();
                    output("[SC] " + m.Name + " is now a " + sp + " specialist (" + m.SpecLevel + " stars)");
                }
                else if (p.Count >= 3 && p[2].Equals("end", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ScWork.OnShift(m.Id)) output("[SC] " + m.Name + " is not on a work shift");
                    else { ScWork.End(m, npc, "sc craft end", true); output("[SC] " + m.Name + "'s shift ended"); }
                }
                foreach (string line in Describe(m, npc)) output("[SC] " + line);
            }
            catch (Exception e)
            {
                output("[SC] craft failed: " + e.Message);
                ScLog.Once("craft-console", e);
            }
        }

        private static string ScConfig_Line()
        {
            return "crafting " + (ScConfig.CraftSkillsEnabled ? "on" : "OFF") + ", work shifts " + (ScConfig.WorkShiftsEnabled ? "on" : "OFF");
        }

        private static List<string> Describe(Member m, EntityAlive npc)
        {
            var o = new List<string>();
            Stats st;
            stats.TryGetValue(m.Id, out st);
            o.Add(m.Name + " [" + m.Id + "] " + m.Status + ", " + m.Specialty + " " + m.SpecLevel + " stars (xp " + m.SpecXp + ", next star at "
                  + (m.SpecLevel < Member.MaxStars ? ScSkills.NeededFor(m.SpecLevel + 1).ToString() : "-") + ", craft carry "
                  + m.CraftXpCarry.ToString("0.##", CultureInfo.InvariantCulture) + ")"
                  + (npc == null ? ", not loaded" : ", Progression " + (npc.Progression != null ? "yes" : "NONE") + (ScXnpc.HasXnpcLevels(npc) ? "" : " (not 0-XNPCCore's body: no crafting)")));
            if (!Resolve()) { o.Add("the progression is not loaded yet"); return o; }
            int si = SpecIndex(m.Specialty);
            var sb = new StringBuilder("skills:");
            if (si < 0 || specSkills[si].Count == 0) sb.Append(" none for ").Append(m.Specialty);
            else foreach (ProgressionClass pc in specSkills[si])
            {
                sb.Append(' ').Append(pc.Name).Append(' ');
                ProgressionValue pv = npc != null && npc.Progression != null ? npc.Progression.GetProgressionValue(pc.Name) : null;
                if (pv != null) sb.Append(pv.Level).Append(" (calc ").Append(pv.GetCalculatedLevel(npc).ToString("0", CultureInfo.InvariantCulture)).Append(')');
                else sb.Append('-');
                sb.Append('/').Append(pc.MaxLevel).Append(" want ").Append(LevelFor(pc, m.SpecLevel)).Append(';');
            }
            o.Add(sb.ToString());
            if (npc != null && npc.Progression != null)
            {
                int others = 0, above = 0;
                string top = null;
                foreach (ProgressionClass pc in managed)
                {
                    if (si >= 0 && specSkills[si].Contains(pc)) continue;
                    others++;
                    ProgressionValue pv = npc.Progression.GetProgressionValue(pc.Name);
                    if (pv != null && pv.Level > 0) { above++; if (top == null) top = pc.Name + " " + pv.Level; }
                }
                o.Add("outside the specialty: " + others + " skills, " + above + " above 0" + (top != null ? " (e.g. " + top + ")" : "") + "; NPCCore's own counters "
                      + NpcCoreCounters(npc));
                o.Add("can make now (her specialty's recipes, 0-XNPCCore's IsUnlocked): " + Unlocked(m, npc));
            }
            int mask = npc != null ? ReadMask(npc) : -1;
            o.Add("roles: body " + (npc != null ? MaskText(mask) + (npc.Buffs.HasCustomVar(RolesMarkCvar) ? " (stamped)" : " (NOT stamped yet)") : "-")
                  + ", registry " + MaskText(m.CraftRoles) + (m.CraftRoles >= 0 ? " for " + m.CraftRolesFor : "") + ", " + m.Specialty + "'s defaults " + MaskText(DefaultMask(m.Specialty)));
            o.Add("shift: " + ScWork.Describe(m, npc));
            o.Add("this session: " + (st == null ? "no crafts" : st.Crafts + " craft(s) (" + st.Backpack + " backpack, " + st.Station + " station, " + st.Swept + " from a station's list), "
                  + st.Counted + " counted, " + st.NotCounted + " not, specialty XP paid " + st.XpPaid + ", stars gained " + st.Stars));
            return o;
        }

        private static string NpcCoreCounters(EntityAlive npc)
        {
            var parts = new List<string>();
            foreach (string c in NpcCoreCraftsCvars) parts.Add(c + "=" + npc.Buffs.GetCustomVar(c).ToString("0", CultureInfo.InvariantCulture));
            return string.Join(" ", parts.ToArray());
        }

        private static string Unlocked(Member m, EntityAlive npc)
        {
            int si = SpecIndex(m.Specialty);
            if (si < 0) return "-";
            if (recipeIndex == null) BuildIndex();
            int total = 0, open = 0;
            var names = new List<string>();
            foreach (var kv in recipeIndex)
            {
                if ((kv.Value & specMask[si]) == 0UL) continue;
                List<Recipe> rs = CraftingManager.GetRecipes(kv.Key);
                if (rs == null || rs.Count == 0) continue;
                total++;
                bool any = false;
                foreach (Recipe r in rs) if (r != null && SdcsCraft.IsUnlocked(npc, r)) { any = true; break; }
                if (!any) continue;
                open++;
                if (names.Count < 12) names.Add(kv.Key);
            }
            return open + " of " + total + (names.Count > 0 ? " (" + string.Join(", ", names.ToArray()) + (open > names.Count ? ", ..." : "") + ")" : "");
        }
    }
}
