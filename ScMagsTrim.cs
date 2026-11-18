using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round m1p (the M1 polish bundle; memory sc-magazine-duplication, "Not fixed"): "sc mags", a member's Locked &amp; Loaded
    /// magazines for her gun against her tier's count, and an opt-in trim for a member inflated before round n07's
    /// duplication fix (X18).
    ///
    /// Before n07, 0-XNPCCore and Locked &amp; Loaded both claimed a gun's rounds on every weapon change, so each change (a world
    /// load included) left her one more magazine holding whatever the gun held at that instant. ScMags.Record wrote those into
    /// her member record, and her next body got them back. X18 stopped new ones; the ones she has stayed, because an automatic
    /// trim would also destroy magazines the player legitimately handed over. So the trim is opt-in, one member at a time, and
    /// shows what it would remove before it removes anything.
    ///
    /// The rule is the n07 note's own test: K, her magazines for her pair gun as Locked &amp; Loaded counts them for the record
    /// (the one seated in the gun in her hand included, as sc xnpc's "K magazine(s) holding R"), against her tier's N
    /// (ScMags.TierMagazines: Locked &amp; Loaded's &lt;npcs&gt; table, its defaults T1 2, T2 3, T3 4). A trim keeps N and removes the
    /// other K - N from her bag and toolbelt. The one in the gun is always kept, then the one marked to go back in on the draw
    /// (round lr1), then the fullest (a twin's two compartments together, as Locked &amp; Loaded's record counts them); the emptiest
    /// go first. Removed magazines are destroyed with their rounds (they are the
    /// duplicates): nothing is handed to the player, and no loose rounds are made.
    ///
    /// "sc mags": every member whose body is loaded, K against N. "sc mags &lt;id|name&gt;": her magazines, each marked keep or
    /// trim. "sc mags &lt;id|name&gt; trim": the same list and what a trim would destroy; nothing changes. "sc mags &lt;id|name&gt; trim
    /// confirm": removes them, writes her record again (ScAmmo.Record) and saves. Her body must be loaded (her magazines are
    /// items on it, and her record follows it every 5 s), her inventory closed, and the magazine adapter on. Nothing is removed
    /// when SC's count of her magazines differs from Locked &amp; Loaded's. Host only, main thread.
    /// </summary>
    internal static class ScMagsTrim
    {
        private sealed class Mag
        {
            public string Where;       // "in the gun", "bag 3", "toolbelt 5"
            public bool InGun, InBag, Marked, HandSlot, Bare;
            public int Slot = -1;
            public string Name;
            public int Rounds;         // both compartments of a twin once Align has read Locked & Loaded's record; the feeding side before
            public string Twin;        // "a + b" for a twin (Align), else null
            public bool Keep;
        }

        private const string Usage = "[SC] usage: sc mags [<id|name> [trim [confirm]]] - a member's Locked & Loaded magazines against her tier's count; trim shows what it would remove, trim confirm removes it";

        internal static void ConsoleCmd(List<string> p, Action<string> output)
        {
            if (!ScCommunity.IsHost) { output("[SC] sc mags runs on the host only"); return; }
            World w = ScCommunity.World;
            if (w == null) { output("[SC] no world"); return; }
            if (!ScMags.Active) { output("[SC] Locked & Loaded magazines: " + ScMags.State + " - nothing to count or trim"); return; }
            if (p.Count < 2) { Summary(w, output); return; }
            bool trim = p.Count > 2 && p[2].Equals("trim", StringComparison.OrdinalIgnoreCase);
            bool confirm = trim && p.Count > 3 && p[3].Equals("confirm", StringComparison.OrdinalIgnoreCase);
            if ((p.Count > 2 && !trim) || (p.Count > 3 && !confirm) || p.Count > 4) { output(Usage); return; }
            Member m = Find(p[1]);
            if (m == null) { output("[SC] no member '" + p[1] + "' (sc members lists them)"); return; }

            bool standIn;
            EntityAlive e = ScAmmo.BodyOf(w, m, out standIn);
            if (e == null || !ScXnpc.IsBody(e))
            {
                int recorded = !string.IsNullOrEmpty(m.Mags) ? ScMags.Entries(m.Mags).Count : 0;
                output("[SC] " + m.Name + "'s body is not loaded here (or is away on a run); the member record holds " + recorded
                       + " magazine(s). The magazines are items on the body: stand near " + m.Name + " and run it again.");
                return;
            }
            ItemClass gun = ScAmmo.PairGun(e);
            if (gun == null) { output("[SC] " + m.Name + " has no gun in the weapon pair - no magazines to count"); return; }
            string gunName = gun.GetItemName();
            string why;
            List<Mag> mags = Collect(e, gun, out why);
            if (mags == null) { output("[SC] " + m.Name + ": " + why); return; }

            // Locked & Loaded's own count for the record (NpcMagazines.Record): the trim acts only when SC's list agrees with it
            string rec = ScMags.Record(e, gunName);
            int lnlCount = rec != null ? ScMags.Entries(rec).Count : -1;
            // the Fable review (L1): each magazine's rounds from that record, a twin's second compartment included, for the ranking
            // and the report; false when a row does not line up with SC's list
            bool aligned = Align(mags, rec);
            int tier = ScMags.TierOf(e);
            int n = ScMags.TierMagazines(tier);
            Choose(mags, n);

            int trimCount = 0, trimRounds = 0, keepCount = 0;
            foreach (Mag x in mags)
            {
                if (x.Keep) keepCount++;
                else { trimCount++; trimRounds += x.Rounds; }
            }
            output("[SC] " + m.Name + " (id " + e.entityId + (standIn ? ", stand-in" : "") + ", T" + tier + "): " + mags.Count + " magazine(s) for the "
                   + gunName + " (the one in the gun included; Locked & Loaded counts " + (lnlCount >= 0 ? lnlCount.ToString() : "?") + "), tier T" + tier
                   + " carries " + n + " (Locked & Loaded's <npcs> table)");
            foreach (Mag x in mags)
                output("[SC]   " + (x.Keep ? "keep " : "TRIM ") + x.Where + (x.InGun ? (x.Bare ? " (loaded, no magazine flag)" : " (seated)") : " " + x.Name) + ": " + x.Rounds + " round(s)"
                       + (x.Twin != null ? " (twin: " + x.Twin + ")" : "")
                       + (x.Marked ? ", marked to go back in on the draw" : "") + (x.HandSlot ? ", in the hand slot (never removed)" : ""));
            if (!aligned)
            {
                output("[SC] SC counts " + mags.Count + " and Locked & Loaded " + lnlCount + (lnlCount == mags.Count ? " but lists them differently" : "")
                       + " - nothing is trimmed while they disagree (a seated gun Locked & Loaded reads differently?). Swap her weapon (sc arms "
                       + m.Name + " melee, then ranged) and try again.");
                return;
            }
            if (trimCount == 0)
            {
                output("[SC] nothing to trim: " + mags.Count + " is not over the tier's " + n);
                return;
            }
            if (!confirm)
            {
                string what = trimCount + " magazine(s) holding " + trimRounds + " round(s), keeping " + keepCount;
                output("[SC] " + (trim ? "a trim would destroy " + what + ". Nothing was changed. " : what.Replace(", keeping", " over the tier (marked TRIM), keeping") + ". ")
                       + "Magazines you handed " + m.Name + " yourself count the same, so check the list first. "
                       + (trim ? "To remove them: sc mags " + p[1] + " trim confirm" : "sc mags " + p[1] + " trim shows what a trim would remove"));
                return;
            }

            // confirm: her inventory must be closed - SCore's (the host's own open, a remote copy, 0-XNPCCore's trade) and SC's panel
            if (ScAmmo.IsWindowOpen(e.entityId) || XUiC_ScMemberInvGroup.ShowsNpc(e.entityId))
            {
                output("[SC] " + m.Name + "'s inventory is open - close it, then run sc mags " + p[1] + " trim confirm again. Nothing was removed.");
                return;
            }
            int roundsBefore = ScMags.RoundsIn(rec);
            int removed = 0, removedRounds = 0;
            string stopped = null;
            foreach (Mag x in mags)
            {
                if (x.Keep) continue;
                ItemStack[] slots = Store(e, x.InBag);
                if (slots == null || x.Slot < 0 || x.Slot >= slots.Length || !IsItem(slots[x.Slot], x.Name))
                {
                    stopped = x.Where + " no longer holds the " + x.Name;
                    break;
                }
                if (x.InBag) e.bag.SetSlot(x.Slot, ItemStack.Empty);
                else e.inventory.SetStackAt(x.Slot, ItemStack.Empty);
                // the slot itself says whether the magazine left (Bag.SetSlot ignores an index past its slot count)
                slots = Store(e, x.InBag);
                if (slots != null && x.Slot < slots.Length && IsItem(slots[x.Slot], x.Name))
                {
                    stopped = "the " + x.Name + " in " + x.Where + " could not be taken out";
                    break;
                }
                removed++;
                removedRounds += x.Rounds;
                output("[SC]   removed " + x.Where + " " + x.Name + ": " + x.Rounds + " round(s)");
            }
            string after = ScMags.Record(e, gunName);
            int afterCount = after != null ? ScMags.Entries(after).Count : -1;
            int roundsAfter = after != null ? ScMags.RoundsIn(after) : -1;
            if (removed > 0)
            {
                ScAmmo.Record(m, e);          // the member record follows the body now, not at the next 5 s tick
                ScRegistry.SaveAtomic();
            }
            string line = m.Name + " (id " + e.entityId + "): sc mags trim confirm removed " + removed + " of " + trimCount + " magazine(s) for the " + gunName
                          + " holding " + removedRounds + " round(s); Locked & Loaded counts " + (afterCount >= 0 ? afterCount.ToString() : "?") + " now, holding "
                          + (roundsAfter >= 0 ? roundsAfter.ToString() : "?") + " (was " + lnlCount + " holding " + roundsBefore + ")"
                          + (stopped != null ? " - stopped: " + stopped : "");
            output("[SC] " + line);
            ScLog.Out("mags trim (round m1p): " + line);
        }

        /// <summary>sc mags: every member whose body is loaded - her gun, K against her tier's N.</summary>
        private static void Summary(World w, Action<string> output)
        {
            int listed = 0, over = 0, away = 0;
            foreach (Member m in ScRegistry.Members.Values)
            {
                bool standIn;
                EntityAlive e = ScAmmo.BodyOf(w, m, out standIn);
                if (e == null || !ScXnpc.IsBody(e)) { away++; continue; }
                ItemClass gun = ScAmmo.PairGun(e);
                if (gun == null) continue;
                string rec = ScMags.Record(e, gun.GetItemName());
                if (string.IsNullOrEmpty(rec)) continue;          // no magazines for this gun: it reloads from loose rounds
                int k = ScMags.Entries(rec).Count;
                int tier = ScMags.TierOf(e);
                int n = ScMags.TierMagazines(tier);
                listed++;
                if (k > n) over++;
                output("[SC] " + m.Id + " " + m.Name + " (T" + tier + (standIn ? ", stand-in" : "") + "): " + k + " magazine(s) for the " + gun.GetItemName()
                       + " holding " + ScMags.RoundsIn(rec) + ", the tier carries " + n + (k > n ? " - " + (k - n) + " over: sc mags " + m.Id + " trim" : ""));
            }
            output("[SC] " + listed + " member(s) with magazines, " + over + " over their tier's count, " + away + " not loaded here (sc mags <id|name> for one)");
        }

        /// <summary>
        /// Her magazines for the gun, as Locked &amp; Loaded's NpcMagazines.Record walks them: the one seated in the gun in her hand,
        /// then her bag, then her toolbelt. Null, with why, when the gun takes no magazine Locked &amp; Loaded knows.
        /// </summary>
        private static List<Mag> Collect(EntityAlive e, ItemClass gun, out string why)
        {
            why = null;
            string gunName = gun.GetItemName();
            HashSet<string> names = ScMags.MagazineNames(gunName);
            if (names == null || names.Count == 0)
            {
                why = "the " + gunName + " takes no Locked & Loaded magazine (or its MagazineRegistry binding is off: see sc xnpc)";
                return null;
            }
            var list = new List<Mag>();
            // the gun in her hand counts as one when Locked & Loaded's Record counts it (Magazines.LooksSeated: its seated flag, or
            // any rounds in it - a bare Meta, 0-XNPCCore's unflagged load, is the n07 phantom's own signature)
            ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
            if (held != null && !held.IsEmpty() && held.type == gun.Id && (ScMags.SeatedFlag(held) || held.Meta > 0))
                list.Add(new Mag { Where = "in the gun", InGun = true, Bare = !ScMags.SeatedFlag(held), Name = gunName, Rounds = Math.Max(0, held.Meta) });
            int hand = e.inventory != null ? e.inventory.holdingItemIdx : -1;
            for (int k = 0; k < 2; k++)
            {
                bool bag = k == 0;
                ItemStack[] slots = Store(e, bag);
                if (slots == null) continue;
                for (int i = 0; i < slots.Length; i++)
                {
                    ItemStack s = slots[i];
                    if (s == null || s.IsEmpty() || s.itemValue == null || s.itemValue.ItemClass == null) continue;
                    string name = s.itemValue.ItemClass.GetItemName();
                    if (!names.Contains(name)) continue;
                    list.Add(new Mag
                    {
                        Where = (bag ? "bag " : "toolbelt ") + i,
                        InBag = bag,
                        Slot = i,
                        Name = name,
                        Rounds = Math.Max(0, s.itemValue.Meta),
                        Marked = MarkedFor(s.itemValue, gunName),
                        HandSlot = !bag && i == hand,
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// The Fable review (L1): each magazine's rounds as Locked &amp; Loaded's record has them (ScMags.Entries: a twin's second
        /// compartment, kept in its metadata, included), so a twin holding 0 + 30 is not taken for the emptiest. Record walks the
        /// gun in the hand, then the bag, then the toolbelt, as Collect does, so row i is mags[i]; every bag or toolbelt row must
        /// name the same magazine. False - nothing changed, and the trim refused - when the counts differ or a row does not match.
        /// </summary>
        private static bool Align(List<Mag> mags, string rec)
        {
            if (rec == null) return false;
            List<ScMags.Entry> rows = ScMags.Entries(rec);
            if (rows.Count != mags.Count) return false;
            for (int i = 0; i < rows.Count; i++)
                if (!mags[i].InGun && !string.Equals(rows[i].Magazine, mags[i].Name, StringComparison.OrdinalIgnoreCase)) return false;
            for (int i = 0; i < rows.Count; i++)
            {
                mags[i].Rounds = rows[i].Rounds;
                // "magName:ammo:rounds:other" - a fourth field is a twin's waiting side
                string[] f = rows[i].Text != null ? rows[i].Text.Split(':') : new string[0];
                int a, b;
                if (f.Length > 3 && int.TryParse(f[2], out a) && int.TryParse(f[3], out b)) mags[i].Twin = Math.Max(0, a) + " + " + Math.Max(0, b);
            }
            return true;
        }

        /// <summary>
        /// Which n to keep: the one in the gun, the one marked to go back in on the draw, then the fullest (the bag before the
        /// toolbelt, the lower slot first on a tie). One in the toolbelt's hand slot is never removed, even past n.
        /// </summary>
        private static void Choose(List<Mag> mags, int n)
        {
            var order = new List<Mag>(mags);
            order.Sort((a, b) =>
            {
                int c = b.InGun.CompareTo(a.InGun);
                if (c != 0) return c;
                c = b.Marked.CompareTo(a.Marked);
                if (c != 0) return c;
                c = b.Rounds.CompareTo(a.Rounds);
                if (c != 0) return c;
                c = b.InBag.CompareTo(a.InBag);
                if (c != 0) return c;
                return a.Slot.CompareTo(b.Slot);
            });
            for (int i = 0; i < order.Count; i++) order[i].Keep = i < n || order[i].InGun || order[i].HandSlot;
        }

        /// <summary>The store slots Locked &amp; Loaded's NPC side walks (3.4 grids, as ScMags reads them): her bag, else her toolbelt.</summary>
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

        /// <summary>Round lr1's mark (ScMags.PutAwayKey): this magazine goes back into this gun on the draw.</summary>
        private static bool MarkedFor(ItemValue iv, string gunName)
        {
            try
            {
                string v;
                return iv != null && iv.Metadata != null && iv.TryGetMetadata(ScMags.PutAwayKey, out v) && string.Equals(v, gunName, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        /// <summary>A member by id, or the first whose name contains the word (as the other sc commands find one).</summary>
        private static Member Find(string key)
        {
            int id;
            if (int.TryParse(key, out id)) return ScRegistry.GetMember(id);
            foreach (Member m in ScRegistry.Members.Values)
                if (m.Name != null && m.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return m;
            return null;
        }
    }
}
