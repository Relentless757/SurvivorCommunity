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
    /// ScTypes.Find and AccessTools.Method with the exact parameter types; each method must be public, static and
    /// return what the hand-off says. Required: Arm, Refill, Rounds, Record, Restore (the API) and Carries (L&amp;L's own "this
    /// NPC uses magazines for this gun", the same test its reload asks). Optional: MagazineRegistry.AllForGun with
    /// MagazineDef.Name / Twin, and NpcMagazines.TierOf with Tier.Magazines - only the issue of empty magazines to a member
    /// who has none and the move of handed-over magazines from the window to the bag (ScAmmo) need them, and both stay
    /// off without them. Round lr1 (optional, all or nothing): MagazineRegistry.ForMagazine, MagazineDef.Capacity and
    /// Magazines.SeatedDefOf / GunOtherRounds / SetGunOtherRounds / SetMagazineSeated / SetSeatedMagazine / OtherRoundsIn -
    /// the seat on the draw (ReseatOnDraw, X25), off with a warning without them. Bound once per game (Reset on GameStarting),
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
        private const string MagazinesTypeName = "REInvMags.Magazines";
        /// <summary>L&amp;L's metadata key for a seated magazine (Magazines.SeatedKey; part of its save data, so stable).</summary>
        internal const string SeatedKey = "reinvMagSeated";

        private static bool resolved, active, threw;
        private static string state = "not checked yet";
        private static MethodInfo arm, refill, rounds, record, restore, carries, allForGun, tierOf, seated, returnSeated;
        private static FieldInfo defName, defTwin, tierMagazines;
        /// <summary>Round lr1 (optional): L&amp;L's public Magazines / MagazineRegistry methods the seat on the draw is made of.</summary>
        private static MethodInfo forMagazine, seatedDefOf, gunOther, setGunOther, setSeated, setSeatedName, otherIn;
        private static FieldInfo defCapacity;
        /// <summary>Round lr1: why the seat on the draw is off ("" while it is on); set when it threw this game.</summary>
        private static string reseatWhy = "not checked yet";

        public static void Reset()
        {
            resolved = false;
            active = false;
            threw = false;
            state = "not checked yet";
            arm = refill = rounds = record = restore = carries = allForGun = tierOf = seated = returnSeated = null;
            defName = defTwin = tierMagazines = null;
            forMagazine = seatedDefOf = gunOther = setGunOther = setSeated = setSeatedName = otherIn = null;
            defCapacity = null;
            reseatWhy = "not checked yet";
            putAwayNotes.Clear();
            seating = false;
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
                if (seated == null)
                    ScLog.Warn("mags: Locked & Loaded's " + MagazinesTypeName + ".HasMagazineSeated(ItemValue) was not found - "
                               + "the magazine duplication fix (X18) is OFF for a loaded gun: 0-XNPCCore keeps banking its rounds "
                               + "on a weapon swap, as it did before round n07. Check the Locked & Loaded version.");
                if (reseatWhy.Length > 0)
                    ScLog.Warn("mags: the seat on the draw (round lr1) is OFF - " + reseatWhy + ": a gun put away with a loaded magazine "
                               + "comes out empty again and reloads on every draw. Check the Locked & Loaded version.");
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
            Type t = ScTypes.Find(ApiTypeName);
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
            Type reg = ScTypes.Find(RegistryTypeName);
            allForGun = reg != null ? Bind(reg, "AllForGun", null, new[] { typeof(string) }, optional) : null;
            Type def = allForGun != null && typeof(IList).IsAssignableFrom(allForGun.ReturnType) && allForGun.ReturnType.IsGenericType
                ? allForGun.ReturnType.GetGenericArguments()[0] : null;
            if (def == null) allForGun = null;
            defName = def != null ? Field(def, "Name", typeof(string)) : null;
            defTwin = def != null ? Field(def, "Twin", typeof(bool)) : null;
            tierOf = Bind(t, "TierOf", null, new[] { typeof(int) }, optional);
            // X18 needs to tell a gun Locked & Loaded will bank on a swap (its seated flag set) from one whose rounds are
            // a bare Meta it will NOT bank. Carries cannot: it is also true when she merely has a magazine in her bag.
            Type mag = ScTypes.Find(MagazinesTypeName);
            seated = mag != null ? Bind(mag, "HasMagazineSeated", typeof(bool), new[] { typeof(ItemValue) }, optional) : null;
            tierMagazines = tierOf != null ? Field(tierOf.ReturnType, "Magazines", typeof(int)) : null;
            // round n12 (optional): L&L's own "the magazine in this gun back into the NPC's bag, the gun left empty" - INTERNAL
            // in L&L, so bound by name without the public test. Without it the member inventory panel refuses a gun with a
            // seated magazine ("take the magazine out first") rather than lose the magazine item.
            MethodInfo rs = AccessTools.Method(t, "ReturnSeated", new[] { typeof(EntityAlive), typeof(ItemValue), typeof(string) });
            returnSeated = rs != null && rs.IsStatic && rs.ReturnType == typeof(bool) ? rs : null;
            // round lr1 (optional, all or nothing): the seat on the draw. L&L's NPC API has no "seat this magazine now" call, so
            // it is made of the public methods its own reload and ReturnSeated use - see ReseatOnDraw
            var seat = new List<string>();
            if (def == null || defName == null) seat.Add("MagazineRegistry.AllForGun / MagazineDef.Name");
            else
            {
                forMagazine = Bind(reg, "ForMagazine", def, new[] { typeof(string) }, seat);
                defCapacity = Field(def, "Capacity", typeof(int));
                if (defCapacity == null) seat.Add("MagazineDef.Capacity");
                if (mag == null) seat.Add(MagazinesTypeName);
                else
                {
                    seatedDefOf = Bind(mag, "SeatedDefOf", def, new[] { typeof(ItemValue) }, seat);
                    gunOther = Bind(mag, "GunOtherRounds", typeof(int), new[] { typeof(ItemValue) }, seat);
                    setGunOther = Bind(mag, "SetGunOtherRounds", typeof(void), new[] { typeof(ItemValue), typeof(int) }, seat);
                    setSeated = Bind(mag, "SetMagazineSeated", typeof(void), new[] { typeof(ItemValue), typeof(bool) }, seat);
                    setSeatedName = Bind(mag, "SetSeatedMagazine", typeof(void), new[] { typeof(ItemValue), def }, seat);
                    otherIn = Bind(mag, "OtherRoundsIn", typeof(int), new[] { typeof(ItemStack), def }, seat);
                }
            }
            if (seated == null) seat.Add("Magazines.HasMagazineSeated");
            reseatWhy = seat.Count > 0 ? "missing or changed: " + string.Join(", ", seat.ToArray()) : "";
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
        /// Does this gun carry a magazine Locked &amp; Loaded has actually SEATED? Its weapon-swap patch banks the rounds
        /// only for a seated gun; a bare Meta it leaves alone. Unknown (the method could not be bound) answers false,
        /// which keeps X18 on the safe side - 0-XNPCCore does the banking, as it did before.
        /// </summary>
        public static bool Seated(ItemValue gun)
        {
            if (gun == null || gun.IsEmpty() || seated == null) return false;
            object r = Call(seated, gun);
            return r is bool && (bool)r;
        }

        /// <summary>
        /// Round n12: does this gun carry Locked &amp; Loaded's seated flag, by the flag itself (L&amp;L's Magazines.SeatedKey in
        /// its metadata) - true even with the adapter off or HasMagazineSeated not bound, where Seated answers false. The
        /// member inventory panel asks this so a magazine in a handed gun is never taken for loose rounds and lost.
        /// </summary>
        public static bool SeatedFlag(ItemValue gun)
        {
            if (gun == null || gun.IsEmpty() || gun.Metadata == null) return false;
            if (Seated(gun)) return true;
            int v;
            try { return gun.TryGetMetadata(SeatedKey, out v) && v != 0; }
            catch (Exception) { return false; }
        }

        /// <summary>
        /// Round n12 review: is Locked &amp; Loaded loaded at all? Without it a seated flag left on a gun is only metadata - no
        /// magazine item exists to lose - so the gun's Meta is plain loose rounds and the flag is dropped.
        /// </summary>
        public static bool Loaded { get { return ModManager.ModLoaded(ModName); } }

        /// <summary>Round n12: can a seated magazine be put back into an NPC's bag (L&amp;L's NpcMagazines.ReturnSeated bound, the adapter on)?</summary>
        public static bool CanEject { get { return Active && returnSeated != null; } }

        /// <summary>
        /// Round n12: L&amp;L's NpcMagazines.ReturnSeated - the magazine seated in this gun goes into the NPC's bag as a real
        /// magazine item with its rounds (its toolbelt, else dropped at its feet, when the bag is full: never lost) and the gun is
        /// left empty, in place. False when the gun had no seated magazine, or it could not be asked.
        /// </summary>
        public static bool Eject(EntityAlive e, ItemValue gun, string why)
        {
            if (returnSeated == null || e == null || gun == null) return false;
            object r = Call(returnSeated, e, gun, why);
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

        // ------------------------------------------------------------------ the magazine put away, seated again on the draw (round lr1) ----
        //
        // The user (2026-11-15): "members have to reload their gun every time they bring it out. It killed a member called
        // Cara." Every swap that writes over a gun with a seated magazine - SCore's UpdateWeapon (0-XNPCCore's equips, ScArms'
        // GunFirst / MeleeFirst / R2 / overwhelmed swaps, ScLoadout.Arm), ScMedic's hand - goes through Inventory.SetStackAt,
        // where Locked & Loaded's NpcGunPatches gives the magazine back to the bag (NpcMagazines.ReturnSeated) and empties the
        // gun; with X18 0-XNPCCore banks nothing for it, and every draw builds the gun fresh from its name. So the gun came out
        // empty and 0-XNPCCore's preload asked for a full reload on every draw - several seconds, and with a target within 3 m
        // and no room that is ScArms' R2 "cornered and reloading", a swap to melee that cancels the reload, round after round.
        //
        // X25 (Patches_Xnpc) marks the magazine L&L gave back with the gun's name, and on the draw ReseatOnDraw puts exactly that
        // magazine back in. L&L's NPC API has no "seat this magazine now" call (LnL_NPC_Magazines_API.md: Arm, Refill, Rounds,
        // Record, Restore), so the seat is made of the public Magazines / MagazineRegistry methods its own reload uses.

        /// <summary>Locked &amp; Loaded's Harmony id (its MagazinesModApi): X25 reads the gun before L&amp;L's swap patch empties it.</summary>
        public const string HarmonyId = "lockedandloaded";

        /// <summary>
        /// Round lr1: the mark on a magazine Locked &amp; Loaded gave back when its gun was put away - the gun's item name, as
        /// ItemValue metadata. It travels with the magazine (moved within her stores, topped up by ScAmmo's Refill, saved with
        /// the bag) until that gun is drawn again and ReseatOnDraw puts the magazine back in. L&amp;L's magazines are
        /// Stacknumber 1, so the mark never splits or blocks a stack; a magazine L&amp;L's own reload seats stops being an
        /// item and comes back from GiveMagazine as a new, unmarked one.
        /// </summary>
        public const string PutAwayKey = "scMagPutAwayFrom";

        /// <summary>
        /// Round lr1 (X25's prefix): a gun with a seated magazine about to be written over, read before Locked &amp; Loaded's
        /// swap patch gives the magazine back and empties this very ItemValue.
        /// </summary>
        public sealed class PutAway
        {
            public ItemValue Gun;
            public string GunName;
            public object Def;
            public string Magazine;
            public int Rounds, Other, Ammo;
        }

        /// <summary>
        /// Round lr1: (entity, gun type) -&gt; the rounds of the magazine marked when the gun was put away this session - what
        /// tells "the magazine is missing" from "nothing was put away" on the draw. Main thread; cleared per game.
        /// </summary>
        private static readonly Dictionary<long, int> putAwayNotes = new Dictionary<long, int>();

        /// <summary>Round lr1: SC's own slot writes while it seats a magazine (X25 stands aside). Main thread.</summary>
        private static bool seating;

        internal static bool Seating { get { return seating; } }

        private static long NoteKey(int entityId, int gunType) { return ((long)entityId << 32) | (uint)gunType; }

        /// <summary>Round lr1: can a gun drawn empty get its put-away magazine back (the adapter on, the seat bound, nothing thrown)?</summary>
        public static bool CanReseat { get { return Active && reseatWhy.Length == 0; } }

        /// <summary>Round lr1, for sc xnpc: "on", or why the seat on the draw is off.</summary>
        public static string ReseatState
        {
            get
            {
                if (!Active) return "off (the magazine adapter is " + State + ")";
                return reseatWhy.Length == 0 ? "on" : "OFF - " + reseatWhy;
            }
        }

        /// <summary>
        /// Round lr1 (X25's prefix, ahead of Locked &amp; Loaded's swap patch): the seated magazine of a gun about to be written
        /// over - its kind, rounds, a twin's waiting side and ammo - or null when it has none. Only a SEATED gun's magazine is
        /// given back by L&amp;L; bare rounds are 0-XNPCCore's or X12's (the n07 rule), and nothing here touches them.
        /// </summary>
        internal static PutAway Snapshot(ItemValue gun)
        {
            if (seating || gun == null || gun.IsEmpty() || gun.Metadata == null || gun.ItemClass == null || !CanReseat) return null;
            if (!SeatedForSeat(gun)) return null;
            object def = CallSeat(seatedDefOf, gun);
            string name = def != null ? defName.GetValue(def) as string : null;
            if (string.IsNullOrEmpty(name)) return null;
            object other = CallSeat(gunOther, gun);
            if (!CanReseat) return null;          // a call above threw: the seat is off
            return new PutAway
            {
                Gun = gun,
                GunName = gun.ItemClass.GetItemName(),
                Def = def,
                Magazine = name,
                Rounds = Math.Max(0, gun.Meta),
                Other = other is int ? Math.Max(0, (int)other) : 0,
                Ammo = gun.SelectedAmmoTypeIndex,
            };
        }

        /// <summary>
        /// Round lr1 (X25's postfix): did Locked &amp; Loaded give the snapshot's magazine back? Its ReturnSeated empties the gun
        /// it puts away in place (Meta 0, no seated flag); a gun it kept - the same gun written back loaded - still has both.
        /// Then that magazine, found where L&amp;L's GiveMagazine puts it (her bag, else her toolbelt), is marked with the gun's
        /// name - and only that one: an older mark for the same gun comes off first. An empty magazine is not marked (the draw
        /// reloads, as it should).
        /// </summary>
        internal static void AfterPutAway(EntityAlive e, PutAway p)
        {
            if (e == null || p == null || p.Gun == null || !CanReseat) return;
            if (p.Gun.Meta > 0 || SeatedForSeat(p.Gun) || !CanReseat) return;
            long key = NoteKey(e.entityId, p.Gun.type);
            putAwayNotes.Remove(key);
            ClearMarks(e, p.GunName);
            if (p.Rounds <= 0) { Patches_Xnpc.Count("mag-putaway-empty"); return; }
            bool inBag;
            int at;
            ItemStack[] slots = FindReturned(e, p, out at, out inBag);
            if (!CanReseat) return;               // a call in the search threw: the seat is off
            if (slots == null)
            {
                Patches_Xnpc.Count("mag-putaway-untracked");
                ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + ") put the " + p.GunName + " away: Locked & Loaded gave back its "
                              + p.Magazine + " holding " + p.Rounds + ", but it is in neither her bag nor her toolbelt (dropped at her feet?) - "
                              + "the draw reloads (round lr1)");
                return;
            }
            Mark(slots[at], p.GunName);
            putAwayNotes[key] = p.Rounds;
            Patches_Xnpc.Count("mag-putaway-marked");
        }

        /// <summary>
        /// Round lr1: the gun in her hand slot was just drawn EMPTY - no rounds, no seated magazine - and the magazine it was put
        /// away with is marked in her stores: that magazine goes back in, seated, now. 0-XNPCCore's preload asks for a reload
        /// only for a gun with Meta 0, so there is no reload animation - and no "cornered and reloading" (ScArms' R2) either.
        ///
        /// The magazine is TAKEN out of her stores and its rounds go into the gun with Locked &amp; Loaded's own bookkeeping (the
        /// seated flag, which magazine, a twin's waiting side), as L&amp;L's reload writes them: the mirror image of its
        /// ReturnSeated. One magazine, one owner - the gun is flagged seated, so X18 keeps 0-XNPCCore from banking its rounds
        /// and L&amp;L gives the magazine back again on the next swap - and no round made or lost.
        ///
        /// Stands aside, and counts why, for a gun that came out holding rounds or a magazine already (0-XNPCCore's table load,
        /// a saved gun, the same gun written back), while 0-XNPCCore still banks rounds for that gun (its magazine table: the
        /// stale-table rule of n07/n08 - those rounds are 0-XNPCCore's to load), and while the gun saved with this body is about
        /// to be put back (X13, ScAmmo.RestoreHeldGun: a magazine seated here would turn the saved one into loose rounds).
        /// route: who drew it, for the log (null: a swap outside 0-XNPCCore's equip). True when it seated one. Host, main thread.
        /// </summary>
        internal static bool ReseatOnDraw(EntityAlive e, string route)
        {
            if (seating || e == null || e.inventory == null || e.IsDead() || !CanReseat) return false;
            int idx = e.inventory.holdingItemIdx;
            if (idx < 0 || idx >= e.inventory.Length) return false;
            ItemStack held = e.inventory.GetItem(idx);
            ItemValue gun = held != null ? held.itemValue : null;
            if (gun == null || gun.IsEmpty() || gun.ItemClass == null || ScAmmo.RangedAction(gun.ItemClass) == null) return false;
            long key = NoteKey(e.entityId, gun.type);
            int noted;
            bool expected = putAwayNotes.TryGetValue(key, out noted);
            if (gun.Meta > 0 || SeatedForSeat(gun) || IntOf(CallSeat(gunOther, gun)) > 0)
            {
                if (expected) { putAwayNotes.Remove(key); Patches_Xnpc.Count("mag-reseat-skip-loaded"); }
                return false;
            }
            if (!CanReseat) return false;         // a call above threw: the seat is off
            string gunName = gun.ItemClass.GetItemName();
            bool inBag;
            int at;
            ItemStack[] slots = FindMarked(e, gunName, out at, out inBag);
            if (slots == null)
            {
                if (!expected) return false;
                putAwayNotes.Remove(key);
                Patches_Xnpc.Count("mag-reseat-missing");
                ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + ") draws the " + gunName + " empty: the magazine she put it away with ("
                              + noted + " round(s)) is in neither her bag nor her toolbelt any more - Locked & Loaded's reload picks one (round lr1)");
                return false;
            }
            putAwayNotes.Remove(key);
            if (Patches_Xnpc.HasMagTable(e, gun.type)) { Patches_Xnpc.Count("mag-reseat-skip-table"); return false; }
            ItemValue savedGun;
            string savedUse;
            if (ScAmmo.SavedHand(e.entityId, out savedGun, out savedUse) && savedGun != null) { Patches_Xnpc.Count("mag-reseat-skip-saved"); return false; }
            ItemStack mag = slots[at];
            // round U1: the ammo type the player chose for the gun (ScAmmoChoice) - a put-away magazine of another type stays in
            // her stores while she has rounds of the chosen one: the draw reloads, and L&L's reload looks for that ammo first
            if (!ScAmmoChoice.ReseatAllowed(e, gun.ItemClass, mag.itemValue.SelectedAmmoTypeIndex))
            {
                Unmark(mag);
                Patches_Xnpc.Count("mag-reseat-skip-ammo");
                ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + ") draws the " + gunName + " empty: the magazine she put it away with"
                              + " holds another ammo type than the one chosen for it - Locked & Loaded's reload loads the chosen one (round U1)");
                return false;
            }
            string magName = mag.itemValue.ItemClass != null ? mag.itemValue.ItemClass.GetItemName() : null;
            object def = magName != null ? CallSeat(forMagazine, magName) : null;
            bool takes = def != null && GunTakes(gunName, def);
            if (!CanReseat) return false;         // a call above threw: the seat is off, the mark stays
            if (!takes)
            {
                Unmark(mag);
                Patches_Xnpc.Count("mag-reseat-not-its-gun");
                return false;
            }
            int rounds = Math.Max(0, mag.itemValue.Meta);
            if (rounds <= 0)
            {
                Unmark(mag);                      // emptied since (unloaded by hand): the draw reloads, as for any empty magazine
                Patches_Xnpc.Count("mag-reseat-empty");
                return false;
            }
            int other = IntOf(CallSeat(otherIn, mag, def));
            if (!CanReseat) return false;         // a call above threw: the seat is off
            return Seat(e, gun, slots, at, inBag, def, magName, rounds, other, route);
        }

        /// <summary>
        /// Round lr1: moves the magazine in slots[at] into the gun - out of the store first (3.4: emptying a slot clears its own
        /// ItemValue in place, so a copy is taken before), and only once the slot no longer holds it: a twin's waiting side,
        /// which magazine and the seated flag (the silent metadata), then the ammo and the rounds last, then one toolbelt change
        /// event for the metadata (as L&amp;L's reload ends). A slot the write did not empty leaves everything as it was
        /// (mag-reseat-not-taken). A throw puts the gun back to empty and unflagged and - when its slot no longer holds it (the
        /// slot decides, not how far the try got: a change listener can throw after the slot was cleared) - the magazine back
        /// into her stores: never both, never neither. Then the seat is off for the rest of the game (one error line); the rest
        /// of the adapter stays on.
        /// </summary>
        private static bool Seat(EntityAlive e, ItemValue gun, ItemStack[] slots, int at, bool inBag, object def, string magName, int rounds, int other, string route)
        {
            ItemStack copy = slots[at].Clone();
            byte ammo = copy.itemValue.SelectedAmmoTypeIndex;
            string gunName = gun.ItemClass.GetItemName();
            bool taken = false;
            seating = true;
            try
            {
                if (inBag) e.bag.SetSlot(at, ItemStack.Empty);
                else e.inventory.SetStackAt(at, ItemStack.Empty);
                // the Fable review (F1): the slot itself says whether the magazine left - Bag.SetSlot ignores an index past its
                // slot count, so a write that did nothing must never go on to fill the gun as well (a duplicate)
                taken = !StillHolds(slots, at, magName, gunName);
                if (taken)
                {
                    // the Fable review (F5): the silent metadata first, the rounds last - a gun stopped half way holds no rounds
                    InvokeSeat(setGunOther, gun, other);
                    InvokeSeat(setSeatedName, gun, def);
                    InvokeSeat(setSeated, gun, true);
                    gun.SelectedAmmoTypeIndex = ammo;
                    gun.Meta = rounds;
                    e.inventory.CallOnToolbeltChangedInternal();
                }
            }
            catch (Exception ex)
            {
                // The Fable review (F5): the gun back to empty and unflagged, the metadata first, each step on its own (one that
                // throws does not keep the rest from running), by L&L's own keys so none depends on the call that threw
                try { gun.RemoveMetaData(SeatedKey); } catch (Exception) { }
                try { gun.RemoveMetaData(SeatedNameKey); } catch (Exception) { }
                try { gun.RemoveMetaData(GunOtherKey); } catch (Exception) { }
                try { if (gun.Metadata != null && gun.Metadata.Count == 0) gun.Metadata = null; } catch (Exception) { }
                try { gun.Meta = 0; } catch (Exception) { }      // the setter writes the field before it notifies: 0 even if a listener throws
                // The Fable review (F1): decided from the slot, not from how far the try got. The store's change event fires
                // AFTER the slot is cleared, so a listener that throws there (a bag UI, another mod) leaves the slot empty with
                // the magazine in no one's hands: it must still go back. A slot that still holds it was never taken.
                taken = !StillHolds(slots, at, magName, gunName);
                if (taken)
                {
                    try { copy.itemValue.RemoveMetaData(PutAwayKey); } catch (Exception) { }
                    GiveBack(e, copy);
                }
                reseatWhy = "seating a magazine threw " + ex.GetType().Name + ": " + ex.Message;
                Patches_Xnpc.Count("mag-reseat-failed");
                ScLog.Err("mags: seating " + ScCommunity.DisplayName(e) + "'s " + magName + " in her " + gun.ItemClass.GetItemName() + " threw - "
                          + (taken ? "the magazine went back to her bag (or toolbelt, or her feet)" : "the magazine never left her stores")
                          + "; the seat on the draw is off for the rest of this game");
                ScLog.Once("mags-reseat", ex);
                return false;
            }
            finally
            {
                seating = false;
            }
            if (!taken)
            {
                // the write did nothing (an index past the store's slot count): the magazine stays where it is, marked, and the
                // gun stays empty - it reloads as before this round. Not a fault of the seat; counted, logged once per body
                Patches_Xnpc.Count("mag-reseat-not-taken");
                ScLog.Once("mags-reseat-not-taken-" + e.entityId, "mags: " + ScCommunity.DisplayName(e) + " (id " + e.entityId + ")'s " + magName + " in "
                           + (inBag ? "bag" : "toolbelt") + " slot " + at + " could not be taken out - the " + gunName + " is left empty and reloads (round lr1; logged once)");
                return false;
            }
            Patches_Xnpc.Count("mag-reseat-on-draw");
            ScLog.Verbose(ScCommunity.DisplayName(e) + " (id " + e.entityId + ") draws the " + gun.ItemClass.GetItemName() + " with the " + magName
                          + " she put it away with, " + rounds + (other > 0 ? " (+" + other + ")" : "") + " round(s), seated - no reload (round lr1; "
                          + (route ?? "a swap") + ")");
            return true;
        }

        /// <summary>Round lr1: a bound seat method; its own exception is thrown on, so Seat can put everything back.</summary>
        private static object InvokeSeat(MethodInfo m, params object[] args)
        {
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
        }

        /// <summary>
        /// Round lr1 (the Fable review, F3): every L&amp;L call the seat on the draw makes outside Seat itself. Unlike Call, a
        /// throw turns only the SEAT off (reseatWhy, one error line) and returns null: the adapter stays on, so X18 keeps
        /// 0-XNPCCore from banking a magazine gun's rounds (the n07 duplication stays closed) and every other caller is
        /// unaffected. Callers test CanReseat after it.
        /// </summary>
        private static object CallSeat(MethodInfo m, params object[] args)
        {
            if (m == null || !CanReseat) return null;
            try
            {
                return m.Invoke(null, args);
            }
            catch (Exception e)
            {
                Exception inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                reseatWhy = "Locked & Loaded's " + m.DeclaringType.Name + "." + m.Name + " threw " + inner.GetType().Name + ": " + inner.Message;
                Patches_Xnpc.Count("mag-reseat-off");
                ScLog.Err("mags: Locked & Loaded's " + m.DeclaringType.Name + "." + m.Name + " threw - the seat on the draw (round lr1) is off for the rest "
                          + "of this game (the rest of the magazine adapter, X18 included, stays on)");
                ScLog.Once("mags-seat-" + m.Name, inner);
                return null;
            }
        }

        /// <summary>Round lr1: L&amp;L's seated flag through CallSeat (false when it threw: the seat is off then).</summary>
        private static bool SeatedForSeat(ItemValue gun)
        {
            if (gun == null || gun.IsEmpty() || seated == null) return false;
            object r = CallSeat(seated, gun);
            return r is bool && (bool)r;
        }

        /// <summary>
        /// Round lr1: L&amp;L's metadata keys for which magazine is seated and a seated twin's waiting side (Magazines.SeatedNameKey,
        /// GunOtherKey - part of its save data, so stable like SeatedKey; checked against the installed 3.4 DLL). Seat's rollback
        /// removes them directly, so putting a gun back to empty never depends on the L&amp;L call that may just have thrown.
        /// </summary>
        private const string SeatedNameKey = "REMagsSeatedName";
        private const string GunOtherKey = "REMagsSeatedOther";

        /// <summary>Round lr1: a magazine Seat could not seat, back where L&amp;L's GiveMagazine would put it: her bag, her toolbelt, her feet.</summary>
        private static void GiveBack(EntityAlive e, ItemStack mag)
        {
            try
            {
                if (e.bag != null && e.bag.AddItem(mag)) return;
                if (e.inventory != null && e.inventory.AddItem(mag)) return;
                GameManager.Instance.ItemDropServer(mag, e.GetPosition(), UnityEngine.Vector3.zero, -1, 60f);
            }
            catch (Exception ex) { ScLog.Once("mags-reseat-giveback", ex); }
        }

        private static int IntOf(object o) { return o is int ? Math.Max(0, (int)o) : 0; }

        /// <summary>Round lr1: does the gun take this magazine kind (MagazineRegistry.AllForGun)?</summary>
        private static bool GunTakes(string gunName, object def)
        {
            IList defs = CallSeat(allForGun, gunName) as IList;
            return defs != null && defs.Contains(def);
        }

        /// <summary>Round lr1: the store slots SC searches, as L&amp;L's GiveMagazine fills them - her bag, then her toolbelt (3.4 grids).</summary>
        private static ItemStack[] Store(EntityAlive e, bool bag)
        {
            if (bag) return e.bag != null && e.bag.ItemGrid != null ? e.bag.ItemGrid.items : null;
            return e.inventory != null && e.inventory.ItemGrid != null ? e.inventory.ItemGrid.items : null;
        }

        private static bool IsItem(ItemStack s, string name)
        {
            return s != null && !s.IsEmpty() && s.itemValue != null && s.itemValue.ItemClass != null
                   && string.Equals(s.itemValue.ItemClass.GetItemName(), name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Round lr1: the gun name a magazine is marked with (PutAwayKey), or null.</summary>
        private static string MarkOf(ItemValue iv)
        {
            string v;
            return iv != null && iv.Metadata != null && iv.TryGetMetadata(PutAwayKey, out v) && !string.IsNullOrEmpty(v) ? v : null;
        }

        /// <summary>
        /// Round lr1 (the Fable review, F1): does this store slot still hold the magazine Seat is taking - that magazine kind, marked
        /// for this gun? Reads only (no throw): Seat decides from it whether the magazine left, in the try and in the catch.
        /// </summary>
        private static bool StillHolds(ItemStack[] slots, int at, string magName, string gunName)
        {
            if (slots == null || at < 0 || at >= slots.Length) return false;
            ItemStack s = slots[at];
            return IsItem(s, magName) && string.Equals(MarkOf(s.itemValue), gunName, StringComparison.OrdinalIgnoreCase);
        }

        private static void Mark(ItemStack s, string gunName)
        {
            s.itemValue.SetMetadata(PutAwayKey, gunName);
            s.OnValueChanged();                   // 3.4: SetMetadata is silent; the store's change event carries it to its save and sync
        }

        private static void Unmark(ItemStack s)
        {
            if (s == null || s.itemValue == null || !s.itemValue.RemoveMetaData(PutAwayKey)) return;
            if (s.itemValue.Metadata != null && s.itemValue.Metadata.Count == 0) s.itemValue.Metadata = null;   // compares as unmarked again
            s.OnValueChanged();
        }

        /// <summary>Round lr1: takes the mark for this gun off every magazine in her stores (only the newest put-away one carries it).</summary>
        private static void ClearMarks(EntityAlive e, string gunName)
        {
            for (int k = 0; k < 2; k++)
            {
                ItemStack[] slots = Store(e, k == 0);
                if (slots == null) continue;
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i] != null && string.Equals(MarkOf(slots[i].itemValue), gunName, StringComparison.OrdinalIgnoreCase)) Unmark(slots[i]);
            }
        }

        /// <summary>
        /// Round lr1: the magazine L&amp;L's GiveMagazine has just put in her bag (else her toolbelt) for the snapshot: that kind,
        /// unmarked, holding exactly what the gun held (clamped to its capacity as GiveMagazine clamps it) with the same waiting
        /// side, the same ammo first. Identical magazines are interchangeable, so any of them is the one put away.
        /// </summary>
        private static ItemStack[] FindReturned(EntityAlive e, PutAway p, out int at, out bool inBag)
        {
            int cap = defCapacity != null && p.Def != null ? (int)defCapacity.GetValue(p.Def) : 0;
            int rounds = cap > 0 ? Math.Min(p.Rounds, cap) : p.Rounds;
            int other = cap > 0 ? Math.Min(p.Other, cap) : p.Other;
            ItemStack[] anyAmmo = null;
            int anyAt = -1;
            bool anyInBag = false;
            for (int k = 0; k < 2; k++)
            {
                ItemStack[] slots = Store(e, k == 0);
                if (slots == null) continue;
                for (int i = 0; i < slots.Length; i++)
                {
                    ItemStack s = slots[i];
                    if (!IsItem(s, p.Magazine) || MarkOf(s.itemValue) != null || s.itemValue.Meta != rounds) continue;
                    if (IntOf(CallSeat(otherIn, s, p.Def)) != other) continue;
                    if (!CanReseat) { at = -1; inBag = false; return null; }   // the call threw: the seat is off
                    if (s.itemValue.SelectedAmmoTypeIndex == p.Ammo) { at = i; inBag = k == 0; return slots; }
                    if (anyAmmo == null) { anyAmmo = slots; anyAt = i; anyInBag = k == 0; }
                }
            }
            at = anyAt;
            inBag = anyInBag;
            return anyAmmo;
        }

        /// <summary>Round lr1: the magazine in her bag (else her toolbelt) marked as put away from this gun, or null.</summary>
        private static ItemStack[] FindMarked(EntityAlive e, string gunName, out int at, out bool inBag)
        {
            for (int k = 0; k < 2; k++)
            {
                ItemStack[] slots = Store(e, k == 0);
                if (slots == null) continue;
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i] != null && !slots[i].IsEmpty() && string.Equals(MarkOf(slots[i].itemValue), gunName, StringComparison.OrdinalIgnoreCase))
                    {
                        at = i;
                        inBag = k == 0;
                        return slots;
                    }
            }
            at = -1;
            inBag = false;
            return null;
        }

        /// <summary>
        /// Round lr1, for sc xnpc &lt;name&gt;: the magazines in her stores marked as put away ("magIGPSCARHDrum 60 for the
        /// IZYgunT4BattleRifleSCARH (bag)"), or "none".
        /// </summary>
        public static string DescribePutAway(EntityAlive e)
        {
            if (e == null) return "none";
            var parts = new List<string>();
            for (int k = 0; k < 2; k++)
            {
                ItemStack[] slots = Store(e, k == 0);
                if (slots == null) continue;
                for (int i = 0; i < slots.Length; i++)
                {
                    ItemStack s = slots[i];
                    string gun = s != null && !s.IsEmpty() ? MarkOf(s.itemValue) : null;
                    if (gun == null || s.itemValue.ItemClass == null) continue;
                    parts.Add(s.itemValue.ItemClass.GetItemName() + " " + Math.Max(0, s.itemValue.Meta) + " for the " + gun + (k == 0 ? " (bag)" : " (toolbelt)"));
                }
            }
            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "none";
        }
    }
}
