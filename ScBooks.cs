using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using NPCSdcsCharacters.Pure;

namespace SurvivorCommunity
{
    /// <summary>
    /// X2c (round 25b, 3.4): 0-XNPCCore 3.4.03's books on community members - "magazines become training" (the user's
    /// decision, 2026-11-09). Its "Read..." row, the voice's "read your books" and the order router all end in SdcsBooks:
    /// List (the rows), Order (a read: one book, or "*" for all), Teaches (the gate), ReadOne (takes the book, Fire runs its
    /// onSelfPrimaryActionEnd effects, Learned picks the line said). A member's crafting levels are her specialty stars'
    /// (ScCraft.SetLevels, every second), so the level a crafting magazine adds would be undone a second later while the
    /// book stays used up. Instead (Patches_Craft C1h-C1m):
    ///
    ///  - Schematics unlock their recipe exactly as 0-XNPCCore does (their recipe cvar is not a level SC sets): untouched.
    ///  - A crafting magazine (identified as SdcsBooks.InfoOf does: an AddProgressionLevel on a crafting skill among its
    ///    onSelfPrimaryActionEnd effects, and no SetProgressionLevel - that is a perk book) trains her when one of its skills
    ///    is one of her specialty's (CraftSkills&lt;Specialty&gt;), her specialty is below 7 stars, that skill is below its max
    ///    level on her and CraftSpecXpPerMagazineLevel is above 0 (Trains). Its row then reads "useful", and reading it pays
    ///    CraftSpecXpPerMagazineLevel specialty XP per level it would have granted (the sandbox's Magazine Points for a
    ///    level-1 action, as vanilla's Execute counts it) through ScSkills.AddSpecialtyXp; the level itself is put back.
    ///  - Any other magazine (another specialty's skill, or her specialty at 7 stars) is listed "won't help her" (NoUse), its
    ///    single read is refused by Voice Control's router from that row and by 0-XNPCCore's own Teaches gate, and ReadAll
    ///    never reads it: never used up.
    ///  - The line: 0-XNPCCore's Learned would say "My Food is at level 15 now." (a level SC resets) or, with the level put
    ///    back, warn "it changed nothing". For a read that paid it answers TrainLineId, which C1m turns into SC's own line
    ///    (scBookPracticeSaid) through Voice Control's SubtitleManager.SayToEarshot - the way its say-lines are spoken - and
    ///    "I read N books." stays for a ReadAll of several. One tooltip per read order shows the XP (or the new star).
    ///
    /// Every community member (followers, residents on a shift, any status), crafting on (CraftSkillsEnabled), on 0-XNPCCore's
    /// body; strangers and 0-XNPCCore's own hires are untouched. The read context is the one Order call (static, main thread):
    /// the payment fires only for that member, only for an AddProgressionLevel instance of the book being read.
    /// </summary>
    public static class ScBooks
    {
        /// <summary>The line id Learned answers for a read that trained her (C1l); C1m says SC's line for it. No Voice Control id has the "sc." prefix.</summary>
        public const string TrainLineId = "sc.book.train";

        /// <summary>A book as SC sees it: a crafting magazine (its crafting skills and the AddProgressionLevel actions that add them) or not.</summary>
        internal sealed class MagInfo
        {
            public ItemClass Item;
            public bool IsMagazine;
            public readonly List<string> Skills = new List<string>();
            public readonly List<MinEventActionAddProgressionLevel> Actions = new List<MinEventActionAddProgressionLevel>();
        }

        /// <summary>One SdcsBooks.Order on a member (C1j): what it paid, for the tooltip at its end.</summary>
        private sealed class Reading
        {
            public EntityAlive Npc;
            public Member M;
            public bool All;
            public int Magazines, Xp, Stars, Undone;
            public object LastValue;
            public bool PaidPending;
        }

        /// <summary>C1k's state between the vanilla Execute's prefix and postfix (public: it is the Harmony patch's __state).</summary>
        public sealed class LevelState
        {
            public ProgressionValue Pv;
            public int Before, Nominal;
            public object Value;
        }

        private static readonly Dictionary<string, MagInfo> scanned = new Dictionary<string, MagInfo>(StringComparer.Ordinal);
        private static Reading reading;
        private static int magazinesRead, xpPaid, starsGained;

        // Voice Control's SubtitleManager.SayToEarshot(EntityAlive, string, string), bound once per session
        private static bool sayBound;
        private static MethodInfo mSay;

        public static void Reset()
        {
            scanned.Clear();
            reading = null;
            magazinesRead = xpPaid = starsGained = 0;
        }

        /// <summary>The member whose books SC rules: crafting on, a registry member, on 0-XNPCCore's body (ScCraft.Apply's test). Null otherwise.</summary>
        public static Member MemberOf(EntityAlive npc)
        {
            if (npc == null || !ScConfig.CraftSkillsEnabled) return null;
            Member m = ScRegistry.GetMember(npc.entityId);
            return m != null && ScXnpc.HasXnpcLevels(npc) ? m : null;
        }

        /// <summary>SdcsBooks.InfoOf's test, cached by item (the item checked by reference, as InfoOf does: a new world brings new ItemClasses).</summary>
        internal static MagInfo Scan(ItemClass ic)
        {
            if (ic == null) return null;
            MagInfo mi;
            if (scanned.TryGetValue(ic.Name, out mi) && ReferenceEquals(mi.Item, ic)) return mi;
            mi = new MagInfo { Item = ic };
            bool perk = false;
            List<MinEffectGroup> groups = ic.Effects != null ? ic.Effects.EffectGroups : null;
            if (groups != null)
            {
                foreach (MinEffectGroup g in groups)
                {
                    List<MinEventActionBase> acts;
                    if (g == null || g.TriggeredEffects == null || !g.TriggeredEffects.TryGetValue(MinEventTypes.onSelfPrimaryActionEnd, out acts) || acts == null) continue;
                    foreach (MinEventActionBase a in acts)
                    {
                        if (a is MinEventActionSetProgressionLevel) { perk = true; continue; }
                        MinEventActionAddProgressionLevel add = a as MinEventActionAddProgressionLevel;
                        ProgressionClass pc = add != null ? SkillClass(add.progressionName) : null;
                        if (pc == null || !pc.IsCrafting) continue;
                        mi.Actions.Add(add);
                        if (!mi.Skills.Contains(pc.Name)) mi.Skills.Add(pc.Name);
                    }
                }
            }
            mi.IsMagazine = mi.Skills.Count > 0 && !perk;     // BookRules.Classify: a perk book first, then a magazine
            scanned[ic.Name] = mi;
            return mi;
        }

        private static ProgressionClass SkillClass(string name)
        {
            ProgressionClass pc;
            return !string.IsNullOrEmpty(name) && Progression.ProgressionClasses != null && Progression.ProgressionClasses.TryGetValue(name, out pc) ? pc : null;
        }

        /// <summary>
        /// Does reading this magazine train her? XP per magazine level above 0, a specialty below 7 stars, and one of its skills
        /// her specialty's and below its max level on her - 0-XNPCCore's own AnySkillBelowMax for that skill, so a read SC calls
        /// useful is never refused by 0-XNPCCore.
        /// </summary>
        internal static bool Trains(Member m, EntityAlive npc, MagInfo mi)
        {
            if (m == null || npc == null || mi == null || !mi.IsMagazine || npc.Progression == null) return false;
            if (ScConfig.CraftSpecXpPerMagazineLevel <= 0f || m.Specialty == Specialty.None || m.SpecLevel >= Member.MaxStars) return false;
            foreach (string skill in mi.Skills)
            {
                if (!ScCraft.InSpecialty(m.Specialty, skill)) continue;
                ProgressionValue pv = npc.Progression.GetProgressionValue(skill);
                if (pv != null && pv.ProgressionClass != null && pv.Level < pv.ProgressionClass.MaxLevel) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ C1h: the rows ----

        /// <summary>
        /// C1h, SdcsBooks.List's postfix: a member's magazine rows become "useful" (it trains her) or "won't help her" (NoUse,
        /// greyed: Voice Control refuses its single read, ReadAll skips it). Schematic and perk book rows stay 0-XNPCCore's.
        /// </summary>
        internal static void RewriteRows(EntityAlive npc, List<string> rows)
        {
            Member m = MemberOf(npc);
            if (m == null || rows == null) return;
            for (int i = 0; i < rows.Count; i++)
            {
                string name, role;
                BookUse use;
                if (!BookRules.TryParseUseRow(rows[i], out name, out use, out role)) continue;
                MagInfo mi = Scan(ItemClass.GetItemClass(name));
                if (mi == null || !mi.IsMagazine) continue;
                bool trains = Trains(m, npc, mi);
                rows[i] = BookRules.UseRow(name, trains ? BookUse.Useful : BookUse.NoUse, null);
                Patches_Craft.Count(trains ? "c1h-magazine-row-useful" : "c1h-magazine-row-wont-help");
            }
        }

        /// <summary>C1i, SdcsBooks.Teaches' postfix: for a member's magazine, true only when it trains her (a single read is then refused, ReadAll stops).</summary>
        internal static bool Teaches(EntityAlive npc, ItemClass ic)
        {
            Member m = MemberOf(npc);
            if (m == null) return true;
            MagInfo mi = Scan(ic);
            if (mi == null || !mi.IsMagazine || Trains(m, npc, mi)) return true;
            if (reading != null && ReferenceEquals(reading.Npc, npc)) Patches_Craft.Count("c1i-magazine-read-refused");
            return false;
        }

        // ------------------------------------------------------------------ C1j: one read order ----

        /// <summary>C1j, SdcsBooks.Order's prefix: a read order ("read:&lt;item&gt;" or "read:*") on a member opens the read context.</summary>
        internal static void Begin(EntityAlive npc, string arg)
        {
            reading = null;
            Member m = MemberOf(npc);
            string item;
            if (m == null || !BookRules.TryParseOrderArg(arg, BookRules.ReadOrderArgPrefix, out item)) return;
            reading = new Reading { Npc = npc, M = m, All = item == BookRules.ReadAllItem };
        }

        /// <summary>C1j, SdcsBooks.Order's finalizer: the context closes; what it paid is saved, stamped (a star) and told once.</summary>
        internal static void End()
        {
            Reading r = reading;
            reading = null;
            if (r == null || (r.Magazines == 0 && r.Undone == 0)) return;
            Member m = r.M;
            if (r.Magazines == 0)
            {
                // nothing paid, yet a book's level was put back: a magazine read that C1h/C1i should have refused (a magazine
                // with a specialty skill and another one pays for the first and puts the second back - that is no warning)
                ScLog.Warn(m.Name + ": " + r.Undone + " crafting level(s) a book would have added were put back with no XP (not a magazine of her "
                           + m.Specialty + ", or already at 7 stars) - the read should have been refused (sc craft: c1k-magazine-level-undone)");
                return;
            }
            if (r.Stars > 0 && r.Npc != null && !r.Npc.IsDead()) ScSkills.StampCvars(m, r.Npc);   // the new star's levels at once
            ScRegistry.SaveAtomic();
            string spec = Localization.Get(ScNames.LocKey(m.Specialty));
            ScLog.Out(m.Name + " read " + r.Magazines + " crafting magazine(s) as training (" + (r.All ? "read all" : "one book") + "): +" + r.Xp + " "
                      + m.Specialty + " XP, now " + m.SpecXp + " (" + m.SpecLevel + " stars" + (r.Stars > 0 ? ", " + r.Stars + " gained" : "") + ")");
            EntityPlayer to = EntityUtilities.GetLeaderOrOwner(r.Npc.entityId) as EntityPlayer;
            if (to == null) to = ScCommunity.LocalPlayer;
            string text = r.Stars > 0
                ? string.Format(Localization.Get("scBookStar"), m.Name, spec, m.SpecLevel)
                : string.Format(Localization.Get("scBookTrained"), m.Name, r.Magazines, r.Xp, spec, m.SpecXp,
                                m.SpecLevel < Member.MaxStars ? ScSkills.NeededFor(m.SpecLevel + 1).ToString(CultureInfo.InvariantCulture) : "-");
            ScCommunity.Tell(to, text, "");
        }

        // ------------------------------------------------------------------ C1k: the payment ----

        /// <summary>
        /// C1k, vanilla MinEventActionAddProgressionLevel.Execute's prefix: inside a member's read order, for one of the book's
        /// own crafting AddProgressionLevel actions with her as a target - her level before it, and the levels it grants
        /// (vanilla's own count: the sandbox's Magazine Points for a crafting action of level 1). Null otherwise: untouched.
        /// </summary>
        internal static LevelState BeforeLevel(MinEventActionAddProgressionLevel act, MinEventParams p)
        {
            Reading r = reading;
            if (r == null || act == null || act.targets == null || !act.targets.Contains(r.Npc) || r.Npc.Progression == null) return null;
            ItemValue iv = p != null ? p.ItemValue : null;
            MagInfo mi = Scan(iv != null ? iv.ItemClass : null);
            if (mi == null || !mi.IsMagazine || !mi.Actions.Contains(act)) return null;
            ProgressionValue pv = r.Npc.Progression.GetProgressionValue(act.progressionName);
            if (pv == null || pv.ProgressionClass == null || !pv.ProgressionClass.IsCrafting) return null;
            int n = act.level;
            if (pv.ProgressionClass.Type == ProgressionType.Crafting && n == 1)
                n = global::SandboxOptions.SandboxOptionManager.GetInt(global::SandboxOptions.SandboxOptions.PointsPerMagazine);
            return new LevelState { Pv = pv, Before = pv.Level, Nominal = n, Value = iv };
        }

        /// <summary>
        /// C1k, the postfix: her level goes back to what her stars made it (the book's level never lands), and a magazine that
        /// trains her pays CraftSpecXpPerMagazineLevel specialty XP per level it granted. One read of a magazine counts once.
        /// </summary>
        internal static void AfterLevel(LevelState s)
        {
            Reading r = reading;
            if (s == null || r == null) return;
            s.Pv.Level = s.Before;
            Member m = r.M;
            string skill = s.Pv.ProgressionClass.Name;
            if (!ScCraft.InSpecialty(m.Specialty, skill) || m.SpecLevel >= Member.MaxStars || s.Nominal <= 0 || ScConfig.CraftSpecXpPerMagazineLevel <= 0f)
            {
                r.Undone++;
                Patches_Craft.Count("c1k-magazine-level-undone");
                return;
            }
            // 0-XNPCCore reads a clone of the stack's value per book: one per magazine read. A magazine with more than one level
            // action for her specialty pays once (Fable review X2, finding 7); its other actions are only put back.
            if (ReferenceEquals(r.LastValue, s.Value))
            {
                r.Undone++;
                Patches_Craft.Count("c1k-magazine-extra-action");
                return;
            }
            r.LastValue = s.Value;
            r.Magazines++;
            magazinesRead++;
            int before = m.SpecXp, star = m.SpecLevel;
            bool gained = ScSkills.AddSpecialtyXp(m, s.Nominal * ScConfig.CraftSpecXpPerMagazineLevel);
            int got = m.SpecXp - before;
            r.Xp += got;
            xpPaid += got;
            if (gained) { r.Stars += m.SpecLevel - star; starsGained += m.SpecLevel - star; }
            r.PaidPending = true;
            Patches_Craft.Count("c1k-magazine-xp-paid");
            ScLog.Verbose(m.Name + " studies " + (s.Value is ItemValue ? ((ItemValue)s.Value).ItemClass.GetItemName() : "?") + " (" + skill + ", " + s.Nominal
                          + " level(s) not added): +" + got + " " + m.Specialty + " XP, now " + m.SpecXp + (gained ? " - a star: " + m.SpecLevel : ""));
        }

        // ------------------------------------------------------------------ C1l, C1m: the line ----

        /// <summary>C1l, SdcsBooks.Learned's prefix: the line for a read that just paid (TrainLineId), instead of a level SC resets.</summary>
        internal static bool TrainingLine(EntityAlive npc, out string line)
        {
            line = null;
            Reading r = reading;
            if (r == null || !ReferenceEquals(r.Npc, npc) || !r.PaidPending) return false;
            r.PaidPending = false;
            line = TrainLineId;
            Patches_Craft.Count("c1l-training-line");
            return true;
        }

        /// <summary>
        /// C1m, SdcsSayLine.Say's prefix: TrainLineId is said as SC's own line through Voice Control's SubtitleManager.SayToEarshot
        /// (its subtitle and voice, the voice gate as for its own lines). True when it was ours (the original then never sees it).
        /// </summary>
        internal static bool SayTraining(EntityAlive npc, string lineId)
        {
            if (!string.Equals(lineId, TrainLineId, StringComparison.Ordinal)) return false;
            MethodInfo say = SayMethod();
            if (npc != null && say != null)
            {
                // no line id (Fable review X2, finding 8): Voice Control routes a say-line's id to its gesture, job-line and network
                // handlers, none of which knows SC's; the caption falls back to the words either way
                say.Invoke(null, new object[] { npc, Localization.Get("scBookPracticeSaid"), null });
                Patches_Craft.Count("c1m-training-line-said");
            }
            return true;
        }

        private static MethodInfo SayMethod()
        {
            if (sayBound) return mSay;
            sayBound = true;
            Type t = ScTypes.Find("XNPCVoiceControl.UI.SubtitleManager");
            mSay = t != null ? AccessTools.Method(t, "SayToEarshot", new[] { typeof(EntityAlive), typeof(string), typeof(string) }) : null;
            if (mSay == null && t != null)
                ScLog.Once("book-say-bind", "books: Voice Control's SubtitleManager.SayToEarshot(EntityAlive, string, string) not found - a member's training line is not said (the tooltip still shows)");
            return mSay;
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>sc craft's line: this session's training reads.</summary>
        public static string Summary()
        {
            return "books: " + magazinesRead + " magazine(s) read as training, " + xpPaid + " specialty XP paid, " + starsGained + " star(s) gained"
                   + (ScConfig.CraftSpecXpPerMagazineLevel > 0f ? "" : " (CraftSpecXpPerMagazineLevel 0: magazines help no member)");
        }
    }
}
