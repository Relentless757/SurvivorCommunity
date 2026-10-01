using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// A survivor's appearance when the NPC uses the player's body (entity classes with ModelType SDCS): the
    /// face/hair archetype plus the four worn armor pieces. Vanilla never saves or network-sends a non-player's
    /// equipment, so the community registry keeps this and the mod re-applies it on every load.
    /// </summary>
    public sealed class Look
    {
        public bool Male = true;
        public string Race = "White";
        public int Variant = 1;
        public string Hair = "";
        public string HairColor = "";
        public string Mustache = "";
        public string Chops = "";
        public string Beard = "";
        public string Eyes = "Blue01";
        /// <summary>Armor item names by equipment slot (Head, Chest, Hands, Feet); "" = nothing worn there.</summary>
        public readonly string[] Gear = { "", "", "", "" };
        public readonly int[] Quality = { 1, 1, 1, 1 };
        /// <summary>Session flag: the base body/head assets were confirmed to exist.</summary>
        public bool Validated;

        public Look Clone()
        {
            var l = new Look { Male = Male, Race = Race, Variant = Variant, Hair = Hair, HairColor = HairColor, Mustache = Mustache, Chops = Chops, Beard = Beard, Eyes = Eyes, Validated = Validated };
            for (int i = 0; i < 4; i++) { l.Gear[i] = Gear[i]; l.Quality[i] = Quality[i]; }
            return l;
        }

        public Archetype ToArchetype()
        {
            return new Archetype("scSurvivor", Male, _canCustomize: true)
            {
                Race = Race,
                Variant = Variant,
                Hair = Hair ?? "",
                HairColor = HairColor ?? "",
                MustacheName = Male ? (Mustache ?? "") : "",
                ChopsName = Male ? (Chops ?? "") : "",
                BeardName = Male ? (Beard ?? "") : "",
                EyeColorName = string.IsNullOrEmpty(Eyes) ? "Blue01" : Eyes
            };
        }

        /// <summary>"M|White|2|buzzcut|06 Black|1||3|Blue05" - no '=', ';' or newlines, so the registry's Clean() leaves it alone.</summary>
        public string Encode()
        {
            return string.Join("|", new[] { Male ? "M" : "F", Race ?? "", Variant.ToString(CultureInfo.InvariantCulture), Hair ?? "", HairColor ?? "", Mustache ?? "", Chops ?? "", Beard ?? "", Eyes ?? "" });
        }

        /// <summary>Fills this look from Encode()'s format; unknown/short input leaves the current values.</summary>
        public bool Decode(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string[] p = s.Split('|');
            if (p.Length < 3) return false;
            Male = p[0].Trim().ToUpperInvariant() != "F";
            Race = p[1].Trim();
            int v; Variant = int.TryParse(p[2].Trim(), out v) ? Math.Max(1, v) : 1;
            if (p.Length > 3) Hair = p[3].Trim();
            if (p.Length > 4) HairColor = p[4].Trim();
            if (p.Length > 5) Mustache = p[5].Trim();
            if (p.Length > 6) Chops = p[6].Trim();
            if (p.Length > 7) Beard = p[7].Trim();
            if (p.Length > 8) Eyes = p[8].Trim();
            if (string.IsNullOrEmpty(Race)) Race = "White";
            Validated = false;
            return true;
        }

        /// <summary>"armorRaiderHelmet:3,armorRaiderOutfit:2,,armorRaiderBoots:4" in slot order.</summary>
        public string EncodeGear()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) sb.Append(',');
                if (!string.IsNullOrEmpty(Gear[i])) sb.Append(Gear[i]).Append(':').Append(Quality[i]);
            }
            return sb.ToString();
        }

        public void DecodeGear(string s)
        {
            for (int i = 0; i < 4; i++) { Gear[i] = ""; Quality[i] = 1; }
            if (string.IsNullOrEmpty(s)) return;
            string[] parts = s.Split(',');
            for (int i = 0; i < 4 && i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length == 0) continue;
                int c = part.IndexOf(':');
                int q = 1;
                if (c > 0) { int.TryParse(part.Substring(c + 1), out q); part = part.Substring(0, c); }
                Gear[i] = part;
                Quality[i] = Math.Max(1, Math.Min(6, q));
            }
        }

        public bool HasGear
        {
            get { for (int i = 0; i < 4; i++) if (!string.IsNullOrEmpty(Gear[i])) return true; return false; }
        }
    }

    /// <summary>
    /// Everything about survivors that wear the player's body: rolling a look, the gear table, staging armor
    /// into the entity's equipment slots, and keeping the entity in step with its registry record.
    ///
    /// The community's survivors are 0-XNPCCore's own (ScXnpc.IsBody; decisions D2 and D11 - SC no longer defines survivor
    /// classes of its own): their model is built from ForEntity's look by Patches_Xnpc (X1), and a rebuild goes through
    /// XNPC's own mesh build (Rebuild). SC's raiders (scRaider*, decision D1) wear the player's body through SC's own model
    /// shim and are dressed from here the same way; the class lists below (RaiderClasses, PickFrom) are theirs.
    /// </summary>
    public static class ScLooks
    {
        private static readonly string[] SlotSuffix = { "Helmet", "Outfit", "Gloves", "Boots" };
        /// <summary>The body armor slot, the one a survivor should never turn up without.</summary>
        private const int ChestSlot = 1;

        /// <summary>Consumed by the next player-body entity that initialises (set right before EntityFactory.CreateEntity).</summary>
        public static Look Pending;
        /// <summary>Looks of survivors that have no registry record (yet), by entity id. Session only.</summary>
        private static readonly Dictionary<int, Look> session = new Dictionary<int, Look>();
        /// <summary>The entity class each session look was rolled for (an id can be reused by a stranger).</summary>
        private static readonly Dictionary<int, string> sessionClass = new Dictionary<int, string>();
        /// <summary>Where ForEntity found each body's look (pending / member / prospect / session / roll), for sc xnpc. Session only.</summary>
        private static readonly Dictionary<int, string> sessionSource = new Dictionary<int, string>();
        private static bool catalogueReady;
        // fallback catalogue (what 3.2 ships) when the game's SDCS data cannot be read
        private static readonly string[] FallbackRaces = { "White", "Black", "Asian", "Native" };
        private static readonly string[] FallbackHairs = { "afro_curly", "buzzcut", "comb_over", "cornrows", "dreads", "flattop_fro", "midpart_karen_messy", "midpart_long", "midpart_mid", "midpart_short", "midpart_shoulder", "mohawk", "pixie_cut", "ponytail", "sidepart_long", "sidepart_mid", "sidepart_short", "slicked_back", "slicked_back_long", "small_fro" };
        private static readonly string[] FallbackHairColors = { "01 Blond", "02 Dark Blond", "03 LightBrown", "04 Brown", "05 DarkBrown", "06 Black", "07 Gray", "08 White", "09 Red", "10 Red 1" };
        private static readonly string[] FallbackEyes = { "Blue01", "Blue03", "Blue05", "Brown01", "Brown02", "Brown05", "Brown07", "Gray02", "Green01", "Green05", "Hazel01", "Hazel03" };
        private static readonly string[] FallbackFacial = { "1", "2", "3", "4", "5" };

        public static void Reset()
        {
            Pending = null;
            session.Clear();
            sessionClass.Clear();
            sessionSource.Clear();
            raiderClasses = null;
            catalogueReady = false;
        }

        /// <summary>Forgets a despawned/purged survivor's session look (entity ids are recycled).</summary>
        public static void Forget(int entityId)
        {
            session.Remove(entityId);
            sessionClass.Remove(entityId);
            sessionSource.Remove(entityId);
        }

        /// <summary>Where the body's look came from when its model was built (pending / member / prospect / session / roll), or "-".</summary>
        public static string SourceOf(int entityId)
        {
            string s;
            return sessionSource.TryGetValue(entityId, out s) ? s : "-";
        }

        // ---- classes ----

        /// <summary>Hostile player-body classes (Config/entityclasses.xml, generated): scRaider&lt;Sex&gt;&lt;Weapon&gt;. The only
        /// player-body classes SurvivorCommunity still defines: its scSurvivor classes are gone (decision D11).</summary>
        public const string RaiderPrefix = "scRaider";

        public static bool IsRaiderClass(string cls)
        {
            return !string.IsNullOrEmpty(cls) && cls.StartsWith(RaiderPrefix, StringComparison.OrdinalIgnoreCase)
                   && cls.IndexOf("Template", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>The weapon token a generated class was built for (property ScWeapon), or "".</summary>
        public static string WeaponOf(string cls)
        {
            EntityClass ec = ClassByName(cls);
            string v;
            return ec != null && ec.Properties != null && ec.Properties.Values.TryGetValue("ScWeapon", out v) ? v : "";
        }

        /// <summary>0 early melee, 1 pipe weapons and bows, 2 late guns (property ScWeaponTier); 0 when absent.</summary>
        public static int TierOf(string cls)
        {
            EntityClass ec = ClassByName(cls);
            string v; int t;
            return ec != null && ec.Properties != null && ec.Properties.Values.TryGetValue("ScWeaponTier", out v) && int.TryParse(v, out t) ? t : 0;
        }

        /// <summary>The highest weapon tier a stranger should carry at this gamestage (mirrors the spawn groups).</summary>
        public static int MaxTierFor(int gamestage)
        {
            return gamestage < 50 ? 0 : (gamestage < 100 ? 1 : 2);
        }

        private static EntityClass ClassByName(string cls)
        {
            if (string.IsNullOrEmpty(cls)) return null;
            EntityClass ec;
            return EntityClass.list.TryGetValue(EntityClass.FromString(cls), out ec) ? ec : null;
        }

        public static bool ClassIsMale(string cls)
        {
            return string.IsNullOrEmpty(cls) || cls.IndexOf("Female", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>
        /// A loaded NPC that wears a look of ours: SC's own player body, or one of the 0-XNPCCore survivor classes the
        /// community owns (never the player, never XNPC's other classes).
        /// </summary>
        public static bool IsSurvivor(Entity e)
        {
            return Patches_Sdcs.IsAnyBodyNpc(e, "looks");
        }

        /// <summary>The hostile player-body classes this mod defines (cached per session).</summary>
        public static List<string> RaiderClasses()
        {
            if (raiderClasses == null) raiderClasses = ScanClasses(IsRaiderClass);
            return raiderClasses;
        }
        private static List<string> raiderClasses;

        private static List<string> ScanClasses(Func<string, bool> match)
        {
            var list = new List<string>();
            try
            {
                foreach (EntityClass ec in EntityClass.list.Dict.Values)
                {
                    if (ec == null || !match(ec.entityClassName)) continue;
                    if (ec.modelType != typeof(EModelSDCS)) continue;
                    list.Add(ec.entityClassName);
                }
                list.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e) { ScLog.Once("looks-classes", e); }
            return list;
        }

        /// <summary>Every weapon token the generated raider classes use, for the console.</summary>
        public static List<string> WeaponTokens()
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string c in RaiderClasses()) { string w = WeaponOf(c); if (w.Length > 0) set.Add(w); }
            return new List<string>(set);
        }

        /// <summary>
        /// A class from a pool: sex "male"/"female" or null, weapon an exact ScWeapon token or null, and no weapon
        /// above maxTier. Falls back to ignoring the tier, then the weapon, rather than returning nothing.
        /// </summary>
        public static string PickFrom(List<string> pool, GameRandom rnd, string sex, string weapon, int maxTier)
        {
            if (pool == null || pool.Count == 0) return null;
            var ok = Filter(pool, sex, weapon, maxTier);
            if (ok.Count == 0) ok = Filter(pool, sex, weapon, int.MaxValue);
            if (ok.Count == 0) ok = Filter(pool, sex, null, int.MaxValue);
            if (ok.Count == 0) ok = pool;
            return ok[rnd != null ? rnd.RandomRange(0, ok.Count) : 0];
        }

        private static List<string> Filter(List<string> pool, string sex, string weapon, int maxTier)
        {
            var ok = new List<string>();
            foreach (string c in pool)
            {
                if (!string.IsNullOrEmpty(sex) && ClassIsMale(c) != (sex == "male")) continue;
                if (!string.IsNullOrEmpty(weapon) && !string.Equals(WeaponOf(c), weapon, StringComparison.OrdinalIgnoreCase)) continue;
                if (TierOf(c) > maxTier) continue;
                ok.Add(c);
            }
            return ok;
        }

        // ---- resolution for the model shim ----

        /// <summary>
        /// The look for an entity that is initialising its player body: the pending spawn look, else the
        /// registry record (reloads), else what this session rolled for it, else a fresh roll.
        /// </summary>
        public static Look ForEntity(Entity e)
        {
            Look look = null;
            string source = "roll";
            try
            {
                string cls = e != null ? ScCommunity.ClassName(e as EntityAlive) : "";
                if (Pending != null) { look = Pending; Pending = null; source = "pending"; }
                if (look == null && e != null)
                {
                    Member m = ScRegistry.GetMember(e.entityId);
                    if (m != null && m.Look != null && (string.IsNullOrEmpty(m.Class) || string.Equals(m.Class, cls, StringComparison.OrdinalIgnoreCase))) { look = m.Look; source = "member"; }
                    if (look == null)
                    {
                        Prospect p = ScRegistry.GetProspect(e.entityId);
                        if (p != null && p.Look != null && (string.IsNullOrEmpty(p.Class) || string.Equals(p.Class, cls, StringComparison.OrdinalIgnoreCase))) { look = p.Look; source = "prospect"; }
                    }
                    if (look == null && session.TryGetValue(e.entityId, out look))
                    {
                        source = "session";
                        string was;
                        if (sessionClass.TryGetValue(e.entityId, out was) && !string.Equals(was, cls, StringComparison.OrdinalIgnoreCase)) look = null;   // recycled id
                    }
                }
                if (look == null)
                {
                    source = "roll";
                    World w = ScCommunity.World;
                    look = Roll(ClassIsMale(cls), w != null ? w.GetGameRandom() : null, true);
                }
                Validate(look);
                if (e != null) { session[e.entityId] = look; sessionClass[e.entityId] = cls; sessionSource[e.entityId] = source; }
            }
            catch (Exception ex)
            {
                ScLog.Once("looks-for-entity", ex);
                if (look == null) look = new Look();
            }
            return look;
        }

        /// <summary>The look this NPC is wearing (null for pack NPCs with their own mesh).</summary>
        public static Look Get(EntityAlive npc)
        {
            if (npc == null) return null;
            Look l;
            if (session.TryGetValue(npc.entityId, out l)) return l;
            Member m = ScRegistry.GetMember(npc.entityId);
            if (m != null && m.Look != null) return m.Look;
            Prospect p = ScRegistry.GetProspect(npc.entityId);
            if (p != null && p.Look != null) return p.Look;
            return null;
        }

        public static void Remember(int entityId, Look look)
        {
            if (look != null) session[entityId] = look;
        }

        // ---- rolling ----

        private static void EnsureCatalogue()
        {
            if (catalogueReady) return;
            try
            {
                if (SDCSDataUtils.GetRaceList(true).Count == 0) SDCSDataUtils.SetupData();   // the character creator loads it lazily
                catalogueReady = SDCSDataUtils.GetRaceList(true).Count > 0;                // else retry next roll (fallback lists meanwhile)
            }
            catch (Exception e) { ScLog.Once("looks-catalogue", e); }
        }

        private static string Pick(GameRandom rnd, List<string> list, string[] fallback)
        {
            if (list == null || list.Count == 0)
            {
                if (fallback == null || fallback.Length == 0) return "";
                return fallback[rnd != null ? rnd.RandomRange(0, fallback.Length) : 0];
            }
            return list[rnd != null ? rnd.RandomRange(0, list.Count) : 0];
        }

        private static float Chance(GameRandom rnd)
        {
            return rnd != null ? rnd.RandomFloat : UnityEngine.Random.value;
        }

        /// <summary>A random face and hair (and a set of clothes when asked) for the given sex.</summary>
        public static Look Roll(bool male, GameRandom rnd, bool withGear)
        {
            var look = new Look { Male = male };
            try
            {
                ScConfig.EnsureParsed();
                EnsureCatalogue();
                List<string> races = null, variants = null, hairs = null, eyes = null, must = null, chops = null, beards = null;
                List<string> colors = null;
                try
                {
                    races = SDCSDataUtils.GetRaceList(male);
                    look.Race = Pick(rnd, races, FallbackRaces);
                    variants = SDCSDataUtils.GetVariantList(male, look.Race);
                    hairs = SDCSDataUtils.GetHairNames(male, SDCSDataUtils.HairTypes.Hair);
                    eyes = SDCSDataUtils.GetEyeColorNames();
                    colors = new List<string>();
                    foreach (var hc in SDCSDataUtils.GetHairColorNames()) if (!string.IsNullOrEmpty(hc.PrefabName)) colors.Add(hc.PrefabName);
                    if (male)
                    {
                        must = SDCSDataUtils.GetHairNames(true, SDCSDataUtils.HairTypes.Mustache);
                        chops = SDCSDataUtils.GetHairNames(true, SDCSDataUtils.HairTypes.Chops);
                        beards = SDCSDataUtils.GetHairNames(true, SDCSDataUtils.HairTypes.Beard);
                    }
                }
                catch (Exception e) { ScLog.Once("looks-sdcsdata", e); }
                if (string.IsNullOrEmpty(look.Race)) look.Race = Pick(rnd, null, FallbackRaces);
                int v;
                string vs = Pick(rnd, variants, new[] { "1", "2", "3", "4" });
                look.Variant = int.TryParse(vs, out v) ? Math.Max(1, v) : 1;
                look.Hair = Chance(rnd) < ScConfig.SurvivorBaldChance ? "" : Pick(rnd, hairs, FallbackHairs);
                look.HairColor = Pick(rnd, colors, FallbackHairColors);
                look.Eyes = Pick(rnd, eyes, FallbackEyes);
                if (male)
                {
                    look.Mustache = Chance(rnd) < ScConfig.SurvivorFacialHairChance ? Pick(rnd, must, FallbackFacial) : "";
                    look.Chops = Chance(rnd) < ScConfig.SurvivorFacialHairChance * 0.5f ? Pick(rnd, chops, FallbackFacial) : "";
                    look.Beard = Chance(rnd) < ScConfig.SurvivorFacialHairChance ? Pick(rnd, beards, FallbackFacial) : "";
                }
                if (withGear) RollGear(look, rnd);
            }
            catch (Exception e)
            {
                ScLog.Once("looks-roll", e);
            }
            return look;
        }

        /// <summary>The item name of one piece of a vanilla armor set ("Raider", slot 0 -> armorRaiderHelmet).</summary>
        public static string SetItem(string set, int slot)
        {
            if (string.IsNullOrEmpty(set) || slot < 0 || slot > 3) return "";
            string name = "armor" + set + SlotSuffix[slot];
            return ItemClass.GetItemClass(name, true) != null ? name : "";
        }

        /// <summary>
        /// Clothes from the config's armor sets: one main set, each piece present with SurvivorGearPieceChance
        /// and swapped for another set's piece with SurvivorGearMixChance (a scavenged look). The body armor is
        /// the exception - at a piece chance of 0.8 one survivor in five turned up bare-chested, which reads as
        /// a missing mesh rather than as scavenging, so that slot ignores the chance (SurvivorGearAlwaysChest).
        /// </summary>
        public static void RollGear(Look look, GameRandom rnd)
        {
            for (int i = 0; i < 4; i++) { look.Gear[i] = ""; look.Quality[i] = 1; }
            var sets = ScConfig.SurvivorGearSets;
            if (sets.Count == 0) return;
            string main = sets[rnd != null ? rnd.RandomRange(0, sets.Count) : 0];
            int qMin = Math.Max(1, Math.Min(ScConfig.SurvivorGearQualityMin, ScConfig.SurvivorGearQualityMax));
            int qMax = Math.Max(qMin, ScConfig.SurvivorGearQualityMax);
            for (int slot = 0; slot < 4; slot++)
            {
                bool must = slot == ChestSlot && ScConfig.SurvivorGearAlwaysChest;
                if (!must && Chance(rnd) >= ScConfig.SurvivorGearPieceChance) continue;
                string set = main;
                if (sets.Count > 1 && Chance(rnd) < ScConfig.SurvivorGearMixChance) set = sets[rnd != null ? rnd.RandomRange(0, sets.Count) : 0];
                string item = SetItem(set, slot);
                if (item.Length == 0) item = SetItem(main, slot);
                // a required piece takes whichever set has one rather than leaving the slot bare
                for (int i = 0; must && item.Length == 0 && i < sets.Count; i++) item = SetItem(sets[i], slot);
                if (item.Length == 0) continue;
                look.Gear[slot] = item;
                look.Quality[slot] = rnd != null ? rnd.RandomRange(qMin, qMax + 1) : qMin;
            }
        }

        /// <summary>Puts a whole set on (or "none" strips everything).</summary>
        public static bool WearSet(Look look, string set, GameRandom rnd)
        {
            if (look == null) return false;
            if (string.Equals(set, "none", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < 4; i++) { look.Gear[i] = ""; look.Quality[i] = 1; }
                return true;
            }
            int qMin = Math.Max(1, Math.Min(ScConfig.SurvivorGearQualityMin, ScConfig.SurvivorGearQualityMax));
            int qMax = Math.Max(qMin, ScConfig.SurvivorGearQualityMax);
            bool any = false;
            for (int slot = 0; slot < 4; slot++)
            {
                string item = SetItem(set, slot);
                look.Gear[slot] = item;                 // a set without this piece leaves the slot bare, never the old piece
                look.Quality[slot] = item.Length > 0 && rnd != null ? rnd.RandomRange(qMin, qMax + 1) : qMin;
                if (item.Length > 0) any = true;
            }
            return any;
        }

        /// <summary>The base body and head assets must exist or the body builder silently produces nothing.</summary>
        public static void Validate(Look look)
        {
            if (look == null || look.Validated) return;
            try
            {
                if (!SDCSUtils.BasePartsExist(look.ToArchetype()))
                {
                    ScLog.Warn("look " + look.Encode() + " has no body assets - using the default face");
                    look.Race = "White";
                    look.Variant = 1;
                    if (!SDCSUtils.BasePartsExist(look.ToArchetype())) ScLog.Err("the default player body (White/01) is missing too - player-body survivors will be invisible");
                }
                look.Validated = true;      // a thrown check is retried on the next use
            }
            catch (Exception e) { ScLog.Once("looks-validate", e); }
        }

        // ---- gear on the entity ----

        /// <summary>Resolves an armor item to its equipment slot (Head 0 .. Feet 3), or -1.</summary>
        private static int SlotOf(string item, int fallback)
        {
            ItemClass ic = ItemClass.GetItemClass(item, true);
            var armor = ic as ItemClassArmor;
            if (armor != null && (int)armor.EquipSlot >= 0 && (int)armor.EquipSlot < 4) return (int)armor.EquipSlot;
            return ic != null ? fallback : -1;
        }

        /// <summary>The wanted item name per slot, resolved through each item's own EquipSlot.</summary>
        private static void Wanted(Look look, string[] names, int[] quality)
        {
            for (int i = 0; i < 4; i++) { names[i] = ""; quality[i] = 1; }
            if (look == null) return;
            for (int i = 0; i < 4; i++)
            {
                if (string.IsNullOrEmpty(look.Gear[i])) continue;
                int slot = SlotOf(look.Gear[i], i);
                if (slot < 0) continue;
                names[slot] = look.Gear[i];
                quality[slot] = look.Quality[i];
            }
        }

        private static ItemValue MakeItem(string name, int quality)
        {
            ItemClass ic = ItemClass.GetItemClass(name, true);
            if (ic == null) return null;
            int q = Math.Max(1, Math.Min(6, quality));
            return new ItemValue(ic.Id, q, q, false);
        }

        private static readonly string[] wantNames = new string[4];
        private static readonly int[] wantQuality = new int[4];

        /// <summary>
        /// Before the FIRST body build (from the model shim's Init): raw slot writes, no events, so the body
        /// is generated already wearing the clothes and nothing has to be rebuilt.
        /// </summary>
        public static void StageGear(EntityAlive npc, Look look)
        {
            if (npc == null || npc.equipment == null || look == null || !look.HasGear) return;
            try
            {
                Wanted(look, wantNames, wantQuality);
                for (int slot = 0; slot < 4; slot++)
                {
                    if (wantNames[slot].Length == 0) continue;
                    ItemValue iv = MakeItem(wantNames[slot], wantQuality[slot]);
                    if (iv != null) npc.equipment.SetSlotItemRaw(slot, iv);
                }
                npc.equipment.ResetArmorGroups();
            }
            catch (Exception e) { ScLog.Once("looks-stage", e); }
        }

        private static string SlotName(EntityAlive npc, int slot)
        {
            ItemValue iv = npc.equipment.GetSlotItem(slot);
            if (iv == null || iv.IsEmpty() || iv.ItemClass == null) return "";
            return iv.ItemClass.GetItemName() ?? "";
        }

        /// <summary>
        /// Makes the loaded entity wear exactly what the look says (equipment is never saved for NPCs, so this
        /// runs from the relink tick). Returns true when something changed and the body was rebuilt.
        /// On one of the community's 0-XNPCCore survivors every SetSlotItem would also fire XNPC's equipment handler,
        /// which rebuilds the whole body - up to four rebuilds before ours - so its own switch for that
        /// (EModelSdcsNpc.SuppressEquipmentRebuild) is held on for the loop and the body is rebuilt once after it.
        /// </summary>
        public static bool SyncGear(EntityAlive npc, Look look)
        {
            if (npc == null || npc.equipment == null || look == null || !IsSurvivor(npc)) return false;
            EModelSdcsNpc x = ScXnpc.IsBody(npc) ? npc.emodel as EModelSdcsNpc : null;
            bool wasSuppressed = x != null && x.SuppressEquipmentRebuild;
            try
            {
                Wanted(look, wantNames, wantQuality);
                bool changed = false;
                if (x != null) x.SuppressEquipmentRebuild = true;
                try
                {
                    for (int slot = 0; slot < 4; slot++)
                    {
                        string have = SlotName(npc, slot);
                        if (string.Equals(have, wantNames[slot], StringComparison.OrdinalIgnoreCase)) continue;
                        npc.equipment.SetSlotItem(slot, wantNames[slot].Length > 0 ? MakeItem(wantNames[slot], wantQuality[slot]) : null);
                        changed = true;
                    }
                }
                finally
                {
                    if (x != null) x.SuppressEquipmentRebuild = wasSuppressed;
                }
                if (changed) Rebuild(npc);
                return changed;
            }
            catch (Exception e)
            {
                ScLog.Once("looks-sync", e);
                return false;
            }
        }

        /// <summary>
        /// Regenerates the body meshes from the archetype and the equipment slots. One of the community's 0-XNPCCore
        /// survivors is rebuilt through XNPC's own GenerateMeshes (the "new" one on EModelSdcsNpc, which also guards
        /// against its equipment handler re-entering), not through its UpdateEquipment: that one does nothing while the
        /// animator is disabled, as on a hidden or culled body. Patches_Xnpc (X2) re-roots the new eyes after it.
        /// </summary>
        public static void Rebuild(EntityAlive npc)
        {
            try
            {
                EModelSdcsNpc x = ScXnpc.IsBody(npc) ? npc.emodel as EModelSdcsNpc : null;
                if (x != null)
                {
                    x.GenerateMeshes();
                    npc.ReassignEquipmentTransforms();
                    x.EnsureTestAnimatorController("SurvivorCommunity");   // what XNPC's own rebuild does after the meshes
                    return;
                }
                EModelSDCS m = npc != null && Patches_Sdcs.IsOurModel(npc.emodel, "looks-rebuild") ? (EModelSDCS)npc.emodel : null;
                if (m == null) return;
                m.GenerateMeshes();
                npc.ReassignEquipmentTransforms();
            }
            catch (Exception e) { ScLog.Once("looks-rebuild", e); }
        }

        /// <summary>A whole new face (same sex) plus the look's gear, applied to a loaded survivor.</summary>
        public static void Apply(EntityAlive npc, Look look)
        {
            EModelSDCS m = npc != null && (Patches_Sdcs.IsOurModel(npc.emodel, "looks-apply") || ScXnpc.IsBody(npc)) ? (EModelSDCS)npc.emodel : null;
            if (m == null || look == null) return;
            try
            {
                look.Male = m.isMale;               // the rig and animator are per sex: never flip it on a live entity
                Validate(look);
                m.archetype = look.ToArchetype();
                session[npc.entityId] = look;
                if (!SyncGear(npc, look)) Rebuild(npc);
            }
            catch (Exception e) { ScLog.Once("looks-apply", e); }
        }

        /// <summary>Relink: a member wearing the player's body keeps the registry's look and gear.</summary>
        public static void Sync(Member m, EntityAlive npc)
        {
            if (m == null || npc == null || !IsSurvivor(npc)) return;
            if (m.Look == null)
            {
                m.Look = Get(npc);      // joined before the record carried a look: adopt what they rolled
                return;
            }
            Look live;
            if (!session.TryGetValue(npc.entityId, out live) || !ReferenceEquals(live, m.Look))
            {
                // the entity was built from another look (record unreadable at init): take the record's face too
                session[npc.entityId] = m.Look;
                sessionClass[npc.entityId] = ScCommunity.ClassName(npc);
                EModelSDCS em = npc.emodel as EModelSDCS;
                if (em != null)
                {
                    m.Look.Male = em.isMale;
                    Validate(m.Look);
                    em.archetype = m.Look.ToArchetype();
                    if (!SyncGear(npc, m.Look)) Rebuild(npc);
                }
                return;
            }
            SyncGear(npc, m.Look);
        }

        public static string Describe(Look l)
        {
            if (l == null) return "-";
            var sb = new StringBuilder();
            sb.Append(l.Male ? "male " : "female ").Append(l.Race ?? "").Append('/').Append(l.Variant);
            sb.Append(" hair ").Append(string.IsNullOrEmpty(l.Hair) ? "none" : l.Hair);
            if (!string.IsNullOrEmpty(l.HairColor)) sb.Append(" (").Append(l.HairColor).Append(')');
            string facial = (l.Mustache ?? "") + "/" + (l.Chops ?? "") + "/" + (l.Beard ?? "");
            if (l.Male && facial != "//") sb.Append(" facial ").Append(facial);
            sb.Append(" eyes ").Append(l.Eyes ?? "");
            sb.Append(" gear [").Append(l.EncodeGear()).Append(']');
            return sb.ToString();
        }
    }
}
