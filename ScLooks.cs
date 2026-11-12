using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// A survivor's (or raider's) appearance on 0-XNPCCore's player body (EModelSdcsNpc, ScXnpc.IsBody): the
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
        /// <summary>
        /// Vanity Vault ids (ScVanity): a body paint ("Sonja_Broken_Full_M"), a face paint ("Sonja_Broken") and one tattoo
        /// ("Maori 03 (Black Orchid) [Blend]"); "" = none. Kept when Vanity Vault is missing, so they come back with it.
        /// </summary>
        public string BodyPaint = "";
        public string FacePaint = "";
        public string Tattoo = "";
        /// <summary>
        /// Round 31: the face is a 0-XNPCCore character-creator character's (X17, ScLooks.AdoptFace), not one SC rolled - SC
        /// leaves its hair color alone (ScLooks.FixHairColor). Saved with the look (Encode's 13th field "C"), so a recruit
        /// keeps it; sc look's new face clears it.
        /// </summary>
        public bool CreatorFace;

        public bool HasPaint { get { return !string.IsNullOrEmpty(BodyPaint) || !string.IsNullOrEmpty(FacePaint) || !string.IsNullOrEmpty(Tattoo); } }

        public Look Clone()
        {
            var l = new Look { Male = Male, Race = Race, Variant = Variant, Hair = Hair, HairColor = HairColor, Mustache = Mustache, Chops = Chops, Beard = Beard, Eyes = Eyes, Validated = Validated,
                              BodyPaint = BodyPaint, FacePaint = FacePaint, Tattoo = Tattoo, CreatorFace = CreatorFace };
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

        /// <summary>
        /// "M|White|2|buzzcut|06 Black|1||3|Blue05" - no '=', ';' or newlines, so the registry's Clean() leaves it alone.
        /// Paints add "|body|face|tattoo" only when there are any: an unpainted look reads as before (older builds and
        /// portrait keys alike), and an older build reading a painted one stops at the eyes. Round 31: a creator face adds the
        /// three paint fields (empty when unpainted) and "|C"; every other look reads as before, and an older build reading
        /// one ignores the 13th field (and drops it at its next save).
        /// </summary>
        public string Encode()
        {
            string s = string.Join("|", new[] { Male ? "M" : "F", Race ?? "", Variant.ToString(CultureInfo.InvariantCulture), Hair ?? "", HairColor ?? "", Mustache ?? "", Chops ?? "", Beard ?? "", Eyes ?? "" });
            if (HasPaint || CreatorFace) s += "|" + (BodyPaint ?? "") + "|" + (FacePaint ?? "") + "|" + (Tattoo ?? "");
            return CreatorFace ? s + "|C" : s;
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
            BodyPaint = p.Length > 9 ? p[9].Trim() : "";
            FacePaint = p.Length > 10 ? p[10].Trim() : "";
            Tattoo = p.Length > 11 ? p[11].Trim() : "";
            CreatorFace = p.Length > 12 && string.Equals(p[12].Trim(), "C", StringComparison.OrdinalIgnoreCase);   // round 31
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
    /// XNPC's own mesh build (Rebuild). SC's raiders ride the same body since M10 (scRaider{Male,Female}T{1,2,3},
    /// ScXnpc.IsRaider) and are built the same way, their clothes rolled from RaiderGearSets (M10-D7). The old
    /// per-weapon raider classes, their class scans and the branches for SC's own player body (an exact EModelSDCS)
    /// went with the model shim in M10 round 2.
    /// Round 31: natural hair colors follow the skin - a mid or a dark skin (ScConfig.SurvivorMidSkins / SurvivorDarkSkins)
    /// rolls only its group's natural colors (NaturalFor), and a stored look with a natural color its skin no longer allows is
    /// given an allowed one as it is validated (FixHairColor, as P2's FixMaleHair); dyed colors and creator faces are left be.
    /// </summary>
    public static class ScLooks
    {
        private static readonly string[] SlotSuffix = { "Helmet", "Outfit", "Gloves", "Boots" };
        /// <summary>The body armor slot, the one a survivor should never turn up without.</summary>
        private const int ChestSlot = 1;

        /// <summary>Consumed by the next player-body entity that initialises (set right before EntityFactory.CreateEntity).</summary>
        public static Look Pending;
        /// <summary>Round 31: the name of the known survivor whose look Pending is (ScSpawn.Create, ScRunners), for the hair color fix's log line; null for a stranger.</summary>
        public static string PendingName;
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
            PendingName = null;
            session.Clear();
            sessionClass.Clear();
            sessionSource.Clear();
            catalogueReady = false;
#if SC_V34
            pendingFaces.Clear();
#endif
        }

        /// <summary>Forgets a despawned/purged survivor's session look (entity ids are recycled).</summary>
        public static void Forget(int entityId)
        {
            session.Remove(entityId);
            sessionClass.Remove(entityId);
            sessionSource.Remove(entityId);
            ScVanity.Forget(entityId);                 // a paint still waiting for the body to show
#if SC_V34
            pendingFaces.Remove(entityId);             // a creator face still waiting for the body's next frame (X17)
#endif
        }

        /// <summary>Where the body's look came from when its model was built (pending / member / prospect / session / roll), or "-".</summary>
        public static string SourceOf(int entityId)
        {
            string s;
            return sessionSource.TryGetValue(entityId, out s) ? s : "-";
        }

        // ---- classes ----

        /// <summary>The highest weapon tier a stranger should carry at this gamestage (mirrors the spawn groups).</summary>
        public static int MaxTierFor(int gamestage)
        {
            return gamestage < 50 ? 0 : (gamestage < 100 ? 1 : 2);
        }

        public static bool ClassIsMale(string cls)
        {
            return string.IsNullOrEmpty(cls) || cls.IndexOf("Female", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>
        /// A loaded NPC that wears a look of ours: one of our XNPC bodies (ScXnpc.IsBody - a survivor class the community
        /// owns, or a raider), never the player, never XNPC's other classes.
        /// </summary>
        public static bool IsSurvivor(Entity e)
        {
            return ScXnpc.IsBody(e);
        }

        // ---- resolution for the body build (Patches_Xnpc X1) ----

        /// <summary>
        /// The look for an entity that is initialising its player body: the pending spawn look, else the
        /// registry record (reloads), else what this session rolled for it, else a fresh roll (a raider's clothes from
        /// RaiderGearSets).
        /// </summary>
        public static Look ForEntity(Entity e)
        {
            Look look = null;
            string source = "roll";
            string name = null;     // round 31: whose look it is, for the hair color fix's log line (null: the body's own name)
            try
            {
                string cls = e != null ? ScCommunity.ClassName(e as EntityAlive) : "";
                if (Pending != null) { look = Pending; name = PendingName; Pending = null; PendingName = null; source = "pending"; }
                if (look == null && e != null)
                {
                    Member m = ScRegistry.GetMember(e.entityId);
                    if (m != null && m.Look != null && (string.IsNullOrEmpty(m.Class) || string.Equals(m.Class, cls, StringComparison.OrdinalIgnoreCase))) { look = m.Look; name = m.Name; source = "member"; }
                    if (look == null)
                    {
                        Prospect p = ScRegistry.GetProspect(e.entityId);
                        if (p != null && p.Look != null && (string.IsNullOrEmpty(p.Class) || string.Equals(p.Class, cls, StringComparison.OrdinalIgnoreCase))) { look = p.Look; name = p.Name; source = "prospect"; }
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
                    look = Roll(ClassIsMale(cls), w != null ? w.GetGameRandom() : null, true, ScXnpc.IsRaiderClass(cls), ScLoadout.TierCap(cls));
                }
                Validate(look, e, name);    // P2's hair and round 31's hair color fixes land here, before the body is built (X1)
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
#if SC_V34

        // ---- 0-XNPCCore 3.4's character-creator faces (Patches_Xnpc X17, decision D4) ----

        /// <summary>Bodies whose adopted creator face waits for their controller's next frame (X7 builds it).</summary>
        private static readonly HashSet<int> pendingFaces = new HashSet<int>();

        /// <summary>
        /// Why this body may not take a creator face, or null when it may: a fresh body - a survivor (never a raider) whose
        /// look ForEntity has just rolled ("roll"), with no member or prospect record, not one of the community's (sc_member,
        /// a stand-in) and not one of an enclave's people. Every other body wears a look SC stores and restores (a member's,
        /// a prospect's, a recruit's or a stand-in's pending look, an enclave person's, this session's) - never replaced.
        /// </summary>
        public static string CustomFaceRefusal(EntityAlive npc)
        {
            if (npc == null || !ScXnpc.IsSurvivorBody(npc)) return "not a survivor body of ours";
            if (ScRegistry.GetMember(npc.entityId) != null) return "a member";
            if (ScRegistry.GetProspect(npc.entityId) != null) return "a prospect";
            if (ScXnpc.IsCommunity(npc)) return "one of the community (or a stand-in)";
            if (ScEnclavePeople.IsEnclaveNpc(npc)) return "one of an enclave's people";
            string source = SourceOf(npc.entityId);
            return source == "roll" ? null : "its look is SC's (" + source + ")";
        }

        /// <summary>
        /// A fresh body takes a character-creator face (X17): the archetype 0-XNPCCore built from the character's picks
        /// replaces the face of its look - race, variant, hair and colour, facial hair, eyes - and keeps SC's clothes. It is
        /// this body's look from now on (the session look, so a recruit's record stores the face it is seen with), and the
        /// body is rebuilt on its controller's next frame (ApplyPendingFace), as 0-XNPCCore defers its own.
        /// </summary>
        public static bool AdoptFace(EntityAlive npc, Archetype a)
        {
            if (npc == null || a == null || !ScXnpc.IsBody(npc)) return false;
            Look cur = Get(npc);
            Look l = cur != null ? cur.Clone() : new Look();
            l.Race = string.IsNullOrEmpty(a.Race) ? l.Race : a.Race;
            l.Variant = a.Variant > 0 ? a.Variant : l.Variant;
            l.Hair = a.Hair ?? "";
            l.HairColor = a.HairColor ?? "";
            l.Mustache = a.MustacheName ?? "";
            l.Chops = a.ChopsName ?? "";
            l.Beard = a.BeardName ?? "";
            l.Eyes = string.IsNullOrEmpty(a.EyeColorName) ? l.Eyes : a.EyeColorName;
            l.CreatorFace = true;   // round 31: the creator's hair color is theirs, never corrected for the skin
            l.Validated = false;
            Validate(l);
            session[npc.entityId] = l;
            sessionClass[npc.entityId] = ScCommunity.ClassName(npc);
            sessionSource[npc.entityId] = "creator face";
            pendingFaces.Add(npc.entityId);
            return true;
        }

        /// <summary>X7, every frame for each of our bodies: builds a creator face AdoptFace left for this one (once).</summary>
        public static void ApplyPendingFace(EntityAlive npc)
        {
            if (pendingFaces.Count == 0 || npc == null || !pendingFaces.Remove(npc.entityId)) return;
            Look l;
            if (session.TryGetValue(npc.entityId, out l) && l != null) Apply(npc, l);
        }
#endif

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

        /// <summary>P2: FallbackHairs without the styles men are not given (SurvivorExcludeHairsMale, SurvivorExcludeHairs).</summary>
        private static string[] MaleFallbackHairs()
        {
            var l = Allowed(Allowed(new List<string>(FallbackHairs), ScConfig.SurvivorExcludeHairs), ScConfig.SurvivorExcludeHairsMale);
            return l != null && l.Count > 0 ? l.ToArray() : FallbackHairs;
        }

        /// <summary>
        /// P2: a man's stored hair that is now excluded (SurvivorExcludeHairsMale) is swapped for an allowed one, picked from
        /// the look's own other parts so the same person gets the same new hair on every load (and keeps it once saved).
        /// </summary>
        private static void FixMaleHair(Look look)
        {
            if (look == null || !look.Male || string.IsNullOrEmpty(look.Hair) || !ScConfig.Listed(ScConfig.SurvivorExcludeHairsMale, look.Hair)) return;
            List<string> ok = null;
            try { ok = Allowed(Allowed(SDCSDataUtils.GetHairNames(true, SDCSDataUtils.HairTypes.Hair), ScConfig.SurvivorExcludeHairs), ScConfig.SurvivorExcludeHairsMale); }
            catch (Exception e) { ScLog.Once("looks-malehair-list", e); }
            string[] pool = ok != null && ok.Count > 0 ? ok.ToArray() : MaleFallbackHairs();
            string seed = (look.Race ?? "") + "|" + look.Variant + "|" + (look.HairColor ?? "") + "|" + (look.Eyes ?? "") + "|" + (look.Beard ?? "");
            int h = seed.GetStableHashCode();
            string was = look.Hair;
            look.Hair = pool[(h & 0x7fffffff) % pool.Length];
            ScLog.Verbose("look: a man's hair " + was + " is no longer given to men - " + look.Hair + " instead");
        }

        /// <summary>A new list of the names not on the exclude list (null in, null out - Pick then uses its fallback).</summary>
        private static List<string> Allowed(List<string> names, List<string> exclude)
        {
            if (names == null) return null;
            var keep = new List<string>(names.Count);
            for (int i = 0; i < names.Count; i++) if (!ScConfig.Listed(exclude, names[i])) keep.Add(names[i]);
            return keep;
        }

        private static float Chance(GameRandom rnd)
        {
            return rnd != null ? rnd.RandomFloat : UnityEngine.Random.value;
        }

        /// <summary>
        /// A random face and hair (and a set of clothes when asked - a raider's from RaiderGearSets) for the given sex. band
        /// is the NPC's effective tier (ScLoadout.TierCap, as for its weapons): it weights the armor and sets its quality;
        /// -1 when unknown.
        /// </summary>
        public static Look Roll(bool male, GameRandom rnd, bool withGear, bool raider = false, int band = -1)
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
                    // the exclude lists (SurvivorExclude*) filter copies: GetEyeColorNames hands out the live static list
                    races = Allowed(SDCSDataUtils.GetRaceList(male), ScConfig.SurvivorExcludeSkins);
                    look.Race = Pick(rnd, races, FallbackRaces);
                    variants = SDCSDataUtils.GetVariantList(male, look.Race);
                    hairs = Allowed(SDCSDataUtils.GetHairNames(male, SDCSDataUtils.HairTypes.Hair), ScConfig.SurvivorExcludeHairs);
                    if (male) hairs = Allowed(hairs, ScConfig.SurvivorExcludeHairsMale);   // P2: no clearly female styles on men
                    eyes = Allowed(SDCSDataUtils.GetEyeColorNames(), ScConfig.SurvivorExcludeEyes);
                    colors = new List<string>();
                    foreach (var hc in SDCSDataUtils.GetHairColorNames()) if (!string.IsNullOrEmpty(hc.PrefabName)) colors.Add(hc.PrefabName);
                    // dyed colours are rare, rarer still on men: pick from the dyed or the natural ones (the natural
                    // ones also when no dyed colour is left to pick). Round 31: the natural ones the rolled skin allows
                    // (NaturalFor: a mid or a dark skin's group list); a dyed color is open to every skin
                    bool dyed = Chance(rnd) < (male ? ScConfig.SurvivorDyedHairChanceMale : ScConfig.SurvivorDyedHairChanceFemale);
                    List<string> all = colors;
                    colors = dyed ? all.FindAll(c => ScConfig.Listed(ScConfig.SurvivorDyedHairColors, c)) : null;
                    if (colors == null || colors.Count == 0) colors = NaturalFor(look.Race, Allowed(all, ScConfig.SurvivorDyedHairColors));
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
                look.Hair = Chance(rnd) < ScConfig.SurvivorBaldChance ? "" : Pick(rnd, hairs, male ? MaleFallbackHairs() : FallbackHairs);
                // the game's list unreadable: the fallback's natural colors, filtered for the skin as well (round 31)
                look.HairColor = colors != null && colors.Count > 0 ? Pick(rnd, colors, null) : Pick(rnd, null, NaturalFallback(look.Race));
                look.Eyes = Pick(rnd, eyes, FallbackEyes);
                if (male)
                {
                    look.Mustache = Chance(rnd) < ScConfig.SurvivorFacialHairChance ? Pick(rnd, must, FallbackFacial) : "";
                    look.Chops = Chance(rnd) < ScConfig.SurvivorFacialHairChance * 0.5f ? Pick(rnd, chops, FallbackFacial) : "";
                    look.Beard = Chance(rnd) < ScConfig.SurvivorFacialHairChance ? Pick(rnd, beards, FallbackFacial) : "";
                }
                if (withGear) RollGear(look, rnd, raider, band);
                ScVanity.Roll(look, rnd, raider);   // Vanity Vault paints and tattoos (host only; none without it)
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
        /// Clothes for an NPC of this effective band (ScLoadout.TierCap; -1 = unknown). With SurvivorOutfitShare
        /// (RaiderOutfitShare for a raider) a whole mod outfit (Outfits) when one fits its sex, side and band - its pieces
        /// only ever swap with another outfit of its mix group, never with vanilla pieces, which clip. Otherwise the
        /// config's armor sets: one main set picked by weight (GearSetTiers x GearTierWeights: lighter sets likelier early,
        /// heavier ones later, nothing ruled out), each piece present with SurvivorGearPieceChance and swapped for another
        /// set's piece with SurvivorGearMixChance (a scavenged look). The body armor is the exception - at a piece chance
        /// of 0.8 one survivor in five turned up bare-chested, which reads as a missing mesh rather than as scavenging, so
        /// that slot ignores the chance (SurvivorGearAlwaysChest). A raider's sets are RaiderGearSets (M10-D7;
        /// SurvivorGearSets when that is empty), with the same chances. Quality from GearQualityByTier for the band.
        /// </summary>
        public static void RollGear(Look look, GameRandom rnd, bool raider = false, int band = -1)
        {
            for (int i = 0; i < 4; i++) { look.Gear[i] = ""; look.Quality[i] = 1; }
            int qMin, qMax;
            ScConfig.GearQuality(band, out qMin, out qMax);
            if (RollOutfit(look, rnd, raider, band, qMin, qMax)) return;
            var sets = ScConfig.GearSetsFor(raider);
            if (sets.Count == 0) return;
            string main = PickSet(sets, band, rnd);
            for (int slot = 0; slot < 4; slot++)
            {
                bool must = slot == ChestSlot && ScConfig.SurvivorGearAlwaysChest;
                if (!must && Chance(rnd) >= ScConfig.SurvivorGearPieceChance) continue;
                string set = main;
                if (sets.Count > 1 && Chance(rnd) < ScConfig.SurvivorGearMixChance) set = PickSet(sets, band, rnd);
                string item = SetItem(set, slot);
                if (item.Length == 0) item = SetItem(main, slot);
                // a required piece takes whichever set has one rather than leaving the slot bare
                for (int i = 0; must && item.Length == 0 && i < sets.Count; i++) item = SetItem(sets[i], slot);
                if (item.Length == 0) continue;
                look.Gear[slot] = item;
                look.Quality[slot] = rnd != null ? rnd.RandomRange(qMin, qMax + 1) : qMin;
            }
        }

        /// <summary>One vanilla armor set by weight for this band (all alike when the band is unknown or nothing weighs anything).</summary>
        private static string PickSet(List<string> sets, int band, GameRandom rnd)
        {
            float total = 0f;
            foreach (string s in sets) total += ScConfig.GearSetWeight(s, band);
            if (total <= 0f) return sets[rnd != null ? rnd.RandomRange(0, sets.Count) : 0];
            float r = Chance(rnd) * total;
            string last = null;
            foreach (string s in sets)
            {
                float w = ScConfig.GearSetWeight(s, band);
                if (w <= 0f) continue;
                last = s;
                if (r < w) return s;
                r -= w;
            }
            return last;
        }

        // ---- mod outfits (ScConfig.Outfits) ----

        /// <summary>An item that exists in this game (a mod outfit's pack is installed).</summary>
        private static bool Has(string item)
        {
            return !string.IsNullOrEmpty(item) && ItemClass.GetItemClass(item, true) != null;
        }

        private static bool SexFits(int sex, bool male)
        {
            return sex == 0 || (sex == 1) == male;
        }

        /// <summary>Installed (its body armor exists), weighted, and right for this sex, side and band (-1: any band).</summary>
        private static bool Fits(OutfitDef o, bool male, bool raider, int band)
        {
            if (o == null || o.Weight <= 0f || !SexFits(o.Sex, male) || !Has(o.Items[ChestSlot])) return false;
            if (raider ? !o.Raiders : !o.Survivors) return false;
            return band < 0 || o.Tier <= band;
        }

        private static OutfitDef PickOutfit(List<OutfitDef> list, GameRandom rnd)
        {
            float total = 0f;
            foreach (OutfitDef o in list) total += o.Weight;
            if (total <= 0f) return null;
            float r = Chance(rnd) * total;
            OutfitDef last = null;
            foreach (OutfitDef o in list)
            {
                if (o.Weight <= 0f) continue;
                last = o;
                if (r < o.Weight) return o;
                r -= o.Weight;
            }
            return last;
        }

        /// <summary>
        /// A whole mod outfit, with the outfit share, when one fits: every piece of the main outfit with the same piece and
        /// body-armor chances as a vanilla set, a piece swapped (SurvivorGearMixChance) only for one of an outfit of the same
        /// mix group that fits too. False (nothing changed) when none fits or the share does not come up.
        /// </summary>
        private static bool RollOutfit(Look look, GameRandom rnd, bool raider, int band, int qMin, int qMax)
        {
            float share = raider ? ScConfig.RaiderOutfitShare : ScConfig.SurvivorOutfitShare;
            if (share <= 0f || ScConfig.Outfits.Count == 0) return false;
            var fit = new List<OutfitDef>();
            foreach (OutfitDef o in ScConfig.Outfits) if (Fits(o, look.Male, raider, band)) fit.Add(o);
            if (fit.Count == 0 || Chance(rnd) >= share) return false;
            OutfitDef main = PickOutfit(fit, rnd);
            if (main == null) return false;
            List<OutfitDef> mates = main.Mix.Length == 0 ? null
                : fit.FindAll(o => o != main && string.Equals(o.Mix, main.Mix, StringComparison.OrdinalIgnoreCase));
            for (int slot = 0; slot < 4; slot++)
            {
                bool must = slot == ChestSlot && ScConfig.SurvivorGearAlwaysChest;
                if (!must && Chance(rnd) >= ScConfig.SurvivorGearPieceChance) continue;
                string item = main.Items[slot];
                if (mates != null && mates.Count > 0 && Chance(rnd) < ScConfig.SurvivorGearMixChance)
                {
                    OutfitDef other = PickOutfit(mates, rnd);
                    if (other != null && Has(other.Items[slot])) item = other.Items[slot];
                }
                if (!Has(item)) continue;
                look.Gear[slot] = item;
                look.Quality[slot] = rnd != null ? rnd.RandomRange(qMin, qMax + 1) : qMin;
            }
            return true;
        }

        /// <summary>A mod outfit by its id ("TG_HitGirl") or the id past its pack prefix ("HitGirl"); null if none.</summary>
        public static OutfitDef FindOutfit(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (OutfitDef o in ScConfig.Outfits) if (string.Equals(o.Id, name, StringComparison.OrdinalIgnoreCase)) return o;
            foreach (OutfitDef o in ScConfig.Outfits)
            {
                int u = o.Id.IndexOf('_');
                if (u >= 0 && string.Equals(o.Id.Substring(u + 1), name, StringComparison.OrdinalIgnoreCase)) return o;
            }
            return null;
        }

        /// <summary>The ids of the mod outfits whose pack is installed.</summary>
        public static List<string> InstalledOutfits()
        {
            var ids = new List<string>();
            foreach (OutfitDef o in ScConfig.Outfits) if (Has(o.Items[ChestSlot])) ids.Add(o.Id);
            return ids;
        }

        /// <summary>0 any, 1 male only, 2 female only: the sex of the first single-sex Outfits entry listing this item.</summary>
        private static int ItemSex(string item)
        {
            foreach (OutfitDef o in ScConfig.Outfits)
            {
                if (o.Sex == 0) continue;
                for (int i = 0; i < 4; i++) if (string.Equals(o.Items[i], item, StringComparison.OrdinalIgnoreCase)) return o.Sex;
            }
            return 0;
        }

        /// <summary>
        /// Puts a whole vanilla set or mod outfit on (an outfit by its id, FindOutfit), or "none" strips everything. False
        /// when there is no such set or installed outfit, or the outfit is built for the other sex. Quality from the band's
        /// GearQualityByTier range (sc spawn), or SurvivorGearQualityMin..Max when it is unknown (-1, sc gear).
        /// </summary>
        public static bool WearSet(Look look, string set, GameRandom rnd, int band = -1)
        {
            if (look == null) return false;
            if (string.Equals(set, "none", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < 4; i++) { look.Gear[i] = ""; look.Quality[i] = 1; }
                return true;
            }
            int qMin, qMax;
            ScConfig.GearQuality(band, out qMin, out qMax);
            OutfitDef outfit = FindOutfit(set);
            if (outfit != null)
            {
                if (!SexFits(outfit.Sex, look.Male) || !Has(outfit.Items[ChestSlot])) return false;
                for (int slot = 0; slot < 4; slot++)
                {
                    string item = Has(outfit.Items[slot]) ? outfit.Items[slot] : "";
                    look.Gear[slot] = item;
                    look.Quality[slot] = item.Length > 0 && rnd != null ? rnd.RandomRange(qMin, qMax + 1) : qMin;
                }
                return true;
            }
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

        /// <summary>
        /// The base body and head assets must exist or the body builder silently produces nothing. Also corrects a stored
        /// look to today's rules, in place, once per look per session: P2's men's hair (FixMaleHair) and round 31's natural
        /// hair color for the skin (FixHairColor, after the race is final). e / name only name the NPC in the hair color fix's
        /// log line (name: whose look it is; null = the body's own name; without e, name is used as it is).
        /// </summary>
        public static void Validate(Look look, Entity e = null, string name = null)
        {
            if (look == null || look.Validated) return;
            try
            {
                FixMaleHair(look);      // P2
                if (!SDCSUtils.BasePartsExist(look.ToArchetype()))
                {
                    ScLog.Warn("look " + look.Encode() + " has no body assets - using the default face");
                    look.Race = "White";
                    look.Variant = 1;
                    if (!SDCSUtils.BasePartsExist(look.ToArchetype())) ScLog.Err("the default player body (White/01) is missing too - player-body survivors will be invisible");
                }
                FixHairColor(look, e, name);    // round 31
                look.Validated = true;      // a thrown check is retried on the next use
            }
            catch (Exception ex) { ScLog.Once("looks-validate", ex); }
        }

        // ---- round 31: natural hair colors by skin ----

        /// <summary>
        /// Round 31: the natural hair colors a skin's group allows - SurvivorDarkSkinHairColors for a skin on SurvivorDarkSkins
        /// (asked first), SurvivorMidSkinHairColors for one on SurvivorMidSkins; null for every other skin (all natural
        /// colors: the light tones, vanilla's White, a skin in neither list). group: "dark", "mid" or null.
        /// </summary>
        internal static List<string> AllowedHairColors(string race, out string group)
        {
            group = null;
            if (string.IsNullOrEmpty(race)) return null;
            if (ScConfig.Listed(ScConfig.SurvivorDarkSkins, race.Trim())) { group = "dark"; return ScConfig.SurvivorDarkSkinHairColors; }
            if (ScConfig.Listed(ScConfig.SurvivorMidSkins, race.Trim())) { group = "mid"; return ScConfig.SurvivorMidSkinHairColors; }
            return null;
        }

        /// <summary>
        /// Round 31, pure: of these natural colors (no dyed one), the ones this skin may have (AllowedHairColors), in their own
        /// order. All of them when the skin's group allows any color, or when the group's list leaves none of them (fellBack).
        /// </summary>
        internal static List<string> NaturalIn(string race, List<string> natural, out string group, out bool fellBack)
        {
            fellBack = false;
            List<string> allow = AllowedHairColors(race, out group);
            if (natural == null || natural.Count == 0 || allow == null) return natural;
            var keep = new List<string>(natural.Count);
            foreach (string c in natural) if (ScConfig.Listed(allow, c)) keep.Add(c);
            if (keep.Count > 0) return keep;
            fellBack = true;
            return natural;
        }

        /// <summary>NaturalIn for a roll, with the log line (once per group) when a group's list leaves no color.</summary>
        private static List<string> NaturalFor(string race, List<string> natural)
        {
            string group;
            bool fellBack;
            List<string> keep = NaturalIn(race, natural, out group, out fellBack);
            if (fellBack) WarnEmptyGroup(group);
            return keep;
        }

        private static void WarnEmptyGroup(string group)
        {
            string key = group == "dark" ? "SurvivorDarkSkinHairColors" : "SurvivorMidSkinHairColors";
            ScLog.Once("looks-haircolor-empty-" + group, "looks: " + key + " leaves none of the game's natural hair colors - skins of that group get any natural color");
        }

        /// <summary>Roll's fallback when the game's color list cannot be read: FallbackHairColors' natural ones, for this skin.</summary>
        private static string[] NaturalFallback(string race)
        {
            List<string> l = NaturalFor(race, Allowed(new List<string>(FallbackHairColors), ScConfig.SurvivorDyedHairColors));
            return l != null && l.Count > 0 ? l.ToArray() : FallbackHairColors;
        }

        /// <summary>
        /// Round 31: the game's natural hair colors - its list (SDCSDataUtils, the game's order: 01 Blond ... 24 Green 1 in 3.2
        /// and 3.4; Vanity Vault adds none) without SurvivorDyedHairColors; FallbackHairColors' when the list cannot be read
        /// (the same ten natural ones, 01 Blond ... 10 Red 1, in the same order).
        /// </summary>
        private static List<string> NaturalColors()
        {
            var all = new List<string>();
            try
            {
                EnsureCatalogue();
                foreach (var hc in SDCSDataUtils.GetHairColorNames()) if (!string.IsNullOrEmpty(hc.PrefabName)) all.Add(hc.PrefabName);
            }
            catch (Exception e) { ScLog.Once("looks-haircolor-list", e); }
            if (all.Count == 0) all.AddRange(FallbackHairColors);
            return Allowed(all, ScConfig.SurvivorDyedHairColors);
        }

        private static bool HasColor(List<string> list, string color)
        {
            if (list == null || string.IsNullOrEmpty(color)) return false;
            foreach (string c in list) if (string.Equals(c, color, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Round 31, pure: the natural hair color a stored look gets instead of its own, or null when it keeps it - a creator
        /// face (X17: the character's own), no color, a dyed one (any skin may have it), a color that is not one of these natural
        /// ones (not the game's: left alone), a skin whose group allows any color or whose list leaves none (NaturalIn), or a
        /// color the group allows. The new one is picked from the group's colors by a stable hash of the look itself (FNV-1a of
        /// its sex, skin, variant, hair, color, eyes and beard), so the same person gets the same color on every load until the
        /// corrected look is saved.
        /// </summary>
        internal static string FixedHairColor(Look look, List<string> natural)
        {
            if (look == null || look.CreatorFace || string.IsNullOrEmpty(look.HairColor) || natural == null) return null;
            if (ScConfig.Listed(ScConfig.SurvivorDyedHairColors, look.HairColor) || !HasColor(natural, look.HairColor)) return null;
            string group;
            bool fellBack;
            List<string> pool = NaturalIn(look.Race, natural, out group, out fellBack);
            if (group == null || fellBack || pool == null || pool.Count == 0 || HasColor(pool, look.HairColor)) return null;
            string seed = (look.Male ? "M" : "F") + "|" + (look.Race ?? "") + "|" + look.Variant + "|" + (look.Hair ?? "") + "|" + look.HairColor
                          + "|" + (look.Eyes ?? "") + "|" + (look.Beard ?? "");
            return pool[(int)(ScVoice.Fnv(seed) % (uint)pool.Count)];
        }

        /// <summary>
        /// Round 31 (mirrors P2's FixMaleHair, from Validate): a stored look whose natural hair color its skin no longer allows
        /// (a mid or a dark skin: AllowedHairColors) is given an allowed one (FixedHairColor), in place - the body is built from
        /// it (ForEntity, X1) and a member's or prospect's record saves it. One log line per swap.
        /// </summary>
        private static void FixHairColor(Look look, Entity e, string name)
        {
            string group;
            if (look == null || look.CreatorFace || string.IsNullOrEmpty(look.HairColor)) return;
            ScConfig.EnsureParsed();    // as Roll does: the groups are the config's before a stored look is changed for good
            if (AllowedHairColors(look.Race, out group) == null) return;
            List<string> natural = NaturalColors();
            bool fellBack;
            NaturalIn(look.Race, natural, out group, out fellBack);
            if (fellBack) { WarnEmptyGroup(group); return; }
            string to = FixedHairColor(look, natural);
            if (to == null) return;
            string was = look.HairColor;
            look.HairColor = to;
            ScLog.Out("looks: " + Label(e, name) + " hair color " + was + " -> " + to + " (skin " + look.Race + ")");
        }

        /// <summary>"Name (id)" for a log line: name, else the body's own name; without a body, name as it is.</summary>
        private static string Label(Entity e, string name)
        {
            string n = name;
            if (string.IsNullOrEmpty(n) && e is EntityAlive)
            {
                try { n = ScCommunity.DisplayName((EntityAlive)e); }
                catch (Exception) { n = null; }
            }
            if (string.IsNullOrEmpty(n)) n = "a stored look";
            return e != null ? n + " (" + e.entityId + ")" : n;
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
                if (!SexFits(ItemSex(look.Gear[i]), look.Male))
                {
                    // a mesh built for the other sex (an old record, a hand edit): left off rather than shown broken
                    ScLog.Once("looks-sex-" + look.Gear[i] + (look.Male ? "-m" : "-f"), "armor " + look.Gear[i] + " is built for "
                               + (look.Male ? "women" : "men") + " only (Outfits) - not put on a " + (look.Male ? "man" : "woman"));
                    continue;
                }
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
        /// Before the FIRST body build (Patches_Xnpc X1, in front of XNPC's createModel): raw slot writes, no events, so
        /// the body is generated already wearing the clothes and nothing has to be rebuilt.
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
#if SC_V34
                    // 3.4: no SetSlotItemRaw - the equipment grid's slot written directly is the same raw write (Equipment
                    // does not listen to its grid; only SetSlotItem raises OnChanged)
                    if (iv != null) npc.equipment.ItemGrid[slot] = new ItemStack(iv, 1);
#else
                    if (iv != null) npc.equipment.SetSlotItemRaw(slot, iv);
#endif
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
        /// Regenerates the body meshes from the archetype and the equipment slots. One of our XNPC bodies is rebuilt
        /// through XNPC's own GenerateMeshes (the "new" one on EModelSdcsNpc, which also guards against its equipment
        /// handler re-entering), not through its UpdateEquipment: that one does nothing while the animator is disabled,
        /// as on a hidden or culled body. Patches_Xnpc (X2) re-roots the new eyes after it.
        /// </summary>
        public static void Rebuild(EntityAlive npc)
        {
            try
            {
                EModelSdcsNpc x = ScXnpc.IsBody(npc) ? npc.emodel as EModelSdcsNpc : null;
                if (x == null) return;
                x.GenerateMeshes();
                npc.ReassignEquipmentTransforms();
                x.EnsureTestAnimatorController("SurvivorCommunity");   // what XNPC's own rebuild does after the meshes
            }
            catch (Exception e) { ScLog.Once("looks-rebuild", e); }
        }

        /// <summary>A whole new face (same sex) plus the look's gear, applied to a loaded survivor.</summary>
        public static void Apply(EntityAlive npc, Look look)
        {
            EModelSDCS m = ScXnpc.IsBody(npc) ? (EModelSDCS)npc.emodel : null;
            if (m == null || look == null) return;
            try
            {
                look.Male = m.isMale;               // the rig and animator are per sex: never flip it on a live entity
                Validate(look, npc);
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
                    Validate(m.Look, npc, m.Name);
                    em.archetype = m.Look.ToArchetype();
                    if (!SyncGear(npc, m.Look)) Rebuild(npc);
                }
                return;
            }
            SyncGear(npc, m.Look);
        }

        /// <summary>
        /// sc gear sim: n clothes rolls at this effective band (-1 = unknown) without making anyone - how many wear a mod
        /// outfit (and which), the vanilla sets' weight classes, and the quality. Sex alternates unless one is given.
        /// </summary>
        public static string SimulateGear(int n, bool raider, int band, int sex)
        {
            World w = ScCommunity.World;
            GameRandom rnd = w != null ? w.GetGameRandom() : null;
            var look = new Look();
            int outfits = 0, bare = 0, pieces = 0, qSum = 0, qLo = 99, qHi = 0;
            var byOutfit = new Dictionary<string, int>();
            var classes = new int[4];      // light, medium, heavy, unlisted
            for (int i = 0; i < n; i++)
            {
                look.Male = sex == 1 || (sex == 0 && i % 2 == 0);
                RollGear(look, rnd, raider, band);
                for (int s = 0; s < 4; s++)
                {
                    if (look.Gear[s].Length == 0) continue;
                    pieces++;
                    qSum += look.Quality[s];
                    qLo = Math.Min(qLo, look.Quality[s]);
                    qHi = Math.Max(qHi, look.Quality[s]);
                }
                string chest = look.Gear[ChestSlot];
                if (chest.Length == 0) { bare++; continue; }
                OutfitDef o = null;
                foreach (OutfitDef d in ScConfig.Outfits) if (string.Equals(d.Items[ChestSlot], chest, StringComparison.OrdinalIgnoreCase)) { o = d; break; }
                if (o != null)
                {
                    outfits++;
                    int c;
                    byOutfit.TryGetValue(o.Id, out c);
                    byOutfit[o.Id] = c + 1;
                    continue;
                }
                string set = chest.StartsWith("armor", StringComparison.Ordinal) && chest.EndsWith("Outfit", StringComparison.Ordinal)
                    ? chest.Substring(5, chest.Length - 11) : null;
                int k;
                classes[set != null && ScConfig.GearSetTiers.TryGetValue(set, out k) ? k : 3]++;
            }
            var sb = new StringBuilder();
            sb.Append("[SC] ").Append(n).Append(raider ? " raider" : " survivor").Append(" outfits (")
              .Append(sex == 1 ? "men, " : sex == 2 ? "women, " : "").Append(band >= 0 ? "band " + band : "band unknown").Append("): ")
              .Append(outfits).Append(" mod outfits (").Append((100.0 * outfits / Math.Max(1, n)).ToString("0.0")).Append("%)");
            if (byOutfit.Count > 0)
            {
                var top = new List<KeyValuePair<string, int>>(byOutfit);
                top.Sort((a, b) => b.Value.CompareTo(a.Value));
                sb.Append(" [");
                for (int i = 0; i < top.Count; i++) sb.Append(i > 0 ? " " : "").Append(top[i].Key).Append('=').Append(top[i].Value);
                sb.Append(']');
            }
            sb.Append(", vanilla light/medium/heavy/other ").Append(classes[0]).Append('/').Append(classes[1]).Append('/').Append(classes[2]).Append('/').Append(classes[3]);
            if (bare > 0) sb.Append(", ").Append(bare).Append(" without body armor");
            if (pieces > 0) sb.Append(", quality ").Append(qLo).Append('-').Append(qHi).Append(" avg ").Append(((double)qSum / pieces).ToString("0.00", CultureInfo.InvariantCulture));
            sb.Append(" (installed outfits ").Append(InstalledOutfits().Count).Append('/').Append(ScConfig.Outfits.Count).Append(')');
            return sb.ToString();
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
            if (l.HasPaint) sb.Append(' ').Append(DescribePaints(l));
            if (l.CreatorFace) sb.Append(" (creator face)");   // round 31
            return sb.ToString();
        }

        public static string DescribePaints(Look l)
        {
            if (l == null || !l.HasPaint) return "no paints";
            return "paint " + (string.IsNullOrEmpty(l.BodyPaint) ? "-" : l.BodyPaint) + "/" + (string.IsNullOrEmpty(l.FacePaint) ? "-" : l.FacePaint)
                   + " tattoo " + (string.IsNullOrEmpty(l.Tattoo) ? "-" : l.Tattoo);
        }
    }
}
