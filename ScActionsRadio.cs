using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP5 (multiplayer, part R): what a client's radio window shows that its copy of the registry lacks - the controls'
    /// readiness with their reasons, the call that is on with the picked ally, the siege's clock and answer, the picked
    /// runner's recall - as the server answers radio.view (ScActionsRadio.View). Parsed once per answer (Read), so the window's
    /// bindings only read fields. The picks an answer was made for are kept beside it (BarterGive ... RunId): a binding whose
    /// pick changed since shows "not ready" until the next answer, never another pick's readiness. Reasons are the server's
    /// words (the verbs' own refusals: a formatted text, in the server's language).
    /// </summary>
    public sealed class ScRadioView
    {
        /// <summary>An answer came since the window opened.</summary>
        public bool Has;
        /// <summary>The home station has power (ScRadio.Online on the server).</summary>
        public bool Online;
        public bool DropReady, CallReady, ScoutReady, BarterReady, AllyTradeReady, FighterReady, AllyDropReady, RecallReady, SiegeAcked;
        public string DropWhy = "", CallWhy = "", ScoutWhy = "", BarterWhy = "", AllyTradeWhy = "", FighterWhy = "", AllyDropWhy = "", RecallWhy = "";
        /// <summary>The sandbox's air drop days (ScRadio.DropMinDays / DropMaxDays on the server).</summary>
        public int DropMin, DropMax;
        /// <summary>The terms with Bartering's cut (ScLifeSkills.RadioBarter / AllyBarter on the server).</summary>
        public int BarterN = 1, BarterGetN = 1, AllyN = 1, AllyGetN = 1;
        /// <summary>The call that is on with the picked ally (ScEncCalls.StateText), "" with none.</summary>
        public string AllyCall = "";
        /// <summary>The open siege's request id (-1 none) and its "12:34" to reach them while called.</summary>
        public int SiegeId = -1;
        public string SiegeClock = "";
        /// <summary>The picks this answer was made for.</summary>
        public int BarterGive = -1, BarterGet = -1, AllyId = -1, AllyGive = -1, AllyGet = -1, RunId = -1;

        public void Clear()
        {
            Has = Online = DropReady = CallReady = ScoutReady = BarterReady = AllyTradeReady = FighterReady = AllyDropReady = RecallReady = SiegeAcked = false;
            DropWhy = CallWhy = ScoutWhy = BarterWhy = AllyTradeWhy = FighterWhy = AllyDropWhy = RecallWhy = AllyCall = SiegeClock = "";
            DropMin = DropMax = 0;
            BarterN = BarterGetN = AllyN = AllyGetN = 1;
            SiegeId = BarterGive = BarterGet = AllyId = AllyGive = AllyGet = RunId = -1;
        }

        /// <summary>The view an answer carries (hint v=1), into this; false when the answer has none (a refusal).</summary>
        public bool Read(ScActionReply r)
        {
            if (r == null || string.IsNullOrEmpty(r.Hint)) return false;
            string[] parts = r.Hint.Split(';');
            bool any = false;
            foreach (string part in parts)
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string k = part.Substring(0, eq), v = ScActionsRadio.Unesc(part.Substring(eq + 1));
                if (k == "v") { any = v == "1"; if (any) Clear(); continue; }
                if (!any) continue;
                switch (k)
                {
                    case "on": Online = v == "1"; break;
                    case "dr": DropReady = v == "1"; break;
                    case "dw": DropWhy = v; break;
                    case "dn": DropMin = Num(v); break;
                    case "dx": DropMax = Num(v); break;
                    case "cr": CallReady = v == "1"; break;
                    case "cw": CallWhy = v; break;
                    case "sr": ScoutReady = v == "1"; break;
                    case "sw": ScoutWhy = v; break;
                    case "bg": BarterGive = Num(v, -1); break;
                    case "bt": BarterGet = Num(v, -1); break;
                    case "br": BarterReady = v == "1"; break;
                    case "bw": BarterWhy = v; break;
                    case "bn": BarterN = Num(v, 1); break;
                    case "bm": BarterGetN = Num(v, 1); break;
                    case "ai": AllyId = Num(v, -1); break;
                    case "ag": AllyGive = Num(v, -1); break;
                    case "at": AllyGet = Num(v, -1); break;
                    case "atr": AllyTradeReady = v == "1"; break;
                    case "atw": AllyTradeWhy = v; break;
                    case "afr": FighterReady = v == "1"; break;
                    case "afw": FighterWhy = v; break;
                    case "adr": AllyDropReady = v == "1"; break;
                    case "adw": AllyDropWhy = v; break;
                    case "ac": AllyCall = v; break;
                    case "an": AllyN = Num(v, 1); break;
                    case "am": AllyGetN = Num(v, 1); break;
                    case "si": SiegeId = Num(v, -1); break;
                    case "sc": SiegeClock = v; break;
                    case "sa": SiegeAcked = v == "1"; break;
                    case "ri": RunId = Num(v, -1); break;
                    case "rr": RecallReady = v == "1"; break;
                    case "rw": RecallWhy = v; break;
                }
            }
            if (any) Has = true;
            return any;
        }

        private static int Num(string s, int def = 0)
        {
            int n;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : def;
        }
    }

    /// <summary>
    /// Round MP5 (multiplayer, part R): the radio for every player - the radio window's acts as ScActions requests, and the view a
    /// client's window polls for what its copy of the registry lacks. The window (XUiC_ScRadio) asks on every machine; on the host
    /// and in single player each handler runs inline with the host's own player and its window shows the answer exactly as the
    /// old inline call did (the same tells, lines and sounds).
    ///
    /// Where the radio is: every op starts with mode, x, y, z - mode 0 at a radio station (x, y, z: its block; the window's
    /// ScRadio.CurrentRadio), 1 over a handheld radio (the Radio tab; x, y, z unused). For a remote sender the server checks a
    /// station request against the world (a radio station block there, the sender within StationReach of it, and for an act
    /// through it power there or at the home station: the host's window opens only with power) and notes it as
    /// theirs (ScRadio.NoteStationFor: ScRadio.Usable(who) then reads its power, as the host's window reads CurrentRadio); a
    /// handheld request is trusted (the bag is the client's own: co-op trust, as the audit allows) while HandheldRadioTab is on.
    /// A control that acts through the station is refused over a handheld while the home station is offline
    /// (scRadioRemoteOffline), as the window grays and refuses it. The host's own requests are its own window's: not checked
    /// again. Every verb re-checks its own rules (cooldowns, the stockpile, the ally, the siege) on the server.
    ///
    /// Ops (whole numbers; m x y z = the mode and the station above):
    ///   radio.view      m x y z, barter give, barter get, ally id, ally give, ally get, runner id   (quiet) the view: hints below
    ///   radio.drop      m x y z                     ScRadio.RequestDrop (the planes fly to the sender: ScRadio.DropFor)
    ///   radio.call      m x y z                     ScRadio.Call (a contact at the sender's gamestage)
    ///   radio.scout     m x y z, resource           ScScouting.Scout
    ///   radio.barter    m x y z, give, get          ScBarter.Trade (stockpile only)
    ///   radio.demand    m x y z, outpost id, pay 1/0   ScOutpostRisk.PayDemand / RefuseDemand (stockpile only)
    ///   radio.siege     m x y z, 1 on our way / 2 can't come, siege request id   ScEncSiege.OnOurWay / CantCome
    ///   radio.allytrade m x y z, enclave id, give, get   ScEncBarter.Trade (stockpile only)
    ///   radio.allycall  m x y z, enclave id, 0 fighter / 1 drop   ScEncCalls.Call (it comes to the sender)
    ///   radio.recall    m x y z, member id          ScRunners.Recall + ScRadioTalk.OnRecalled ("Copy that." to the sender)
    /// A pick that changed on the server since the client saw it (another siege, another demand) answers the verb's own "gone"
    /// words or scActionChanged, never the other one's act.
    /// The view's hints (values %-escaped: Esc): v=1; on (home station); dr/dw, dn/dx (drop ready, why, the sandbox days);
    /// cr/cw (call); sr/sw (scout); bg/bt the barter pair answered, br/bw, bn/bm (terms); ai/ag/at the ally pick answered,
    /// atr/atw (trade), afr/afw (fighter), adr/adw (drop), ac (the call that is on), an/am (terms); si (open siege id, -1),
    /// sc (its clock), sa (answered "on our way"); ri the runner answered, rr/rw (recall).
    /// Nothing here moves an item: every trade is the stockpile's (MP3's no-loss rules have nothing to hold).
    /// </summary>
    public static class ScActionsRadio
    {
        public const string OpView = "radio.view", OpDrop = "radio.drop", OpCall = "radio.call", OpScout = "radio.scout", OpBarter = "radio.barter",
                            OpDemand = "radio.demand", OpSiege = "radio.siege", OpAllyTrade = "radio.allytrade", OpAllyCall = "radio.allycall",
                            OpRecall = "radio.recall";
        public const int ModeStation = 0, ModeHandheld = 1;
        /// <summary>A remote player at a radio station acts within this many meters (3D) of its block - a little more than a block's reach, for lag.</summary>
        public const float StationReach = 10f;

        // ------------------------------------------------------------------ the radio a request names ----

        /// <summary>
        /// The radio a request names (args 0..3), checked for a remote sender and noted as theirs (ScRadio.NoteStationFor).
        /// stationControl: the control acts through the station (refused over a handheld with the home station offline). False:
        /// refused, the reply set.
        /// </summary>
        private static bool Radio(EntityPlayer sender, ScActionArgs args, bool stationControl, ScActionReply reply)
        {
            bool handheld = args.Int(0, ModeStation) == ModeHandheld;
            if (!(sender is EntityPlayerLocal))
            {
                if (handheld)
                {
                    // the bag is the client's own (its window closes when the radio leaves it): trusted, while the server has the tab on
                    if (!ScConfig.HandheldRadioTab) { reply.Fail("scRadioTooFar"); return false; }
                    ScRadio.NoteStationFor(sender, null);
                }
                else
                {
                    Vector3i pos = args.Pos(1);
                    string why = WhyNotStation(sender, pos);
                    if (why != null) { reply.Fail(why); return false; }
                    ScRadio.NoteStationFor(sender, pos);
                    if (!ScRadio.Radios.Contains(pos)) ScRadio.NoteRadio(pos);      // as the host's Open notes its own
                    // a powered station (theirs, or the home one): the host's window opens only with power - a client's act
                    // through the station is refused once it has none (the view grays those buttons: View)
                    if (stationControl && !ScRadio.Usable(sender)) { reply.Fail(Localization.Get("scRadioNoPower")); return false; }
                }
            }
            if (handheld && stationControl && !ScRadio.Online()) { reply.Fail("scRadioRemoteOffline"); return false; }
            return true;
        }

        /// <summary>A remote sender's station: a radio station block at pos, within StationReach of them; null when it is, else the refusal's key.</summary>
        private static string WhyNotStation(EntityPlayer sender, Vector3i pos)
        {
            World w = ScCommunity.World;
            if (w == null || sender == null) return "scActionNotReady";
            BlockValue bv = w.GetBlock(pos);
            if (!(bv.Block is BlockScRadio)) return "scRadioGone";
            Vector3 c = new Vector3(pos.x + 0.5f, pos.y + 0.5f, pos.z + 0.5f);
            if (Vector3.Distance(sender.position, c) > StationReach) return "scRadioTooFar";
            if (ScGates.OutsiderAt(pos, sender)) return "scGateNotYourStation";     // round MP6 (part G): another community's station (decision 3)
            return null;
        }

        private static bool ResourceArg(ScActionArgs args, int i, int count, out Resource r)
        {
            int n = args.Int(i, -1);
            r = (Resource)Math.Max(0, n);
            return n >= 0 && n < count;
        }

        // ------------------------------------------------------------------ the view ----

        /// <summary>radio.view: what a client's window needs and its copy lacks (ScRadioView), for the picks it names.</summary>
        [ScAction(OpView, ScActionAccess.Community, Quiet = true)]
        private static void View(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            if (!Radio(sender, args, false, reply)) return;
            string why;
            reply.Done();
            reply.SetHint("v", 1);
            reply.SetHint("on", ScRadio.Online() ? 1 : 0);
            // a remote player at a station that lost its power (and no home station with it): the drop and the call gray as the
            // acts are refused (Radio); the rest say so through their own Usable(who)
            bool powered = sender is EntityPlayerLocal || args.Int(0, ModeStation) == ModeHandheld || ScRadio.Usable(sender);
            string noPower = Localization.Get("scRadioNoPower");
            bool ok = ScRadio.CanDrop(out why) && powered;
            Put(reply, "dr", ok, "dw", ok ? "" : powered ? ScRadio.DropWhy(why) : noPower);
            reply.SetHint("dn", ScRadio.DropMinDays());
            reply.SetHint("dx", ScRadio.DropMaxDays());
            ok = ScRadio.CanCall(out why) && powered;
            Put(reply, "cr", ok, "cw", ok ? "" : powered ? ScRadio.CallWhy(why) : noPower);
            ok = ScScouting.CanScout(out why, sender);
            Put(reply, "sr", ok, "sw", why);
            Resource give, get;
            if (ResourceArg(args, 4, ScNames.ResourceCount, out give) && ResourceArg(args, 5, ScNames.ResourceCount, out get))
            {
                reply.SetHint("bg", (int)give);
                reply.SetHint("bt", (int)get);
                ok = ScBarter.CanTrade(give, get, out why, sender);
                Put(reply, "br", ok, "bw", why);
            }
            ScLifeSkills.Terms radio = ScLifeSkills.RadioBarter();
            reply.SetHint("bn", radio.Give);
            reply.SetHint("bm", radio.Get);
            int allyId = args.Int(6, -1);
            Enclave e = allyId >= 0 ? ScEnclaves.ById(allyId) : null;
            reply.SetHint("ai", allyId);
            if (ResourceArg(args, 7, ScNames.ResourceCount, out give) && ResourceArg(args, 8, ScNames.ResourceCount, out get))
            {
                reply.SetHint("ag", (int)give);
                reply.SetHint("at", (int)get);
                ok = ScEncBarter.CanTrade(e, give, get, out why, sender);
                Put(reply, "atr", ok, "atw", why);
            }
            ok = ScEncCalls.CanCall(e, ScEncCalls.Fighter, false, out why, sender);
            Put(reply, "afr", ok, "afw", why);
            ok = ScEncCalls.CanCall(e, ScEncCalls.Drop, false, out why, sender);
            Put(reply, "adr", ok, "adw", why);
            reply.SetHint("ac", Esc(ScEncCalls.StateText(e)));
            ScLifeSkills.Terms ally = ScLifeSkills.AllyBarter();
            reply.SetHint("an", ally.Give);
            reply.SetHint("am", ally.Get);
            EnclaveRequest siege = ScEncSiege.Open;
            reply.SetHint("si", siege != null ? siege.Id : -1);
            reply.SetHint("sc", Esc(ScEncSiege.Countdown(siege)));
            reply.SetHint("sa", ScEncSiege.Acknowledged(siege) ? 1 : 0);
            int runId = args.Int(9, -1);
            reply.SetHint("ri", runId);
            ok = ScRadioTalk.CanRecall(ScGates.OwnMember(runId), out why);      // round MP6 (part G): the sender's community's runner only
            Put(reply, "rr", ok, "rw", why);
        }

        private static void Put(ScActionReply reply, string readyKey, bool ready, string whyKey, string why)
        {
            reply.SetHint(readyKey, ready ? 1 : 0);
            if (!ready && !string.IsNullOrEmpty(why)) reply.SetHint(whyKey, Esc(why));
        }

        /// <summary>A hint value with '%', ';' and '=' escaped (a reason is free text).</summary>
        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf('%') < 0 && s.IndexOf(';') < 0 && s.IndexOf('=') < 0) return s;
            return s.Replace("%", "%25").Replace(";", "%3B").Replace("=", "%3D");
        }

        public static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('%') < 0) return s ?? "";
            return s.Replace("%3B", ";").Replace("%3D", "=").Replace("%25", "%");
        }

        // ------------------------------------------------------------------ the acts ----

        /// <summary>radio.drop: the radio's air drop. RequestDrop tells the sender either way, so the answer carries no words of its own.</summary>
        [ScAction(OpDrop)]
        private static void Drop(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            if (!Radio(sender, args, true, reply)) return;
            if (ScRadio.RequestDrop(sender)) reply.Done();
            else reply.Fail("", "");
        }

        /// <summary>radio.call: reach out for a survivor (a contact). Call tells the sender either way.</summary>
        [ScAction(OpCall)]
        private static void Call(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            if (!Radio(sender, args, true, reply)) return;
            if (ScRadio.Call(sender)) reply.Done();
            else reply.Fail("", "");
        }

        /// <summary>radio.scout: the chatter about one resource. The answer's text is Scout's words (the window shows and tells them).</summary>
        [ScAction(OpScout)]
        private static void Scout(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            if (!Radio(sender, args, true, reply)) return;
            Resource r;
            if (!ResourceArg(args, 4, ScNames.ResourceCount, out r)) { reply.Fail(Localization.Get("scScoutClassicOnly")); return; }
            string msg;
            if (ScScouting.Scout(r, sender, out msg)) reply.Done(msg ?? "");
            else reply.Fail(msg ?? "");
        }

        /// <summary>radio.barter: the radio's barter (stockpile only). The answer's text is Trade's words.</summary>
        [ScAction(OpBarter)]
        private static void Barter(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            if (!Radio(sender, args, true, reply)) return;
            Resource give, get;
            if (!ResourceArg(args, 4, ScNames.ResourceCount, out give) || !ResourceArg(args, 5, ScNames.ResourceCount, out get))
            {
                reply.Fail(Localization.Get("scBarterNoLuxury"));
                return;
            }
            string msg;
            if (ScBarter.Trade(give, get, sender, out msg)) reply.Done(msg ?? "");
            else reply.Fail(msg ?? "");
        }

        /// <summary>
        /// radio.demand: pay or refuse the raiders' demand on an outpost (stockpile only) - the first one asking, as the window
        /// shows it; the outpost id is the one the client saw (another one now: scActionChanged). A refusal's failure is silent,
        /// as the window's was.
        /// </summary>
        [ScAction(OpDemand)]
        private static void Demand(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            if (!Radio(sender, args, true, reply)) return;
            bool pay = args.Int(5, 1) != 0;
            Outpost d = ScOutpostRisk.FirstDemand();
            int seen = args.Int(4, -1);
            if (d != null && seen >= 0 && d.Id != seen) { reply.Fail("scActionChanged"); return; }
            string msg;
            // round MP5 (merge): PayDemand / RefuseDemand take any player (part O), so a client's refusal starts the raid with its
            // radio call told to the community and the raiders' answer, as the host's does
            if (pay)
            {
                if (ScOutpostRisk.PayDemand(d, sender, out msg)) reply.Done(msg ?? "");
                else reply.Fail(msg ?? "");
                return;
            }
            if (ScOutpostRisk.RefuseDemand(d, sender, out msg)) reply.Done(msg ?? "");
            else reply.Fail(msg ?? "", "");
        }

        /// <summary>
        /// radio.siege: a called siege answered - 1 "On our way" (their thanks; OnOurWay tells the sender), 2 "We can't come" (the
        /// siege is lost; the window asked twice). The id is the siege the client saw (another one, or none: the verb's own "That
        /// call is over.").
        /// </summary>
        [ScAction(OpSiege)]
        private static void Siege(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            if (!Radio(sender, args, true, reply)) return;
            EnclaveRequest open = ScEncSiege.Open;
            if (open == null || open.Id != args.Int(5, -1)) { reply.Fail(Localization.Get("scRadioSiegeGone")); return; }
            string msg;
            bool ok = args.Int(4, 1) == 2 ? ScEncSiege.CantCome(sender, out msg) : ScEncSiege.OnOurWay(sender, out msg);
            if (ok) reply.Done(msg ?? "");
            else reply.Fail(msg ?? "");
        }

        /// <summary>radio.allytrade: the barter with an ally (stockpile only). The answer's text is Trade's words.</summary>
        [ScAction(OpAllyTrade)]
        private static void AllyTrade(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            if (!Radio(sender, args, true, reply)) return;
            int id = args.Int(4, -1);
            Enclave e = id >= 0 ? ScEnclaves.ById(id) : null;
            Resource give, get;
            if (!ResourceArg(args, 5, ScNames.ResourceCount, out give) || !ResourceArg(args, 6, ScNames.ResourceCount, out get))
            {
                reply.Fail(Localization.Get("scBarterNoLuxury"));
                return;
            }
            string msg;
            if (ScEncBarter.Trade(e, give, get, sender, out msg)) reply.Done(msg ?? "");
            else reply.Fail(msg ?? "");
        }

        /// <summary>radio.allycall: a fighter (0) or a supply drop (1) from an ally - it comes to the sender. Made: Call told them (no words here); refused: why.</summary>
        [ScAction(OpAllyCall)]
        private static void AllyCall(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            if (!Radio(sender, args, true, reply)) return;
            int id = args.Int(4, -1);
            Enclave e = id >= 0 ? ScEnclaves.ById(id) : null;
            string msg;
            if (ScEncCalls.Call(e, args.Int(5, 0) == 1 ? ScEncCalls.Drop : ScEncCalls.Fighter, sender, false, false, out msg)) reply.Done();
            else reply.Fail(msg ?? "");
        }

        /// <summary>
        /// radio.recall: a runner told to come home over the radio (decision 6) - no station needed (radio to radio). Their answer
        /// "Copy that." goes to the sender's radio (ScRadioTalk.OnRecalled).
        /// </summary>
        [ScAction(OpRecall)]
        private static void Recall(EntityPlayer sender, ScActionArgs args, ScActionReply reply)
        {
            if (!Radio(sender, args, false, reply)) return;
            Member m = ScGates.OwnMember(args.Int(4, -1));          // round MP6 (part G): the sender's community's only
            string why;
            if (!ScRadioTalk.CanRecall(m, out why)) { reply.Fail(why ?? ""); return; }
            string err;
            if (!ScRunners.Recall(m, sender, out err)) { reply.Fail(Localization.Get(err ?? "scInternal")); return; }
            ScRadioTalk.OnRecalled(m, sender);
            ScRegistry.SaveAtomic();
            ScLog.Out(m.Name + " was called home over the radio" + (args.Int(0, ModeStation) == ModeHandheld ? " (handheld)" : "")
                      + (sender is EntityPlayerLocal ? "" : " by " + sender.PlayerDisplayName));
            reply.Done(string.Format(Localization.Get("scRunRecalled"), m.Name));
        }
    }
}
