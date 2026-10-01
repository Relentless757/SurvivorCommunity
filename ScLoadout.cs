using System;
using System.Collections.Generic;
using System.Text;

namespace SurvivorCommunity
{
    /// <summary>
    /// An NPC's two weapons, one ranged and one melee: generated weapon tokens (ScWeapon) for a raider, vanilla item
    /// names for one of the community's 0-XNPCCore survivors.
    /// </summary>
    public class Loadout
    {
        public string Ranged;
        public string Melee;

        public Loadout() { }
        public Loadout(string ranged, string melee) { Ranged = ranged; Melee = melee; }

        public Loadout Clone() { return new Loadout(Ranged, Melee); }

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

        public override string ToString() { return (Ranged ?? "-") + " + " + (Melee ?? "-"); }
    }

    /// <summary>
    /// Two weapons, one ranged and one melee, for every NPC wearing the player's body: SC's raiders and the community's
    /// 0-XNPCCore survivors. Both are made legal to hold by rewriting the entity's own itemsOnEnterGame list in a PostInit
    /// PREFIX - before SCore's EntityAlive.PostInit postfix copies it into the inventory slots, before SetupStartingItems
    /// and before PostInit re-equips the saved weapon. SCore's FindWeapon accepts anything on that list, so UpdateWeapon
    /// can swap between the two and a reload brings back whichever was held.
    ///
    /// Raiders (weapons task W2, decision D1: unchanged). The class weapon stays the main one; the other kind is rolled
    /// per entity from the weapons SC's own raider classes carry: a melee class rolls a bow or crossbow (a firearm at
    /// SurvivorFirearmChance), a ranged class a melee weapon. The list becomes [main + ammo, second + ammo] (NPC items with
    /// their NPC ammo), and the roll is capped by the higher of the class weapon's tier and the gamestage tier.
    ///
    /// The community's 0-XNPCCore survivors (migration M7, decision D4). They use XNPC's weapon system: real vanilla player
    /// weapons with finite ammo, the melee swap up close and the dry-gun failover. Their classes carry no weapon of ours,
    /// so both kinds are rolled from the XnpcWeapons catalogue (Config/blocks.xml, written by Tools/gen_arsenal.py from the
    /// same table as the raiders' weapons): a melee weapon, and a bow or crossbow - a firearm at SurvivorFirearmChance -
    /// each at or below the tier cap, which is the lower of the class's band (T1..T3) and the gamestage tier, so the
    /// primitive weapons are there from day 1 and better ones come with the gamestage. The list becomes [melee, ranged],
    /// weapons only: SCore's PostInit postfix refills every non-quality item on the list to a full stack on every load,
    /// so ammo on it would be endless. Their ammo is a finite stack of the gun's basic rounds put into the inventory
    /// window once, when a new survivor is first created (AfterPostInit); SCore reloads an NPC's gun from that window,
    /// and the stockpile tops it up only in the base or at an outpost (ScAmmo, decision D3).
    ///
    /// The pair resolves, in order: Pending (a console spawn, a radio contact, a respawn of a known survivor), the member
    /// or prospect record (loadout=), the pair this entity already carries (two cvars, saved with it), a roll seeded by
    /// (entityId, WorldTimeBorn). The result goes into the cvars at once, so a later gamestage never changes a saved NPC's
    /// weapons. Wild NPCs that are never saved re-roll as new NPCs.
    /// </summary>
    public static class ScLoadout
    {
        /// <summary>The pair this entity carries, as token hashes (exact in a float; 0 = none). Saved with the entity.</summary>
        public const string RangedCvar = "sc_loadout_r";
        public const string MeleeCvar = "sc_loadout_m";
        /// <summary>Set once a 0-XNPCCore survivor has been given its starting ammo (value 1: a cvar of 0 is not saved).</summary>
        public const string AmmoSeededCvar = "sc_ammoSeeded";

        /// <summary>Consumed by the next player-body entity that runs PostInit (set right before EntityFactory.CreateEntity).</summary>
        public static Loadout Pending;

        private class Weapon
        {
            public string Token;
            public string Family;          // melee, unarmed, bow, firearm, launcher
            public bool Ranged;
            public int Tier;
            public string Class;           // raiders: a leaf class that carries it, whose ItemsOnEnterGame lists its items
            public List<string> Items;     // the items it puts on the list: the weapon first, then a raider's NPC ammo
            public float Hash;
            public string Alias = "";      // 0-XNPCCore survivors: the generator's short weapon token, for the console
            public string Item { get { return Items.Count > 0 ? Items[0] : ""; } }
            /// <summary>Rolled as a weapon of its kind: not fists, not the rocket launcher (its attack cadence is its class's).</summary>
            public bool Rollable { get { return Family != "unarmed" && Family != "launcher"; } }
            /// <summary>The share it counts towards: melee (fists too), bow, or firearm (the launcher too).</summary>
            public string Group { get { return Family == "unarmed" ? "melee" : Family == "launcher" ? "firearm" : Family; } }
        }

        // per side (raiders ride SC's templates, the 0-XNPCCore survivors carry vanilla weapons), sorted by token so a
        // seeded roll is stable. Every "raider" flag below picks the side: false is the 0-XNPCCore survivors.
        private static List<Weapon> raiderWeapons, xnpcWeapons;
        private static readonly Dictionary<int, string> sources = new Dictionary<int, string>();

        public static void Reset()
        {
            Pending = null;
            raiderWeapons = null;
            xnpcWeapons = null;
            sources.Clear();
        }

        // ---- catalogue ----

        private static List<Weapon> Catalogue(bool raider)
        {
            if (raider) { if (raiderWeapons == null) raiderWeapons = Build(ScLooks.RaiderClasses()); return raiderWeapons; }
            if (xnpcWeapons == null) xnpcWeapons = BuildXnpc();
            return xnpcWeapons;
        }

        private static List<Weapon> Build(List<string> classes)
        {
            var byToken = new Dictionary<string, Weapon>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string mode = GameModeName();
                foreach (string c in classes)
                {
                    string token = ScLooks.WeaponOf(c);
                    if (token.Length == 0 || byToken.ContainsKey(token)) continue;
                    EntityClass ec;
                    if (!EntityClass.list.TryGetValue(EntityClass.FromString(c), out ec) || ec == null || ec.Properties == null) continue;
                    var w = new Weapon { Token = token, Class = c, Tier = ScLooks.TierOf(c), Items = ItemsOf(ec, mode) };
                    if (w.Items.Count == 0) continue;
                    string kind, fam;
                    ec.Properties.Values.TryGetValue("ScWeaponKind", out kind);
                    ec.Properties.Values.TryGetValue("ScWeaponFamily", out fam);
                    // an older entityclasses.xml has neither: fall back on the item's own tags
                    w.Ranged = kind != null ? kind == "ranged" : IsRangedItem(w.Item);
                    w.Family = !string.IsNullOrEmpty(fam) ? fam : (w.Ranged ? (token == "RocketL" ? "launcher" : "firearm") : (token == "EmptyHand" ? "unarmed" : "melee"));
                    w.Hash = Hash(token);
                    byToken[token] = w;
                }
            }
            catch (Exception e) { ScLog.Once("loadout-catalogue", e); }
            return Sorted(byToken);
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
        /// The 0-XNPCCore survivors' weapons: the config's XnpcWeapons (vanilla item, family, tier). Without that block -
        /// an old blocks.xml - the six classes' own SdcsWeapons lists stand in, each item at the tier of the lowest class
        /// that lists it (T1 = 0), so the survivors are still armed, if by XNPC's tiering rather than SC's.
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
            if (sorted.Count == 0) ScLog.Once("loadout-xnpc-empty", "no weapons for 0-XNPCCore's survivors - they keep their class items (XnpcWeapons in Config/blocks.xml)");
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
            byToken[name] = new Weapon { Token = name, Family = family, Ranged = ranged, Tier = tier, Items = new List<string> { name }, Hash = Hash(name), Alias = alias ?? "" };
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

        private static string GameModeName()
        {
            try
            {
                GameMode gm = GameMode.GetGameModeForId(GameStats.GetInt(EnumGameStats.GameModeId));
                if (gm != null) return gm.GetTypeName();
            }
            catch (Exception) { }
            return "GameModeSurvival";
        }

        /// <summary>A class's ItemsOnEnterGame item names for this game mode - the list vanilla gives the entity (EntityAlive.CopyPropertiesFromEntityClass).</summary>
        private static List<string> ItemsOf(EntityClass ec, string mode)
        {
            var list = new List<string>();
            DynamicProperties dp;
            if (!ec.Properties.Classes.TryGetValue(EntityClass.PropItemsOnEnterGame, out dp) || dp == null) return list;
            string s;
            if (!dp.Values.TryGetValue(mode, out s) || string.IsNullOrEmpty(s)) dp.Values.TryGetValue("GameModeSurvival", out s);
            if (string.IsNullOrEmpty(s)) return list;
            foreach (string part in s.Split(','))
            {
                string t = part.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        private static bool IsRangedItem(string item)
        {
            ItemClass ic = ItemClass.GetItemClass(StackName(item), true);
            if (ic == null) return false;
            if (ic.HasAnyTags(FastTags<TagGroup.Global>.Parse("ranged"))) return true;
            if (ic.Actions != null) foreach (ItemAction a in ic.Actions) if (a is ItemActionRanged) return true;   // NPC guns shoot on Action1
            return false;
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

        private static Weapon Find(bool raider, string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            foreach (Weapon w in Catalogue(raider)) if (string.Equals(w.Token, token, StringComparison.OrdinalIgnoreCase)) return w;
            return null;
        }

        private static Weapon FromHash(bool raider, float hash)
        {
            if (hash <= 0f) return null;
            foreach (Weapon w in Catalogue(raider)) if (w.Hash == hash) return w;
            return null;
        }

        /// <summary>Does this side's catalogue carry the weapon (so it can be one of that side's pair)?</summary>
        public static bool Knows(bool raider, string token) { return Find(raider, token) != null; }

        /// <summary>Is the weapon ranged (either side's catalogue)?</summary>
        public static bool IsRangedToken(string token)
        {
            Weapon w = Find(false, token) ?? Find(true, token);
            return w != null && w.Ranged;
        }

        // ---- resolution ----

        /// <summary>A class whose NPCs carry a pair: SC's raider classes and the community's 0-XNPCCore survivor classes.</summary>
        public static bool IsBodyClass(string cls) { return ScLooks.IsRaiderClass(cls) || ScXnpc.IsManagedClass(cls); }

        /// <summary>
        /// PostInit PREFIX for both framework classes and EntityNPCBandit: resolves the pair of a raider or of one of the
        /// community's 0-XNPCCore survivors and rewrites itemsOnEnterGame to it. Idempotent.
        /// </summary>
        public static void BeforePostInit(EntityAlive e)
        {
            if (e == null || e is EntityPlayer) return;
            string cls = ScCommunity.ClassName(e);
            if (!IsBodyClass(cls)) return;
            Loadout pending = Pending;
            Pending = null;                                       // one entity only, used or not
            // a known survivor's ammo record travels with the pair (ScSpawn) and is acted on in the postfix (AfterPostInit)
            ScAmmo.TakePending(e, ScXnpc.IsManagedClass(cls) && ScNpc.IsV4(e));
            if (!ScConfig.Parsed) ScConfig.EnsureParsed();
            if (ScXnpc.IsManagedClass(cls))
            {
                // not behind SurvivorSecondWeapon: the class's own list (XNPC's pipe pistol, and a full 9 mm stack refilled
                // on every load) has to go whatever that switch says
                if (!ScNpc.IsV4(e)) return;
                Loadout x = ResolveXnpc(e, cls, pending);
                if (x != null) Rewrite(e, cls, XnpcOrder(x));
                return;
            }
            if (!ScConfig.SurvivorSecondWeapon) return;
            Loadout l = ResolveRaider(e, cls, pending);
            if (l != null)
            {
                Weapon main = Find(true, ScLooks.WeaponOf(cls));
                Rewrite(e, cls, new List<Weapon> { Find(true, l.Of(main.Ranged)), Find(true, l.Of(!main.Ranged)) });
            }
        }

        private static Loadout ResolveRaider(EntityAlive e, string cls, Loadout pending)
        {
            const bool raider = true;
            Weapon main = Find(raider, ScLooks.WeaponOf(cls));
            if (main == null) return null;
            Loadout l = null;
            string src = null;
            if (pending != null) { l = pending.Clone(); src = "spawn"; }
            if (l == null) { l = FromRecord(e, cls); if (l != null) src = "record"; }
            if (l == null) { l = FromCvars(e, raider); if (l != null) src = "saved"; }
            if (l == null) l = new Loadout();
            Validate(l, raider);
            if (!LauncherOk(l, main, raider)) l.Ranged = null;
            if (l.Of(main.Ranged) == null) l.Set(main.Ranged, main.Token);   // the class weapon is the main one
            if (l.Of(!main.Ranged) == null)
            {
                int cap = Math.Max(main.Tier, ScLooks.MaxTierFor(GameStageNow()));
                l.Set(!main.Ranged, Roll(raider, !main.Ranged, cap, Seeded(e)));
                src = src == null ? "rolled" : src + "+rolled";
            }
            if (l.Of(!main.Ranged) == null) return null;           // nothing of that kind is loaded: leave the class list alone
            WriteCvars(e, l, raider);
            sources[e.entityId] = src ?? "?";
            return l;
        }

        /// <summary>
        /// A 0-XNPCCore survivor's pair: whatever of it the sources above give, and the missing kind(s) rolled at the
        /// class's tier cap. Null (the class list stays) only when the catalogue has no weapon of a kind at all.
        /// </summary>
        private static Loadout ResolveXnpc(EntityAlive e, string cls, Loadout pending)
        {
            const bool raider = false;
            Loadout l = null;
            string src = null;
            if (pending != null) { l = pending.Clone(); src = "spawn"; }
            if (l == null) { l = FromRecord(e, cls); if (l != null) src = "record"; }
            if (l == null) { l = FromCvars(e, raider); if (l != null) src = "saved"; }
            if (l == null) l = new Loadout();
            Validate(l, raider);
            if (l.Ranged == null || l.Melee == null)
            {
                // ranged first, then melee, from one seeded stream: the same entity rolls the same pair on every attempt
                Random rnd = Seeded(e);
                int cap = TierCap(cls);
                if (l.Ranged == null) l.Ranged = Roll(raider, true, cap, rnd);
                if (l.Melee == null) l.Melee = Roll(raider, false, cap, rnd);
                src = src == null ? "rolled" : src + "+rolled";
            }
            if (l.Ranged == null || l.Melee == null)
            {
                ScLog.Once("loadout-xnpc-nopair", "no " + (l.Ranged == null ? "ranged" : "melee") + " weapon for 0-XNPCCore's survivors - " + cls + " keeps its class items");
                return null;
            }
            WriteCvars(e, l, raider);
            sources[e.entityId] = src;
            return l;
        }

        /// <summary>
        /// The highest weapon tier a 0-XNPCCore survivor of this class rolls: the lower of the class's band (T1 early, T2
        /// middle, T3 late - the place and time of day its spawn group stands for, as the raiders' class weapons do) and
        /// the gamestage tier (decision D4: tiers by gamestage). Tier 0 holds the pipe guns and the primitive bow, so
        /// primitive ranged weapons are there from day 1.
        /// </summary>
        public static int TierCap(string cls)
        {
            return Math.Min(ScXnpc.TierOf(cls), ScLooks.MaxTierFor(GameStageNow()));
        }

        /// <summary>[melee, ranged]: XNPC's own weapon logic picks which of the two is in the hand (it re-arms a loaded gun
        /// on world entry, swaps to the melee one up close and when the gun runs dry), so the order only decides SCore's
        /// fallback weapon (the first entry) - and the melee weapon is the one fallback that can never be an empty gun.</summary>
        private static List<Weapon> XnpcOrder(Loadout l)
        {
            return new List<Weapon> { Find(false, l.Melee), Find(false, l.Ranged) };
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

        private static Loadout FromCvars(EntityAlive e, bool raider)
        {
            if (e.Buffs == null) return null;
            Weapon r = FromHash(raider, e.Buffs.GetCustomVar(RangedCvar));
            Weapon m = FromHash(raider, e.Buffs.GetCustomVar(MeleeCvar));
            if (r == null && m == null) return null;
            return new Loadout(r != null ? r.Token : null, m != null ? m.Token : null);
        }

        /// <summary>Drops a token that is unknown or in the wrong slot (a removed weapon, a typo in a record).</summary>
        private static void Validate(Loadout l, bool raider)
        {
            Weapon r = Find(raider, l.Ranged), m = Find(raider, l.Melee);
            l.Ranged = r != null && r.Ranged ? r.Token : null;
            l.Melee = m != null && !m.Ranged ? m.Token : null;
        }

        /// <summary>The rocket launcher only as the class weapon: the 5 s between rockets is its class's attack timeout.</summary>
        private static bool LauncherOk(Loadout l, Weapon main, bool raider)
        {
            Weapon r = Find(raider, l.Ranged);
            return r == null || r.Family != "launcher" || (main != null && main.Token == r.Token);
        }

        /// <summary>Is this token the rocket launcher (never a second weapon)?</summary>
        public static bool IsLauncher(string token)
        {
            Weapon w = Find(false, token) ?? Find(true, token);
            return w != null && w.Family == "launcher";
        }

        private static void WriteCvars(EntityAlive e, Loadout l, bool raider)
        {
            if (e.Buffs == null) return;
            Weapon r = Find(raider, l.Ranged), m = Find(raider, l.Melee);
            e.Buffs.SetCustomVar(RangedCvar, r != null ? r.Hash : 0f, false);
            e.Buffs.SetCustomVar(MeleeCvar, m != null ? m.Hash : 0f, false);
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

        /// <summary>
        /// A weapon of one kind at or below the tier cap: a melee weapon (never fists), or a bow/crossbow - a firearm when
        /// SurvivorFirearmChance comes up (never the rocket launcher). Null if none is loaded.
        /// </summary>
        private static string Roll(bool raider, bool ranged, int cap, Random rnd)
        {
            string want = !ranged ? "melee" : (rnd.NextDouble() < ScConfig.SurvivorFirearmChance ? "firearm" : "bow");
            string other = !ranged ? null : (want == "firearm" ? "bow" : "firearm");
            List<Weapon> pool = Rollable(raider, want, cap);
            if (pool.Count == 0) pool = Rollable(raider, want, int.MaxValue);
            if (pool.Count == 0 && other != null) pool = Rollable(raider, other, cap);
            if (pool.Count == 0 && other != null) pool = Rollable(raider, other, int.MaxValue);
            return pool.Count > 0 ? pool[rnd.Next(pool.Count)].Token : null;
        }

        private static List<Weapon> Rollable(bool raider, string family, int cap)
        {
            var list = new List<Weapon>();
            foreach (Weapon w in Catalogue(raider))
                if (w.Rollable && w.Family == family && w.Tier <= cap) list.Add(w);
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
                foreach (string item in w.Items)
                    if (!want.Exists(x => string.Equals(StackName(x), StackName(item), StringComparison.OrdinalIgnoreCase))) want.Add(item);
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

        // ---- ammo (0-XNPCCore survivors, decision D3) ----

        /// <summary>
        /// PostInit POSTFIX: a 0-XNPCCore survivor's starting ammo, once per survivor. Its gun's basic rounds
        /// (XnpcSpawnAmmo, or XnpcSpawnArrows for a bow or crossbow) go into the inventory window, which SCore saves with
        /// the NPC and reloads its gun from (ItemActionRangedHarvestAmmo: an EntityAliveSDXV4 with a bag - the classes keep
        /// a BagItems property for that - and a window). The cvar is saved with the entity, so a reload never refills.
        /// Only a genuinely new survivor gets the stack (a wild spawn, a radio contact's or a console spawn's body): a body
        /// the community re-creates for somebody it knows (ScSpawn - a call-back, a stand-in, a runner's fresh body) gets
        /// the rounds their record says they carry instead (ScAmmo.RestoreRecorded; the round 2 review's P1), so
        /// re-creating a member never makes ammo. Server only: the window lives there.
        /// </summary>
        public static void AfterPostInit(EntityAlive e)
        {
            try
            {
                if (e == null || !ScXnpc.IsBody(e) || !ScCommunity.IsHost || e.Buffs == null) return;
                if (ScAmmo.RestoreRecorded(e)) return;
                if (e.Buffs.GetCustomVar(AmmoSeededCvar) > 0f) return;
                Loadout l = Current(e);
                Weapon r = l != null ? Find(false, l.Ranged) : null;
                if (r == null) return;
                string ammo = AmmoOf(ItemClass.GetItemClass(r.Item, true));
                int n = r.Family == "bow" ? ScConfig.XnpcSpawnArrows : ScConfig.XnpcSpawnAmmo;
                if (ammo == null || n <= 0) return;
                ItemValue iv = ItemClass.GetItem(ammo, true);
                if (iv == null || iv.IsEmpty()) return;
                // marked only now: a body that had nothing to seed yet (no ranged weapon) is not marked for life
                e.Buffs.SetCustomVar(AmmoSeededCvar, 1f);
                ScNpc.AddItem(e, new ItemStack(iv, n));
                ScAmmo.NoteSeeded();
                ScLog.Verbose("starting ammo for " + ScCommunity.DisplayName(e) + " (id " + e.entityId + "): " + n + " " + ammo + " for " + r.Token);
            }
            catch (Exception ex)
            {
                ScLog.Once("loadout-seed-ammo", ex);
            }
        }

        // ---- live NPCs (console; XNPC's melee choice) ----

        /// <summary>The pair a live NPC carries (null for anything that is not a raider or one of the community's 0-XNPCCore survivors).</summary>
        public static Loadout Current(EntityAlive e)
        {
            string cls = ScCommunity.ClassName(e);
            if (e == null || !IsBodyClass(cls)) return null;
            bool raider = ScLooks.IsRaiderClass(cls);
            Loadout l = FromCvars(e, raider);
            if (l == null) return null;
            Validate(l, raider);
            return l;
        }

        /// <summary>
        /// The melee weapon of a 0-XNPCCore survivor's pair while it can still hold it (SCore's FindWeapon: it is on its
        /// item list), else null. XNPC's own melee choice (X6) asks this, so its close-range swap and dry-gun failover
        /// always reach for the pair's melee weapon.
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
        /// Gives a live NPC a new pair (either half may be null: kept). Refills its slots and re-equips the same kind.
        /// freshAmmo: a 0-XNPCCore survivor given a new gun also gets a new starting stack for it (the console's sc loadout);
        /// false for a weapon the player handed over (ScArms: its ammo is what the player gives, or a resupply at home).
        /// </summary>
        public static bool Set(EntityAlive e, Loadout change, out string why, bool freshAmmo = true)
        {
            why = null;
            string cls = ScCommunity.ClassName(e);
            if (!IsBodyClass(cls)) { why = "not a raider or one of the community's 0-XNPCCore survivors"; return false; }
            if (ScMedic.IsUsing(e.entityId)) { why = "busy using a medical item"; return false; }
            bool raider = ScLooks.IsRaiderClass(cls);
            if (!raider && !ScNpc.IsV4(e)) { why = cls + " is not an EntityAliveSDXV4"; return false; }
            Loadout l = Current(e) ?? (raider ? ResolveRaider(e, cls, null) : ResolveXnpc(e, cls, null));
            if (l == null) { why = "no weapons loaded for " + cls; return false; }
            l = l.Clone();
            if (change.Ranged != null) l.Ranged = change.Ranged;
            if (change.Melee != null) l.Melee = change.Melee;
            Validate(l, raider);
            if (l.Ranged == null || l.Melee == null) { why = "a pair needs one ranged and one melee weapon"; return false; }
            List<Weapon> order;
            if (raider)
            {
                Weapon main = Find(true, ScLooks.WeaponOf(cls));
                if (!LauncherOk(l, main, true)) { why = "the rocket launcher only comes with its own class (that class sets its attack cadence)"; return false; }
                order = new List<Weapon> { Find(true, l.Of(main.Ranged)), Find(true, l.Of(!main.Ranged)) };
            }
            else order = XnpcOrder(l);
            bool heldRanged = ScSkills.HoldsRanged(e);
            int before = e.itemsOnEnterGame.Count;
            WriteCvars(e, l, raider);
            Remember(e, l);
            sources[e.entityId] = "console";
            if (Rewrite(e, cls, order)) Refill(e, before);
            Arm(e, heldRanged, out why);
            if (freshAmmo && !raider && change.Ranged != null && ScXnpc.IsBody(e))
            {
                e.Buffs.RemoveCustomVar(AmmoSeededCvar);
                AfterPostInit(e);
            }
            return true;
        }

        /// <summary>
        /// A member's or prospect's new pair goes into its record too: the record outranks the cvars when the body is next
        /// built (FromRecord), and every body the community re-creates for them is armed from it (ScSpawn).
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
        /// Puts the ranged or melee weapon of the pair in the hand. Survivors go through SCore's own
        /// UpdateWeapon(ItemValue) (it records the weapon for the save, and skips the network broadcast of the string
        /// overload); raiders have none and get the weapon in their hand slot with the same model refresh. A raider's
        /// CurrentWeaponClass is then written at once - XNPCCore's AI reads it to pick melee or ranged attacks, and its own
        /// buff would only catch up a second later. A 0-XNPCCore survivor's is left alone: XNPC's controller writes it
        /// (0 melee, 1 ranged) on every change of the held item, and SC's 1-3 range classes would sit there until then.
        /// </summary>
        public static bool Arm(EntityAlive e, bool ranged, out string why)
        {
            why = null;
            if (e == null || e.inventory == null) { why = "no inventory"; return false; }
            if (ScMedic.IsUsing(e.entityId)) { why = "busy using a medical item"; return false; }
            string cls = ScCommunity.ClassName(e);
            bool raider = ScLooks.IsRaiderClass(cls);
            Loadout l = Current(e);
            Weapon w = l != null ? Find(raider, l.Of(ranged)) : null;
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

        // ---- class picks (hostile console spawns) ----

        /// <summary>
        /// A raider class for a console spawn: ranged at SurvivorRangedMainChance, and then a firearm at
        /// SurvivorFirearmChance (else a bow or crossbow), uniform among the classes of that sex and group at or below
        /// maxTier. Together with the second weapon's roll this keeps a raider's one ranged weapon a firearm at
        /// SurvivorFirearmChance whichever it is.
        /// </summary>
        public static string PickClass(List<string> pool, GameRandom rnd, string sex, int maxTier)
        {
            if (pool == null || pool.Count == 0) return null;
            bool ranged = Chance(rnd) < ScConfig.SurvivorRangedMainChance;
            string group = !ranged ? "melee" : (Chance(rnd) < ScConfig.SurvivorFirearmChance ? "firearm" : "bow");
            var ok = Classes(pool, sex, group, maxTier);
            if (ok.Count == 0) ok = Classes(pool, sex, group, int.MaxValue);
            if (ok.Count == 0) return ScLooks.PickFrom(pool, rnd, sex, null, maxTier);
            return ok[rnd != null ? rnd.RandomRange(0, ok.Count) : 0];
        }

        private static List<string> Classes(List<string> pool, string sex, string group, int maxTier)
        {
            var ok = new List<string>();
            foreach (string c in pool)
            {
                if (!string.IsNullOrEmpty(sex) && ScLooks.ClassIsMale(c) != (sex == "male")) continue;
                if (ScLooks.TierOf(c) > maxTier) continue;
                Weapon w = Find(ScLooks.IsRaiderClass(c), ScLooks.WeaponOf(c));
                if (w == null || w.Group != group) continue;
                ok.Add(c);
            }
            return ok;
        }

        private static float Chance(GameRandom rnd) { return rnd != null ? rnd.RandomFloat : UnityEngine.Random.value; }

        /// <summary>Is this token a firearm (a gun or the launcher, not a bow)?</summary>
        public static bool IsFirearm(string token)
        {
            Weapon w = Find(false, token) ?? Find(true, token);
            return w != null && w.Group == "firearm";
        }

        // ---- console names ----

        /// <summary>A side's weapons for the console: raider tokens, or the survivors' vanilla items with their short names.</summary>
        public static List<string> Tokens(bool raider)
        {
            var list = new List<string>();
            foreach (Weapon w in Catalogue(raider)) list.Add(w.Alias.Length > 0 ? w.Token + " (" + w.Alias + ")" : w.Token);
            return list;
        }

        /// <summary>
        /// A weapon of one side by what the console was given: the token (a raider's "Club", a survivor's item
        /// "meleeWpnClubT0WoodenClub"), a survivor weapon's short name ("Club"), a family word (melee, bow, firearm), or a
        /// part of a name. Several matches are picked from at random when rnd is given, else refused as ambiguous.
        /// </summary>
        public static string MatchToken(bool raider, string text, GameRandom rnd, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(text)) { why = "no weapon given"; return null; }
            string a = text.Trim();
            if (raider && string.Equals(a, "ak47", StringComparison.OrdinalIgnoreCase)) a = "Rifle";   // the AK-47 raiders are named Rifle
            List<Weapon> cat = Catalogue(raider);
            foreach (Weapon w in cat) if (string.Equals(w.Token, a, StringComparison.OrdinalIgnoreCase)) return w.Token;
            foreach (Weapon w in cat) if (w.Alias.Length > 0 && string.Equals(w.Alias, a, StringComparison.OrdinalIgnoreCase)) return w.Token;
            var hits = new List<Weapon>();
            string fam = a.ToLowerInvariant() == "gun" ? "firearm" : a.ToLowerInvariant();
            if (fam == "melee" || fam == "bow" || fam == "firearm")
            {
                foreach (Weapon w in cat) if (w.Rollable && w.Family == fam) hits.Add(w);
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
            sb.Append("[SC] ").Append(ScCommunity.DisplayName(e)).Append(" id ").Append(e.entityId).Append(" (").Append(cls).Append(")");
            Loadout l = Current(e);
            bool raider = ScLooks.IsRaiderClass(cls);
            if (l == null)
                sb.Append(" carries no pair").Append(!IsBodyClass(cls) ? " (not a raider or one of the community's 0-XNPCCore survivors)"
                                                     : raider && !ScConfig.SurvivorSecondWeapon ? " (SurvivorSecondWeapon is off)" : " (not resolved yet)");
            else
            {
                string src;
                sources.TryGetValue(e.entityId, out src);
                sb.Append(": ranged ").Append(Label(raider, l.Ranged)).Append(", melee ").Append(Label(raider, l.Melee))
                  .Append(" [").Append(src ?? "loaded").Append("]");
                if (!raider) sb.Append(" tier cap now ").Append(TierCap(cls)).Append(", starting ammo given ").Append(e.Buffs != null && e.Buffs.GetCustomVar(AmmoSeededCvar) > 0f);
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

        private static string Label(bool raider, string token)
        {
            Weapon w = Find(raider, token);
            return w == null ? (token ?? "-") : w.Token + " (" + w.Family + ", tier " + w.Tier + ")";
        }

        /// <summary>
        /// sc loadout sim: n picks without making anyone. Raiders: a console spawn's class and second weapon. Survivors: a
        /// 0-XNPCCore survivor's pair at today's gamestage tier cap (a late-band class, so only the gamestage caps it).
        /// </summary>
        public static string Simulate(int n, bool raider)
        {
            var rnd = new Random(Environment.TickCount);
            int cap = ScLooks.MaxTierFor(GameStageNow()), fire = 0, rangedMain = 0, noPair = 0;
            var fams = new Dictionary<string, int>();
            var tiers = new int[3];
            List<string> pool = raider ? ScLooks.RaiderClasses() : null;
            if (raider && pool.Count == 0) return "[SC] no raider classes loaded";
            if (!raider && Catalogue(false).Count == 0) return "[SC] no weapons for 0-XNPCCore's survivors (XnpcWeapons)";
            var gr = ScCommunity.World != null ? ScCommunity.World.GetGameRandom() : null;
            for (int i = 0; i < n; i++)
            {
                Weapon rw;
                if (raider)
                {
                    string cls = PickClass(pool, gr, null, int.MaxValue);
                    Weapon main = Find(true, ScLooks.WeaponOf(cls));
                    if (main == null) { noPair++; continue; }
                    Weapon second = Find(true, Roll(true, !main.Ranged, Math.Max(main.Tier, cap), rnd));
                    if (second == null) { noPair++; continue; }
                    rw = main.Ranged ? main : second;
                    if (main.Ranged) rangedMain++;
                }
                else
                {
                    rw = Find(false, Roll(false, true, cap, rnd));
                    Weapon mw = Find(false, Roll(false, false, cap, rnd));
                    if (rw == null || mw == null) { noPair++; continue; }
                    tiers[Math.Max(0, Math.Min(2, rw.Tier))]++;
                }
                if (rw.Group == "firearm") fire++;
                int c;
                fams.TryGetValue(rw.Family, out c);
                fams[rw.Family] = c + 1;
            }
            var sb = new StringBuilder();
            sb.Append("[SC] ").Append(n).Append(raider ? " raider" : " 0-XNPCCore survivor").Append(" spawns (gamestage tier cap ").Append(cap).Append("): ")
              .Append(fire).Append(" carry a firearm (").Append((100.0 * fire / Math.Max(1, n - noPair)).ToString("0.0")).Append("%, configured ")
              .Append((100f * ScConfig.SurvivorFirearmChance).ToString("0.0")).Append("%)");
            if (raider) sb.Append(", ranged class weapon ").Append(rangedMain);
            else sb.Append(", ranged weapon tiers 0/1/2: ").Append(tiers[0]).Append('/').Append(tiers[1]).Append('/').Append(tiers[2]);
            sb.Append(", ranged weapons by family:");
            foreach (var kv in fams) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
            if (noPair > 0) sb.Append(", ").Append(noPair).Append(" without a pair");
            return sb.ToString();
        }
    }
}
