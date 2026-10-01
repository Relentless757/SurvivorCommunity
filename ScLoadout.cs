using System;
using System.Collections.Generic;
using System.Text;

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

        public string Ranged;
        public string Melee;
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
    /// SurvivorFirearmChance (RaiderFirearmChance for a raider) - each at or below the tier cap, which is the lower of the
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
    /// The pair resolves, in order: Pending (a console spawn, a radio contact, a respawn of a known survivor), the member
    /// or prospect record (loadout=; survivors only), the pair this entity already carries (two cvars, saved with it), a
    /// roll seeded by (entityId, WorldTimeBorn). The result goes into the cvars at once, so a later gamestage never changes
    /// a saved NPC's weapons. Wild NPCs that are never saved re-roll as new NPCs.
    /// </summary>
    public static class ScLoadout
    {
        /// <summary>The pair this entity carries, as item-name hashes (exact in a float; 0 = none). Saved with the entity.</summary>
        public const string RangedCvar = "sc_loadout_r";
        public const string MeleeCvar = "sc_loadout_m";
        /// <summary>A raider's lead (Loadout.LeadRanged 1 / LeadMelee 2; never 0, which a save drops). Saved with the entity.</summary>
        public const string LeadCvar = "sc_raiderLead";
        /// <summary>Set once a body has been given its starting ammo (value 1: a cvar of 0 is not saved).</summary>
        public const string AmmoSeededCvar = "sc_ammoSeeded";

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
            public string Item { get { return Token; } }
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
                    foreach (XnpcWeaponDef d in ScConfig.XnpcWeapons) AddXnpc(byToken, d.Item, d.Family, d.Tier, d.Alias);
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
                            AddXnpc(byToken, item, FamilyOfItem(item), tier, "");
                        }
                    }
                }
            }
            catch (Exception e) { ScLog.Once("loadout-xnpc-catalogue", e); }
            List<Weapon> sorted = Sorted(byToken);
            if (sorted.Count == 0) ScLog.Once("loadout-xnpc-empty", "no weapons for 0-XNPCCore's survivors and SC's raiders - they keep their class items (XnpcWeapons in Config/blocks.xml)");
            return sorted;
        }

        /// <summary>One catalogue entry, when the item exists and a ranged one names its ammo (XNPC reloads and fails over by it).</summary>
        private static void AddXnpc(Dictionary<string, Weapon> byToken, string item, string family, int tier, string alias)
        {
            if (string.IsNullOrEmpty(item) || string.IsNullOrEmpty(family) || byToken.ContainsKey(item)) return;
            ItemClass ic = ItemClass.GetItemClass(item, true);
            if (ic == null) { ScLog.Once("loadout-xnpc-item-" + item, "XnpcWeapons: no item " + item + " - left out"); return; }
            bool ranged = family != "melee";
            if (ranged && AmmoOf(ic) == null) { ScLog.Once("loadout-xnpc-ammo-" + item, "XnpcWeapons: " + item + " has no ranged action with ammo - left out"); return; }
            string name = ic.GetItemName();
            byToken[name] = new Weapon { Token = name, Family = family, Ranged = ranged, Tier = tier, Hash = Hash(name), Alias = alias ?? "" };
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
        /// one of SC's raiders and rewrites itemsOnEnterGame to it. Idempotent. (Also still called by the old raider shim's
        /// EntityNPCBandit hook, where no class is managed any more and it returns at once.)
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
                if (l.Ranged == null) l.Ranged = Roll(raider, true, cap, rnd);
                if (l.Melee == null) l.Melee = Roll(raider, false, cap, rnd);
                src = src == null ? "rolled" : src + "+rolled";
            }
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
        /// The highest weapon tier a body of this class rolls: the lower of the class's band (T1 early, T2 middle, T3 late
        /// - the place and time of day its spawn group stands for) and the gamestage tier (decision D4: tiers by
        /// gamestage). Tier 0 holds the pipe guns and the primitive bow, so primitive ranged weapons are there from day 1.
        /// </summary>
        public static int TierCap(string cls)
        {
            return Math.Min(ScXnpc.TierOf(cls), ScLooks.MaxTierFor(GameStageNow()));
        }

        /// <summary>[melee, ranged]: XNPC's own weapon logic picks which of the two is in the hand (it re-arms a loaded gun
        /// on world entry, swaps to the melee one up close and when the gun runs dry), so the order only decides SCore's
        /// fallback weapon (the first entry, the one a new body holds) - and the melee weapon is the one fallback that can
        /// never be an empty gun. A melee-lead raider keeps it: ScArms holds off XNPC's re-arm for it.</summary>
        private static List<Weapon> XnpcOrder(Loadout l)
        {
            return new List<Weapon> { Find(l.Melee), Find(l.Ranged) };
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
            Weapon r = FromHash(e.Buffs.GetCustomVar(RangedCvar));
            Weapon m = FromHash(e.Buffs.GetCustomVar(MeleeCvar));
            int lead = (int)e.Buffs.GetCustomVar(LeadCvar);
            if (r == null && m == null) return null;
            return new Loadout(r != null ? r.Token : null, m != null ? m.Token : null) { Lead = lead == Loadout.LeadRanged || lead == Loadout.LeadMelee ? lead : Loadout.LeadNone };
        }

        /// <summary>Drops a token that is unknown or in the wrong slot (a removed weapon, a typo in a record). Only a raider has a lead.</summary>
        private static void Validate(Loadout l, bool raider)
        {
            if (!raider || (l.Lead != Loadout.LeadRanged && l.Lead != Loadout.LeadMelee)) l.Lead = Loadout.LeadNone;
            Weapon r = Find(l.Ranged), m = Find(l.Melee);
            l.Ranged = r != null && r.Ranged ? r.Token : null;
            l.Melee = m != null && !m.Ranged ? m.Token : null;
        }

        private static void WriteCvars(EntityAlive e, Loadout l)
        {
            if (e.Buffs == null) return;
            Weapon r = Find(l.Ranged), m = Find(l.Melee);
            e.Buffs.SetCustomVar(RangedCvar, r != null ? r.Hash : 0f, false);
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
        private static int GameStageNow()
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

        /// <summary>
        /// A weapon of one kind at or below the tier cap: a melee weapon, or a bow/crossbow - a firearm when the side's
        /// firearm chance comes up. Null if none is loaded.
        /// </summary>
        private static string Roll(bool raider, bool ranged, int cap, Random rnd)
        {
            string want = !ranged ? "melee" : (rnd.NextDouble() < FirearmChance(raider) ? "firearm" : "bow");
            string other = !ranged ? null : (want == "firearm" ? "bow" : "firearm");
            List<Weapon> pool = Rollable(want, cap);
            if (pool.Count == 0) pool = Rollable(want, int.MaxValue);
            if (pool.Count == 0 && other != null) pool = Rollable(other, cap);
            if (pool.Count == 0 && other != null) pool = Rollable(other, int.MaxValue);
            return pool.Count > 0 ? pool[rnd.Next(pool.Count)].Token : null;
        }

        private static List<Weapon> Rollable(string family, int cap)
        {
            var list = new List<Weapon>();
            foreach (Weapon w in Catalogue())
                if (w.Family == family && w.Tier <= cap) list.Add(w);
            return list;
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
            if (l.Ranged == null) l.Ranged = Roll(false, true, cap, r);
            if (l.Melee == null) l.Melee = Roll(false, false, cap, r);
            return l.Ranged != null || l.Melee != null ? l : null;
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
        /// members. Server only: the bag is the server's.
        /// </summary>
        public static void AfterPostInit(EntityAlive e)
        {
            try
            {
                if (e == null || !ScXnpc.IsBody(e) || !ScCommunity.IsHost || e.Buffs == null) return;
                ScAmmo.MoveWindowAmmoToBag(e);           // a no-op without a window (raiders never have one)
                bool raider = ScXnpc.IsRaider(e);
                if (!raider && ScAmmo.RestoreRecorded(e)) return;
                if (e.Buffs.GetCustomVar(AmmoSeededCvar) > 0f) return;
                Loadout l = Current(e);
                Weapon r = l != null ? Find(l.Ranged) : null;
                if (r == null) return;
                string ammo = AmmoOf(ItemClass.GetItemClass(r.Item, true));
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
            catch (Exception ex)
            {
                ScLog.Once("loadout-seed-ammo", ex);
            }
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
        /// also gets a new starting stack for it (the console's sc loadout; a raider gets the raider amounts); false for a
        /// weapon the player handed over (ScArms: its ammo is what the player gives, or a resupply at home).
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
            if (l.Ranged == null || l.Melee == null) { why = "a pair needs one ranged and one melee weapon"; return false; }
            List<Weapon> order = XnpcOrder(l);
            // a new lead is put in the hand at once (a test from the console); otherwise the same kind as before
            bool heldRanged = raider && change.Lead != Loadout.LeadNone ? change.Lead == Loadout.LeadRanged : ScSkills.HoldsRanged(e);
            int before = e.itemsOnEnterGame.Count;
            WriteCvars(e, l);
            Remember(e, l);
            sources[e.entityId] = "console";
            if (Rewrite(e, cls, order)) Refill(e, before);
            Arm(e, heldRanged, out why);
            if (freshAmmo && change.Ranged != null && ScXnpc.IsBody(e))
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
                ItemStack[] slots = e.inventory.GetSlots();
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
                sb.Append(" tier cap now ").Append(TierCap(cls)).Append(", starting ammo given ").Append(e.Buffs != null && e.Buffs.GetCustomVar(AmmoSeededCvar) > 0f);
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
            return w == null ? (token ?? "-") : w.Token + " (" + w.Family + ", tier " + w.Tier + ")";
        }

        /// <summary>
        /// sc loadout sim: n rolls without making anyone. Survivors: a 0-XNPCCore survivor's pair at today's gamestage tier
        /// cap (a late-band class, so only the gamestage caps it). Raiders: the class tier spread evenly over T1..T3, each
        /// capped by the lower of its tier and today's gamestage tier, with the raiders' firearm odds and their lead.
        /// </summary>
        public static string Simulate(int n, bool raider)
        {
            var rnd = new Random(Environment.TickCount);
            int cap = ScLooks.MaxTierFor(GameStageNow()), fire = 0, noPair = 0, armed = 0;
            var fams = new Dictionary<string, int>();
            var tiers = new int[3];
            var rangedLead = new int[3];
            var perTier = new int[3];
            if (Catalogue().Count == 0) return "[SC] no weapons in XnpcWeapons (Config/blocks.xml)";
            for (int i = 0; i < n; i++)
            {
                Weapon rw;
                if (raider)
                {
                    int t = i % 3;
                    perTier[t]++;
                    int tcap = Math.Min(t, cap);
                    rw = Find(Roll(true, true, tcap, rnd));
                    Weapon mw = Find(Roll(true, false, tcap, rnd));
                    if (rw == null || mw == null) { noPair++; continue; }
                    if (rnd.NextDouble() < ScConfig.RaiderRangedShareFor(t)) rangedLead[t]++;
                }
                else
                {
                    rw = Find(Roll(false, true, cap, rnd));
                    Weapon mw = Find(Roll(false, false, cap, rnd));
                    if (rw == null || mw == null) { noPair++; continue; }
                }
                armed++;
                tiers[Math.Max(0, Math.Min(2, rw.Tier))]++;
                if (rw.Group == "firearm") fire++;
                int c;
                fams.TryGetValue(rw.Family, out c);
                fams[rw.Family] = c + 1;
            }
            float chance = FirearmChance(raider);
            var sb = new StringBuilder();
            sb.Append("[SC] ").Append(n).Append(raider ? " raider" : " 0-XNPCCore survivor").Append(" spawns (gamestage tier cap ").Append(cap).Append("): ")
              .Append(fire).Append(" carry a firearm (").Append((100.0 * fire / Math.Max(1, armed)).ToString("0.0")).Append("%, configured ")
              .Append((100f * chance).ToString("0.0")).Append("%)");
            if (raider)
            {
                // every raider carries both; the lead says which one it relies on
                sb.Append(", ranged-lead T1/T2/T3: ");
                for (int t = 0; t < 3; t++)
                    sb.Append(t > 0 ? "/" : "").Append((100.0 * rangedLead[t] / Math.Max(1, perTier[t])).ToString("0")).Append('%');
                sb.Append(" (configured ");
                for (int t = 0; t < 3; t++) sb.Append(t > 0 ? "/" : "").Append((100f * ScConfig.RaiderRangedShareFor(t)).ToString("0")).Append('%');
                sb.Append("; the rest melee-lead)");
            }
            sb.Append(", ranged weapon tiers 0/1/2: ").Append(tiers[0]).Append('/').Append(tiers[1]).Append('/').Append(tiers[2]);
            sb.Append(", ranged weapons by family:");
            foreach (var kv in fams) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
            if (noPair > 0) sb.Append(", ").Append(noPair).Append(" without a pair");
            return sb.ToString();
        }
    }
}
