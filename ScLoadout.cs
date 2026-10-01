using System;
using System.Collections.Generic;
using System.Text;

namespace SurvivorCommunity
{
    /// <summary>An NPC's two weapons as generated weapon tokens (ScWeapon): one ranged, one melee.</summary>
    public class Loadout
    {
        public string Ranged;
        public string Melee;

        public Loadout() { }
        public Loadout(string ranged, string melee) { Ranged = ranged; Melee = melee; }

        public Loadout Clone() { return new Loadout(Ranged, Melee); }

        public string Of(bool ranged) { return ranged ? Ranged : Melee; }

        public void Set(bool ranged, string token) { if (ranged) Ranged = token; else Melee = token; }

        /// <summary>"ranged,melee" (either may be empty) - the registry form T3 stores.</summary>
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
    /// Two weapons for every survivor and raider wearing the player's body (weapons task W2). The class weapon stays
    /// the main one; the other kind is rolled per entity: a melee class rolls a bow or crossbow (a firearm at
    /// SurvivorFirearmChance), a ranged class rolls a melee weapon. Both are made legal to hold by rewriting the
    /// entity's own itemsOnEnterGame list in a PostInit PREFIX - before SCore's EntityAlive.PostInit postfix copies it
    /// into the inventory slots, before both SetupStartingItems calls and before PostInit re-equips the saved weapon.
    /// SCore's FindWeapon accepts anything on that list, so UpdateWeapon can swap between the two and a reload brings
    /// back whichever was held (SCore's NPC classes save their held weapon by name; raiders always come back on slot 0).
    ///
    /// The pair resolves, in order: Pending (a console spawn), the member/prospect record (T3), the pair this entity
    /// already carries (two cvars, saved with it), a roll seeded by (entityId, WorldTimeBorn). The roll is capped by the
    /// higher of the class weapon's tier and the gamestage tier, and the result goes into the cvars at once, so a later
    /// gamestage never changes a saved NPC's second weapon. Wild survivors are never saved: they re-roll as new NPCs.
    /// </summary>
    public static class ScLoadout
    {
        /// <summary>The pair this entity carries, as token hashes (exact in a float; 0 = none). Saved with the entity.</summary>
        public const string RangedCvar = "sc_loadout_r";
        public const string MeleeCvar = "sc_loadout_m";

        /// <summary>Consumed by the next player-body entity that runs PostInit (set right before EntityFactory.CreateEntity).</summary>
        public static Loadout Pending;

        private class Weapon
        {
            public string Token;
            public string Family;          // melee, unarmed, bow, firearm, launcher
            public bool Ranged;
            public int Tier;
            public string Class;           // a leaf class that carries it, whose ItemsOnEnterGame lists its items
            public List<string> Items;     // the class's ItemsOnEnterGame: the weapon first, then its ammo
            public float Hash;
            public string Item { get { return Items.Count > 0 ? Items[0] : ""; } }
            /// <summary>Rolled as a second weapon: not fists, not the rocket launcher (its attack cadence is its class's).</summary>
            public bool Rollable { get { return Family != "unarmed" && Family != "launcher"; } }
            /// <summary>The share it counts towards: melee (fists too), bow, or firearm (the launcher too).</summary>
            public string Group { get { return Family == "unarmed" ? "melee" : Family == "launcher" ? "firearm" : Family; } }
        }

        // per side (survivors and raiders ride different templates), sorted by token so a seeded roll is stable
        private static List<Weapon> survivorWeapons, raiderWeapons;
        private static readonly Dictionary<int, string> sources = new Dictionary<int, string>();

        public static void Reset()
        {
            Pending = null;
            survivorWeapons = null;
            raiderWeapons = null;
            sources.Clear();
        }

        // ---- catalogue ----

        private static List<Weapon> Catalogue(bool raider)
        {
            if (raider) { if (raiderWeapons == null) raiderWeapons = Build(ScLooks.RaiderClasses()); return raiderWeapons; }
            if (survivorWeapons == null) survivorWeapons = Build(ScLooks.SurvivorClasses());
            return survivorWeapons;
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
            var list = new List<Weapon>(byToken.Values);
            list.Sort((a, b) => string.CompareOrdinal(a.Token, b.Token));
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                    if (list[i].Hash == list[j].Hash) ScLog.Once("loadout-hash-" + list[i].Token, "weapon tokens " + list[i].Token + " and " + list[j].Token + " share a loadout hash - the cvar cannot tell them apart");
            return list;
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

        /// <summary>Does a generated class of this side carry the weapon (so it can be that side's second weapon)?</summary>
        public static bool Knows(bool raider, string token) { return Find(raider, token) != null; }

        /// <summary>Is the weapon ranged (either side's classes)?</summary>
        public static bool IsRangedToken(string token)
        {
            Weapon w = Find(false, token) ?? Find(true, token);
            return w != null && w.Ranged;
        }

        // ---- resolution ----

        private static bool IsBody(string cls) { return ScLooks.IsSurvivorClass(cls) || ScLooks.IsRaiderClass(cls); }

        /// <summary>
        /// PostInit PREFIX for both framework classes and EntityNPCBandit: resolves the pair for a player-body survivor or raider
        /// and rewrites itemsOnEnterGame to [main weapon + ammo, second weapon + ammo]. Idempotent.
        /// </summary>
        public static void BeforePostInit(EntityAlive e)
        {
            if (e == null || e is EntityPlayer) return;
            string cls = ScCommunity.ClassName(e);
            if (!IsBody(cls)) return;
            Loadout pending = Pending;
            Pending = null;                                       // one entity only, used or not
            if (!ScConfig.Parsed) ScConfig.EnsureParsed();
            if (!ScConfig.SurvivorSecondWeapon) return;
            Loadout l = Resolve(e, cls, pending);
            if (l != null) Rewrite(e, cls, l);
        }

        private static Loadout Resolve(EntityAlive e, string cls, Loadout pending)
        {
            bool raider = ScLooks.IsRaiderClass(cls);
            Weapon main = Find(raider, ScLooks.WeaponOf(cls));
            if (main == null) return null;
            Loadout l = null;
            string src = null;
            if (pending != null) { l = pending.Clone(); src = "spawn"; }
            if (l == null) { l = FromRecord(e); if (l != null) src = "record"; }
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

        /// <summary>The member or prospect record's pair. T3 adds the registry key; until then members roll like anyone.</summary>
        private static Loadout FromRecord(EntityAlive e)
        {
            return null;
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
        /// A weapon of one kind for a second-weapon roll, at or below the tier cap: a melee weapon (never fists), or a
        /// bow/crossbow - a firearm when SurvivorFirearmChance comes up (never the rocket launcher). Null if none is loaded.
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

        /// <summary>Rewrites itemsOnEnterGame to the main weapon's items, then the second's. Unchanged when it already is.</summary>
        private static bool Rewrite(EntityAlive e, string cls, Loadout l)
        {
            bool raider = ScLooks.IsRaiderClass(cls);
            Weapon main = Find(raider, ScLooks.WeaponOf(cls));
            if (main == null) return false;
            Weapon first = Find(raider, l.Of(main.Ranged)), second = Find(raider, l.Of(!main.Ranged));
            if (first == null || second == null) return false;
            var want = new List<string>();
            foreach (Weapon w in new[] { first, second })
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
                    ScLog.Once("loadout-item-" + item, "loadout: item " + item + " not found - " + cls + " keeps its class weapon only");
                    return false;
                }
                stacks.Add(st);
            }
            have.Clear();
            have.AddRange(stacks);
            return true;
        }

        // ---- live NPCs (console; W4's switcher builds on these) ----

        /// <summary>The pair a live NPC carries (null for anything that is not a player-body survivor or raider).</summary>
        public static Loadout Current(EntityAlive e)
        {
            string cls = ScCommunity.ClassName(e);
            if (e == null || !IsBody(cls)) return null;
            bool raider = ScLooks.IsRaiderClass(cls);
            Loadout l = FromCvars(e, raider);
            if (l == null) return null;
            Validate(l, raider);
            return l;
        }

        /// <summary>Gives a live NPC a new pair (either half may be null: kept). Refills its slots and re-equips the same kind.</summary>
        public static bool Set(EntityAlive e, Loadout change, out string why)
        {
            why = null;
            string cls = ScCommunity.ClassName(e);
            if (!IsBody(cls)) { why = "not a survivor or raider with the player's body"; return false; }
            if (ScMedic.IsUsing(e.entityId)) { why = "busy using a medical item"; return false; }
            bool raider = ScLooks.IsRaiderClass(cls);
            Loadout l = Current(e) ?? Resolve(e, cls, null);
            if (l == null) { why = "no weapons loaded for " + cls; return false; }
            l = l.Clone();
            if (change.Ranged != null) l.Ranged = change.Ranged;
            if (change.Melee != null) l.Melee = change.Melee;
            Validate(l, raider);
            if (l.Ranged == null || l.Melee == null) { why = "a pair needs one ranged and one melee weapon"; return false; }
            if (!LauncherOk(l, Find(raider, ScLooks.WeaponOf(cls)), raider)) { why = "the rocket launcher only comes with its own class (that class sets its attack cadence)"; return false; }
            bool heldRanged = ScSkills.HoldsRanged(e);
            int before = e.itemsOnEnterGame.Count;
            WriteCvars(e, l, raider);
            sources[e.entityId] = "console";
            if (Rewrite(e, cls, l)) Refill(e, before);
            Arm(e, heldRanged, out why);
            return true;
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
        /// overload); raiders have none and get the weapon in their hand slot with the same model refresh. Then
        /// CurrentWeaponClass is written at once - XNPCCore's AI reads it to pick melee or ranged attacks, and its
        /// own buff would only catch up a second later.
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
            SetWeaponClass(e);
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

        // ---- class picks (console spawns, radio contacts) ----

        /// <summary>
        /// A class for a new NPC: ranged at SurvivorRangedMainChance, and then a firearm at SurvivorFirearmChance (else a
        /// bow or crossbow), uniform among the classes of that sex and group at or below maxTier. Together with the second
        /// weapon's roll this keeps an NPC's one ranged weapon a firearm at SurvivorFirearmChance whichever it is.
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

        // ---- diagnostics ----

        public static string Describe(EntityAlive e)
        {
            var sb = new StringBuilder();
            string cls = ScCommunity.ClassName(e);
            sb.Append("[SC] ").Append(ScCommunity.DisplayName(e)).Append(" id ").Append(e.entityId).Append(" (").Append(cls).Append(")");
            Loadout l = Current(e);
            bool raider = ScLooks.IsRaiderClass(cls);
            if (l == null) sb.Append(" carries no pair").Append(IsBody(cls) ? (ScConfig.SurvivorSecondWeapon ? " (not resolved yet)" : " (SurvivorSecondWeapon is off)") : " (not a player-body survivor or raider)");
            else
            {
                string src;
                sources.TryGetValue(e.entityId, out src);
                sb.Append(": ranged ").Append(Label(raider, l.Ranged)).Append(", melee ").Append(Label(raider, l.Melee))
                  .Append(" [").Append(src ?? "loaded").Append("]");
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

        /// <summary>sc loadout sim: n console-spawn picks with their second-weapon rolls, no NPCs made.</summary>
        public static string Simulate(int n, bool raider)
        {
            List<string> pool = raider ? ScLooks.RaiderClasses() : ScLooks.SurvivorClasses();
            if (pool.Count == 0) return "[SC] no classes loaded";
            var rnd = new Random(Environment.TickCount);
            var gr = ScCommunity.World != null ? ScCommunity.World.GetGameRandom() : null;
            int cap = ScLooks.MaxTierFor(GameStageNow()), fire = 0, rangedMain = 0, noPair = 0;
            var fams = new Dictionary<string, int>();
            for (int i = 0; i < n; i++)
            {
                string cls = PickClass(pool, gr, null, int.MaxValue);
                Weapon main = Find(raider, ScLooks.WeaponOf(cls));
                if (main == null) { noPair++; continue; }
                Weapon second = Find(raider, Roll(raider, !main.Ranged, Math.Max(main.Tier, cap), rnd));
                if (second == null) { noPair++; continue; }
                Weapon rw = main.Ranged ? main : second;
                if (main.Ranged) rangedMain++;
                if (rw.Group == "firearm") fire++;
                int c;
                fams.TryGetValue(rw.Family, out c);
                fams[rw.Family] = c + 1;
            }
            var sb = new StringBuilder();
            sb.Append("[SC] ").Append(n).Append(raider ? " raider" : " survivor").Append(" spawns (gamestage tier cap ").Append(cap).Append("): ")
              .Append(fire).Append(" carry a firearm (").Append((100.0 * fire / Math.Max(1, n - noPair)).ToString("0.0")).Append("%, configured ")
              .Append((100f * ScConfig.SurvivorFirearmChance).ToString("0.0")).Append("%), ranged class weapon ").Append(rangedMain)
              .Append(", ranged weapons by family:");
            foreach (var kv in fams) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
            if (noPair > 0) sb.Append(", ").Append(noPair).Append(" without a pair");
            return sb.ToString();
        }
    }
}
