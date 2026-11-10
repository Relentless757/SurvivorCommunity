using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// R2 (round 27, research_radio_DESIGN_R2.md): the handheld radio, item scHandheldRadio (Config/items.xml; crafted at a
    /// workbench, found now and then in electrical loot, sold by traders, dropped by SC's raiders now and then).
    ///  - Carried by the player anywhere in the bag or the toolbelt (Carries): the Radio tab shows left of the Community tab
    ///    (XUiC_ScRadioTab) and opens the radio station's window from anywhere ("remote", XUiC_ScRadio), which relays through
    ///    a powered radio station at home - without one it opens and says so, its station pages grayed. Runners who carry
    ///    one are heard (ScRadioTalk), and a distress call reaches the player even with the home station down.
    ///  - Carried by a member (their inventory window or bag: NpcCarries): on a supply run they talk to the player over it
    ///    (ScRadioTalk), may radio for help (decision 5: only with a radio, DistressAllowed) and can be told to come home from
    ///    the radio window. Whether a runner carries one is snapshot into the record (Member.RunRadio) when the run starts and
    ///    refreshed whenever their body is seen (online every 5 s, at each capture), so it is known while the body is unloaded.
    /// Host only (SC is single player), main thread.
    /// </summary>
    public static class ScHandheld
    {
        public const string ItemName = "scHandheldRadio";

        private static ItemValue value;                    // the item, once found (null: not looked up yet, or not loaded)
        private static float nextLookup;
        private static readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Per world (ScConfig.Reset at GameStarting): the item is looked up again, the counters start over.</summary>
        public static void Reset()
        {
            value = null;
            nextLookup = 0f;
            counts.Clear();
        }

        /// <summary>The item, or null while items.xml has none (looked up again every 10 s; logged once in a loaded world).</summary>
        public static ItemValue Item()
        {
            if (value != null) return value;
            float now = Time.unscaledTime;
            if (now < nextLookup) return null;
            nextLookup = now + 10f;
            try
            {
                ItemValue iv = ItemClass.GetItem(ItemName, false);
                if (iv != null && !iv.IsEmpty() && iv.type > 0) value = iv;
                else if (ScCommunity.World != null)
                    ScLog.Once("handheld-item", "handheld radio: items.xml has no " + ItemName + " - no Radio tab, no runner talk, and no runner can radio for help while DistressNeedsRadio is on");
            }
            catch (Exception e)
            {
                ScLog.Once("handheld-item-lookup", e);
            }
            return value;
        }

        /// <summary>The player carries a handheld radio: anywhere in the toolbelt or the bag.</summary>
        public static bool Carries(EntityPlayer p)
        {
            ItemValue iv = Item();
            if (iv == null || p == null) return false;
            try
            {
                if (p.inventory != null && p.inventory.GetItemCount(iv, false, -1, -1, false) > 0) return true;
                if (p.bag != null && p.bag.GetItemCount(iv, -1, -1, false) > 0) return true;
            }
            catch (Exception e)
            {
                ScLog.Once("handheld-carries", e);
            }
            return false;
        }

        /// <summary>
        /// A survivor's body carries one: their inventory window (SCore's, where the player puts things for them - never
        /// created here), their bag or their toolbelt.
        /// </summary>
        public static bool NpcCarries(EntityAlive e)
        {
            ItemValue iv = Item();
            if (iv == null || e == null) return false;
            try
            {
                if (ScNpc.UsesWindow(e) && HarvestManager.Has(e.entityId))
                {
                    SCoreLootContainer w = HarvestManager.GetOrCreate(e.entityId);
                    if (w != null && Has(w.GetItems(), iv.type)) return true;
                }
                if (e.bag != null && Has(e.bag.ItemGrid.items, iv.type)) return true;
                if (e.inventory != null && e.inventory.GetItemCount(iv, false, -1, -1, false) > 0) return true;
            }
            catch (Exception ex)
            {
                ScLog.Once("handheld-npc-carries", ex);
            }
            return false;
        }

        private static bool Has(ItemStack[] items, int type)
        {
            if (items == null) return false;
            for (int i = 0; i < items.Length; i++)
            {
                ItemStack s = items[i];
                if (s != null && s.count > 0 && s.itemValue != null && s.itemValue.type == type) return true;
            }
            return false;
        }

        /// <summary>
        /// The runner's body was seen: whether it carries a radio goes into the record (Member.RunRadio), for when the body is
        /// unloaded. True when it changed (the caller saves). Logged on a change.
        /// </summary>
        public static bool NoteCarried(Member m, EntityAlive npc, string when)
        {
            if (m == null || npc == null) return false;
            bool has = NpcCarries(npc);
            if (has == m.RunRadio) return false;
            m.RunRadio = has;
            ScLog.Out(m.Name + (has ? " carries a handheld radio" : " carries no handheld radio any more") + " (" + when + ")");
            return true;
        }

        /// <summary>
        /// Decision 5: may this runner radio for help? With DistressNeedsRadio (the default) only while carrying a radio, and the
        /// call must reach someone - the player carrying a handheld, or a radio station at home with power (ScRadio.Online).
        /// Without it, the rule before R2: any runner, while a station at home has power.
        /// </summary>
        public static bool DistressAllowed(Member m, EntityPlayerLocal player)
        {
            if (m == null) return false;
            if (!ScConfig.DistressNeedsRadio) return ScRadio.Online();
            if (!m.RunRadio) { Count("distress roll skipped: runner has no radio"); return false; }
            if (ScRadio.Online() || Carries(player)) return true;
            Count("distress roll skipped: no station or handheld to hear it");
            return false;
        }

        // ------------------------------------------------------------------ raiders ----

        /// <summary>
        /// One of SC's raiders died (ScBandits.OnDied, once per raider): with RaiderRadioChance a handheld radio drops where it
        /// fell, in a backpack of its own labeled with the raider's name - their loot bag is 0-XNPCCore's (zombie-style) and
        /// shared with every other enemy, so the radio cannot go into it.
        /// </summary>
        public static void OnRaiderDied(EntityAlive e)
        {
            try
            {
                if (e == null || ScConfig.RaiderRadioChance <= 0f || !ScCommunity.IsHost) return;
                World w = ScCommunity.World;
                ItemValue iv = Item();
                if (w == null || iv == null || GameManager.Instance == null) return;
                if (w.GetGameRandom().RandomFloat >= ScConfig.RaiderRadioChance) return;
                string className = ScCommunity.ClassExists(EntityClass.FromString("BackpackNPC")) ? "BackpackNPC" : "Backpack";
                Vector3 at = e.GetPosition() + new Vector3(0f, 0.5f, 0f);
                EntityItem pack = EntityFactory.CreateEntity(EntityClass.FromString(className), new Vector3i(at)) as EntityItem;
                if (pack == null) return;
                // as SC's death drop (Patches_Npc.DropBackpack, 3.4): a one-slot grid Bag carried as the creation data's blob
                Bag bag = new Bag(new Vector2i(1, 1), XUiC_ItemStack.StackLocationTypes.LootContainer, null);
                bag.SetSlot(0, new ItemStack(iv.Clone(), 1));
                var ecd = new EntityCreationData(pack, StreamModeWrite.ToClient) { entityName = ScCommunity.DisplayName(e), id = -1 };
                ecd.bagData = StreamUtils.ToBlob(pbw => bag.Write(pbw, StreamModeWrite.ToClient));
                GameManager.Instance.RequestToSpawnEntityServer(ecd);
                pack.OnEntityUnload();
                Count("raider dropped a handheld radio");
                ScLog.Out("raider " + ScCommunity.DisplayName(e) + " (" + e.entityId + ") dropped a handheld radio at " + World.worldToBlockPos(at));
            }
            catch (Exception ex)
            {
                ScLog.Once("handheld-raider-drop", ex);
            }
        }

        // ------------------------------------------------------------------ counters and the console ----

        public static void Count(string what)
        {
            int n;
            counts.TryGetValue(what, out n);
            counts[what] = n + 1;
        }

        /// <summary>This session's counters ("sc radio").</summary>
        public static string Counters()
        {
            if (counts.Count == 0) return "nothing yet";
            var keys = new List<string>(counts.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (string k in keys) sb.Append(sb.Length > 0 ? ", " : "").Append(k).Append('=').Append(counts[k]);
            return sb.ToString();
        }

        /// <summary>
        /// A handheld radio into a member's inventory (sc radio give): their window when they use one, else their bag. A test aid;
        /// in play the player puts one there through their inventory. Null on success, else why not.
        /// </summary>
        public static string GiveTo(Member m)
        {
            ItemValue iv = Item();
            if (iv == null) return "items.xml has no " + ItemName;
            EntityAlive npc = m != null ? ScCommunity.FindNpc(m.Id) : null;
            if (npc == null) return "their body is not loaded";
            ScNpc.AddItem(npc, new ItemStack(iv.Clone(), 1));
            if (ScRunners.IsPhysical(m)) { NoteCarried(m, npc, "sc radio give"); ScRegistry.SaveAtomic(); }
            return null;
        }
    }
}
