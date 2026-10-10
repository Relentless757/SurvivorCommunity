using System;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// The radio window (Config/XUi_InGame/windows.xml, group "scradio"): status, the two calls with their
    /// cooldowns, the contacts still out there and the survivors in distress. Opened by the radio block's
    /// "Use radio" command while it has power. Every binding is non-null before Init.
    /// </summary>
    [Preserve]
    public class XUiC_ScRadio : XUiController
    {
        private const float RefreshSeconds = 1f;
        private float nextRefresh;
        private string message = "";
        private Resource barterGive = Resource.Food;
        private Resource barterGet = Resource.Meds;

        public EntityPlayerLocal Player
        {
            get { return (xui != null && xui.playerUI != null) ? xui.playerUI.entityPlayer : null; }
        }

        public override void Init()
        {
            base.Init();
            try
            {
                Hook("btnClose", Close);
                Hook("btnDrop", () => { message = ScRadio.RequestDrop(Player) ? Localization.Get("scRadioDropSent") : ""; RefreshBindings(); });
                Hook("btnCall", () => { message = ScRadio.Call(Player) ? Localization.Get("scRadioCalled") : ""; RefreshBindings(); });
                Hook("btnScoutFood", () => DoScout(Resource.Food));
                Hook("btnScoutMeds", () => DoScout(Resource.Meds));
                Hook("btnScoutAmmo", () => DoScout(Resource.Ammo));
                Hook("btnScoutMaterials", () => DoScout(Resource.Materials));
                Hook("btnScoutFuel", () => DoScout(Resource.Fuel));
                // the five classic supplies and water (round wl); luxuries are not bartered
                Hook("btnBarterGive", () => { barterGive = (Resource)(((int)barterGive + 1) % ScBarter.Kinds); if (barterGive == barterGet) barterGive = (Resource)(((int)barterGive + 1) % ScBarter.Kinds); RefreshBindings(); });
                Hook("btnBarterGet", () => { barterGet = (Resource)(((int)barterGet + 1) % ScBarter.Kinds); if (barterGet == barterGive) barterGet = (Resource)(((int)barterGet + 1) % ScBarter.Kinds); RefreshBindings(); });
                Hook("btnDemandPay", () =>
                {
                    string msg;
                    EntityPlayerLocal p = Player;
                    if (!ScOutpostRisk.PayDemand(ScOutpostRisk.FirstDemand(), p, out msg) && p != null) p.PlayOneShot("ui_denied");
                    message = msg ?? "";
                    RefreshBindings();
                });
                Hook("btnDemandRefuse", () =>
                {
                    string msg;
                    ScOutpostRisk.RefuseDemand(ScOutpostRisk.FirstDemand(), Player, out msg);
                    message = msg ?? "";
                    RefreshBindings();
                });
                Hook("btnBarterTrade", () =>
                {
                    string msg;
                    EntityPlayerLocal p = Player;
                    if (!ScBarter.Trade(barterGive, barterGet, p, out msg) && p != null) p.PlayOneShot("ui_denied");
                    message = msg ?? "";
                    RefreshBindings();
                });
            }
            catch (Exception e)
            {
                ScLog.Once("radio-window-init", e);
            }
        }

        private void DoScout(Resource res)
        {
            string msg;
            EntityPlayerLocal p = Player;
            if (!ScScouting.Scout(res, p, out msg) && p != null) p.PlayOneShot("ui_denied");
            message = msg ?? "";
            if (p != null && !string.IsNullOrEmpty(msg)) ScCommunity.Tell(p, msg, "");
            RefreshBindings();
        }

        private void Hook(string id, Action action)
        {
            XUiController c = GetChildById(id);
            if (c == null) { ScLog.Err("radio window: no control named '" + id + "' in windows.xml"); return; }
            c.OnPress += (s, m) => { if (m == -1) action(); };
        }

        public override void OnOpen()
        {
            base.OnOpen();
            try
            {
                message = "";
                if (xui != null && xui.playerUI != null && xui.playerUI.CursorController != null)
                {
                    xui.playerUI.CursorController.Locked = false;
                    xui.playerUI.CursorController.SetCursorHidden(false);
                }
                RefreshBindings();
            }
            catch (Exception e)
            {
                ScLog.Once("radio-window-open", e);
            }
        }

        public override void Update(float _dt)
        {
            base.Update(_dt);
            try
            {
                if (xui == null || xui.playerUI == null || xui.playerUI.windowManager == null || !xui.playerUI.windowManager.IsWindowOpen(ScRadio.WindowGroup)) return;
                float now = Time.unscaledTime;
                if (now < nextRefresh) return;
                nextRefresh = now + RefreshSeconds;
                RefreshBindings();
            }
            catch (Exception e)
            {
                ScLog.Once("radio-window-update", e);
            }
        }

        private void Close()
        {
            try
            {
                if (xui != null && xui.playerUI != null && xui.playerUI.windowManager != null) xui.playerUI.windowManager.Close(ScRadio.WindowGroup);
            }
            catch (Exception e)
            {
                ScLog.Once("radio-window-close", e);
            }
        }

        public override bool GetBindingValueInternal(ref string _value, string _bindingName)
        {
            string why;
            switch (_bindingName)
            {
                case "title": _value = Localization.Get("scRadioTitle"); return true;
                case "status":
                    _value = ScRegistry.Loaded
                        ? string.Format(Localization.Get("scRadioStatus"), ScRadio.Online() ? Localization.Get("scRadioOnline") : Localization.Get("scRadioOffline"),
                                        ScRegistry.Contacts.Count, ScConfig.RadioMaxContacts)
                        : "";
                    return true;
                case "droplabel":
                    _value = ScRadio.CanDrop(out why) ? Localization.Get("scRadioDrop") : ScRadio.DropWhy(why);
                    return true;
                case "dropcolor":
                    _value = ScRadio.CanDrop(out why) ? "50,110,50,255" : "70,70,70,255";
                    return true;
                case "dropdesc":
                    _value = ScRadio.DropMaxDays() > 0 ? string.Format(Localization.Get("scRadioDropDesc"), ScRadio.DropDaysText()) : Localization.Get("scRadioDropsOff");
                    return true;
                case "calllabel":
                    _value = ScRadio.CanCall(out why) ? Localization.Get("scRadioCall") : ScRadio.CallWhy(why);
                    return true;
                case "callcolor":
                    _value = ScRadio.CanCall(out why) ? "50,80,120,255" : "70,70,70,255";
                    return true;
                case "calldesc":
                    _value = string.Format(Localization.Get("scRadioCallDesc"), ScConfig.RadioContactMinDist, ScConfig.RadioContactMaxDist, ScConfig.RadioContactDays, ScConfig.RadioCallDays);
                    return true;
                case "contacts": _value = ContactsText(); return true;
                case "distress": _value = DistressText(); return true;
                case "outposts": _value = OutpostsText(); return true;
                case "demandvisible": _value = (ScOutpostRisk.FirstDemand() != null).ToString(); return true;
                case "demandtext":
                {
                    Outpost d = ScOutpostRisk.FirstDemand();
                    _value = d != null ? string.Format(Localization.Get("scRadioDemandText"), d.DemandAmmo, d.DemandFood, d.Name) : "";
                    return true;
                }
                case "bartergive": _value = string.Format(Localization.Get("scBarterGive"), Math.Max(1, ScConfig.RadioBarterRate), Localization.Get(ScNames.LocKey(barterGive))); return true;
                case "barterget": _value = string.Format(Localization.Get("scBarterGet"), Localization.Get(ScNames.LocKey(barterGet))); return true;
                case "bartercolor": _value = ScBarter.CanTrade(barterGive, barterGet, out why) ? "60,90,60,255" : "70,70,70,255"; return true;
                case "barterdesc":
                    _value = ScBarter.CanTrade(barterGive, barterGet, out why) ? Localization.Get("scRadioBarterDesc") : why ?? "";
                    return true;
                case "scoutcolor": _value = ScScouting.CanScout(out why) ? "60,90,60,255" : "70,70,70,255"; return true;
                case "scoutdesc":
                    _value = ScScouting.CanScout(out why) ? Localization.Get("scRadioScoutDesc") : why ?? "";
                    return true;
                case "message": _value = message ?? ""; return true;
                default: return base.GetBindingValueInternal(ref _value, _bindingName);
            }
        }

        private static string ContactsText()
        {
            if (!ScRegistry.Loaded) return "";
            if (ScRegistry.Contacts.Count == 0) return Localization.Get("scRadioNoContacts");
            var sb = new StringBuilder();
            int today = ScCommunity.Today;
            foreach (var ct in ScRegistry.Contacts)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(string.Format(Localization.Get("scRadioContactLine"), ct.Label, ct.PoiName, Math.Max(0, ct.ExpireDay - today)));
            }
            return sb.ToString();
        }

        /// <summary>Whether the outposts report in through this radio, and what each one sends or keeps waiting.</summary>
        private static string OutpostsText()
        {
            if (!ScRegistry.Loaded) return "";
            bool online = ScOutposts.RadioOnlineAnywhere();
            var sb = new StringBuilder();
            sb.Append(string.Format(Localization.Get(online ? "scRadioOutpostsOnline" : "scRadioOutpostsSilent"), ScOutposts.HeldCount(), ScOutposts.Slots()));
            int lines = 0;
            foreach (Outpost o in ScRegistry.Outposts)
            {
                if (lines >= 6) break;
                sb.Append('\n');
                string alert = ScOutpostRisk.Alert(o);          // an unheard attack is not listed: the radio did not hear it
                string what = alert.Length > 0 ? alert : o.State == OutpostState.Active ? ScOutposts.YieldText(ScOutposts.Estimate(o)) : ScOutposts.StateLabel(o);
                sb.Append(string.Format(Localization.Get("scRadioOutpostLine"), o.Name, ScOutposts.TypeLabel(o), what));
                if (o.PendingTotal > 0) sb.Append(' ').Append(string.Format(Localization.Get("scOpReportPending"), o.PendingTotal));
                lines++;
            }
            if (ScRegistry.Outposts.Count == 0) sb.Append('\n').Append(Localization.Get("scRadioNoOutposts"));
            return sb.ToString();
        }

        private static string DistressText()
        {
            if (!ScRegistry.Loaded) return "";
            var sb = new StringBuilder();
            foreach (var m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.OnRun || !m.RunDistress) continue;
                if (sb.Length > 0) sb.Append('\n');
                int until = ScRunners.IsPhysical(m) ? GameUtils.WorldTimeToDays(m.LegEnd) : m.RunEndDay;   // a pinned runner holds out until the leg ends
                sb.Append(string.Format(Localization.Get(m.RunSafe ? "scRadioDistressSafe" : "scRadioDistressLine"), m.Name, m.RunPoiName, until));
            }
            return sb.Length > 0 ? sb.ToString() : Localization.Get("scRadioNoDistress");
        }
    }
}
