using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// The packing controls appended into vanilla's loot window (Config/XUi_InGame/windows.xml): a rect that
    /// only shows while the open container belongs to a packing station. Its buttons pack one category of
    /// the box's contents into bundles, deposit the bundles into the stockpile, or withdraw one.
    /// Bindings are evaluated during XML parsing, before Init and before any container is open, so every
    /// value below is non-null and "false" by default.
    /// </summary>
    [Preserve]
    public class XUiC_ScPackingStrip : XUiController
    {
        private const float RefreshSeconds = 0.5f;
        private float nextRefresh;
        private bool visible;
        private string stock = "";
        private string message = "";
        private float messageUntil;

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
                Hook("btnTakeFood", () => DoWithdraw(Resource.Food));
                Hook("btnTakeMeds", () => DoWithdraw(Resource.Meds));
                Hook("btnTakeAmmo", () => DoWithdraw(Resource.Ammo));
                Hook("btnTakeMaterials", () => DoWithdraw(Resource.Materials));
                Hook("btnTakeFuel", () => DoWithdraw(Resource.Fuel));
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
                string s = v ? ScStockpile.StockText() : "";
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
            if (lw != null && lw.lootContainer != null && st != null) lw.lootContainer.SetSlots(st, st.items);
        }

        private EntityPlayerLocal Player
        {
            get { return (xui != null && xui.playerUI != null) ? xui.playerUI.entityPlayer : null; }
        }

        private void DoPack(Resource r)
        {
            TEFeatureStorage st = Station();
            if (st == null) return;
            string msg;
            ScStockpile.Pack(st, r, Player, out msg);
            RefreshGrid(st);
            Say(msg);
        }

        private void DoWithdraw(Resource r)
        {
            TEFeatureStorage st = Station();
            if (st == null) return;
            string msg;
            ScStockpile.Withdraw(st, r, out msg);
            RefreshGrid(st);
            stock = ScStockpile.StockText();
            Say(msg);
        }

        private void DoDeposit()
        {
            TEFeatureStorage st = Station();
            if (st == null) return;
            string msg;
            ScStockpile.Deposit(st, out msg);
            RefreshGrid(st);
            stock = ScStockpile.StockText();
            Say(msg);
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
