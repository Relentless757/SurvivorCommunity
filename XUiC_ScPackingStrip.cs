using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// The packing controls appended into vanilla's loot window (Config/XUi_InGame/windows.xml): a rect that
    /// only shows while the open container belongs to a packing station. Its buttons pack one category of
    /// the box's contents into bundles (Large first), deposit the bundles into the stockpile, or withdraw one Small.
    /// Bindings are evaluated during XML parsing, before Init and before any container is open, so every
    /// value below is non-null and "false" by default.
    /// Round MP3 (part T): every machine. The box is this machine's own open grid, so the items move here; the stockpile and
    /// the XP are the server's, asked through ScActionsStock (inline on the host: the same lines, in the same order, as before).
    /// A client's stock line is the server's copy (MP1), shown from a request's answer at once until the copy catches up.
    /// </summary>
    [Preserve]
    public class XUiC_ScPackingStrip : XUiController
    {
        private const float RefreshSeconds = 0.5f;
        /// <summary>Round MP3: a client shows an answer's stock line at most this long while its copy has not caught up.</summary>
        private const float PushedSeconds = 5f;
        private float nextRefresh;
        private bool visible;
        private string stock = "";
        private string message = "";
        private float messageUntil;
        // round MP3: a client's stock line from the server's last answer, kept until the copy changes (ScMirror.Version)
        private string pushed;
        private int pushedVersion;
        private float pushedUntil;

        public override void Init()
        {
            base.Init();
            try
            {
                Hook("btnPackFood", () => DoPack(Resource.Food));
                Hook("btnPackMeds", () => DoPack(Resource.Meds));
                Hook("btnPackAmmo", () => DoPack(Resource.Ammo));
                Hook("btnPackMaterials", () => DoPack(Resource.Materials));
                Hook("btnPackFuel", () => DoPack(Resource.Fuel));
                Hook("btnPackWater", () => DoPack(Resource.Water));
                Hook("btnPackLuxury", () => DoPack(Resource.Luxury));
                Hook("btnTakeFood", () => DoWithdraw(Resource.Food));
                Hook("btnTakeMeds", () => DoWithdraw(Resource.Meds));
                Hook("btnTakeAmmo", () => DoWithdraw(Resource.Ammo));
                Hook("btnTakeMaterials", () => DoWithdraw(Resource.Materials));
                Hook("btnTakeFuel", () => DoWithdraw(Resource.Fuel));
                Hook("btnTakeWater", () => DoWithdraw(Resource.Water));
                Hook("btnTakeLuxury", () => DoWithdraw(Resource.Luxury));
                Hook("btnDeposit", DoDeposit);
            }
            catch (Exception e)
            {
                ScLog.Once("packing-strip-init", e);
            }
        }

        private void Hook(string id, Action action)
        {
            XUiController c = GetChildById(id);
            if (c == null) { ScLog.Err("packing strip: no control named '" + id + "' in windows.xml"); return; }
            c.OnPress += (s, m) => { if (m == -1) action(); };   // -1 = left mouse button (NGUI touch id)
        }

        /// <summary>The open station's storage feature, or null.</summary>
        private TEFeatureStorage Station()
        {
            try
            {
                XUiC_LootWindowGroup lg = (windowGroup != null) ? windowGroup.Controller as XUiC_LootWindowGroup : null;
                if (lg == null) return null;
                return ScStockpile.StationOf(lg.te);
            }
            catch (Exception) { return null; }
        }

        private XUiC_LootWindow LootWindow()
        {
            XUiC_LootWindowGroup lg = (windowGroup != null) ? windowGroup.Controller as XUiC_LootWindowGroup : null;
            return lg != null ? lg.lootWindow : null;
        }

        public override void OnOpen()
        {
            base.OnOpen();
            nextRefresh = 0f;        // evaluate the station on the first frame, no stale strip from the last box
            message = "";
        }

        public override void OnClose()
        {
            base.OnClose();
            visible = false;
            stock = "";
            message = "";
            pushed = null;           // round MP3
            RefreshBindings();
        }

        public override void Update(float _dt)
        {
            base.Update(_dt);
            try
            {
                float now = Time.unscaledTime;
                if (now < nextRefresh) return;
                nextRefresh = now + RefreshSeconds;
                TEFeatureStorage st = Station();
                bool v = st != null;
                string s = v ? CurrentStock(now) : "";
                if (message.Length > 0 && now > messageUntil) message = "";
                if (v != visible || s != stock)
                {
                    visible = v;
                    stock = s;
                    RefreshBindings();
                }
            }
            catch (Exception e)
            {
                ScLog.Once("packing-strip-update", e);
            }
        }

        private void Say(string text)
        {
            message = text ?? "";
            messageUntil = Time.unscaledTime + 6f;
            RefreshBindings();
        }

        private void RefreshGrid(TEFeatureStorage st)
        {
            XUiC_LootWindow lw = LootWindow();
#if SC_V34
            if (lw != null && lw.lootContainer != null && st != null) lw.lootContainer.SetSlots(st);      // 3.4: the grid comes with the feature
#else
            if (lw != null && lw.lootContainer != null && st != null) lw.lootContainer.SetSlots(st, st.items);
#endif
        }

        private EntityPlayerLocal Player
        {
            get { return (xui != null && xui.playerUI != null) ? xui.playerUI.entityPlayer : null; }
        }

        // ---- round MP3: requests, and what an answer finds ----

        /// <summary>Round MP3: the stock line - the stockpile (the host) or the copy, or a client's last answer until the copy changes.</summary>
        private string CurrentStock(float now)
        {
            if (pushed != null && (ScMirror.Version != pushedVersion || now > pushedUntil || !ScMirror.Active)) pushed = null;
            return pushed ?? ScStockpile.StockText();
        }

        /// <summary>Round MP3: after a stock op - the host reads the stockpile itself (as before); a client shows the answer's figures.</summary>
        private void TakeStock(ScActionReply reply)
        {
            int[] n = ScMirror.Active ? ScActionsStock.StockFromHint(reply) : null;
            if (n != null)
            {
                pushed = ScStockpile.StockText(n);
                pushedVersion = ScMirror.Version;
                pushedUntil = Time.unscaledTime + PushedSeconds;
                stock = pushed;
            }
            else stock = CurrentStock(Time.unscaledTime);       // the host: ScStockpile.StockText(), as before
        }

        /// <summary>
        /// Round MP3: whether this machine still has that box open - the strip shows it, it is the world's live one, the player
        /// is accessing it and holds its lock. An answer that finds it closed (a client's, after a round trip) never writes it:
        /// another player may have it open by then. The host's answer is inline, inside the press: the box is the one the old
        /// code wrote, with no new test.
        /// </summary>
        private bool StillOpen(TEFeatureStorage st)
        {
            try
            {
                if (ScCommunity.IsHost) return st != null;
                if (st == null || st.Parent == null || !visible || !ReferenceEquals(Station(), st)) return false;
                World w = ScCommunity.World;
                TileEntity live = w != null ? w.GetTileEntity(st.ToWorldPos()) : null;
                if (live == null || !ReferenceEquals(live.GetSelfOrFeature<TEFeatureStorage>(), st)) return false;
                return st.Parent.IsUserAccessing() && LockManager.Instance.IsLockedByLocalPlayer(st, 0);
            }
            catch (Exception e)
            {
                ScLog.Once("packing-strip-open", e);
                return false;
            }
        }

        /// <summary>Round MP3: an answer's line - in the strip while it still shows that station, else as a tooltip.</summary>
        private void SayFor(TEFeatureStorage st, string text)
        {
            if (ScCommunity.IsHost || (visible && ReferenceEquals(Station(), st))) { Say(text); return; }
            if (!string.IsNullOrEmpty(text)) ScCommunity.Tell(Player, text, "");
        }

        private static string ResName(Resource r)
        {
            return Localization.Get(ScNames.LocKey(r));
        }

        /// <summary>Round MP3: this machine can ask the server (the host always): else the reason, said in the strip.</summary>
        private bool CanAsk()
        {
            string why = ScActionsWindow.RequestRefusal();
            if (why == null) return true;
            Say(ScActions.Localize(why));
            return false;
        }

        private void DoPack(Resource r)
        {
            TEFeatureStorage st = Station();
            if (st == null) return;
            // round MP3: a client packs in its own open grid with its copy's stars, so it needs the copy and the server (XP)
            if (!CanAsk()) return;
            string msg;
            ScStockpile.Pack(st, r, Player, out msg);
            RefreshGrid(st);
            Say(msg);
        }

        private void DoWithdraw(Resource r)
        {
            TEFeatureStorage st = Station();
            if (st == null) return;
            if (!CanAsk()) return;
            // round MP3: the server debits first (stock.withdraw), then the bundle goes into this machine's grid; if the box
            // filled up or was closed in the meantime, the unit goes back (stock.refund) - or, refused, into the bag
            ScActionsStock.RequestWithdraw(st, r, ScStockpile.HasRoom(st), reply =>
            {
                if (!reply.Ok)
                {
                    if (StillOpen(st)) RefreshGrid(st);
                    TakeStock(null);
                    SayFor(st, reply.Message == "scStockEmpty" ? string.Format(Localization.Get("scStockEmpty"), ResName(r)) : reply.Text);
                    return;
                }
                TakeStock(reply);
                if (StillOpen(st) && ScStockpile.AddSmall(st, r))
                {
                    RefreshGrid(st);
                    SayFor(st, string.Format(Localization.Get("scWithdrawn"), ResName(r)));
                    return;
                }
                int ticket = reply.HintInt(ScActionsStock.HintTicket, 0);
                ScActionsStock.RequestRefund(ticket, back =>
                {
                    if (back.Ok)
                    {
                        TakeStock(back);
                        SayFor(st, Localization.Get("scBoxFull"));
                        return;
                    }
                    ItemStack one = ScStockpile.SmallStack(r);
                    if (one != null) ScItemTx.GiveToLocal(Player, new List<ItemStack> { one });
                    SayFor(st, string.Format(Localization.Get("scWithdrawnBag"), ResName(r)));
                });
            });
        }

        private void DoDeposit()
        {
            TEFeatureStorage st = Station();
            if (st == null) return;
            if (!CanAsk()) return;
            // round MP3: the bundles leave this machine's grid first (recorded), then the server credits them (stock.deposit);
            // a refusal puts them back - into the box while it is still open here, else into the bag
            var taken = new List<KeyValuePair<int, ItemStack>>();
            var counts = new int[ScNames.ResourceCount];
            string msg;
            int moved = ScStockpile.TakeBundles(st, taken, counts, out msg);
            RefreshGrid(st);
            if (moved <= 0)
            {
                stock = CurrentStock(Time.unscaledTime);
                Say(msg);
                return;
            }
            ScActionsStock.RequestDeposit(st, moved, counts, reply =>
            {
                if (reply.Ok)
                {
                    TakeStock(reply);
                    SayFor(st, string.Format(Localization.Get("scDepositedUnits"), moved, ScOutposts.Amounts(counts)));
                    return;
                }
                bool open = StillOpen(st);
                int toBag = ScStockpile.PutBack(st, taken, Player, open);
                if (open) RefreshGrid(st);
                TakeStock(null);
                string why = reply.Text;
                if (toBag > 0) why = (string.IsNullOrEmpty(why) ? "" : why + " ") + string.Format(Localization.Get("scDepositBack"), toBag);
                SayFor(st, why);
            });
        }

        public override bool GetBindingValueInternal(ref string _value, string _bindingName)
        {
            switch (_bindingName)
            {
                case "scvisible": _value = visible ? "true" : "false"; return true;
                case "scstock": _value = stock ?? ""; return true;
                case "scmessage": _value = message ?? ""; return true;
            }
            return base.GetBindingValueInternal(ref _value, _bindingName);
        }
    }
}
