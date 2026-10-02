using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// Locked &amp; Loaded's NPC magazines (L&amp;L 1.4, REInvMags.NpcMagazines; the hand-off LnL_NPC_Magazines_API.md), the
    /// one file that knows that mod exists (migration task M11). With L&amp;L loaded, an NPC that carries a magazine for its
    /// gun - in its bag or toolbelt, or seated in the gun - reloads like a player, and one that carries none reloads from
    /// loose rounds as before; so the rest of SC only asks the questions below ("does this NPC reload this gun by
    /// magazine?", "how many rounds does it have for it?") and arms, refills, records and restores through here.
    ///
    /// Bound by name, no compile-time reference: ModManager.ModLoaded("LockedAndLoaded") (its ModInfo Name), then
    /// AccessTools.TypeByName and AccessTools.Method with the exact parameter types; each method must be public, static and
    /// return what the hand-off says. Required: Arm, Refill, Rounds, Record, Restore (the API) and Carries (L&amp;L's own "this
    /// NPC uses magazines for this gun", the same test its reload asks). Optional: MagazineRegistry.AllForGun with
    /// MagazineDef.Name / Twin, and NpcMagazines.TierOf with Tier.Magazines - only the issue of empty magazines to a member
    /// who has none and the move of handed-over magazines from the window to the bag (ScAmmo) need them, and both stay
    /// off without them. Bound once per game (Reset on GameStarting),
    /// lazily or from GameStartDone, with one log line either way. Anything missing, or any call that throws, turns the
    /// adapter off for the rest of the game with one error line, and every caller falls back to loose rounds: an NPC
    /// with magazines then keeps them (L&amp;L still reloads it from them), it just stops getting more. Host only, like L&amp;L's
    /// NPC side.
    /// </summary>
    public static class ScMags
    {
        /// <summary>Locked &amp; Loaded's mod name (ModInfo.xml Name).</summary>
        public const string ModName = "LockedAndLoaded";
        private const string ApiTypeName = "REInvMags.NpcMagazines";
        private const string RegistryTypeName = "REInvMags.MagazineRegistry";

        private static bool resolved, active, threw;
        private static string state = "not checked yet";
        private static MethodInfo arm, refill, rounds, record, restore, carries, allForGun, tierOf;
        private static FieldInfo defName, defTwin, tierMagazines;

        public static void Reset()
        {
            resolved = false;
            active = false;
            threw = false;
            state = "not checked yet";
            arm = refill = rounds = record = restore = carries = allForGun = tierOf = null;
            defName = defTwin = tierMagazines = null;
        }

        /// <summary>Is L&amp;L loaded and its NPC API bound (and has nothing thrown this game)?</summary>
        public static bool Active { get { return resolved ? active : Resolve(); } }

        /// <summary>
        /// Was the adapter on this game and then switched off by a throw (the review's F2)? The bodies still carry their
        /// magazines then, but SC can no longer read them: the member record keeps the last magazines it could name rather
        /// than writing "none" over them (ScAmmo.Record). False when L&amp;L simply is not loaded.
        /// </summary>
        public static bool Threw { get { return threw; } }

        /// <summary>The adapter's state for sc xnpc: "on", "off (Locked &amp; Loaded is not loaded)", "off - ..." with the reason.</summary>
        public static string State { get { if (!resolved) Resolve(); return state; } }

        /// <summary>Binds once per game; logs the outcome once (GameStartDone calls it so the line is there every game).</summary>
        public static bool Resolve()
        {
            if (resolved) return active;
            resolved = true;
            active = false;
            try
            {
                if (!ModManager.ModLoaded(ModName))
                {
                    state = "off (Locked & Loaded is not loaded)";
                    ScLog.Out("NPC magazines: Locked & Loaded is not loaded - SC's NPCs carry loose rounds");
                    return false;
                }
                string why;
                if (!BindTypes(out why))
                {
                    state = "off - " + why;
                    ScLog.Warn("NPC magazines: Locked & Loaded is loaded but its NPC magazine API could not be bound (" + why
                               + ") - SC's NPCs carry loose rounds (Locked & Loaded older than 1.4, or renamed?)");
                    return false;
                }
                active = true;
                state = "on";
                ScLog.Out("NPC magazines: Locked & Loaded's NPC API bound - a survivor's or raider's gun that takes magazines gets its tier's"
                          + " loaded magazines instead of loose rounds" + (allForGun == null || defName == null || defTwin == null
                              ? " (MagazineRegistry.AllForGun not bound: a member without magazines is not issued any)" : ""));
                return true;
            }
            catch (Exception e)
            {
                state = "off - binding threw " + e.GetType().Name + ": " + e.Message;
                threw = true;            // L&L is loaded but unreadable: the member records keep their magazines (Threw)
                ScLog.Once("mags-bind", e);
                return false;
            }
        }

        /// <summary>
        /// Resolves the types and methods (no ModLoaded test and no logging, so the offline check can run it against the
        /// real LockedAndLoaded.dll). False, with what is missing, when a required one is not there as expected.
        /// </summary>
        internal static bool BindTypes(out string why)
        {
            var missing = new List<string>();
            Type t = AccessTools.TypeByName(ApiTypeName);
            if (t == null) { why = ApiTypeName + " not found"; return false; }
            arm = Bind(t, "Arm", typeof(int), new[] { typeof(EntityAlive), typeof(string), typeof(int), typeof(string) }, missing);
            refill = Bind(t, "Refill", typeof(int), new[] { typeof(EntityAlive), typeof(string), typeof(string), typeof(int) }, missing);
            rounds = Bind(t, "Rounds", typeof(int), new[] { typeof(EntityAlive), typeof(string) }, missing);
            record = Bind(t, "Record", typeof(string), new[] { typeof(EntityAlive), typeof(string) }, missing);
            restore = Bind(t, "Restore", typeof(int), new[] { typeof(EntityAlive), typeof(string) }, missing);
            carries = Bind(t, "Carries", typeof(bool), new[] { typeof(EntityAlive), typeof(ItemValue) }, missing);
            if (missing.Count > 0) { why = "missing or changed: " + string.Join(", ", missing.ToArray()); return false; }

            // optional: the gun's magazine kinds (for the issue of empty magazines) and the tier table's counts
            var optional = new List<string>();
            Type reg = AccessTools.TypeByName(RegistryTypeName);
            allForGun = reg != null ? Bind(reg, "AllForGun", null, new[] { typeof(string) }, optional) : null;
            Type def = allForGun != null && typeof(IList).IsAssignableFrom(allForGun.ReturnType) && allForGun.ReturnType.IsGenericType
                ? allForGun.ReturnType.GetGenericArguments()[0] : null;
            if (def == null) allForGun = null;
            defName = def != null ? Field(def, "Name", typeof(string)) : null;
            defTwin = def != null ? Field(def, "Twin", typeof(bool)) : null;
            tierOf = Bind(t, "TierOf", null, new[] { typeof(int) }, optional);
            tierMagazines = tierOf != null ? Field(tierOf.ReturnType, "Magazines", typeof(int)) : null;
            why = "";
            return true;
        }

        /// <summary>A public static method of exactly these parameter types and this return type (null: any), else null and a note.</summary>
        private static MethodInfo Bind(Type t, string name, Type ret, Type[] args, List<string> missing)
        {
            MethodInfo m = AccessTools.Method(t, name, args);
            if (m != null && m.IsStatic && m.IsPublic && (ret == null || m.ReturnType == ret)) return m;
            var sig = new List<string>();
            foreach (Type a in args) sig.Add(a.Name);
            missing.Add((ret != null ? ret.Name + " " : "") + name + "(" + string.Join(", ", sig.ToArray()) + ")");
            return null;
        }

        private static FieldInfo Field(Type t, string name, Type type)
        {
            FieldInfo f = AccessTools.Field(t, name);
            return f != null && !f.IsStatic && f.FieldType == type ? f : null;
        }

        /// <summary>
        /// Calls one of the bound methods. A throw turns the adapter off for the rest of the game (one error line with the
        /// stack) and returns null, so the caller falls back to loose rounds.
        /// </summary>
        private static object Call(MethodInfo m, params object[] args)
        {
            if (!Active || m == null) return null;
            try
            {
                return m.Invoke(null, args);
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                active = false;
                threw = true;
                state = "off - NpcMagazines." + m.Name + " threw " + inner.GetType().Name + ": " + inner.Message;
                ScLog.Err("NPC magazines: Locked & Loaded's NpcMagazines." + m.Name + " threw - SC's NPCs fall back to loose rounds for the rest of this game");
                ScLog.Once("mags-" + m.Name, inner);
                return null;
            }
        }

        // ------------------------------------------------------------------ the questions ----

        /// <summary>
        /// Does this NPC reload this gun by magazine: one seated in it, or one for it in its bag or toolbelt (L&amp;L's own
        /// test)? gun: the held ItemValue when it holds that gun (GunValue), so a seated magazine counts.
        /// </summary>
        public static bool Carries(EntityAlive e, ItemValue gun)
        {
            if (e == null || gun == null || gun.IsEmpty()) return false;
            object r = Call(carries, e, gun);
            return r is bool && (bool)r;
        }

        /// <summary>
        /// The gun as Carries wants it: the one in the hand when the NPC holds it (its seated magazine counts), else a bare
        /// ItemValue of its type - no quality roll or mod slots, which Carries does not read and the ticker would pay for
        /// on every member every 5 s.
        /// </summary>
        public static ItemValue GunValue(EntityAlive e, ItemClass gun)
        {
            if (gun == null) return null;
            ItemValue held = e != null && e.inventory != null ? e.inventory.holdingItemItemValue : null;
            return held != null && !held.IsEmpty() && held.type == gun.Id ? held : new ItemValue { type = gun.Id };
        }

        /// <summary>Rounds in its magazines for the gun plus the gun's own count while it holds it (NpcMagazines.Rounds); false when it could not be asked.</summary>
        public static bool TryRounds(EntityAlive e, string gun, out int n)
        {
            n = 0;
            object r = Call(rounds, e, gun);
            if (!(r is int)) return false;
            n = (int)r;
            return true;
        }

        /// <summary>
        /// Gives the NPC its tier's loaded magazines for the gun (basic ammo) into its bag: the rounds handed out, 0 when the
        /// gun takes none (a bow, a gun L&amp;L has no magazines for, one with only twin magazines) or L&amp;L is off.
        /// </summary>
        public static int Arm(EntityAlive e, string gun, int tier)
        {
            object r = Call(arm, e, gun, tier, null);
            return r is int ? (int)r : 0;
        }

        /// <summary>Loads up to n rounds of the ammo into its magazines for the gun: the rounds that went in (0 when they are full or it has none).</summary>
        public static int Refill(EntityAlive e, string gun, string ammo, int n)
        {
            if (n <= 0) return 0;
            object r = Call(refill, e, gun, ammo, n);
            return r is int ? (int)r : 0;
        }

        /// <summary>Every magazine it has for the gun, the one in the gun included, as L&amp;L's text ("" for none); null when it could not be asked.</summary>
        public static string Record(EntityAlive e, string gun)
        {
            object r = Call(record, e, gun);
            return r as string;
        }

        /// <summary>A Record string back as magazine items in the bag: the rounds restored, -1 when it could not be asked.</summary>
        public static int Restore(EntityAlive e, string rec)
        {
            object r = Call(restore, e, rec);
            return r is int ? (int)r : -1;
        }

        /// <summary>
        /// Does the gun take a magazine L&amp;L would hand an NPC (a non-twin one)? Only known with the optional registry
        /// binding; false without it.
        /// </summary>
        public static bool TakesMagazines(string gun)
        {
            return StandardMagazine(gun) != null;
        }

        /// <summary>The gun's standard magazine as Arm picks it - its first magazine that is not a twin - or null.</summary>
        public static string StandardMagazine(string gun)
        {
            if (allForGun == null || defName == null || defTwin == null || string.IsNullOrEmpty(gun)) return null;
            IList defs = Call(allForGun, gun) as IList;
            if (defs == null) return null;
            foreach (object d in defs)
            {
                if (d == null || (bool)defTwin.GetValue(d)) continue;
                string name = defName.GetValue(d) as string;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            return null;
        }

        /// <summary>
        /// The item names of every magazine the gun takes, twins included (ScAmmo moves these from a body's window into its
        /// bag, where L&amp;L looks for them); null without the optional registry binding or when L&amp;L is off.
        /// </summary>
        public static HashSet<string> MagazineNames(string gun)
        {
            if (allForGun == null || defName == null || string.IsNullOrEmpty(gun)) return null;
            IList defs = Call(allForGun, gun) as IList;
            if (defs == null) return null;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object d in defs)
            {
                string name = d != null ? defName.GetValue(d) as string : null;
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            }
            return names;
        }

        /// <summary>How many magazines L&amp;L's table gives this tier (&lt;npcs&gt; in its magazines.xml); its defaults 2/3/4 without the binding.</summary>
        public static int TierMagazines(int tier)
        {
            if (tierOf != null && tierMagazines != null)
            {
                object t = Call(tierOf, tier);
                if (t != null) return Math.Max(1, (int)tierMagazines.GetValue(t));
            }
            return Math.Max(2, Math.Min(4, tier + 1));
        }

        /// <summary>The L&amp;L tier of a managed body: 1..3 from its class name's T1..T3 (ScXnpc.TierOf; a class without one is tier 3).</summary>
        public static int TierOf(EntityAlive e)
        {
            return ScXnpc.TierOf(ScCommunity.ClassName(e)) + 1;
        }

        // ------------------------------------------------------------------ L&L's record text ----

        /// <summary>One magazine of a Record string: its item, ammo ("" when empty) and rounds, a twin's other compartment included.</summary>
        public struct Entry
        {
            public string Magazine;
            public string Ammo;
            public int Rounds;
            public string Text;
        }

        /// <summary>
        /// A Record string's magazines ("magName:ammo:rounds[:other];..."), parsed the way NpcMagazines.Restore reads it -
        /// for counting, and for the loose-round fallback when a magazine cannot be restored.
        /// </summary>
        public static List<Entry> Entries(string rec)
        {
            var list = new List<Entry>();
            if (string.IsNullOrEmpty(rec)) return list;
            foreach (string part in rec.Split(';'))
            {
                string[] f = part.Split(':');
                int n, other = 0;
                if (f.Length < 3 || f[0].Length == 0 || !int.TryParse(f[2], out n)) continue;
                if (f.Length > 3) int.TryParse(f[3], out other);
                list.Add(new Entry { Magazine = f[0], Ammo = f[1], Rounds = Math.Max(0, n) + Math.Max(0, other), Text = part });
            }
            return list;
        }

        /// <summary>A Record string's rounds.</summary>
        public static int RoundsIn(string rec)
        {
            int n = 0;
            foreach (Entry x in Entries(rec)) n += x.Rounds;
            return n;
        }

        /// <summary>
        /// "3 magazine(s) holding 45" for the console (L&amp;L's count, the one in the gun included), or "" when the NPC does
        /// not reload this gun by magazine or the adapter is off.
        /// </summary>
        public static string Describe(EntityAlive e, ItemClass gun)
        {
            if (!Active || e == null || gun == null || !Carries(e, GunValue(e, gun))) return "";
            string name = gun.GetItemName();
            string rec = Record(e, name);
            int n;
            if (rec == null || !TryRounds(e, name, out n)) return "";
            var sb = new StringBuilder();
            sb.Append(Entries(rec).Count).Append(" magazine(s) holding ").Append(n);
            return sb.ToString();
        }
    }
}
