using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP3 (multiplayer, Stage 1): items between a player's own inventory and the community.
    /// A player's inventory belongs to their own machine: the server cannot read or write a remote player's bag (its copy is
    /// only what the client last saved), so every give and take is split in two (the plan's engine rule 10):
    /// - Take (the community gets items: a deposit, a hand-over, a gear slot filled): the acting player's machine removes the
    ///   items first (TakeFromLocal, recording exactly what left), then asks the server (ScActions.Request) to credit them.
    ///   A refusal - any refusal - gives the recorded items back (GiveToLocal).
    /// - Give (the player gets items: a withdrawal, a bundle, a gear slot emptied): the server debits first and answers with
    ///   the items in the reply (Encode); the acting player's machine adds them (GiveToLocal: into the bag and toolbelt, the
    ///   rest dropped at their feet - never lost). An op that wants a full bag refused instead asks the client's room first
    ///   (RoomFor) and sends it with the request.
    /// On the host and in single player the same two halves run back to back, inline, on the host's own player.
    /// The game's channel is reliable and ordered: a request and its answer are lost only when the client disconnects in
    /// between (then a take is lost to the player, a give to the community - documented, not handled).
    /// Stacks travel as base64 of the game's own ItemStack.Write (quality, mods, cosmetics and metadata included), one per string
    /// argument.
    /// </summary>
    public static class ScItemTx
    {
        /// <summary>An encoded stack longer than this is refused by Encode (NetPackageScAction caps a string argument at 4096).</summary>
        public const int MaxEncoded = 4000;

        // ------------------------------------------------------------------ encoding ----

        /// <summary>One stack as base64 (ItemStack.Write); "" for an empty one; null when it is too large to send.</summary>
        public static string Encode(ItemStack s)
        {
            if (s == null || s.IsEmpty()) return "";
            try
            {
                using (var ms = new MemoryStream())
                {
                    using (PooledBinaryWriter w = MemoryPools.poolBinaryWriter.AllocSync(false))
                    {
                        w.SetBaseStream(ms);
                        s.Write(w);
                    }
                    string b64 = Convert.ToBase64String(ms.ToArray());
                    return b64.Length <= MaxEncoded ? b64 : null;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("itemtx-encode", e);
                return null;
            }
        }

        /// <summary>The stack an Encode string holds; ItemStack.Empty for "" or anything unreadable (never null).</summary>
        public static ItemStack Decode(string b64)
        {
            if (string.IsNullOrEmpty(b64) || b64.Length > MaxEncoded) return ItemStack.Empty.Clone();
            try
            {
                byte[] bytes = Convert.FromBase64String(b64);
                using (var ms = new MemoryStream(bytes))
                using (PooledBinaryReader r = MemoryPools.poolBinaryReader.AllocSync(false))
                {
                    r.SetBaseStream(ms);
                    ItemStack s = new ItemStack();
                    s.Read(r);
                    // an item id this game does not know reads as an empty or invalid value: never hand that on
                    if (s.itemValue == null || s.itemValue.ItemClass == null || s.count <= 0) return ItemStack.Empty.Clone();
                    return s;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("itemtx-decode", e);
                return ItemStack.Empty.Clone();
            }
        }

        /// <summary>Every non-empty stack encoded, in order; null when any one is too large to send.</summary>
        public static string[] EncodeAll(IList<ItemStack> stacks)
        {
            var list = new List<string>();
            if (stacks != null)
            {
                foreach (ItemStack s in stacks)
                {
                    if (s == null || s.IsEmpty()) continue;
                    string e = Encode(s);
                    if (e == null) return null;
                    list.Add(e);
                }
            }
            return list.ToArray();
        }

        /// <summary>The stacks in strs from index from on (ScActionArgs.Strs, or a reply hint split on '|'); empty ones skipped.</summary>
        public static List<ItemStack> DecodeAll(string[] strs, int from = 0)
        {
            var list = new List<ItemStack>();
            if (strs == null) return list;
            for (int i = Math.Max(0, from); i < strs.Length; i++)
            {
                ItemStack s = Decode(strs[i]);
                if (!s.IsEmpty()) list.Add(s);
            }
            return list;
        }

        /// <summary>
        /// Stacks for a reply hint (key=value pairs cannot hold ';' or '='): base64's '=' padding becomes '~', and the stacks are
        /// joined with '|'. HintToStacks reverses it. Null when a stack is too large.
        /// </summary>
        public static string StacksToHint(IList<ItemStack> stacks)
        {
            string[] e = EncodeAll(stacks);
            if (e == null) return null;
            return string.Join("|", e).Replace('=', '~');
        }

        public static List<ItemStack> HintToStacks(string hint)
        {
            if (string.IsNullOrEmpty(hint)) return new List<ItemStack>();
            return DecodeAll(hint.Replace('~', '=').Split('|'));
        }

        // ------------------------------------------------------------------ the acting player's own machine ----

        private static XUiM_PlayerInventory Inv(EntityPlayerLocal p)
        {
            return p != null && p.PlayerUI != null && p.PlayerUI.xui != null ? p.PlayerUI.xui.PlayerInventory : null;
        }

        /// <summary>
        /// The player's own machine: the stacks into their bag and toolbelt; what does not fit is dropped at their feet (never
        /// lost). Returns how many items were dropped (0 = all fitted).
        /// Round MP3 (review packing/gear m4): an armor piece with AutoEquip never goes through XUiM_PlayerInventory.AddItem, which
        /// would put it ON and then place the piece it took off - and with a full bag lose that old piece while reporting the new
        /// one as left over (dropped too: doubled). It goes into the bag or the toolbelt as an item, and is dropped at the feet when
        /// neither has room.
        /// </summary>
        public static int GiveToLocal(EntityPlayerLocal p, IList<ItemStack> stacks)
        {
            if (p == null || stacks == null) return 0;
            XUiM_PlayerInventory inv = Inv(p);
            int dropped = 0;
            foreach (ItemStack s in stacks)
            {
                try
                {
                    if (s == null || s.IsEmpty()) continue;
                    ItemStack c = s.Clone();
                    ItemClassArmor armor = c.itemValue != null ? c.itemValue.ItemClass as ItemClassArmor : null;
                    if (inv != null && armor != null && armor.AutoEquip)
                    {
                        // round MP3 (review packing/gear m4): both lower c.count in place by what they stacked; a true placed the rest
                        if (inv.AddItemToBackpack(c) || c.count <= 0) continue;
                        if (inv.AddItemToToolbelt(c) || c.count <= 0) continue;
                    }
                    else if (inv != null && inv.AddItem(c)) continue;     // AddItem lowers c.count by what fitted
                    if (c.count <= 0) continue;
                    dropped += c.count;
                    GameManager gm = GameManager.Instance;
                    if (gm != null) gm.ItemDropServer(c, p.GetPosition(), Vector3.zero, p.entityId, 60f, false);
                }
                catch (Exception e)
                {
                    ScLog.Once("itemtx-give", e);
                }
            }
            return dropped;
        }

        /// <summary>
        /// The player's own machine: removes up to count items that match (bag first, then toolbelt) and records in removed
        /// exactly what left (to give back on a refusal). Returns how many items left.
        /// The removal is vanilla's XUiM_PlayerInventory.RemoveItems: by item TYPE, skipping modded stacks, and nothing at all when
        /// the planned amount is not there in unmodded stacks (round MP3 review, m5). Fit for bundles and plain resources; for
        /// one exact stack (a modded gun, a slot) move that stack itself instead.
        /// </summary>
        public static int TakeFromLocal(EntityPlayerLocal p, Func<ItemValue, bool> match, int count, List<ItemStack> removed)
        {
            if (p == null || match == null || count <= 0) return 0;
            try
            {
                XUiM_PlayerInventory inv = Inv(p);
                if (inv == null) return 0;
                var take = new List<ItemStack>();
                int planned = 0;
                ItemStack[][] sources = { p.bag != null ? (ItemStack[])p.bag.ItemGrid.items.Clone() : null,
                                          p.inventory != null ? (ItemStack[])p.inventory.ItemGrid.items.Clone() : null };
                for (int src = 0; src < sources.Length && planned < count; src++)
                {
                    ItemStack[] slots = sources[src];
                    if (slots == null) continue;
                    for (int i = 0; i < slots.Length && planned < count; i++)
                    {
                        ItemStack s = slots[i];
                        if (s == null || s.IsEmpty() || s.itemValue == null || !match(s.itemValue)) continue;
                        int k = Math.Min(s.count, count - planned);
                        take.Add(new ItemStack(s.itemValue.Clone(), k));
                        planned += k;
                    }
                }
                if (take.Count == 0) return 0;
                var got = new List<ItemStack>();
                inv.RemoveItems(take, 1, got);
                int n = 0;
                foreach (ItemStack g in got) if (g != null && !g.IsEmpty()) { n += g.count; if (removed != null) removed.Add(g.Clone()); }
                return n;
            }
            catch (Exception e)
            {
                ScLog.Once("itemtx-take", e);
                return 0;
            }
        }

        /// <summary>The player's own machine: how many of the matching items they carry (bag and toolbelt).</summary>
        public static int CountLocal(EntityPlayerLocal p, Func<ItemValue, bool> match)
        {
            if (p == null || match == null) return 0;
            int n = 0;
            ItemStack[][] sources = { p.bag != null ? p.bag.ItemGrid.items : null, p.inventory != null ? p.inventory.ItemGrid.items : null };
            foreach (ItemStack[] slots in sources)
            {
                if (slots == null) continue;
                foreach (ItemStack s in slots) if (s != null && !s.IsEmpty() && s.itemValue != null && match(s.itemValue)) n += s.count;
            }
            return n;
        }
    }
}
