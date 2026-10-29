using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace SurvivorCommunity
{
    /// <summary>
    /// An NPC's two weapons, one ranged and one melee: vanilla item names, for one of the community's 0-XNPCCore survivors
    /// or one of SC's raiders on the same body. A raider also has a lead weapon (Lead).
    /// </summary>
    public class Loadout
    {
        /// <summary>Lead values (raiders only): not decided yet, the ranged weapon first, the melee weapon first.</summary>
        public const int LeadNone = 0, LeadRanged = 1, LeadMelee = 2;

        /// <summary>The ranged weapon's item; NoRanged for an NPC that carries only its melee weapon; null = not decided yet.</summary>
        public string Ranged;
        public string Melee;
        /// <summary>
        /// Ranged's value for an NPC that carries only a melee weapon (SurvivorMeleeOnlyChance / RaiderMeleeOnlyChance, or
        /// the console's melee-only): decided, so nothing re-rolls a ranged one. Kept in the record ("none,club") and in the
        /// ranged cvar (ScLoadout.NoRangedCvar). An older build reads it as unknown and rolls one.
        /// </summary>
        public const string NoRanged = "none";

        /// <summary>A real ranged weapon (not undecided, not NoRanged).</summary>
        public bool HasRanged { get { return Ranged != null && !IsNoRanged(Ranged); } }

        public static bool IsNoRanged(string token) { return string.Equals(token, NoRanged, StringComparison.OrdinalIgnoreCase); }
        /// <summary>
        /// Raiders only (M10-D1): which of its two weapons it relies on - LeadRanged (the gun or bow first, as a survivor
        /// fights) or LeadMelee (charges in with the melee weapon and takes the gun only for a target out of reach), rolled
        /// against RaiderRangedShare or fixed by the console. Saved with the entity (ScLoadout.LeadCvar); never part of a
        /// registry record (Encode).
        /// </summary>
        public int Lead;

        public Loadout() { }
        public Loadout(string ranged, string melee) { Ranged = ranged; Melee = melee; }

        public Loadout Clone() { return new Loadout(Ranged, Melee) { Lead = Lead }; }

        public string Of(bool ranged) { return ranged ? Ranged : Melee; }

        public void Set(bool ranged, string token) { if (ranged) Ranged = token; else Melee = token; }

        /// <summary>"ranged,melee" (either may be empty) - the registry form (loadout= in member, prospect and contact records).</summary>
        public string Encode() { return (Ranged ?? "") + "," + (Melee ?? ""); }

        public static Loadout Decode(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string[] parts = s.Split(',');
            var l = new Loadout(parts[0].Trim(), parts.Length > 1 ? parts[1].Trim() : "");
            if (l.Ranged.Length == 0) l.Ranged = null;
            if (l.Melee.Length == 0) l.Melee = null;
            return l.Ranged == null && l.Melee == null ? null : l;
        }

        public override string ToString() { return (Ranged ?? "-") + " + " + (Melee ?? "-") + (Lead == LeadRanged ? " (ranged-lead)" : Lead == LeadMelee ? " (melee-lead)" : ""); }

        /// <summary>"ranged-lead", "melee-lead" or "-".</summary>
        public static string LeadName(int lead) { return lead == LeadRanged ? "ranged-lead" : lead == LeadMelee ? "melee-lead" : "-"; }
    }

    /// <summary>
    /// Two weapons, one ranged and one melee, for every NPC SC drives on 0-XNPCCore's player body: the community's
    /// survivors and SC's raiders (ScXnpc's managed classes). Both are made legal to hold by rewriting the entity's own
    /// itemsOnEnterGame list in a PostInit PREFIX - before SCore's EntityAlive.PostInit postfix copies it into the
    /// inventory slots, before SetupStartingItems and before PostInit re-equips the saved weapon. SCore's FindWeapon
    /// accepts anything on that list, so XNPC's weapon logic can swap between the two and a reload brings back whichever
    /// was held.
    ///
    /// They use XNPC's weapon system: real vanilla player weapons with finite ammo, the melee swap up close and the
    /// dry-gun failover. Their classes carry no weapon of ours, so both kinds are rolled from one catalogue, XnpcWeapons
    /// (Config/blocks.xml, written by Tools/gen_arsenal.py): a melee weapon, and a bow or crossbow - a firearm at
    /// SurvivorFirearmChance (RaiderFirearmChance for a raider; a T3 class carries a firearm at all at
    /// SurvivorFirearmChanceT3 / RaiderFirearmChanceT3, 85%, round t3, RollRanged) - each at or below the tier cap, which is the lower of the
    /// class's band (T1..T3) and the gamestage tier, so the primitive weapons are there from day 1 and better ones come
    /// with the gamestage. The list becomes [melee, ranged], weapons only: SCore's PostInit postfix refills every
    /// non-quality item on the list to a full stack on every load, so ammo on it would be endless.
    ///
    /// Survivors (migration M7, decision D4): their ammo is a finite stack of the gun's basic rounds put into the bag once,
    /// when a new survivor is first created (AfterPostInit); the stockpile tops it up only in the base or at an outpost
    /// (ScAmmo, decision D3).
    ///
    /// Raiders (M10, DESIGN_M10_RAIDERS.md 2.5/2.6, the user's M10-D1): the same catalogue and the same full pair - every
    /// raider carries both, its ranged weapon a firearm at RaiderFirearmChance - plus a lead: RaiderRangedShare per class
    /// tier (the old bands' mix, 30/40/50%) leads with the ranged weapon, the rest with the melee weapon (ScArms keeps a
    /// melee-lead raider on its melee weapon until the target stays out of reach). Their ammo is a finite starting stack of
    /// RaiderSpawnAmmo / RaiderSpawnArrows in the bag that is never resupplied (M10-D2).
    ///
    /// With Locked &amp; Loaded (M11), on both sides a gun that takes magazines starts with its class tier's loaded
    /// magazines (ScMags.Arm) instead of the loose stack; bows, crossbows and guns without magazines keep the stack.
    ///
    /// The pair resolves, in order: Pending (a console spawn, a radio contact, a respawn of a known survivor), the member
    /// or prospect record (loadout=; survivors only), the pair this entity already carries (two cvars, saved with it), a
    /// roll seeded by (entityId, WorldTimeBorn). The result goes into the cvars at once, so a later gamestage never changes
    /// a saved NPC's weapons. Wild NPCs that are never saved re-roll as new NPCs.
    /// </summary>
    public static class ScLoadout
    {
        /// <summary>The pair this entity carries, as item-name hashes (exact in a float; 0 = none). Saved with the entity.</summary>
        public const string RangedCvar = "sc_loadout_r";
        /// <summary>RangedCvar's value for a melee-only NPC (Loadout.NoRanged): never a hash, which is always above 0.</summary>
        public const float NoRangedCvar = -1f;
        public const string MeleeCvar = "sc_loadout_m";
        /// <summary>A raider's lead (Loadout.LeadRanged 1 / LeadMelee 2; never 0, which a save drops). Saved with the entity.</summary>
        public const string LeadCvar = "sc_raiderLead";
        /// <summary>Set once a body has been given its starting ammo (value 1: a cvar of 0 is not saved).</summary>
        public const string AmmoSeededCvar = "sc_ammoSeeded";
        /// <summary>
        /// A raider's kill XP bonus from its pair (design 2.9): read by the raider templates' passive
        /// "ExperienceGain perc_add @sc_raiderXp" (Config/entityclasses.xml, Tools/gen_arsenal.py RAIDER_XP_CVAR), which
        /// vanilla evaluates on the killed entity when it pays a kill (EntityPlayer.AddKillXP). Saved with the entity, and
        /// written again from the pair on every PostInit.
        /// </summary>
        public const string RaiderXpCvar = "sc_raiderXp";

        /// <summary>Consumed by the next managed body that runs PostInit (set right before EntityFactory.CreateEntity).</summary>
        public static Loadout Pending;

        private class Weapon
        {
            public string Token;           // the vanilla item name
            public string Family;          // melee, bow, firearm
            public bool Ranged;
            public int Tier;
            public float Hash;
            public string Alias = "";      // the generator's short weapon token, for the console
            public float[] Bands;          // weight at band 0/1/2 (bands=, with IZY Classic); null = 1 from Tier up
            public string Pool = "";       // group=: the weapons of this family and pool split Bands
            public int[] PoolSize;         // per band, how many of them weigh anything there (BuildXnpc)
            public string Item { get { return Token; } }
            /// <summary>How strong it counts as (the raider XP bonus, sc loadout sim): the band it is likeliest at, the
            /// higher one on a tie; its tier without bands.</summary>
            public int Level
            {
                get
                {
                    if (Bands == null) return Tier;
                    int best = Tier;
                    for (int b = 0; b < Bands.Length; b++) if (Bands[b] > 0f && Bands[b] >= Bands[best]) best = b;
                    return best;
                }
            }
            /// <summary>Its weight in a roll at this effective band - its share of its pool's when it has one; band -1 = any
            /// band (the fallback), every weapon 1.</summary>
            public float Weight(int band)
            {
                if (band < 0) return 1f;
                if (Bands != null)
                {
                    int b = Math.Min(band, Bands.Length - 1);
                    return PoolSize != null && PoolSize[b] > 1 ? Bands[b] / PoolSize[b] : Bands[b];
                }
                return Tier <= band ? 1f : 0f;
            }
            /// <summary>The share it counts towards: melee, bow or firearm.</summary>
            public string Group { get { return Family; } }
        }

        // sorted by item name so a seeded roll is stable
        private static List<Weapon> xnpcWeapons;
        private static readonly Dictionary<int, string> sources = new Dictionary<int, string>();

        public static void Reset()
        {
            Pending = null;
            xnpcWeapons = null;
            sources.Clear();
        }

        // ---- catalogue ----

        private static List<Weapon> Catalogue()
        {
            if (xnpcWeapons == null) xnpcWeapons = BuildXnpc();
            return xnpcWeapons;
        }

        private static List<Weapon> Sorted(Dictionary<string, Weapon> byToken)
        {
            var list = new List<Weapon>(byToken.Values);
            list.Sort((a, b) => string.CompareOrdinal(a.Token, b.Token));
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    if (list[i].Hash == list[j].Hash) ScLog.Once("loadout-hash-" + list[i].Token, "weapon tokens " + list[i].Token + " and " + list[j].Token + " share a loadout hash - the cvar cannot tell them apart");
            return list;
        }

        /// <summary>
        /// The catalogue both sides roll from: the config's XnpcWeapons (vanilla item, family, tier). Without that block -
        /// an old blocks.xml - the survivor classes' own SdcsWeapons lists stand in, each item at the tier of the lowest
        /// class that lists it (T1 = 0), so the bodies are still armed, if by XNPC's tiering rather than SC's.
        /// </summary>
        private static List<Weapon> BuildXnpc()
        {
            var byToken = new Dictionary<string, Weapon>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!ScConfig.Parsed) ScConfig.EnsureParsed();
                if (ScConfig.XnpcWeapons.Count > 0)
                {
                    foreach (XnpcWeaponDef d in ScConfig.XnpcWeapons) AddXnpc(byToken, d.Item, d.Family, d.Tier, d.Alias, d.Bands, d.Pool);
                }
                else
                {
                    ScLog.Once("loadout-xnpc-fallback", "config has no XnpcWeapons block - arming 0-XNPCCore's survivors from their classes' SdcsWeapons lists");
                    foreach (string c in ScConfig.XnpcSurvivorClasses)
                    {
                        EntityClass ec;
                        string list;
                        if (!EntityClass.list.TryGetValue(EntityClass.FromString(c), out ec) || ec == null || ec.Properties == null
                            || !ec.Properties.Values.TryGetValue("SdcsWeapons", out list) || string.IsNullOrEmpty(list)) continue;
                        int tier = ScXnpc.TierOf(c);
                        foreach (string part in list.Split(','))
                        {
                            string item = part.Trim();
                            if (item.Length == 0) continue;
                            Weapon have;
                            if (byToken.TryGetValue(item, out have)) { have.Tier = Math.Min(have.Tier, tier); continue; }
                            AddXnpc(byToken, item, FamilyOfItem(item), tier, "", null, "");
                        }
                    }
                }
            }
            catch (Exception e) { ScLog.Once("loadout-xnpc-catalogue", e); }
            List<Weapon> sorted = Sorted(byToken);
            // group=: the loaded weapons of one family and pool split its bands, so a pool's share does not grow with its size
            foreach (Weapon w in sorted)
            {
                if (w.Pool.Length == 0) continue;
                w.PoolSize = new int[w.Bands.Length];
                foreach (Weapon o in sorted)
                    if (o.Family == w.Family && string.Equals(o.Pool, w.Pool, StringComparison.OrdinalIgnoreCase) && o.Bands != null)
                        for (int b = 0; b < w.Bands.Length && b < o.Bands.Length; b++) if (o.Bands[b] > 0f) w.PoolSize[b]++;
            }
            if (sorted.Count == 0) ScLog.Once("loadout-xnpc-empty", "no weapons for 0-XNPCCore's survivors and SC's raiders - they keep their class items (XnpcWeapons in Config/blocks.xml)");
            return sorted;
        }

        /// <summary>One catalogue entry, when the item exists and a ranged one names its ammo (XNPC reloads and fails over by it).</summary>
        private static void AddXnpc(Dictionary<string, Weapon> byToken, string item, string family, int tier, string alias, float[] bands, string pool)
        {
            if (string.IsNullOrEmpty(item) || string.IsNullOrEmpty(family) || byToken.ContainsKey(item)) return;
            ItemClass ic = ItemClass.GetItemClass(item, true);
            if (ic == null) { ScLog.Once("loadout-xnpc-item-" + item, "XnpcWeapons: no item " + item + " - left out"); return; }
            bool ranged = family != "melee";
            if (ranged && AmmoOf(ic) == null) { ScLog.Once("loadout-xnpc-ammo-" + item, "XnpcWeapons: " + item + " has no ranged action with ammo - left out"); return; }
            string name = ic.GetItemName();
            byToken[name] = new Weapon { Token = name, Family = family, Ranged = ranged, Tier = tier, Hash = Hash(name), Alias = alias ?? "", Bands = bands, Pool = bands != null ? pool ?? "" : "" };
        }

        /// <summary>melee, bow (bows and crossbows) or firearm, from the item's own tags (the SdcsWeapons fallback only).</summary>
        private static string FamilyOfItem(string item)
        {
            ItemClass ic = ItemClass.GetItemClass(item, true);
            if (ic == null) return null;
            if (ic.HasAnyTags(FastTags<TagGroup.Global>.Parse("melee"))) return "melee";
            if (ic.HasAnyTags(FastTags<TagGroup.Global>.Parse("bow,crossbow"))) return "bow";
            return AmmoOf(ic) != null ? "firearm" : null;
        }

        /// <summary>The basic ammo of a vanilla gun or bow: its first ranged action's first magazine item (XNPC reads Actions[0]).</summary>
        private static string AmmoOf(ItemClass ic)
        {
            if (ic == null || ic.Actions == null) return null;
            foreach (ItemAction a in ic.Actions)
            {
                ItemActionRanged r = a as ItemActionRanged;
                if (r != null && r.MagazineItemNames != null && r.MagazineItemNames.Length > 0 && !string.IsNullOrEmpty(r.MagazineItemNames[0])) return r.MagazineItemNames[0];
            }
            return null;
        }

        /// <summary>The item name of an ItemsOnEnterGame entry ("name" or "name=count").</summary>
        private static string StackName(string entry)
        {
            int i = entry.IndexOf('=');
            return (i >= 0 ? entry.Substring(0, i) : entry).Trim();
        }

        /// <summary>A token's cvar value: FNV-1a, 23 bits plus one, so it is never 0 (unset) and is exact in a float.</summary>
        private static float Hash(string token)
        {
            uint h = 2166136261;
            foreach (char ch in token.ToLowerInvariant()) { h ^= ch; h *= 16777619; }
            return (h & 0x7FFFFF) + 1;
        }

        private static Weapon Find(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            foreach (Weapon w in Catalogue()) if (string.Equals(w.Token, token, StringComparison.OrdinalIgnoreCase)) return w;
            return null;
        }

        private static Weapon FromHash(float hash)
        {
            if (hash <= 0f) return null;
            foreach (Weapon w in Catalogue()) if (w.Hash == hash) return w;
            return null;
        }

        /// <summary>Does the catalogue carry the weapon (so it can be one of a pair)?</summary>
        public static bool Knows(string token) { return Find(token) != null; }

        /// <summary>
        /// The catalogue's weapons an NPC can be given at this gamestage tier: a weight above 0 at some band up to it (an NPC's
        /// band is capped at the gamestage tier - TierCap and ClassByGamestage). ScWeaponWarm loads their models ahead.
        /// </summary>
        public static List<string> RollableNow(int gsTier)
        {
            var list = new List<string>();
            foreach (Weapon w in Catalogue())
            {
                for (int b = 0; b <= gsTier; b++)
                {
                    if (w.Weight(b) > 0f) { list.Add(w.Token); break; }
                }
            }
            return list;
        }

        /// <summary>A weapon's strength band (Weapon.Level: the band it is likeliest at), or -1 when it is not carried.</summary>
        public static int LevelOf(string token)
        {
            Weapon w = Find(token);
            return w != null ? w.Level : -1;
        }

        /// <summary>Is the weapon ranged?</summary>
        public static bool IsRangedToken(string token)
        {
            Weapon w = Find(token);
            return w != null && w.Ranged;
        }

        // ---- resolution ----

        /// <summary>A class whose NPCs carry a pair: ScXnpc's managed classes, survivors and raiders.</summary>
        public static bool IsBodyClass(string cls) { return ScXnpc.IsManagedClass(cls); }

        /// <summary>
        /// PostInit PREFIX for the framework classes: resolves the pair of one of the community's 0-XNPCCore survivors or
        /// one of SC's raiders and rewrites itemsOnEnterGame to it. Idempotent.
        /// </summary>
        public static void BeforePostInit(EntityAlive e)
        {
            if (e == null || e is EntityPlayer) return;
            string cls = ScCommunity.ClassName(e);
            if (!IsBodyClass(cls)) return;
            Loadout pending = Pending;
            Pending = null;                                       // one entity only, used or not
            // a known survivor's ammo record travels with the pair (ScSpawn) and is acted on in the postfix (AfterPostInit);
            // a raider has no record, so whatever is pending is dropped here
            ScAmmo.TakePending(e, ScXnpc.IsSurvivorClass(cls) && ScNpc.IsV4(e));
            if (!ScConfig.Parsed) ScConfig.EnsureParsed();
            // not behind any switch: the class's own list (XNPC's pipe pistol, and a full 9 mm stack refilled on every
            // load) has to go
            if (!ScNpc.IsV4(e)) return;
            Loadout x = ResolveXnpc(e, cls, pending, ScXnpc.IsRaiderClass(cls));
            if (x != null) Rewrite(e, cls, XnpcOrder(x));
        }

        /// <summary>
        /// A body's pair: whatever of it the sources above give, and the missing kind(s) rolled at the class's tier cap.
        /// A raider also gets its lead (RaiderRangedShare by class tier), from the same seeded stream after the pair. Null
        /// (the class list stays) only when the catalogue has no weapon of a kind at all.
        /// </summary>
        private static Loadout ResolveXnpc(EntityAlive e, string cls, Loadout pending, bool raider)
        {
            Loadout l = null;
            string src = null;
            if (pending != null) { l = pending.Clone(); src = "spawn"; }
            if (l == null && !raider) { l = FromRecord(e, cls); if (l != null) src = "record"; }   // raiders have no record
            if (l == null) { l = FromCvars(e); if (l != null) src = "saved"; }
            if (l == null) l = new Loadout();
            Validate(l, raider);
            Random rnd = null;
            if (l.Ranged == null || l.Melee == null)
            {
                // one seeded stream: the same entity rolls the same pair on every attempt. Ranged then melee (unchanged
                // since M7 for the survivors); a raider then draws its lead from the same stream
                rnd = Seeded(e);
                int cap = TierCap(cls);
                if (l.Ranged == null) l.Ranged = RollRanged(raider, ScXnpc.TierOf(cls), cap, rnd);   // a T3 class: a firearm at 85% (round t3)
                if (l.Melee == null) l.Melee = Roll(raider, false, cap, rnd);
                src = src == null ? "rolled" : src + "+rolled";
            }
            if (raider && !l.HasRanged) l.Lead = Loadout.LeadMelee;          // nothing else to lead with
            if (raider && l.Lead == Loadout.LeadNone)
            {
                if (rnd == null) rnd = Seeded(e);
                l.Lead = rnd.NextDouble() < ScConfig.RaiderRangedShareFor(ScXnpc.TierOf(cls)) ? Loadout.LeadRanged : Loadout.LeadMelee;
            }
            if (l.Ranged == null || l.Melee == null)
            {
                ScLog.Once("loadout-xnpc-nopair", "no " + (l.Melee == null ? "melee" : "ranged") + " weapon in XnpcWeapons - " + cls + " keeps its class items");
                return null;
            }
            WriteCvars(e, l);
            sources[e.entityId] = src;
            return l;
        }

        /// <summary>
        /// The tier band a body of this class rolls its weapons and clothes at: its class's own (T1 = 0, T2 = 1, T3 = 2 - the
        /// place and time of day its spawn group stands for), so a T2 NPC never carries T0 gear (the user, 2026-10-09).
        /// Until then it was capped by the gamestage tier too; decision D4 (tiers by gamestage) now picks the class instead:
        /// a spawn group's class above the gamestage tier comes in as the same side and sex at that tier
        /// (Patch_ClassByGamestage), and the radio and outpost raids pick their class by gamestage already.
        /// </summary>
        public static int TierCap(string cls)
        {
            return ScXnpc.TierOf(cls);
        }

        /// <summary>The gamestage tier now: 0 below gamestage 50, 1 below 100, else 2 (the highest player's gamestage).</summary>
        public static int GamestageTier()
        {
            return ScLooks.MaxTierFor(GameStageNow());
        }

        private static int classDowngrades;

        /// <summary>How many group picks of ours came in at a lower tier this session (sc loadout sim prints it).</summary>
        public static int ClassDowngrades { get { return classDowngrades; } }

        private static string ClassNameOf(int classId)
        {
            EntityClass ec;
            return EntityClass.list.TryGetValue(classId, out ec) && ec != null ? ec.entityClassName : classId.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Decision D4 at spawn time: one of our tiered survivor or raider classes picked above the gamestage tier becomes
        /// the same side and sex's class of that tier (false: keep the pick - not ours, at or below the tier, switched off,
        /// or no class of that tier loaded).
        /// </summary>
        public static bool ClassByGamestage(int classId, out int to)
        {
            to = classId;
            if (!ScConfig.XnpcClassByGamestage || !ScXnpc.IsManagedClass(classId)) return false;
            EntityClass ec;
            if (!EntityClass.list.TryGetValue(classId, out ec) || ec == null || string.IsNullOrEmpty(ec.entityClassName)) return false;
            string name = ec.entityClassName;
            // only a tiered class (...T1..T3): one without a tier in its name (TierOf's 2) is no T3 to bring down
            int ti = name.LastIndexOf('T'), parsed;
            if (ti < 0 || !int.TryParse(name.Substring(ti + 1), out parsed)) return false;
            int tier = ScXnpc.TierOf(name), gs = GamestageTier();
            if (tier <= gs) return false;
            bool male = ScLooks.ClassIsMale(name);
            string want = ScXnpc.IsRaiderClass(classId) ? ScXnpc.RaiderClassFor(male, gs) : ScXnpc.ClassFor(male, gs);
            if (want == null || ScXnpc.TierOf(want) != gs) return false;      // the pickers fall back to any tier
            int id = EntityClass.FromString(want);
            if (id == classId || !ScCommunity.ClassExists(id)) return false;
            to = id;
            return true;
        }

        /// <summary>
        /// Every entity-group pick (biome spawns, sleeper volumes, SC's own group rolls) ends in
        /// EntityGroups.GetRandomFromGroupList: a pick of one of our tiered classes above the gamestage tier is swapped for
        /// its tier's class there (ClassByGamestage). Zombies and everything else pass through untouched (one set lookup).
        /// First, though, an infested room's pick may come feral (ScInfestations.FeralShare, I1): the check is one static
        /// read, null for every pick outside an infested room's spawn step. Then a biome spawn's NPC at a spot on an
        /// enclave's grounds is refused (E2, ScEnclavePeople.FilterWild): one static read outside a biome spawn.
        /// </summary>
        [HarmonyPatch]
        public static class Patch_ClassByGamestage
        {
            public static MethodBase Target()
            {
                return AccessTools.DeclaredMethod(typeof(EntityGroups), "GetRandomFromGroupList", new[] { typeof(List<SEntityClassAndProb>), typeof(GameRandom) });
            }

            [HarmonyPrepare]
            public static bool Prepare()
            {
                try
                {
                    if (Target() != null) return true;
                    ScLog.Err("EntityGroups.GetRandomFromGroupList(List<SEntityClassAndProb>, GameRandom) not found (game update?) - survivors and raiders spawn at their class tier whatever the gamestage");
                }
                catch (Exception e) { ScLog.Once("loadout-bind-groups", e); }
                return false;
            }

            [HarmonyTargetMethod]
            public static MethodBase TargetMethod() { return Target(); }

            [HarmonyPostfix]
            public static void Postfix(ref int __result, GameRandom random)
            {
                try
                {
                    if (ScInfestations.CurrentVolume != null) ScInfestations.FeralShare(ref __result, random);
                    // E2: a biome spawn's NPC on an enclave's grounds comes as "none" (one static read outside a biome spawn)
                    if (ScEnclavePeople.WildSpotNoted) ScEnclavePeople.FilterWild(ref __result);
                    int to;
                    if (!ClassByGamestage(__result, out to)) return;
                    classDowngrades++;
                    ScLog.Verbose("spawn: " + ClassNameOf(__result) + " comes in as " + ClassNameOf(to) + " (gamestage tier " + GamestageTier() + ")");
                    __result = to;
                }
                catch (Exception e)
                {
                    ScLog.Once("loadout-class-by-gamestage", e);
                }
            }
        }

        /// <summary>[melee, ranged]: XNPC's own weapon logic picks which of the two is in the hand (it re-arms a loaded gun
        /// on world entry, swaps to the melee one up close and when the gun runs dry), so the order only decides SCore's
        /// fallback weapon (the first entry, the one a new body holds) - and the melee weapon is the one fallback that can
        /// never be an empty gun. A melee-lead raider keeps it: ScArms holds off XNPC's re-arm for it.</summary>
        private static List<Weapon> XnpcOrder(Loadout l)
        {
            var order = new List<Weapon> { Find(l.Melee) };
            if (l.HasRanged) order.Add(Find(l.Ranged));                 // a melee-only NPC's list is its melee weapon alone
            return order;
        }

        /// <summary>The member or prospect record's pair: the registry is the truth for the community's own people.</summary>
        private static Loadout FromRecord(EntityAlive e, string cls)
        {
            Member m = ScRegistry.GetMember(e.entityId);
            if (m != null && m.Loadout != null && SameClass(m.Class, cls)) return m.Loadout.Clone();
            Prospect p = ScRegistry.GetProspect(e.entityId);
            if (p != null && p.Loadout != null && SameClass(p.Class, cls)) return p.Loadout.Clone();
            return null;
        }

        /// <summary>A record of no class, or of this one (an entity id can be reused by a stranger of another class).</summary>
        private static bool SameClass(string recorded, string cls)
        {
            return string.IsNullOrEmpty(recorded) || string.Equals(recorded, cls, StringComparison.OrdinalIgnoreCase);
        }

        private static Loadout FromCvars(EntityAlive e)
        {
            if (e.Buffs == null) return null;
            float rv = e.Buffs.GetCustomVar(RangedCvar);
            Weapon r = FromHash(rv);
            Weapon m = FromHash(e.Buffs.GetCustomVar(MeleeCvar));
            int lead = (int)e.Buffs.GetCustomVar(LeadCvar);
            string ranged = rv == NoRangedCvar ? Loadout.NoRanged : r != null ? r.Token : null;
            if (ranged == null && m == null) return null;
            return new Loadout(ranged, m != null ? m.Token : null) { Lead = lead == Loadout.LeadRanged || lead == Loadout.LeadMelee ? lead : Loadout.LeadNone };
        }

        /// <summary>Drops a token that is unknown or in the wrong slot (a removed weapon, a typo in a record). Only a raider has a lead.</summary>
        private static void Validate(Loadout l, bool raider)
        {
            if (!raider || (l.Lead != Loadout.LeadRanged && l.Lead != Loadout.LeadMelee)) l.Lead = Loadout.LeadNone;
            Weapon r = Find(l.Ranged), m = Find(l.Melee);
            l.Ranged = Loadout.IsNoRanged(l.Ranged) ? Loadout.NoRanged : r != null && r.Ranged ? r.Token : null;
            l.Melee = m != null && !m.Ranged ? m.Token : null;
        }

        private static void WriteCvars(EntityAlive e, Loadout l)
        {
            if (e.Buffs == null) return;
            Weapon r = Find(l.Ranged), m = Find(l.Melee);
            e.Buffs.SetCustomVar(RangedCvar, r != null ? r.Hash : Loadout.IsNoRanged(l.Ranged) ? NoRangedCvar : 0f, false);
            e.Buffs.SetCustomVar(MeleeCvar, m != null ? m.Hash : 0f, false);
            if (l.Lead != Loadout.LeadNone) e.Buffs.SetCustomVar(LeadCvar, l.Lead, false);
        }

        /// <summary>A raider that leads with its melee weapon (its lead cvar; false for everyone else).</summary>
        public static bool IsMeleeLead(EntityAlive e)
        {
            return e != null && e.Buffs != null && e.Buffs.GetCustomVar(LeadCvar) == Loadout.LeadMelee && ScXnpc.IsRaider(e);
        }

        /// <summary>The same numbers for the same entity on every load: its id and the world time it was created at.</summary>
        private static Random Seeded(EntityAlive e)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                h = (h ^ (uint)e.entityId) * 1099511628211UL;
                h = (h ^ e.WorldTimeBorn) * 1099511628211UL;
                return new Random((int)(h ^ (h >> 32)));
            }
        }

        /// <summary>The highest gamestage among the players (the host's own when there is one).</summary>
        internal static int GameStageNow()
        {
            try
            {
                EntityPlayerLocal p = ScCommunity.LocalPlayer;
                if (p != null) return p.gameStage;
                World w = ScCommunity.World;
                int best = 0;
                if (w != null && w.Players != null) foreach (EntityPlayer ep in w.Players.list) if (ep != null && ep.gameStage > best) best = ep.gameStage;
                return best;
            }
            catch (Exception) { return 0; }
        }

        /// <summary>The odds a rolled ranged weapon is a firearm: RaiderFirearmChance for a raider, SurvivorFirearmChance for a survivor.</summary>
        private static float FirearmChance(bool raider) { return raider ? ScConfig.RaiderFirearmChance : ScConfig.SurvivorFirearmChance; }

        /// <summary>The odds a new NPC carries only a melee weapon: RaiderMeleeOnlyChance for a raider, SurvivorMeleeOnlyChance for a survivor.</summary>
        private static float MeleeOnlyChance(bool raider) { return raider ? ScConfig.RaiderMeleeOnlyChance : ScConfig.SurvivorMeleeOnlyChance; }

        /// <summary>A T3 class's odds of carrying a firearm at all (round t3), -1 below T3 (classTier 0..2, ScXnpc.TierOf).</summary>
        private static float FirearmChanceT3(bool raider, int classTier)
        {
            if (classTier < 2) return -1f;
            return raider ? ScConfig.RaiderFirearmChanceT3 : ScConfig.SurvivorFirearmChanceT3;
        }

        /// <summary>
        /// The ranged weapon of a new NPC of this class tier, or Loadout.NoRanged (melee only). Below T3 as before: melee-only
        /// at MeleeOnlyChance, else a bow/crossbow or, at FirearmChance, a firearm. A T3 class (round t3, the user's ask
        /// of 2026-10-15: at gamestage 100 most of them carried bows) carries a firearm at FirearmChanceT3; the rest are
        /// melee-only or carry a bow or crossbow in the proportion the two chances give them (2.5% / 12.5% at the defaults).
        /// </summary>
        private static string RollRanged(bool raider, int classTier, int cap, Random rnd)
        {
            float t3 = FirearmChanceT3(raider, classTier);
            if (t3 < 0f) return rnd.NextDouble() < MeleeOnlyChance(raider) ? Loadout.NoRanged : Roll(raider, true, cap, rnd);
            if (rnd.NextDouble() < t3) return RollFamily("firearm", cap, rnd);
            float meleeOnly = MeleeOnlyChance(raider), bow = (1f - meleeOnly) * (1f - FirearmChance(raider));
            if (meleeOnly + bow <= 0f) return RollFamily("firearm", cap, rnd);   // configured: every ranged weapon a firearm
            return rnd.NextDouble() * (meleeOnly + bow) < meleeOnly ? Loadout.NoRanged : RollFamily("bow", cap, rnd);
        }

        /// <summary>
        /// A weapon of one kind for an NPC of this effective band (the tier cap): a melee weapon, or a bow/crossbow - a
        /// firearm when the side's firearm chance comes up. Picked by weight (Weapon.Weight: 1 at its tier and above, or
        /// its bands= weights), so IZY Classic's guns follow their own tier rule. When nothing of that kind weighs
        /// anything at this band, any of that kind; then the other ranged kind. Null if none is loaded.
        /// </summary>
        private static string Roll(bool raider, bool ranged, int cap, Random rnd)
        {
            string want = !ranged ? "melee" : (rnd.NextDouble() < FirearmChance(raider) ? "firearm" : "bow");
            return RollFamily(want, cap, rnd);
        }

        /// <summary>A weapon of this family (melee, bow, firearm) at the tier cap, as Roll; a ranged family falls back to the other.</summary>
        private static string RollFamily(string want, int cap, Random rnd)
        {
            string other = want == "melee" ? null : (want == "firearm" ? "bow" : "firearm");
            Weapon w = Pick(want, cap, rnd) ?? Pick(want, -1, rnd);
            if (w == null && other != null) w = Pick(other, cap, rnd) ?? Pick(other, -1, rnd);
            return w != null ? w.Token : null;
        }

        /// <summary>One weapon of this family by weight at this band (-1: any, all alike); null when none weighs anything.</summary>
        private static Weapon Pick(string family, int band, Random rnd)
        {
            List<Weapon> all = Catalogue();
            float total = 0f;
            foreach (Weapon w in all) if (w.Family == family) total += w.Weight(band);
            if (total <= 0f) return null;
            double r = rnd.NextDouble() * total;
            Weapon last = null;
            foreach (Weapon w in all)
            {
                if (w.Family != family) continue;
                float x = w.Weight(band);
                if (x <= 0f) continue;
                last = w;
                if (r < x) return w;
                r -= x;
            }
            return last;                // rounding at the very end
        }

        /// <summary>
        /// A 0-XNPCCore survivor's pair rolled before there is an entity (a radio contact, shown on the radio and handed
        /// to the body as Pending): what is fixed stays, the rest is rolled at this class's tier cap.
        /// </summary>
        public static Loadout RollXnpc(string cls, Loadout fix, GameRandom rnd)
        {
            var l = fix != null ? fix.Clone() : new Loadout();
            Validate(l, false);
            var r = new Random(rnd != null ? rnd.RandomInt : Environment.TickCount);
            int cap = TierCap(cls);
            if (l.Ranged == null) l.Ranged = RollRanged(false, ScXnpc.TierOf(cls), cap, r);
            if (l.Melee == null) l.Melee = Roll(false, false, cap, r);
            return l.HasRanged || l.Melee != null ? l : null;
        }

        /// <summary>Rewrites itemsOnEnterGame to the weapons' items, in order. Unchanged when it already is.</summary>
        private static bool Rewrite(EntityAlive e, string cls, List<Weapon> order)
        {
            if (order == null || order.Exists(x => x == null)) return false;
            var want = new List<string>();
            foreach (Weapon w in order)
                if (!want.Exists(x => string.Equals(StackName(x), StackName(w.Item), StringComparison.OrdinalIgnoreCase))) want.Add(w.Item);
            List<ItemStack> have = e.itemsOnEnterGame;
            if (have == null) return false;
            if (have.Count == want.Count)
            {
                bool same = true;
                for (int i = 0; i < want.Count && same; i++)
                    same = have[i] != null && have[i].itemValue != null && have[i].itemValue.ItemClass != null
                           && string.Equals(have[i].itemValue.ItemClass.GetItemName(), StackName(want[i]), StringComparison.OrdinalIgnoreCase);
                if (same) return false;
            }
            var stacks = new List<ItemStack>();
            foreach (string item in want)
            {
                ItemStack st = ItemStack.FromString(item);
                if (st == null || st.itemValue == null || st.itemValue.IsEmpty())
                {
                    ScLog.Once("loadout-item-" + item, "loadout: item " + item + " not found - " + cls + " keeps its class items");
                    return false;
                }
                stacks.Add(st);
            }
            have.Clear();
            have.AddRange(stacks);
            return true;
        }

        // ---- ammo (survivors: decision D3; raiders: M10-D2) ----

        /// <summary>
        /// PostInit POSTFIX: a body's starting ammo, once per body. Its gun's basic rounds go into the bag (ScAmmo.AddRounds:
        /// the store vanilla makes a bow's arrow from; V4 saves it with the NPC). The cvar is saved with the entity, so a
        /// reload never refills.
        /// A survivor gets XnpcSpawnAmmo (XnpcSpawnArrows for a bow or crossbow), but only a genuinely new one (a wild spawn,
        /// a radio contact's or a console spawn's body): a body the community re-creates for somebody it knows (ScSpawn - a
        /// call-back, a stand-in, a runner's fresh body) gets the rounds their record says they carry instead
        /// (ScAmmo.RestoreRecorded; the round 2 review's P1), so re-creating a member never makes ammo; the stockpile tops
        /// members up later (ScAmmo).
        /// A raider gets RaiderSpawnAmmo / RaiderSpawnArrows and nothing ever again (M10-D2): ScAmmo's resupply only walks
        /// members. Server only: the bag is the server's. A raider's kill XP bonus is written here too (SetRaiderXp).
        /// With Locked &amp; Loaded (M11, ArmMagazines) a gun that takes magazines gets its class tier's loaded magazines
        /// instead of either loose stack; and every body - new, re-created or loaded from the save - then has the loose
        /// rounds of its gun's ammo in its bag loaded into its magazines (ScAmmo.LoadLoose).
        /// </summary>
        public static void AfterPostInit(EntityAlive e)
        {
            try
            {
                if (e == null || !ScXnpc.IsBody(e) || !ScCommunity.IsHost || e.Buffs == null) return;
                // a runner coming back from its snapshot: ScRunners puts the snapshot's bag back right after this, so what
                // would go into the bag here waits for AfterRunnerRestore (the re-check's R1)
                bool runnerRestore = ScRunners.Restoring;
                // saved while bandaging: the item back into its inventory, the weapon back into the hand (re-check)
                ScMedic.FixLoadedHand(e);
                // the gun it held when it was saved (review F1, X13): before the window move and the arming, so their
                // guards see the real gun (each unloads a loose-loaded gun before magazines arrive)
                ScAmmo.RestoreHeldGun(e, runnerRestore);
                bool raider = ScXnpc.IsRaider(e);
                if (raider) SetRaiderXp(e);              // every PostInit, before the seeded raider returns below
                if (runnerRestore) return;               // the rest: AfterRunnerRestore, on the bag the runner keeps
                ScAmmo.MoveWindowAmmoToBag(e);           // a no-op without a window (raiders never have one)
                SeedAmmo(e, raider);
                ScAmmo.LoadLoose(e);                     // once at load (M11): loose rounds of a magazine gun into its magazines
            }
            catch (Exception ex)
            {
                ScLog.Once("loadout-seed-ammo", ex);
            }
        }

        /// <summary>
        /// ScRunners, right after it has put a re-created runner's snapshot bag back (the re-check's R1): the part of
        /// AfterPostInit that works on the bag runs now, on the bag the runner keeps - the saved gun's loose fallback, the
        /// window move (ammo and magazines), the starting ammo (a snapshot's body is seeded already) and the loose rounds
        /// into magazines. Nothing went into the bag that was thrown away, so nothing is lost or done twice.
        /// </summary>
        public static void AfterRunnerRestore(EntityAlive e)
        {
            try
            {
                if (e == null || !ScXnpc.IsBody(e) || !ScCommunity.IsHost || e.Buffs == null) return;
                ScAmmo.FlushDeferredLoose(e);
                ScAmmo.MoveWindowAmmoToBag(e);
                SeedAmmo(e, ScXnpc.IsRaider(e));
                ScAmmo.LoadLoose(e);
            }
            catch (Exception ex)
            {
                ScLog.Once("loadout-runner-restore", ex);
            }
        }

        /// <summary>AfterPostInit's starting ammo: the record for a re-created survivor, else once per body its magazines or its loose stack.</summary>
        private static void SeedAmmo(EntityAlive e, bool raider)
        {
            if (!raider && ScAmmo.RestoreRecorded(e)) return;
            if (e.Buffs.GetCustomVar(AmmoSeededCvar) > 0f) return;
            Loadout l = Current(e);
            Weapon r = l != null ? Find(l.Ranged) : null;
            if (r == null) return;
            ItemClass gun = ItemClass.GetItemClass(r.Item, true);
            if (ScMags.Active && ArmMagazines(e, gun, raider)) return;
            string ammo = AmmoOf(gun);
            bool bow = r.Family == "bow";
            int n = raider ? (bow ? ScConfig.RaiderSpawnArrows : ScConfig.RaiderSpawnAmmo) : (bow ? ScConfig.XnpcSpawnArrows : ScConfig.XnpcSpawnAmmo);
            if (ammo == null || n <= 0) return;
            ItemValue iv = ItemClass.GetItem(ammo, true);
            if (iv == null || iv.IsEmpty()) return;
            // marked only now: a body that had nothing to seed yet (no ranged weapon) is not marked for life
            e.Buffs.SetCustomVar(AmmoSeededCvar, 1f);
            ScAmmo.AddRounds(e, iv, n);    // the bag: the store a bow can shoot from (see ScAmmo.AddRounds)
            ScAmmo.NoteSeeded(raider);
            ScLog.Verbose("starting ammo for " + (raider ? "raider " : "") + ScCommunity.DisplayName(e) + " (id " + e.entityId + "): " + n + " " + ammo + " for " + r.Token);
        }

        /// <summary>
        /// M11, the user's decision 1: with Locked &amp; Loaded a new survivor's or raider's gun that takes magazines gets the
        /// tier's loaded magazines of its basic ammo (NpcMagazines.Arm; tier = the class's T1..T3) and no loose stack. False
        /// - the loose stack then - when L&amp;L hands out nothing: a bow or crossbow, a gun it has no magazines for, one with
        /// only twin magazines. Marked with the same AmmoSeededCvar as the stack, so a saved body is never armed again; a
        /// body that already reloads the gun by magazine is marked and gets nothing more.
        /// Runs here, inside PostInit, which is before 0-XNPCCore's first reload of the gun: that is its controller's
        /// preload ([SDCS-READY] event=preload), a tick of AvatarSdcsNpcController.Update once the body is in the world,
        /// and a body's PostInit runs inside EntityFactory before it is spawned. A gun that already holds rounds would be
        /// taken by L&amp;L for a found gun with a seated magazine - one magazine more than the tier - so its rounds go back
        /// to the bag first (ScAmmo.UnloadHeld; LoadLoose loads them afterwards). A guard rather than a path seen today: a
        /// new body holds its melee weapon here, SCore's copies of the gun are fresh items with no rounds, a body loaded from
        /// the save (its held gun put back by RestoreHeldGun) is marked already, and sc loadout's re-arm puts a fresh gun in
        /// the hand before it gets here.
        /// </summary>
        private static bool ArmMagazines(EntityAlive e, ItemClass gun, bool raider)
        {
            if (gun == null) return false;
            // the review's F3: a body that already reloads this gun by magazine (sc loadout setting the same gun again,
            // magazines handed over) is armed - no second tier set made from nothing
            if (ScMags.Carries(e, ScMags.GunValue(e, gun)))
            {
                e.Buffs.SetCustomVar(AmmoSeededCvar, 1f);
                return true;
            }
            ScAmmo.UnloadHeld(e, gun, "armed with magazines");
            int tier = ScMags.TierOf(e);
            int given = ScMags.Arm(e, gun.GetItemName(), tier);
            if (given <= 0) return false;
            e.Buffs.SetCustomVar(AmmoSeededCvar, 1f);
            ScAmmo.NoteArmed(raider, given);
            ScLog.Verbose("starting magazines for " + (raider ? "raider " : "") + ScCommunity.DisplayName(e) + " (id " + e.entityId + "): tier " + tier
                          + ", " + given + " round(s) for " + gun.GetItemName() + " (Locked & Loaded)");
            return true;
        }

        // ---- kill XP (raiders, design 2.9) ----

        /// <summary>
        /// The kill XP bonus a raider's pair earns (design 2.9), as the perc_add on its class's ExperienceGain (600): the
        /// old raiders paid 600 for a melee weapon and 800-1400 for a gun, so the ranged weapon decides it - a bow or
        /// crossbow +33% at tier 0, +67% above; a firearm +50% / +100% / +133% at tier 0 / 1 / 2 (900, 1200, 1398 XP; an IZY
        /// gun with bands= at the band it is likeliest at, Weapon.Level); no
        /// ranged weapon 0. The lead does not count: a melee-lead raider still takes its gun for a target out of reach.
        /// Fixed by the pair, which is fixed per raider (seeded, saved), so the same raider is always worth the same.
        /// </summary>
        private static float RaiderXpBonus(Weapon ranged)
        {
            if (ranged == null || !ranged.Ranged) return 0f;
            int level = ranged.Level;  // a banded IZY gun counts at the band it is likeliest at
            if (ranged.Family == "bow") return level <= 0 ? 0.33f : 0.67f;
            if (ranged.Family == "firearm") return level <= 0 ? 0.5f : level == 1 ? 1f : 1.33f;
            return 0f;
        }

        /// <summary>
        /// Writes a raider's kill XP bonus from its current pair (RaiderXpCvar; removed when it is 0). Host: the cvar is
        /// saved with the entity, and a client's copy gets it with the entity's spawn data (EntityAliveSDXV4.Write carries
        /// its buffs and cvars), which is what a bleed-out kill paid on that client reads.
        /// </summary>
        public static void SetRaiderXp(EntityAlive e)
        {
            if (e == null || e.Buffs == null) return;
            Loadout l = Current(e);
            float bonus = RaiderXpBonus(l != null ? Find(l.Ranged) : null);
            if (bonus > 0f) e.Buffs.SetCustomVar(RaiderXpCvar, bonus, false);
            else if (e.Buffs.HasCustomVar(RaiderXpCvar)) e.Buffs.RemoveCustomVar(RaiderXpCvar);
        }

        /// <summary>
        /// What killing this NPC pays the killer before party and difficulty scaling: its class's ExperienceGain through
        /// the ExperienceGain passive evaluated on it - exactly EntityPlayer.AddKillXP's first two lines. For sc raiders.
        /// </summary>
        public static int KillXp(EntityAlive e)
        {
            EntityClass ec;
            if (e == null || !EntityClass.list.TryGetValue(e.entityClass, out ec) || ec == null) return 0;
            ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
            return (int)EffectManager.GetValue(PassiveEffects.ExperienceGain, held, ec.ExperienceValue, e);
        }

        // ---- live NPCs (console; XNPC's melee choice) ----

        /// <summary>The pair a live NPC carries (null for anything that is not one of the managed survivor or raider classes).</summary>
        public static Loadout Current(EntityAlive e)
        {
            if (e == null) return null;
            string cls = ScCommunity.ClassName(e);
            if (!IsBodyClass(cls)) return null;
            Loadout l = FromCvars(e);
            if (l == null) return null;
            Validate(l, ScXnpc.IsRaiderClass(cls));
            return l;
        }

        /// <summary>
        /// The melee weapon of a managed body's pair while it can still hold it (SCore's FindWeapon: it is on its item
        /// list), else null. XNPC's own melee choice (X6) asks this, so its close-range swap and dry-gun failover always
        /// reach for the pair's melee weapon.
        /// </summary>
        public static string XnpcMelee(EntityAlive e)
        {
            EntityAliveSDXV4 v = e as EntityAliveSDXV4;
            if (v == null || !ScXnpc.IsBody(e)) return null;
            Loadout l = Current(e);
            if (l == null || l.Melee == null) return null;
            return v.FindWeapon(l.Melee) ? l.Melee : null;
        }

        /// <summary>
        /// Gives a live NPC a new pair (either half may be null: kept; a raider's change may carry a new Lead, which then
        /// puts that weapon in its hand). Refills its slots and re-equips the same kind. freshAmmo: a body given a new gun
        /// also gets a new starting stack - or its tier's magazines (M11) - for it (the console's sc loadout; a raider gets
        /// the raider amounts); false for a weapon the player handed over (ScArms: its ammo is what the player gives, or a
        /// resupply at home).
        /// </summary>
        public static bool Set(EntityAlive e, Loadout change, out string why, bool freshAmmo = true)
        {
            why = null;
            string cls = ScCommunity.ClassName(e);
            if (!IsBodyClass(cls)) { why = "not one of the community's 0-XNPCCore survivors or SC's raiders"; return false; }
            if (ScMedic.IsUsing(e.entityId)) { why = "busy using a medical item"; return false; }
            if (!ScNpc.IsV4(e)) { why = cls + " is not an EntityAliveSDXV4"; return false; }
            bool raider = ScXnpc.IsRaiderClass(cls);
            Loadout l = Current(e) ?? ResolveXnpc(e, cls, null, raider);
            if (l == null) { why = "no weapons loaded for " + cls; return false; }
            l = l.Clone();
            if (change.Ranged != null) l.Ranged = change.Ranged;
            if (change.Melee != null) l.Melee = change.Melee;
            if (raider && change.Lead != Loadout.LeadNone) l.Lead = change.Lead;
            Validate(l, raider);
            if (l.Ranged == null || l.Melee == null) { why = "a pair needs a melee weapon and a ranged one (or none: melee-only)"; return false; }
            if (raider && !l.HasRanged)
            {
                if (change.Lead == Loadout.LeadRanged) { why = "a melee-only raider has nothing to lead with but its melee weapon"; return false; }
                l.Lead = Loadout.LeadMelee;
            }
            List<Weapon> order = XnpcOrder(l);
            // a new lead is put in the hand at once (a test from the console); otherwise the same kind as before
            bool heldRanged = l.HasRanged && (raider && change.Lead != Loadout.LeadNone ? change.Lead == Loadout.LeadRanged : ScSkills.HoldsRanged(e));
            int before = e.itemsOnEnterGame.Count;
            WriteCvars(e, l);
            Remember(e, l);
            if (raider) SetRaiderXp(e);                          // a new ranged weapon changes what it is worth
            sources[e.entityId] = "console";
            if (Rewrite(e, cls, order)) Refill(e, before);
            Arm(e, heldRanged, out why);
            if (freshAmmo && change.HasRanged && ScXnpc.IsBody(e))
            {
                e.Buffs.RemoveCustomVar(AmmoSeededCvar);
                AfterPostInit(e);
            }
            return true;
        }

        /// <summary>
        /// A member's or prospect's new pair goes into its record too: the record outranks the cvars when the body is next
        /// built (FromRecord), and every body the community re-creates for them is armed from it (ScSpawn). Nothing for a
        /// raider or a stranger: neither has a record.
        /// </summary>
        private static void Remember(EntityAlive e, Loadout l)
        {
            bool changed = false;
            Member m = ScRegistry.GetMember(e.entityId);
            if (m != null) { m.Loadout = l.Clone(); changed = true; }
            Prospect p = ScRegistry.GetProspect(e.entityId);
            if (p != null) { p.Loadout = l.Clone(); changed = true; }
            if (changed && ScRegistry.Loaded) ScRegistry.SaveAtomic();
        }

        /// <summary>SCore's EntityAlive.PostInit fill again (slots = itemsOnEnterGame), and empties the slots the old list used beyond it.</summary>
        private static void Refill(EntityAlive e, int oldCount)
        {
            List<ItemStack> list = e.itemsOnEnterGame;
            for (int i = 0; i < list.Count; i++)
            {
                if (i == e.inventory.holdingItemIdx) continue;                 // Arm puts the hand item back
                ItemStack st = list[i].Clone();
                ItemClass ic = ItemClass.GetForId(st.itemValue.type);
                if (ic == null) continue;
                if (ic.HasQuality) st.itemValue = new ItemValue(st.itemValue.type, 1, 6, false, null, 1f);
                else st.count = ic.Stacknumber.Value;
                e.inventory.SetItem(i, st);
            }
            for (int i = list.Count; i < oldCount; i++)
                if (i != e.inventory.holdingItemIdx) e.inventory.SetItem(i, ItemStack.Empty.Clone());
        }

        /// <summary>
        /// Puts the ranged or melee weapon of the pair in the hand, through SCore's own UpdateWeapon(ItemValue) (it records
        /// the weapon for the save, and skips the network broadcast of the string overload). On an XNPC body - every managed
        /// body - CurrentWeaponClass is left alone: XNPC's controller writes it (0 melee, 1 ranged) on every change of the
        /// held item. The SetWeaponClass fallback is for a managed class configured onto some other body.
        /// </summary>
        public static bool Arm(EntityAlive e, bool ranged, out string why)
        {
            why = null;
            if (e == null || e.inventory == null) { why = "no inventory"; return false; }
            if (ScMedic.IsUsing(e.entityId)) { why = "busy using a medical item"; return false; }
            Loadout l = Current(e);
            Weapon w = l != null ? Find(l.Of(ranged)) : null;
            if (w == null) { why = "no " + (ranged ? "ranged" : "melee") + " weapon in the pair"; return false; }
            ItemValue iv = null;
            foreach (ItemStack st in e.itemsOnEnterGame)
                if (st != null && st.itemValue != null && st.itemValue.ItemClass != null
                    && string.Equals(st.itemValue.ItemClass.GetItemName(), StackName(w.Item), StringComparison.OrdinalIgnoreCase)) { iv = st.itemValue.Clone(); break; }
            if (iv == null) { why = w.Item + " is not on the NPC's item list"; return false; }
            if (!ScNpc.UpdateWeapon(e, iv, true)) ScMedic.Hold(e, e.inventory.holdingItemIdx, iv, 1);
            if (!ScXnpc.IsBody(e)) SetWeaponClass(e);
            return true;
        }

        /// <summary>XNPCCore's WeaponUser2 buff, done now: 0 melee, 1 shortRange, 2 medRange, 3 longRange (the last tag wins).</summary>
        private static void SetWeaponClass(EntityAlive e)
        {
            ItemValue iv = e.inventory.holdingItemItemValue;
            ItemClass ic = iv != null && !iv.IsEmpty() ? iv.ItemClass : null;
            if (ic == null || e.Buffs == null) return;
            int v = -1;
            if (ic.HasAnyTags(FastTags<TagGroup.Global>.Parse("melee"))) v = 0;
            if (ic.HasAnyTags(FastTags<TagGroup.Global>.Parse("shortRange"))) v = 1;
            if (ic.HasAnyTags(FastTags<TagGroup.Global>.Parse("medRange"))) v = 2;
            if (ic.HasAnyTags(FastTags<TagGroup.Global>.Parse("longRange"))) v = 3;
            if (v >= 0) e.Buffs.SetCustomVar("CurrentWeaponClass", v, false);
        }

        /// <summary>Is this token a firearm (a gun, not a bow)?</summary>
        public static bool IsFirearm(string token)
        {
            Weapon w = Find(token);
            return w != null && w.Group == "firearm";
        }

        /// <summary>
        /// The pair's weapons as the body carries them (the toolbelt copies SCore made from itemsOnEnterGame at PostInit,
        /// quality 1-6; the hand slot's live copy), for the death drop (M10-D6). Empty when there is no pair.
        /// </summary>
        public static List<ItemValue> CarriedPair(EntityAlive e)
        {
            var list = new List<ItemValue>();
            Loadout l = Current(e);
            if (l == null || e.inventory == null) return list;
            foreach (string token in new[] { l.Melee, l.Ranged })
            {
                Weapon w = Find(token);
                if (w == null) continue;
                ItemClass ic = ItemClass.GetItemClass(w.Item, true);
                if (ic == null) continue;
                ItemValue found = null;
#if SC_V34
                ItemStack[] slots = e.inventory.ItemGrid.items;      // 3.4: the toolbelt's grid (read only; 10 slots on our bodies, Patches_Npc)
#else
                ItemStack[] slots = e.inventory.GetSlots();
#endif
                for (int i = 0; slots != null && i < slots.Length && found == null; i++)
                    if (slots[i] != null && !slots[i].IsEmpty() && slots[i].itemValue.type == ic.Id) found = slots[i].itemValue.Clone();
                if (found != null) list.Add(found);
            }
            return list;
        }

        // ---- console names ----

        /// <summary>The catalogue for the console: each vanilla item with its short name.</summary>
        public static List<string> Tokens()
        {
            var list = new List<string>();
            foreach (Weapon w in Catalogue()) list.Add(w.Alias.Length > 0 ? w.Token + " (" + w.Alias + ")" : w.Token);
            return list;
        }

        /// <summary>
        /// A weapon by what the console was given: the item ("meleeWpnClubT0WoodenClub"), its short name ("Club"), a family
        /// word (melee, bow, firearm), or a part of a name. Several matches are picked from at random when rnd is given, else
        /// refused as ambiguous.
        /// </summary>
        public static string MatchToken(string text, GameRandom rnd, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(text)) { why = "no weapon given"; return null; }
            string a = text.Trim();
            List<Weapon> cat = Catalogue();
            foreach (Weapon w in cat) if (string.Equals(w.Token, a, StringComparison.OrdinalIgnoreCase)) return w.Token;
            foreach (Weapon w in cat) if (w.Alias.Length > 0 && string.Equals(w.Alias, a, StringComparison.OrdinalIgnoreCase)) return w.Token;
            var hits = new List<Weapon>();
            string fam = a.ToLowerInvariant() == "gun" ? "firearm" : a.ToLowerInvariant();
            if (fam == "melee" || fam == "bow" || fam == "firearm")
            {
                foreach (Weapon w in cat) if (w.Family == fam) hits.Add(w);
            }
            else
            {
                foreach (Weapon w in cat)
                    if (w.Token.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0 || (w.Alias.Length > 0 && w.Alias.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0)) hits.Add(w);
            }
            if (hits.Count == 0) { why = "no weapon '" + text + "' (sc weapons lists them)"; return null; }
            if (hits.Count == 1) return hits[0].Token;
            if (rnd != null) return hits[rnd.RandomRange(0, hits.Count)].Token;
            var names = new List<string>();
            foreach (Weapon w in hits) names.Add(w.Token);
            why = "'" + text + "' matches " + string.Join(", ", names.ToArray());
            return null;
        }

        // ---- diagnostics ----

        public static string Describe(EntityAlive e)
        {
            var sb = new StringBuilder();
            string cls = ScCommunity.ClassName(e);
            bool raider = ScXnpc.IsRaiderClass(cls);
            sb.Append("[SC] ").Append(ScCommunity.DisplayName(e)).Append(" id ").Append(e.entityId).Append(" (").Append(cls)
              .Append(IsBodyClass(cls) ? (raider ? ", raider" : ", survivor") : "").Append(")");
            Loadout l = Current(e);
            if (l == null)
                sb.Append(" carries no pair").Append(!IsBodyClass(cls) ? " (not one of the community's 0-XNPCCore survivors or SC's raiders)" : " (not resolved yet)");
            else
            {
                string src;
                sources.TryGetValue(e.entityId, out src);
                sb.Append(": ranged ").Append(Label(l.Ranged)).Append(", melee ").Append(Label(l.Melee))
                  .Append(" [").Append(src ?? "loaded").Append("]");
                if (raider) sb.Append(" lead ").Append(Loadout.LeadName(l.Lead));
                sb.Append(" tier ").Append(TierCap(cls)).Append(", starting ammo given ").Append(e.Buffs != null && e.Buffs.GetCustomVar(AmmoSeededCvar) > 0f);
            }
            ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
            sb.Append("\n[SC]    holding ").Append(held != null && !held.IsEmpty() && held.ItemClass != null ? held.ItemClass.GetItemName() : "nothing")
              .Append(" (slot ").Append(e.inventory != null ? e.inventory.holdingItemIdx : -1).Append(") CurrentWeaponClass=")
              .Append(e.Buffs != null ? e.Buffs.GetCustomVar("CurrentWeaponClass").ToString() : "?");
            if (ScNpc.IsFramework(e)) sb.Append(" saved weapon '").Append(ScNpc.CurrentWeapon(e)).Append("'");
            sb.Append("\n[SC]    item list:");
            foreach (ItemStack st in e.itemsOnEnterGame)
                sb.Append(' ').Append(st != null && st.itemValue != null && st.itemValue.ItemClass != null ? st.itemValue.ItemClass.GetItemName() : "?");
            return sb.ToString();
        }

        private static string Label(string token)
        {
            Weapon w = Find(token);
            if (w == null) return token ?? "-";
            return w.Token + " (" + w.Family + ", tier " + w.Tier
                   + (w.Bands != null ? ", weights " + string.Join("/", Array.ConvertAll(w.Bands, x => x.ToString("0.##", CultureInfo.InvariantCulture)))
                                        + (w.Pool.Length > 0 ? " for group " + w.Pool : "") : "") + ")";
        }

        /// <summary>
        /// sc loadout sim: n rolls without making anyone, as the spawns would roll them now. Without a class tier: survivors
        /// as a T3 class spawned now (the gamestage picks the class: Patch_ClassByGamestage), raiders spread evenly over
        /// T1..T3 and each brought down to the gamestage tier the same way; anyStage ignores the gamestage. classTier 0..2
        /// (T1..T3) rolls that class as it is (a console spawn of it). Prints the melee-only share, the firearm share of
        /// the ranged weapons, a raider's lead, and each family's weapons by the tier in their item name (T0..T4).
        /// </summary>
        public static string Simulate(int n, bool raider, int classTier = -1, bool anyStage = false)
        {
            var rnd = new Random(Environment.TickCount);
            int gs = anyStage ? 2 : GamestageTier(), fire = 0, noPair = 0, armed = 0, meleeOnly = 0, ranged = 0, izy = 0;
            var byName = new Dictionary<string, int[]> { { "melee", new int[5] }, { "bow", new int[5] }, { "firearm", new int[5] } };
            var rangedLead = new int[3];
            var perTier = new int[3];
            if (Catalogue().Count == 0) return "[SC] no weapons in XnpcWeapons (Config/blocks.xml)";
            for (int i = 0; i < n; i++)
            {
                int t = classTier >= 0 ? classTier : raider ? i % 3 : 2;
                int band = classTier >= 0 ? classTier : Math.Min(t, gs);
                // the class the spawn ends up as is the band (Patch_ClassByGamestage brings a T3 down to it), so the T3 rule follows it
                string rolled = RollRanged(raider, band, band, rnd);
                bool noGun = rolled == Loadout.NoRanged;
                Weapon rw = noGun ? null : Find(rolled);
                Weapon mw = Find(Roll(raider, false, band, rnd));
                if (mw == null || (!noGun && rw == null)) { noPair++; continue; }
                armed++;
                if (raider)
                {
                    perTier[band]++;
                    if (!noGun && rnd.NextDouble() < ScConfig.RaiderRangedShareFor(band)) rangedLead[band]++;
                }
                Count(byName, mw);
                if (noGun) { meleeOnly++; continue; }
                ranged++;
                Count(byName, rw);
                if (rw.Group == "firearm") fire++;
                if (rw.Token.StartsWith("IZY", StringComparison.Ordinal)) izy++;
            }
            var sb = new StringBuilder();
            sb.Append("[SC] ").Append(n).Append(raider ? " raider" : " 0-XNPCCore survivor").Append(" spawns (")
              .Append(classTier >= 0 ? "class T" + (classTier + 1) : (anyStage ? "any gamestage" : "gamestage tier " + gs)).Append("): ")
              .Append(meleeOnly).Append(" melee-only (").Append((100.0 * meleeOnly / Math.Max(1, armed)).ToString("0.0")).Append("%, configured ")
              .Append((100f * MeleeOnlyChance(raider)).ToString("0.0")).Append("%), ")
              .Append(fire).Append(" of ").Append(ranged).Append(" ranged weapons are firearms (").Append((100.0 * fire / Math.Max(1, ranged)).ToString("0.0"))
              .Append("%, configured ").Append((100f * FirearmChance(raider)).ToString("0.0")).Append("% below T3; a T3 class carries one at ")
              .Append((100f * (raider ? ScConfig.RaiderFirearmChanceT3 : ScConfig.SurvivorFirearmChanceT3)).ToString("0")).Append("% all told); ")
              .Append((100.0 * fire / Math.Max(1, armed)).ToString("0.0")).Append("% of all of them carry a firearm");
            if (raider)
            {
                sb.Append(", ranged-lead T1/T2/T3: ");
                for (int k = 0; k < 3; k++)
                    sb.Append(k > 0 ? "/" : "").Append(perTier[k] > 0 ? (100.0 * rangedLead[k] / perTier[k]).ToString("0") + "%" : "-");
                sb.Append(" (configured ");
                for (int k = 0; k < 3; k++) sb.Append(k > 0 ? "/" : "").Append((100f * ScConfig.RaiderRangedShareFor(k)).ToString("0")).Append('%');
                sb.Append(" of those with a ranged weapon; the rest melee-lead)");
            }
            sb.Append(". By the tier in the name, T0/T1/T2/T3/T4:");
            foreach (var kv in byName)
            {
                sb.Append(' ').Append(kv.Key).Append(' ');
                for (int k = 0; k < 5; k++) sb.Append(k > 0 ? "/" : "").Append(kv.Value[k]);
                sb.Append(';');
            }
            sb.Append(" IZY's own ").Append(izy);
            if (noPair > 0) sb.Append(", ").Append(noPair).Append(" without a pair");
            if (ClassDowngrades > 0) sb.Append(". This session ").Append(ClassDowngrades).Append(" spawn(s) came in at the gamestage tier");
            return sb.ToString();
        }

        private static void Count(Dictionary<string, int[]> byName, Weapon w)
        {
            int[] row;
            int nt = NameTier(w.Token);
            if (nt >= 0 && byName.TryGetValue(w.Family, out row)) row[Math.Min(4, nt)]++;
        }

        /// <summary>The tier in a weapon's item name - "IZYgunT3ARAKM" 3, "gunMGT1AK47" 1, "IZYgunTminus1..." 0 - or -1.</summary>
        private static int NameTier(string item)
        {
            if (string.IsNullOrEmpty(item)) return -1;
            for (int i = 1; i + 1 < item.Length; i++)
            {
                if (item[i] != 'T' || !char.IsLetter(item[i - 1])) continue;
                if (char.IsDigit(item[i + 1])) return item[i + 1] - '0';
                if (string.CompareOrdinal(item, i + 1, "minus1", 0, 6) == 0) return 0;
            }
            return -1;
        }
    }
}
