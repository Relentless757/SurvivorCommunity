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
    /// R2 (round 27, 3.4 build) - the handheld radio: opened from the Radio tab (XUiC_ScRadioTab) the window is "remote" and
    /// relays through a powered radio station at home (ScRadio.Online); with none it opens and says so (the status line and
    /// the message), and every control that needs the station is grayed with "Home station offline" and refused when pressed
    /// (StationControls). Opened at a radio block (ScRadio.Open sets ScRadio.OpeningAtBlock around its open) it works as
    /// before. The left column's distress section became Supply runs: a runner pick (by name, the first with a radio first)
    /// with where they are, and Call home (decision 6) - ScRunners.Recall for a physical runner who carries a radio, not
    /// pinned down and not on the way home already; they answer on the radio (ScRadioTalk.OnRecalled). It needs no station:
    /// radio to radio. Remote and the handheld gone from the inventory, the window closes.
    /// Round MP5 (part R): every community player uses it. Every button asks the server (ScActionsRadio: radio.drop, radio.call,
    /// radio.scout, radio.barter, radio.demand, radio.siege, radio.allytrade, radio.allycall, radio.recall) on every machine;
    /// on the host and in single player the request runs inline and the answer is shown exactly as the old inline call's
    /// result was. A client's window reads its copy of the registry for the lists (contacts, outposts, distress, enclaves,
    /// requests, allies, runners, the siege) and the server's view for what the copy lacks (radio.view: readiness and reasons,
    /// the call that is on, the siege's clock and answer, the recall), asked every ViewSeconds while open, one at a time
    /// (ScRadioView, parsed once per answer: the bindings read fields). At a station a client's window closes beyond
    /// ScActionsRadio.StationReach of it (the server refuses farther). The host's bindings read the registry as before.
    /// </summary>
    [Preserve]
    public class XUiC_ScRadio : XUiController
    {
        /// <summary>The window's size in windows.xml (windowScRadio), for ScMothUI.FitPage.</summary>
        private const int PageWidth = 1384, PageHeight = 940;

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
#if SC_V34

        // ---- R2 (round 27): the handheld radio. Every field is set here, not in Init (bindings are read while the XML is parsed) ----
        /// <summary>Opened over a handheld radio (the Radio tab), not at a radio block: the station's controls relay through a powered station at home.</summary>
        private bool remote;
        /// <summary>The runners out now, by name: the Supply runs section's pick (rebuilt on open and once a second).</summary>
        private readonly List<Member> runners = new List<Member>();
        /// <summary>The picked runner's member id; -1 or gone: the first who carries a radio, else the first.</summary>
        private int runId = -1;
        /// <summary>The controls that act through the station: refused while remote with the home station offline (Hook).</summary>
        private static readonly HashSet<string> StationControls = new HashSet<string>(StringComparer.Ordinal)
        {
            "btnDrop", "btnCall", "btnScoutFood", "btnScoutMeds", "btnScoutAmmo", "btnScoutMaterials", "btnScoutFuel", "btnBarterTrade",
            "btnDemandPay", "btnDemandRefuse", "btnSiegeGo", "btnSiegeCant", "btnAllyTrade", "btnAllyFighter", "btnAllyDrop"
        };
#endif

        // ---- round MP5 (part R): a client's window - the server's view of what its copy of the registry lacks (radio.view) ----
        // Every field is set here, not in Init (bindings are read while the XML is parsed).
        private readonly ScRadioView view = new ScRadioView();
        /// <summary>A client asks for the view this often while the window is open, and again after this long without an answer.</summary>
        private const float ViewSeconds = 1.5f, ViewWaitSeconds = 10f;
        /// <summary>Bumped at each opening: an answer of an earlier one is dropped.</summary>
        private int viewToken;
        private bool viewAsking, viewAgain;
        private float viewAskedAt = -100f;

        /// <summary>A client's window (the copy of the registry, the server's view); false on the host and in single player, which read the registry as before.</summary>
        private static bool Client { get { return !ScCommunity.IsHost; } }

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
                // round MP5 (part R): every act is a request (ScActionsRadio) - inline on the host, the old lines and sounds; the verb
                // tells the drop's and the call's outcome itself, so their answers carry words only when the request was refused
                // before the verb (a client too far from the station, a server that cannot answer)
                Hook("btnDrop", () => Act(ScActionsRadio.OpDrop, r => { Sound(r); message = r.Ok ? Localization.Get("scRadioDropSent") : r.Text; RefreshBindings(); }));
                Hook("btnCall", () => Act(ScActionsRadio.OpCall, r => { Sound(r); message = r.Ok ? Localization.Get("scRadioCalled") : r.Text; RefreshBindings(); }));
                Hook("btnScoutFood", () => DoScout(Resource.Food));
                Hook("btnScoutMeds", () => DoScout(Resource.Meds));
                Hook("btnScoutAmmo", () => DoScout(Resource.Ammo));
                Hook("btnScoutMaterials", () => DoScout(Resource.Materials));
                Hook("btnScoutFuel", () => DoScout(Resource.Fuel));
                // the five classic supplies and water (round wl); luxuries are not bartered
                Hook("btnBarterGive", () => { barterGive = (Resource)(((int)barterGive + 1) % ScBarter.Kinds); if (barterGive == barterGet) barterGive = (Resource)(((int)barterGive + 1) % ScBarter.Kinds); AskView(true); RefreshBindings(); });   // round MP5 (part R): a client asks for the new pair's view
                Hook("btnBarterGet", () => { barterGet = (Resource)(((int)barterGet + 1) % ScBarter.Kinds); if (barterGet == barterGive) barterGet = (Resource)(((int)barterGet + 1) % ScBarter.Kinds); AskView(true); RefreshBindings(); });
                // round MP5 (part R): the demand, the barter and the siege's answers as requests (the outpost and the siege the window
                // shows go along: another one on the server by then is refused, never answered in its place)
                Hook("btnDemandPay", () => Act(ScActionsRadio.OpDemand, r => { Sound(r); message = r.Text; RefreshBindings(); }, DemandId(), 1));
                Hook("btnDemandRefuse", () => Act(ScActionsRadio.OpDemand, r => { Sound(r); message = r.Text; RefreshBindings(); }, DemandId(), 0));
                Hook("btnBarterTrade", () => Act(ScActionsRadio.OpBarter, r => { Sound(r); message = r.Text; RefreshBindings(); }, (int)barterGive, (int)barterGet));
                // E6: a siege call answered - "On our way" (thanks, nothing else changes), "We can't come" (pressed twice: lost)
                Hook("btnSiegeGo", () =>
                {
                    confirmCantUntil = 0f;
                    Act(ScActionsRadio.OpSiege, r => { Sound(r); siegeHint = r.Text; BuildEnclaves(); RefreshBindings(); }, 1, SiegeId());
                });
                Hook("btnSiegeCant", () =>
                {
                    float now = Time.unscaledTime;
                    if (now > confirmCantUntil)
                    {
                        confirmCantUntil = now + 5f;
                        siegeHint = Localization.Get("scRadioSiegeCantConfirm");
                        RefreshBindings();
                        return;
                    }
                    confirmCantUntil = 0f;
                    Act(ScActionsRadio.OpSiege, r => { Sound(r); siegeHint = r.Text; BuildEnclaves(); RefreshBindings(); }, 2, SiegeId());
                });
                // E7: the Allies panel - the next ally (nearest first), what you give, what you get (a scavengers' group), the trade
                Hook("btnAllyPick", () =>
                {
                    if (allies.Count > 1) PickAlly(allies[(IndexOfAlly() + 1) % allies.Count]);
                    allyHint = "";
                    AskView(true);              // round MP5 (part R): a client asks for the new pick's view
                    RefreshBindings();
                });
                Hook("btnAllyGive", () =>
                {
                    allyGive = ScEncBarter.NextGive(allyGive, allyGet);
                    allyHint = "";
                    AskView(true);              // round MP5 (part R)
                    RefreshBindings();
                });
                Hook("btnAllyGet", () =>
                {
                    Enclave a = PickedAlly();
                    if (a == null || !ScEncBarter.ChoosesGet(a)) return;
                    allyGet = ScEncBarter.NextGet(a, allyGet);
                    allyGive = ScEncBarter.FixGive(allyGive, allyGet);
                    allyHint = "";
                    AskView(true);              // round MP5 (part R)
                    RefreshBindings();
                });
                Hook("btnAllyTrade", () => Act(ScActionsRadio.OpAllyTrade, r =>      // round MP5 (part R): a request
                {
                    Sound(r);
                    allyHint = r.Text;
                    BuildEnclaves();
                    RefreshBindings();
                }, PickedAllyId(), (int)allyGive, (int)allyGet));
                // E9: the radio commands - a fighter, a supply drop (the call that is on then shows on the hint line)
                Hook("btnAllyFighter", () => DoCall(ScEncCalls.Fighter));
                Hook("btnAllyDrop", () => DoCall(ScEncCalls.Drop));
#if SC_V34
                // R2: the Supply runs section - the next runner (by name), and Call home over the radio (decision 6)
                Hook("btnRunPick", () =>
                {
                    if (runners.Count > 1) runId = runners[(IndexOfRunner() + 1) % runners.Count].Id;
                    AskView(true);              // round MP5 (part R): a client asks for the new pick's view
                    RefreshBindings();
                });
                Hook("btnRunRecall", DoRecall);
#endif
            }
            catch (Exception e)
            {
                ScLog.Once("radio-window-init", e);
            }
        }

        /// <summary>E9: a call to the picked ally. Made: the radio's answer is told and the hint shows the call; refused: the reason, the denied sound.</summary>
        private void DoCall(string kind)
        {
            // round MP5 (part R): a request (radio.allycall) - the call comes to whoever made it
            Act(ScActionsRadio.OpAllyCall, r =>
            {
                Sound(r);
                allyHint = r.Ok ? "" : r.Text;
                BuildEnclaves();
                RefreshBindings();
            }, PickedAllyId(), kind == ScEncCalls.Drop ? 1 : 0);
        }

        private void DoScout(Resource res)
        {
            // round MP5 (part R): a request (radio.scout); the answer's words are Scout's, shown and told here as before
            Act(ScActionsRadio.OpScout, r =>
            {
                EntityPlayerLocal p = Player;
                Sound(r);
                message = r.Text;
                if (p != null && !string.IsNullOrEmpty(message)) ScCommunity.Tell(p, message, "");
                RefreshBindings();
            }, (int)res);
        }

        // ---- round MP5 (part R): the requests and the client's view ----

        /// <summary>
        /// One act of the window as a request (ScActionsRadio): the radio it is used at goes first - mode, x, y, z (at a station
        /// its block, ScRadio.CurrentRadio; over a handheld radio nothing) - then the op's own numbers. On the host the answer comes
        /// before this returns. A client asks for the view again at once, so the readiness follows the act.
        /// </summary>
        private void Act(string op, Action<ScActionReply> done, params int[] extra)
        {
            int n = extra != null ? extra.Length : 0;
            var ints = new int[4 + n];
            FillRadio(ints);
            for (int i = 0; i < n; i++) ints[4 + i] = extra[i];
            int token = viewToken;
            ScActions.Request(op, ints, null, r =>
            {
                if (done != null) done(r);
                if (token == viewToken) AskView(true);
            });
        }

        /// <summary>mode, x, y, z of the radio this window is used at, into ints[0..3].</summary>
        private void FillRadio(int[] ints)
        {
#if SC_V34
            bool handheld = remote;
#else
            bool handheld = false;
#endif
            Vector3i at = !handheld && ScRadio.CurrentRadio.HasValue ? ScRadio.CurrentRadio.Value : Vector3i.zero;
            ints[0] = handheld ? ScActionsRadio.ModeHandheld : ScActionsRadio.ModeStation;
            ints[1] = at.x;
            ints[2] = at.y;
            ints[3] = at.z;
        }

        /// <summary>An answer's sound, on this machine (the old inline code's PlayOneShot).</summary>
        private void Sound(ScActionReply r)
        {
            EntityPlayerLocal p = Player;
            if (p != null && r != null && !string.IsNullOrEmpty(r.Sound)) p.PlayOneShot(r.Sound);
        }

        private static int DemandId()
        {
            Outpost d = ScOutpostRisk.FirstDemand();
            return d != null ? d.Id : -1;
        }

        private static int SiegeId()
        {
            EnclaveRequest r = ScEncSiege.Open;
            return r != null ? r.Id : -1;
        }

        private int PickedAllyId()
        {
            Enclave a = PickedAlly();
            return a != null ? a.Id : -1;
        }

        private int PickedRunnerId()
        {
#if SC_V34
            Member m = PickedRunner();
            return m != null ? m.Id : -1;
#else
            return -1;
#endif
        }

        /// <summary>A client's view starts over (an opening): an answer of an earlier one is dropped.</summary>
        private void ResetView()
        {
            view.Clear();
            viewToken++;
            viewAsking = viewAgain = false;
            viewAskedAt = -100f;
        }

        /// <summary>
        /// A client asks the server for the view (radio.view, quiet) - every ViewSeconds, or now (an opening, a pick, an act); one
        /// request at a time (a "now" while one is on its way asks again after it; none answered in ViewWaitSeconds: asked again).
        /// The host never asks: its bindings read the registry.
        /// </summary>
        private void AskView(bool now)
        {
            if (!Client || !ScActionsWindow.CanRequest) return;
            float t = Time.unscaledTime;
            if (viewAsking && t - viewAskedAt > ViewWaitSeconds) viewAsking = false;
            if (viewAsking)
            {
                if (now) viewAgain = true;
                return;
            }
            if (!now && t - viewAskedAt < ViewSeconds) return;
            viewAsking = true;
            viewAgain = false;
            viewAskedAt = t;
            var ints = new int[10];
            FillRadio(ints);
            ints[4] = (int)barterGive;
            ints[5] = (int)barterGet;
            ints[6] = PickedAllyId();
            ints[7] = (int)allyGive;
            ints[8] = (int)allyGet;
            ints[9] = PickedRunnerId();
            int token = viewToken;
            ScActions.Request(ScActionsRadio.OpView, ints, null, r =>
            {
                try
                {
                    if (token != viewToken) return;              // an earlier opening's answer
                    viewAsking = false;
                    if (!view.Read(r) && !r.Ok)
                    {
                        // round MP5 (review R-m2): refused (too far, the station gone, dead) - the old readiness goes, so the
                        // controls gray, and the reason always shows; a station that is gone closes the window
                        view.Clear();
                        message = r.Text;
                        if (r.Message == "scRadioGone")
                        {
                            EntityPlayerLocal gp = Player;      // re-check F-n1: the window goes, so the reason is a tooltip
                            if (gp != null) ScCommunity.TellKey(gp, "scRadioGone", "ui_denied");
                            Close();
                            return;
                        }
                    }
                    if (IsShowing()) RefreshBindings();
                    if (viewAgain) AskView(true);
                }
                catch (Exception e) { ScLog.Once("radio-window-view", e); }
            });
        }

        private bool IsShowing()
        {
            return xui != null && xui.playerUI != null && xui.playerUI.windowManager != null && xui.playerUI.windowManager.IsWindowOpen(ScRadio.WindowGroup);
        }

        /// <summary>A client at a station: farther from its block than the server lets it act (a meter short of StationReach, for lag).</summary>
        private bool WalkedAway()
        {
            EntityPlayerLocal p = Player;
            if (p == null || !ScRadio.CurrentRadio.HasValue) return false;
            Vector3i at = ScRadio.CurrentRadio.Value;
            return Vector3.Distance(p.position, new Vector3(at.x + 0.5f, at.y + 0.5f, at.z + 0.5f)) > ScActionsRadio.StationReach - 1f;
        }

        // ---- round MP5 (part R): each control's readiness and words - the registry on the host (exactly the old expressions), the
        // server's view on a client (for the pick it was asked for: another pick shows "not ready" until its answer) ----

        private bool DropOk(out string why)
        {
            if (Client) { why = view.DropWhy; return view.DropReady; }
            string key;
            bool ok = ScRadio.CanDrop(out key);
            why = ok ? null : ScRadio.DropWhy(key);
            return ok;
        }

        private bool CallOk(out string why)
        {
            if (Client) { why = view.CallWhy; return view.CallReady; }
            string key;
            bool ok = ScRadio.CanCall(out key);
            why = ok ? null : ScRadio.CallWhy(key);
            return ok;
        }

        private bool ScoutOk(out string why)
        {
            if (Client) { why = view.ScoutWhy; return view.ScoutReady; }
            return ScScouting.CanScout(out why);
        }

        private bool BarterOk(out string why)
        {
            if (!Client) return ScBarter.CanTrade(barterGive, barterGet, out why);
            bool mine = view.BarterGive == (int)barterGive && view.BarterGet == (int)barterGet;
            why = mine ? view.BarterWhy : "";
            return mine && view.BarterReady;
        }

        /// <summary>A client: the view was asked for the ally picked now.</summary>
        private bool AllyAnswered() { return view.Has && view.AllyId == PickedAllyId(); }

        private bool AllyTradeOk(out string why)
        {
            if (!Client) return ScEncBarter.CanTrade(PickedAlly(), allyGive, allyGet, out why);
            bool mine = AllyAnswered() && view.AllyGive == (int)allyGive && view.AllyGet == (int)allyGet;
            why = mine ? view.AllyTradeWhy : "";
            return mine && view.AllyTradeReady;
        }

        private bool CallAllyOk(string kind, out string why)
        {
            if (!Client) return ScEncCalls.CanCall(PickedAlly(), kind, false, out why);
            bool mine = AllyAnswered();
            bool drop = kind == ScEncCalls.Drop;
            why = mine ? (drop ? view.AllyDropWhy : view.FighterWhy) : "";
            return mine && (drop ? view.AllyDropReady : view.FighterReady);
        }

        /// <summary>The call that is on with the picked ally ("" none): ScEncCalls.StateText, a client's from the view.</summary>
        private string AllyCallText()
        {
            if (!Client) return ScEncCalls.StateText(PickedAlly());
            return AllyAnswered() ? view.AllyCall : "";
        }

        private int AllyGiveN() { return Client && view.Has ? view.AllyN : ScLifeSkills.AllyBarter().Give; }
        private int AllyGetN() { return Client && view.Has ? view.AllyGetN : ScLifeSkills.AllyBarter().Get; }
        private int RadioGiveN() { return Client && view.Has ? view.BarterN : ScLifeSkills.RadioBarter().Give; }
        private int RadioGetN() { return Client && view.Has ? view.BarterGetN : ScLifeSkills.RadioBarter().Get; }

        /// <summary>The home station has power: ScRadio.Online, a client's from the view once it came (its copy's derived block before).</summary>
        private bool OnlineNow() { return Client && view.Has ? view.Online : ScRadio.Online(); }

        private bool SiegeAcked(EnclaveRequest r)
        {
            if (!Client) return ScEncSiege.Acknowledged(r);
            return r != null && view.SiegeId == r.Id && view.SiegeAcked;
        }

        private string SiegeClock(EnclaveRequest r)
        {
            if (!Client) return ScEncSiege.Countdown(r);
            return r != null && view.SiegeId == r.Id ? view.SiegeClock : "";
        }

        /// <summary>The drop's line: the sandbox's days (a client's from the server's view: the setting is the server's).</summary>
        private string DropDesc()
        {
            if (!Client) return ScRadio.DropMaxDays() > 0 ? string.Format(Localization.Get("scRadioDropDesc"), ScRadio.DropDaysText()) : Localization.Get("scRadioDropsOff");
            int a = view.Has ? view.DropMin : ScRadio.DropMinDays(), b = view.Has ? view.DropMax : ScRadio.DropMaxDays();
            return b > 0 ? string.Format(Localization.Get("scRadioDropDesc"), a == b ? a.ToString() : a + "-" + b) : Localization.Get("scRadioDropsOff");
        }

        private void Hook(string id, Action action)
        {
            XUiController c = GetChildById(id);
            if (c == null) { ScLog.Err("radio window: no control named '" + id + "' in windows.xml"); return; }
#if SC_V34
            // R2: a control that acts through the station is refused while remote with the home station offline
            bool station = StationControls.Contains(id);
            c.OnPress += (s, m) =>
            {
                if (m != -1) return;
                if (station && !StationUp()) { RefuseOffline(); return; }
                action();
            };
#else
            c.OnPress += (s, m) => { if (m == -1) action(); };
#endif
        }
#if SC_V34

        // ---- R2 (round 27): the handheld radio ----

        /// <summary>The station can act: at a radio block (its power was checked as it opened), or remote with a powered station at home.</summary>
        private bool StationUp() { return !remote || OnlineNow(); }     // round MP5 (part R): a client's from the server's view

        private void RefuseOffline()
        {
            EntityPlayerLocal p = Player;
            if (p != null) p.PlayOneShot("ui_denied");
            message = Localization.Get("scRadioRemoteOffline");
            RefreshBindings();
        }

        /// <summary>The runners out now, by name; the pick kept while they are still out, else the first with a radio, else the first.</summary>
        private void BuildRunners()
        {
            runners.Clear();
            try
            {
                if (!ScRegistry.Ready) return;            // round MP5 (part R): a client's copy too
                foreach (Member m in ScRegistry.Members.Values) if (m.Status == MemberStatus.OnRun) runners.Add(m);
                runners.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                if (IndexOfRunner() >= 0) return;
                runId = -1;
                foreach (Member m in runners) if (m.RunRadio) { runId = m.Id; break; }
                if (runId < 0 && runners.Count > 0) runId = runners[0].Id;
            }
            catch (Exception e)
            {
                ScLog.Once("radio-window-runners", e);
            }
        }

        private int IndexOfRunner()
        {
            for (int i = 0; i < runners.Count; i++) if (runners[i].Id == runId) return i;
            return -1;
        }

        private Member PickedRunner()
        {
            int i = IndexOfRunner();
            return i >= 0 ? runners[i] : null;
        }

        /// <summary>Decision 6: the picked runner told to come home - ScRunners.Recall, then their answer on the radio.</summary>
        private void DoRecall()
        {
            // round MP5 (part R): a request (radio.recall: CanRecall, ScRunners.Recall, ScRadioTalk.OnRecalled on the server)
            Act(ScActionsRadio.OpRecall, r =>
            {
                Sound(r);
                message = r.Text;
                BuildRunners();
                RefreshBindings();
            }, PickedRunnerId());
        }

        /// <summary>Round MP5 (part R): the picked runner's recall - ScRadioTalk.CanRecall on the host, the server's view on a client.</summary>
        private bool RecallOk(out string why)
        {
            if (!Client) return ScRadioTalk.CanRecall(PickedRunner(), out why);
            bool mine = view.Has && view.RunId == PickedRunnerId();
            why = mine ? view.RecallWhy : "";
            return mine && view.RecallReady;
        }

        /// <summary>
        /// R2's bindings, asked before the rest: the Supply runs section, the two controls R2 gave a ready binding, the remote
        /// title and status line - and, while remote with the home station offline, every station control grayed with the reason.
        /// </summary>
        private bool R2Binding(string name, ref string value)
        {
            string why;
            switch (name)
            {
                case "runvisible": value = (runners.Count > 0).ToString(); return true;
                case "runnone": value = runners.Count == 0 && ScRegistry.Ready ? Localization.Get("scRadioRunNone") : ""; return true;   // round MP5 (part R): a client's copy too
                case "runname": { Member m = PickedRunner(); value = m != null ? m.Name : ""; return true; }
                case "runpickready": value = (runners.Count > 1).ToString(); return true;
                case "runstate": { Member m = PickedRunner(); value = m != null ? ScRuns.StatusLine(m) : ""; return true; }
                case "runrecallready": value = RecallOk(out why).ToString(); return true;                                          // round MP5 (part R)
                case "runrecalllabel": value = RecallOk(out why) ? Localization.Get("scRadioRunRecall") : why ?? ""; return true;
                case "demandready": value = StationUp().ToString(); return true;
                case "siegecantready": value = (SiegeCalled() && StationUp()).ToString(); return true;
                case "title": value = Localization.Get(remote ? "scRadioTitleRemote" : "scRadioTitle"); return true;
                case "status":
                    if (!remote) return false;                                     // the station's own line
                    value = ScRegistry.Ready                                       // round MP5 (part R): a client's copy, the server's word on the station
                        ? string.Format(Localization.Get("scRadioStatusRemote"), Localization.Get(OnlineNow() ? "scRadioOnline" : "scRadioRemoteDown"),
                                        ScRegistry.Contacts.Count, ScConfig.RadioMaxContacts)
                        : "";
                    return true;
            }
            if (StationUp()) return false;
            switch (name)
            {
                case "dropready": case "callready": case "scoutready": case "barterready":
                case "allyfighterready": case "allydropready": case "allytradeready": case "siegegoready":
                    value = "False";
                    return true;
                case "droplabel": case "calllabel": case "allyfighterlabel": case "allydroplabel": case "scoutdesc": case "barterdesc":
                    value = Localization.Get("scRadioRemoteOfflineShort");
                    return true;
                case "dropcolor": case "callcolor": case "bartercolor": case "scoutcolor":
                    value = "70,70,70,255";
                    return true;
                // allyhint stays its own: a call that is on still shows, and a trade's reason is already "no power" (ScRadio.Usable)
            }
            return false;
        }
#endif

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
#if SC_V34
                // R2: at a radio block (ScRadio.Open) or over a handheld radio (the Radio tab): remote relays through the home station
                remote = !ScRadio.OpeningAtBlock;
                if (remote)
                {
                    ScRadio.CurrentRadio = null;                   // no block in reach: ScRadio.Usable is the home station's power alone
                    bool up = ScRadio.Online();
                    ScHandheld.Count(up ? "remote opens" : "remote opens, home station offline");
                    if (!up) message = Localization.Get("scRadioRemoteOffline");
                }
                BuildRunners();
                // round ui2: MothUI's frame (no-op without it), held in Update; the bar comes with this page while the Radio
                // tab shows (a handheld radio carried, XUiC_ScPageGroup), here before the group opens it
                ScMothUI.FitPage(this, PageWidth, PageHeight, ScHandheld.Carries(Player));
#endif
                BuildEnclaves();
                // round MP5 (part R): a client's view starts over and is asked at once (the host never asks)
                ResetView();
                AskView(true);
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
            ScMothUI.FitPage(this, PageWidth, PageHeight, ScMothUI.FrameOpen(xui));   // round ui2: after the view's updateData
            try
            {
                if (xui == null || xui.playerUI == null || xui.playerUI.windowManager == null || !xui.playerUI.windowManager.IsWindowOpen(ScRadio.WindowGroup)) return;
                AskView(false);                 // round MP5 (part R): a client's view, every ViewSeconds (a time test; the host never asks)
                float now = Time.unscaledTime;
                if (now < nextRefresh) return;
                nextRefresh = now + RefreshSeconds;
                if (confirmCantUntil > 0f && now > confirmCantUntil)
                {
                    confirmCantUntil = 0f;      // "We can't come" was not pressed again in time
                    siegeHint = "";
                }
#if SC_V34
                // R2: remote, and the handheld radio is no longer carried (dropped, used up by a console command): the link is gone
                if (remote && !ScHandheld.Carries(Player))
                {
                    ScLog.Out("radio window: the handheld radio is no longer carried - closing the remote window");
                    Close();
                    return;
                }
                // round MP5 (part R): a client walked away from the station - the server would refuse its acts from there
                if (!remote && Client && WalkedAway())
                {
                    ScLog.Out("radio window: more than " + ScActionsRadio.StationReach.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                              + " m from the radio station - closing the window");
                    EntityPlayerLocal p = Player;
                    if (p != null) ScCommunity.TellKey(p, "scRadioTooFar", "ui_denied");
                    Close();
                    return;
                }
                BuildRunners();
#endif
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
                if (xui != null && xui.playerUI != null && xui.playerUI.windowManager != null)
                {
                    xui.playerUI.windowManager.Close(ScRadio.WindowGroup);
#if SC_V34
                    // R2: as a page of the player menu the tab bar goes with it (the page group does too: a no-op then)
                    xui.playerUI.windowManager.Close(XUiC_ScPageGroup.PagingGroup);
#endif
                }
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
#if SC_V34
            if (R2Binding(_bindingName, ref _value)) return true;       // R2: Supply runs, remote mode
#else
            if (R2Fallback(_bindingName, ref _value)) return true;      // R2's markup in a 3.2 build: no Supply runs row, the controls as before
#endif
            switch (_bindingName)
            {
                // ---- E7: the Allies panel ----
                case "allyname": { Enclave a = PickedAlly(); _value = a != null ? a.Name : Localization.Get("scRadioAllyNone"); return true; }
                case "allystate": _value = AllyState(); return true;
                case "allypickready": _value = (allies.Count > 1).ToString(); return true;
                case "allyvisible": _value = (PickedAlly() != null && ScConfig.EnclaveBarter).ToString(); return true;
                case "allynone": _value = AllyNone(); return true;
                // round m1t: the terms with Bartering's cut (ScLifeSkills.AllyBarter). Round MP5 (part R): every readiness, reason and
                // term below through the helpers - the registry on the host (the old expressions), the server's view on a client
                case "allygive":
                    _value = string.Format(Localization.Get("scBarterGive"), AllyGiveN(), Localization.Get(ScNames.LocKey(allyGive)));
                    return true;
                case "allyget":
                    _value = string.Format(Localization.Get("scRadioAllyGet"), AllyGetN(), Localization.Get(ScNames.LocKey(allyGet)));
                    return true;
                case "allygetready": _value = ScEncBarter.ChoosesGet(PickedAlly()).ToString(); return true;
                case "allytradeready": _value = AllyTradeOk(out why).ToString(); return true;
                case "allyhint": _value = AllyHint(); return true;
                case "allyhintcolor": _value = allyHint.Length > 0 || AllyCallText().Length > 0 ? Gold : Dim; return true;
                // ---- E9: the radio commands (each label is the command, or why it can't be made now) ----
                case "allycallvisible": _value = (PickedAlly() != null && ScConfig.EnclaveCalls).ToString(); return true;
                case "allycallnone": _value = AllyCallNone(); return true;
                case "allyfighterready": _value = CallAllyOk(ScEncCalls.Fighter, out why).ToString(); return true;
                case "allyfighterlabel":
                    _value = CallAllyOk(ScEncCalls.Fighter, out why) ? Localization.Get("scRadioAllyFighter") : why ?? "";
                    return true;
                case "allydropready": _value = CallAllyOk(ScEncCalls.Drop, out why).ToString(); return true;
                case "allydroplabel":
                    _value = CallAllyOk(ScEncCalls.Drop, out why) ? Localization.Get("scRadioAllyDrop") : why ?? "";
                    return true;
                // ---- E6: the Enclaves column ----
                case "encnone":
                    _value = encKnown == 0 ? Localization.Get("scRadioEncNone") : encKnown < 0 && ScRegistry.Ready && !ScConfig.Enclaves ? Localization.Get("scRadioEncOff") : "";
                    return true;
                case "encmore": _value = encMore > 0 ? string.Format(Localization.Get("scRadioEncMore"), encMore) : ""; return true;
                case "reqnone": _value = encKnown >= 0 && reqRows.Count == 0 ? Localization.Get("scRadioReqNone") : ""; return true;
                case "reqmore": _value = reqMore > 0 ? string.Format(Localization.Get("scRadioReqMore"), reqMore) : ""; return true;
                case "siegetext": _value = SiegeText(); return true;
                case "siegecolor": _value = SiegeCalled() ? Danger : Body; return true;
                case "siegebuttons": _value = SiegeCalled().ToString(); return true;
                case "siegegoready": _value = (SiegeCalled() && !SiegeAcked(ScEncSiege.Open)).ToString(); return true;
                case "siegegolabel": _value = Localization.Get(SiegeAcked(ScEncSiege.Open) ? "scRadioSiegeGoDone" : "scRadioSiegeGo"); return true;
                case "siegehint": _value = siegeHint ?? ""; return true;
                case "title": _value = Localization.Get("scRadioTitle"); return true;
                case "status":
                    _value = ScRegistry.Ready
                        ? string.Format(Localization.Get("scRadioStatus"), OnlineNow() ? Localization.Get("scRadioOnline") : Localization.Get("scRadioOffline"),
                                        ScRegistry.Contacts.Count, ScConfig.RadioMaxContacts)
                        : "";
                    return true;
                case "droplabel":
                    _value = DropOk(out why) ? Localization.Get("scRadioDrop") : why;
                    return true;
                case "dropcolor":
                    _value = DropOk(out why) ? "50,110,50,255" : "70,70,70,255";
                    return true;
                // Whether each control can act now (Tools/generate_radio_ui.py: a control that cannot keeps its
                // plate but loses its hover plate and its label dims). The *color bindings are kept for any
                // older markup.
                case "dropready": _value = DropOk(out why).ToString(); return true;
                case "callready": _value = CallOk(out why).ToString(); return true;
                case "scoutready": _value = ScoutOk(out why).ToString(); return true;
                case "barterready": _value = BarterOk(out why).ToString(); return true;
                case "dropdesc": _value = DropDesc(); return true;
                case "calllabel":
                    _value = CallOk(out why) ? Localization.Get("scRadioCall") : why;
                    return true;
                case "callcolor":
                    _value = CallOk(out why) ? "50,80,120,255" : "70,70,70,255";
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
                // round m1t: the terms with Bartering's cut and a Haggler's extra (ScLifeSkills.RadioBarter)
                case "bartergive": _value = string.Format(Localization.Get("scBarterGive"), RadioGiveN(), Localization.Get(ScNames.LocKey(barterGive))); return true;
                case "barterget":
                {
                    int barterN = RadioGetN();
                    _value = barterN == 1 ? string.Format(Localization.Get("scBarterGet"), Localization.Get(ScNames.LocKey(barterGet)))
                                    : string.Format(Localization.Get("scRadioAllyGet"), barterN, Localization.Get(ScNames.LocKey(barterGet)));
                    return true;
                }
                case "bartercolor": _value = BarterOk(out why) ? "60,90,60,255" : "70,70,70,255"; return true;
                case "barterdesc":
                    _value = BarterOk(out why) ? Localization.Get("scRadioBarterDesc") : why ?? "";
                    return true;
                case "scoutcolor": _value = ScoutOk(out why) ? "60,90,60,255" : "70,70,70,255"; return true;
                case "scoutdesc":
                    _value = ScoutOk(out why) ? Localization.Get("scRadioScoutDesc") : why ?? "";
                    return true;
                case "message": _value = message ?? ""; return true;
                default: return base.GetBindingValueInternal(ref _value, _bindingName);
            }
        }

#if !SC_V34
        /// <summary>
        /// R2's markup (Tools/generate_radio_ui.py) is shared by both builds: a 3.2 build answers its bindings so no NCalc
        /// expression meets an unknown name - the Supply runs row hidden, the demand and siege buttons as they were.
        /// </summary>
        private static bool R2Fallback(string name, ref string value)
        {
            switch (name)
            {
                case "runvisible": case "runpickready": case "runrecallready": value = "False"; return true;
                case "runnone": case "runname": case "runstate": case "runrecalllabel": value = ""; return true;
                case "demandready": value = "True"; return true;
                case "siegecantready": value = SiegeCalled().ToString(); return true;
            }
            return false;
        }

#endif
        private static string ContactsText()
        {
            if (!ScRegistry.Ready) return "";               // round MP5 (part R): a client's copy too
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
            if (!ScRegistry.Ready) return "";               // round MP5 (part R): a client's copy too
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
            if (!ScRegistry.Ready) return "";               // round MP5 (part R): a client's copy too
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
                if (!ScRegistry.Ready || !ScConfig.Enclaves) return;      // round MP5 (part R): a client's copy too
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
            string call = AllyCallText();               // round MP5 (part R): ScEncCalls.StateText, a client's from the view
            if (call.Length > 0) return call;
            if (a == null || !ScConfig.EnclaveBarter) return "";
            string why;
            if (!AllyTradeOk(out why)) return why ?? "";
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
            return ScRegistry.Ready && r != null && r.Stage == ScEncSiege.StageCalled;     // round MP5 (part R): a client's copy too
        }

        /// <summary>The siege panel's words: none open, called (who, the countdown, where; "you told them" once answered), live (how many left).</summary>
        private string SiegeText()
        {
            if (!ScRegistry.Ready) return "";               // round MP5 (part R): a client's copy too
            EnclaveRequest r = ScEncSiege.Open;
            Enclave e = r != null ? ScEnclaves.ById(r.EnclaveId) : null;
            if (r == null || e == null) return Localization.Get("scRadioSiegeNone");
            if (r.Stage != ScEncSiege.StageCalled) return string.Format(Localization.Get("scRadioSiegeLive"), e.Name, Math.Max(0, r.SiegeSize - r.SiegeKilled));
            EntityPlayerLocal pl = Player;
            // round MP5 (part R): the clock and the answer - a client's from the server's view
            string s = string.Format(Localization.Get("scRadioSiegeCalled"), e.Name, SiegeClock(r), pl != null ? ScEnclaves.Where(pl.position, e.Centre) : e.PoiName);
            if (SiegeAcked(r)) s += "\n" + Localization.Get("scRadioSiegeAcked");
            return s;
        }
    }
}
