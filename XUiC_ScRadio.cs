using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// The radio window (Config/XUi_InGame/windows.xml, group "scradio"): status, the two calls with their
    /// cooldowns, the contacts still out there and the survivors in distress. Opened by the radio block's
    /// "Use radio" command while it has power. Every binding is non-null before Init.
    /// E6 adds the Enclaves column (Tools/generate_radio_ui.py): the enclaves the player knows, nearest first (standing in
    /// the markers' colours, benefit - blue while it applies - and where), their open requests (what, and the day to answer
    /// or deliver by), and
    /// a siege call with its countdown and "On our way" / "We can't come" (pressed twice) while it is called. Its rows are
    /// built once a second (BuildEnclaves) and bound by index; empty until the first build.
    /// E7 adds the Allies panel above the siege calls (the column's Enclaves list is 10 rows, its requests 3 since): one
    /// Allied enclave picked with a cycling button, nearest first, what they do for you and where; and the barter with them
    /// (ScEncBarter: Give cycles your supplies, Get is theirs - any classic one from a scavengers' group - and Trade, once a
    /// day per ally). E9's radio commands are to reuse the pick.
    /// E9 adds the radio commands to the Allies panel, under the pick (the Enclaves list is 8 rows since): Send a fighter and
    /// Supply drop (ScEncCalls), each greyed with the reason in its label as the station's own calls are; the hint line
    /// shows the call that is on with that ally (on the way, with you until when) when nothing newer is to be said.
    /// </summary>
    [Preserve]
    public class XUiC_ScRadio : XUiController
    {
        private const float RefreshSeconds = 1f;
        private float nextRefresh;
        private string message = "";
        private Resource barterGive = Resource.Food;
        private Resource barterGet = Resource.Meds;

        // ---- E6: the Enclaves column (its row counts are the generator's ENC_ROWS / REQ_ROWS; E7 and E9 made room for the Allies panel) ----
        private const int EncRows = 8, ReqRows = 3;
        /// <summary>The palette's text colours; Ally and Friend are the enclave markers' own (nav_objects.xml: sc_enclave_allied / _friendly).</summary>
        private const string Body = "226,220,208,255", Dim = "190,182,166,255", Off = "130,124,112,255", Ally = "90,170,255,255",
                             Friend = "140,220,120,255", Gold = "206,178,120,255", Danger = "240,150,130,255";
        private sealed class EncRow { public string Name = "", Standing = "", StandingColor = Dim, Benefit = "", BenefitColor = Off, Where = ""; }
        private sealed class ReqRow { public string Enc = "", What = "", Due = "", DueColor = Body; }
        private readonly List<EncRow> encRows = new List<EncRow>();
        private readonly List<ReqRow> reqRows = new List<ReqRow>();
        private int encMore, reqMore, encKnown = -1;
        /// <summary>"We can't come" asks to be pressed twice: until when the second press counts.</summary>
        private float confirmCantUntil;
        private string siegeHint = "";

        // ---- E7: the Allies panel (the generator's allies()): an Allied enclave picked, nearest first, and the barter with them ----
        // Every field is set here, not in Init: the bindings are read while the XML is parsed (xui-gotchas).
        private readonly List<Enclave> allies = new List<Enclave>();
        /// <summary>
        /// The picked ally's enclave id (-1: none). While the window stays open it stays picked even if they stop being Allied,
        /// so the trade greys with the reason; the next open drops it.
        /// </summary>
        private int allyId = -1;
        private Resource allyGive = Resource.Food, allyGet = Resource.Meds;
        /// <summary>The last ally action's words (the trade, or why not) on the hint line, until the next press or open.</summary>
        private string allyHint = "";

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
                // E6: a siege call answered - "On our way" (thanks, nothing else changes), "We can't come" (pressed twice: lost)
                Hook("btnSiegeGo", () =>
                {
                    string msg;
                    EntityPlayerLocal p = Player;
                    confirmCantUntil = 0f;
                    if (!ScEncSiege.OnOurWay(p, out msg) && p != null) p.PlayOneShot("ui_denied");
                    siegeHint = msg ?? "";
                    BuildEnclaves();
                    RefreshBindings();
                });
                Hook("btnSiegeCant", () =>
                {
                    EntityPlayerLocal p = Player;
                    float now = Time.unscaledTime;
                    if (now > confirmCantUntil)
                    {
                        confirmCantUntil = now + 5f;
                        siegeHint = Localization.Get("scRadioSiegeCantConfirm");
                        RefreshBindings();
                        return;
                    }
                    confirmCantUntil = 0f;
                    string msg;
                    if (!ScEncSiege.CantCome(p, out msg) && p != null) p.PlayOneShot("ui_denied");
                    siegeHint = msg ?? "";
                    BuildEnclaves();
                    RefreshBindings();
                });
                // E7: the Allies panel - the next ally (nearest first), what you give, what you get (a scavengers' group), the trade
                Hook("btnAllyPick", () =>
                {
                    if (allies.Count > 1) PickAlly(allies[(IndexOfAlly() + 1) % allies.Count]);
                    allyHint = "";
                    RefreshBindings();
                });
                Hook("btnAllyGive", () =>
                {
                    allyGive = ScEncBarter.NextGive(allyGive, allyGet);
                    allyHint = "";
                    RefreshBindings();
                });
                Hook("btnAllyGet", () =>
                {
                    Enclave a = PickedAlly();
                    if (a == null || !ScEncBarter.ChoosesGet(a)) return;
                    allyGet = ScEncBarter.NextGet(a, allyGet);
                    allyGive = ScEncBarter.FixGive(allyGive, allyGet);
                    allyHint = "";
                    RefreshBindings();
                });
                Hook("btnAllyTrade", () =>
                {
                    string msg;
                    EntityPlayerLocal p = Player;
                    if (!ScEncBarter.Trade(PickedAlly(), allyGive, allyGet, p, out msg) && p != null) p.PlayOneShot("ui_denied");
                    allyHint = msg ?? "";
                    BuildEnclaves();
                    RefreshBindings();
                });
                // E9: the radio commands - a fighter, a supply drop (the call that is on then shows on the hint line)
                Hook("btnAllyFighter", () => DoCall(ScEncCalls.Fighter));
                Hook("btnAllyDrop", () => DoCall(ScEncCalls.Drop));
            }
            catch (Exception e)
            {
                ScLog.Once("radio-window-init", e);
            }
        }

        /// <summary>E9: a call to the picked ally. Made: the radio's answer is told and the hint shows the call; refused: the reason, the denied sound.</summary>
        private void DoCall(string kind)
        {
            string msg;
            EntityPlayerLocal p = Player;
            bool done = ScEncCalls.Call(PickedAlly(), kind, p, false, false, out msg);
            if (!done && p != null) p.PlayOneShot("ui_denied");
            allyHint = done ? "" : msg ?? "";
            BuildEnclaves();
            RefreshBindings();
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
                siegeHint = "";
                confirmCantUntil = 0f;
                // E7: an ally picked last time stays picked while they are still Allied
                allyHint = "";
                Enclave was = allyId >= 0 ? ScEnclaves.ById(allyId) : null;
                if (was == null || !ScEncAllies.IsAlly(was)) allyId = -1;
                if (xui != null && xui.playerUI != null && xui.playerUI.CursorController != null)
                {
                    xui.playerUI.CursorController.Locked = false;
                    xui.playerUI.CursorController.SetCursorHidden(false);
                }
                BuildEnclaves();
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
                if (confirmCantUntil > 0f && now > confirmCantUntil)
                {
                    confirmCantUntil = 0f;      // "We can't come" was not pressed again in time
                    siegeHint = "";
                }
                BuildEnclaves();
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
            if (RowBinding(_bindingName, ref _value)) return true;      // E6: enc<n>..., req<n>...
            switch (_bindingName)
            {
                // ---- E7: the Allies panel ----
                case "allyname": { Enclave a = PickedAlly(); _value = a != null ? a.Name : Localization.Get("scRadioAllyNone"); return true; }
                case "allystate": _value = AllyState(); return true;
                case "allypickready": _value = (allies.Count > 1).ToString(); return true;
                case "allyvisible": _value = (PickedAlly() != null && ScConfig.EnclaveBarter).ToString(); return true;
                case "allynone": _value = AllyNone(); return true;
                case "allygive":
                    _value = string.Format(Localization.Get("scBarterGive"), Math.Max(1, ScConfig.EnclaveBarterGive), Localization.Get(ScNames.LocKey(allyGive)));
                    return true;
                case "allyget":
                    _value = string.Format(Localization.Get("scRadioAllyGet"), Math.Max(1, ScConfig.EnclaveBarterGet), Localization.Get(ScNames.LocKey(allyGet)));
                    return true;
                case "allygetready": _value = ScEncBarter.ChoosesGet(PickedAlly()).ToString(); return true;
                case "allytradeready": _value = ScEncBarter.CanTrade(PickedAlly(), allyGive, allyGet, out why).ToString(); return true;
                case "allyhint": _value = AllyHint(); return true;
                case "allyhintcolor": _value = allyHint.Length > 0 || ScEncCalls.StateText(PickedAlly()).Length > 0 ? Gold : Dim; return true;
                // ---- E9: the radio commands (each label is the command, or why it can't be made now) ----
                case "allycallvisible": _value = (PickedAlly() != null && ScConfig.EnclaveCalls).ToString(); return true;
                case "allycallnone": _value = AllyCallNone(); return true;
                case "allyfighterready": _value = ScEncCalls.CanCall(PickedAlly(), ScEncCalls.Fighter, false, out why).ToString(); return true;
                case "allyfighterlabel":
                    _value = ScEncCalls.CanCall(PickedAlly(), ScEncCalls.Fighter, false, out why) ? Localization.Get("scRadioAllyFighter") : why ?? "";
                    return true;
                case "allydropready": _value = ScEncCalls.CanCall(PickedAlly(), ScEncCalls.Drop, false, out why).ToString(); return true;
                case "allydroplabel":
                    _value = ScEncCalls.CanCall(PickedAlly(), ScEncCalls.Drop, false, out why) ? Localization.Get("scRadioAllyDrop") : why ?? "";
                    return true;
                // ---- E6: the Enclaves column ----
                case "encnone":
                    _value = encKnown == 0 ? Localization.Get("scRadioEncNone") : encKnown < 0 && ScRegistry.Loaded && !ScConfig.Enclaves ? Localization.Get("scRadioEncOff") : "";
                    return true;
                case "encmore": _value = encMore > 0 ? string.Format(Localization.Get("scRadioEncMore"), encMore) : ""; return true;
                case "reqnone": _value = encKnown >= 0 && reqRows.Count == 0 ? Localization.Get("scRadioReqNone") : ""; return true;
                case "reqmore": _value = reqMore > 0 ? string.Format(Localization.Get("scRadioReqMore"), reqMore) : ""; return true;
                case "siegetext": _value = SiegeText(); return true;
                case "siegecolor": _value = SiegeCalled() ? Danger : Body; return true;
                case "siegebuttons": _value = SiegeCalled().ToString(); return true;
                case "siegegoready": _value = (SiegeCalled() && !ScEncSiege.Acknowledged(ScEncSiege.Open)).ToString(); return true;
                case "siegegolabel": _value = Localization.Get(ScEncSiege.Acknowledged(ScEncSiege.Open) ? "scRadioSiegeGoDone" : "scRadioSiegeGo"); return true;
                case "siegehint": _value = siegeHint ?? ""; return true;
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
                // Whether each control can act now (Tools/generate_radio_ui.py: a control that cannot keeps its
                // plate but loses its hover plate and its label dims). The *color bindings are kept for any
                // older markup.
                case "dropready": _value = ScRadio.CanDrop(out why).ToString(); return true;
                case "callready": _value = ScRadio.CanCall(out why).ToString(); return true;
                case "scoutready": _value = ScScouting.CanScout(out why).ToString(); return true;
                case "barterready": _value = ScBarter.CanTrade(barterGive, barterGet, out why).ToString(); return true;
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
                    _value = d != null ? ScOutpostRisk.DemandText(d) : "";      // E10: an enclave's demand names them
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

        // ---- E6: the Enclaves column ----

        /// <summary>
        /// The column's rows, made again (OnOpen, once a second, after a button): the known enclaves on the map, nearest the
        /// player first, the first EncRows of them; the open requests but a siege (it has its own panel), the soonest due first.
        /// Nothing while the registry is not loaded or Enclaves is off (encKnown -1).
        /// </summary>
        private void BuildEnclaves()
        {
            encRows.Clear();
            reqRows.Clear();
            allies.Clear();
            encMore = reqMore = 0;
            encKnown = -1;
            try
            {
                if (!ScRegistry.Loaded || !ScConfig.Enclaves) return;
                EntityPlayerLocal pl = Player;
                Vector3 from = pl != null ? pl.position : ScZone.HasHome ? new Vector3(ScZone.Home.x, ScZone.Home.y, ScZone.Home.z) : Vector3.zero;
                var known = new List<Enclave>();
                foreach (Enclave e in ScEnclaves.All) if (e.Known && !e.Lost) known.Add(e);
                known.Sort((a, b) => a.DistanceXZ(from.x, from.z).CompareTo(b.DistanceXZ(from.x, from.z)));
                encKnown = known.Count;
                for (int i = 0; i < known.Count; i++)
                {
                    if (i >= EncRows) { encMore = known.Count - EncRows; break; }
                    Enclave e = known[i];
                    string band = ScEnclaves.StandingKey(e);
                    var row = new EncRow
                    {
                        Name = e.Name,
                        Standing = ScEnclaves.StandingWord(e),
                        StandingColor = band == "allied" ? Ally : band == "friendly" ? Friend : band == "hostile" ? Danger : Dim,
                        Where = ScEnclaves.Where(from, e.Centre)
                    };
                    if (e.Hostile) row.Benefit = Localization.Get("scRadioEncRaiders");
                    else if (e.TurnedHostile || e.Benefit.Length == 0) row.Benefit = "-";
                    else
                    {
                        row.Benefit = ScEncAllies.Name(e.Benefit);
                        row.BenefitColor = ScEncAllies.IsAlly(e) ? Ally : Off;
                    }
                    encRows.Add(row);
                }
                int now = ScEncRequests.Now;
                var open = new List<EnclaveRequest>();
                foreach (EnclaveRequest r in ScEncRequests.All) if (r.Kind != EnclaveRequest.KindSiege) open.Add(r);
                open.Sort((a, b) => DueOf(a).CompareTo(DueOf(b)));
                for (int i = 0; i < open.Count; i++)
                {
                    if (i >= ReqRows) { reqMore = open.Count - ReqRows; break; }
                    EnclaveRequest r = open[i];
                    Enclave e = ScEnclaves.ById(r.EnclaveId);
                    var row = new ReqRow { Enc = e != null ? e.Name : "?", What = ScEncRequests.WhatText(r) };
                    if (r.IsAccepted)
                    {
                        row.Due = string.Format(Localization.Get("scRadioReqDue"), r.DueDay);
                        row.DueColor = r.DueDay <= now ? Danger : Body;
                    }
                    else
                    {
                        row.Due = string.Format(Localization.Get("scRadioReqAnswer"), r.LapseDay);
                        row.DueColor = Gold;
                    }
                    reqRows.Add(row);
                }
                BuildAllies(from);       // E7
            }
            catch (Exception ex)
            {
                ScLog.Once("radio-window-enclaves", ex);
            }
        }

        // ---- E7: the Allies panel ----

        /// <summary>
        /// The pick's list (BuildEnclaves): the allies, nearest first, plus the ally picked earlier while this window is open
        /// even if no longer Allied (the trade greys "Only allies trade this way."). Nothing picked yet: the nearest.
        /// </summary>
        private void BuildAllies(Vector3 from)
        {
            ScEncBarter.Allies(from, allies);
            if (allyId >= 0 && IndexOfAlly() < 0)
            {
                Enclave was = ScEnclaves.ById(allyId);
                if (was != null && !was.Lost && !was.IsHostile)
                {
                    allies.Add(was);
                    allies.Sort((a, b) => a.DistanceXZ(from.x, from.z).CompareTo(b.DistanceXZ(from.x, from.z)));
                }
                else allyId = -1;
            }
            if (allyId < 0 && allies.Count > 0) PickAlly(allies[0]);
        }

        private int IndexOfAlly()
        {
            for (int i = 0; i < allies.Count; i++) if (allies[i].Id == allyId) return i;
            return -1;
        }

        /// <summary>The picked ally, or null (none, or not in the list).</summary>
        private Enclave PickedAlly()
        {
            int i = IndexOfAlly();
            return i >= 0 ? allies[i] : null;
        }

        /// <summary>A new ally picked: Get starts on their resource (food for a scavengers' group), Give on one that differs.</summary>
        private void PickAlly(Enclave a)
        {
            allyId = a.Id;
            allyGet = ScEncBarter.DefaultGet(a);
            allyGive = ScEncBarter.FixGive(allyGive, allyGet);
        }

        /// <summary>"Munitions Manufacture, 900 m north-east": what they do for you as allies (their standing if no longer), and where.</summary>
        private string AllyState()
        {
            Enclave a = PickedAlly();
            if (a == null) return "";
            EntityPlayerLocal pl = Player;
            string where = pl != null ? ScEnclaves.Where(pl.position, a.Centre) : a.PoiName;
            string what = ScEncAllies.IsAlly(a) ? ScEncAllies.Name(a.Benefit) : ScEnclaves.StandingWord(a);
            return string.IsNullOrEmpty(what) ? where : string.Format(Localization.Get("scRadioAllyState"), what, where);
        }

        /// <summary>Where the barter row would be, while it is hidden: the barter switched off (E9: no ally yet is said a row higher).</summary>
        private string AllyNone()
        {
            if (PickedAlly() == null) return "";
            return ScConfig.EnclaveBarter ? "" : Localization.Get("scEncBarterOff");
        }

        /// <summary>E9: where the commands row would be, while it is hidden: no ally yet (how to get one), or the calls switched off.</summary>
        private string AllyCallNone()
        {
            if (PickedAlly() == null) return ScConfig.Enclaves ? Localization.Get("scRadioAllyNoneDesc") : Localization.Get("scRadioEncOff");
            return ScConfig.EnclaveCalls ? "" : Localization.Get("scEncCallOff");
        }

        /// <summary>
        /// The hint line: the last ally action's words, else (E9) the call that is on with this ally, else why the trade can't be
        /// made now, else how it works.
        /// </summary>
        private string AllyHint()
        {
            if (allyHint.Length > 0) return allyHint;
            Enclave a = PickedAlly();
            string call = ScEncCalls.StateText(a);
            if (call.Length > 0) return call;
            if (a == null || !ScConfig.EnclaveBarter) return "";
            string why;
            if (!ScEncBarter.CanTrade(a, allyGive, allyGet, out why)) return why ?? "";
            return Localization.Get(ScEncBarter.ChoosesGet(a) ? "scRadioAllyBarterDescAny" : "scRadioAllyBarterDesc");
        }

        /// <summary>The day a request is due by (accepted) or must be answered by (offered): the request list's order.</summary>
        private static int DueOf(EnclaveRequest r) { return r.IsAccepted ? r.DueDay : r.LapseDay; }

        /// <summary>
        /// enc&lt;1..EncRows&gt;&lt;name|standing|standingcolor|benefit|benefitcolor|where|visible&gt; and
        /// req&lt;1..ReqRows&gt;&lt;enc|what|due|duecolor|visible&gt;: a row of the column ("" / False past the rows built).
        /// </summary>
        private bool RowBinding(string name, ref string value)
        {
            bool enc = name.StartsWith("enc", StringComparison.Ordinal);
            if (!enc && !name.StartsWith("req", StringComparison.Ordinal)) return false;
            int i = 3, n = 0;
            while (i < name.Length && i < 6 && char.IsDigit(name[i])) { n = n * 10 + (name[i] - '0'); i++; }
            if (i == 3 || n < 1) return false;
            string field = name.Substring(i);
            if (enc)
            {
                if (n > EncRows) return false;
                EncRow r = n <= encRows.Count ? encRows[n - 1] : null;
                switch (field)
                {
                    case "visible": value = (r != null).ToString(); return true;
                    case "name": value = r != null ? r.Name : ""; return true;
                    case "standing": value = r != null ? r.Standing : ""; return true;
                    case "standingcolor": value = r != null ? r.StandingColor : Dim; return true;
                    case "benefit": value = r != null ? r.Benefit : ""; return true;
                    case "benefitcolor": value = r != null ? r.BenefitColor : Off; return true;
                    case "where": value = r != null ? r.Where : ""; return true;
                }
                return false;
            }
            if (n > ReqRows) return false;
            ReqRow q = n <= reqRows.Count ? reqRows[n - 1] : null;
            switch (field)
            {
                case "visible": value = (q != null).ToString(); return true;
                case "enc": value = q != null ? q.Enc : ""; return true;
                case "what": value = q != null ? q.What : ""; return true;
                case "due": value = q != null ? q.Due : ""; return true;
                case "duecolor": value = q != null ? q.DueColor : Body; return true;
            }
            return false;
        }

        /// <summary>A siege is open and still called (the countdown runs; the buttons show).</summary>
        private static bool SiegeCalled()
        {
            EnclaveRequest r = ScEncSiege.Open;
            return ScRegistry.Loaded && r != null && r.Stage == ScEncSiege.StageCalled;
        }

        /// <summary>The siege panel's words: none open, called (who, the countdown, where; "you told them" once answered), live (how many left).</summary>
        private string SiegeText()
        {
            if (!ScRegistry.Loaded) return "";
            EnclaveRequest r = ScEncSiege.Open;
            Enclave e = r != null ? ScEnclaves.ById(r.EnclaveId) : null;
            if (r == null || e == null) return Localization.Get("scRadioSiegeNone");
            if (r.Stage != ScEncSiege.StageCalled) return string.Format(Localization.Get("scRadioSiegeLive"), e.Name, Math.Max(0, r.SiegeSize - r.SiegeKilled));
            EntityPlayerLocal pl = Player;
            string s = string.Format(Localization.Get("scRadioSiegeCalled"), e.Name, ScEncSiege.Countdown(r), pl != null ? ScEnclaves.Where(pl.position, e.Centre) : e.PoiName);
            if (ScEncSiege.Acknowledged(r)) s += "\n" + Localization.Get("scRadioSiegeAcked");
            return s;
        }
    }
}
