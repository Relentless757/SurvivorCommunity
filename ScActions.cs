using System;
using System.Collections.Generic;
using System.Reflection;

namespace SurvivorCommunity
{
    /// <summary>Round MP2: who may use an action in Shared mode (the plan's permission matrix).</summary>
    public enum ScActionAccess
    {
        /// <summary>Any player of the community (Stage 1, Shared mode: every player).</summary>
        Community,
        /// <summary>
        /// A destructive or community-wide choice (dismiss a member, disband an outpost, move the home, rationing): any player,
        /// unless the server sets SharedOwnerOnly - then the community's owner and admins only.
        /// </summary>
        Owner,
        /// <summary>Server admins only (the host's own player always counts).</summary>
        Admin,
        /// <summary>
        /// Round MP7 (core): any player, in a community or not - founding (community.start). The handler checks what it needs.
        /// </summary>
        Anyone
    }

    /// <summary>
    /// Round MP2: marks a static method as the server's handler for one action op. The method's signature is ScActionHandler:
    /// static void Name(EntityPlayer sender, ScActionArgs args, ScActionReply reply). ScActions finds every one by reflection,
    /// so each file registers its own ops without a shared list.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class ScActionAttribute : Attribute
    {
        public readonly string Op;
        public readonly ScActionAccess Access;
        /// <summary>Round MP3: a read a client polls (the member panel's ammo line, every 2 s) - not written to the DebugLogging log.</summary>
        public bool Quiet { get; set; }

        public ScActionAttribute(string op, ScActionAccess access = ScActionAccess.Community)
        {
            Op = op ?? "";
            Access = access;
        }
    }

    /// <summary>Round MP2: an action's arguments - whole numbers and strings, in the order the op's catalog entry gives.</summary>
    public sealed class ScActionArgs
    {
        private static readonly int[] NoInts = new int[0];
        private static readonly string[] NoStrs = new string[0];

        public readonly int[] Ints;
        public readonly string[] Strs;

        public ScActionArgs(int[] ints, string[] strs)
        {
            Ints = ints ?? NoInts;
            Strs = strs ?? NoStrs;
        }

        /// <summary>The i-th whole number, def when absent (an older client, a shorter list).</summary>
        public int Int(int i, int def = 0) { return i >= 0 && i < Ints.Length ? Ints[i] : def; }

        /// <summary>The i-th string, def when absent; never null.</summary>
        public string Str(int i, string def = "") { return i >= 0 && i < Strs.Length && Strs[i] != null ? Strs[i] : def; }

        /// <summary>Three whole numbers from index i as a block position (a claim, a post, a well).</summary>
        public Vector3i Pos(int i) { return new Vector3i(Int(i), Int(i + 1), Int(i + 2)); }
    }

    /// <summary>
    /// Round MP2: the server's answer to one action. Message is a localization key or plain text: the machine that shows it
    /// localizes a key in its own language and shows text as it is (ScActions.Localize). Hint is a follow-up for the UI that
    /// asked ("key=value;key=value": switch a tab, select a row, close a quest) - its keys belong to that UI.
    /// </summary>
    public sealed class ScActionReply
    {
        public bool Ok;
        public string Message = "";
        public string Sound = "";
        public string Hint = "";

        /// <summary>Refused: the message (key or text) and a sound (ui_denied unless given).</summary>
        public ScActionReply Fail(string message, string sound = "ui_denied")
        {
            Ok = false;
            Message = message ?? "";
            Sound = sound ?? "";
            return this;
        }

        /// <summary>Done: an optional message (key or text) and sound.</summary>
        public ScActionReply Done(string message = "", string sound = "")
        {
            Ok = true;
            Message = message ?? "";
            Sound = sound ?? "";
            return this;
        }

        /// <summary>Adds key=value to the hint (no ';' or '=' in either).</summary>
        public ScActionReply SetHint(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return this;
            string pair = key + "=" + (value ?? "");
            Hint = string.IsNullOrEmpty(Hint) ? pair : Hint + ";" + pair;
            return this;
        }

        public ScActionReply SetHint(string key, int value)
        {
            return SetHint(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>The hint's value for key, def when absent.</summary>
        public string HintStr(string key, string def = "")
        {
            if (string.IsNullOrEmpty(Hint) || string.IsNullOrEmpty(key)) return def;
            foreach (string part in Hint.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0 && string.Equals(part.Substring(0, eq), key, StringComparison.Ordinal)) return part.Substring(eq + 1);
            }
            return def;
        }

        public int HintInt(string key, int def = 0)
        {
            int n;
            return int.TryParse(HintStr(key, null), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out n) ? n : def;
        }

        public bool HasHint(string key) { return HintStr(key, null) != null; }

        /// <summary>The message for display on this machine (a key localized here, text as it is).</summary>
        public string Text { get { return ScActions.Localize(Message); } }
    }

    /// <summary>Round MP2: the server's handler for one op (see ScActionAttribute).</summary>
    public delegate void ScActionHandler(EntityPlayer sender, ScActionArgs args, ScActionReply reply);

    /// <summary>
    /// Round MP2 (multiplayer, Stage 1): clients act. One request protocol for every SC button, radial command and dialog
    /// action that changes the community:
    /// - Request(op, ints, strs, onResult) is what the UI calls, on every machine. On the server (single player, the listen
    ///   host's own player) it runs the op's handler at once, inline, with the local player - the same code path a client's
    ///   request takes, so single player behaves exactly as the handler does. On a client it sends NetPackageScAction and calls
    ///   onResult when NetPackageScResult comes back.
    /// - The server checks every request: the sender is a player who said hello with SC's protocol, the op exists, the
    ///   sender may use it (Allowed, the Shared-mode matrix), and then the handler re-checks the target's current state (the
    ///   client's copy can be a push behind) - a stale button only costs a refusal.
    /// - Requests are processed on the main thread one after the other (the game's package queue), so two players pressing
    ///   the same button get two answers in order, and the second is judged against the state the first left.
    /// - The request id only pairs an answer with its callback: the server does not dedupe by it (the reliable channel delivers
    ///   each request once). A toggle carries its intent instead (round MP2, review core/window n3 and M1: ScActionsWindow).
    /// - After a handler changes the registry it saves (SaveAtomic), which marks the mirror dirty: every client's copy follows
    ///   within about a second (ScMirror).
    /// Handlers: static methods marked [ScAction("op")], found by reflection at first use (each file registers its own).
    /// </summary>
    public static class ScActions
    {
        /// <summary>
        /// A client's request with no answer after this many real seconds is forgotten. The server answers every request it gets,
        /// so only a broken connection leaves one unanswered (and world shutdown clears the table): round MP3's review (m1) - a
        /// short timeout dropped a SLOW answer's callback (a stalled server), and with it the give-back of items in flight.
        /// </summary>
        private const float PendingSeconds = 900f;

        private struct Entry
        {
            public ScActionHandler Handler;
            public ScActionAccess Access;
            public bool Quiet;
        }

        private struct Pending
        {
            public Action<ScActionReply> Callback;
            public float At;
            public string Op;
        }

        private static readonly Dictionary<string, Entry> handlers = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static bool scanned;

        private static readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        private static readonly List<int> stale = new List<int>();
        private static int nextRequest;

        /// <summary>Every machine, at world start and shutdown: a client's unanswered requests go (the handler table stays).</summary>
        public static void Reset()
        {
            pending.Clear();
            nextRequest = 0;
        }

        /// <summary>Finds every [ScAction] handler once. Logs a bad signature or a duplicate op (the first one wins).</summary>
        private static void Scan()
        {
            if (scanned) return;
            scanned = true;
            try
            {
                Type[] types;
                try { types = typeof(ScActions).Assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                foreach (Type t in types)
                {
                    if (t == null) continue;
                    // the review's m2: a try per type and per method - one that throws must not leave every later op unknown
                    MethodInfo[] methods;
                    try { methods = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
                    catch (Exception e) { ScLog.Once("actions-scan-" + t.Name, e); continue; }
                    foreach (MethodInfo mi in methods)
                    {
                        try
                        {
                            ScActionAttribute a = mi.GetCustomAttribute<ScActionAttribute>();
                            if (a == null) continue;
                            if (string.IsNullOrEmpty(a.Op)) { ScLog.Warn("action handler " + t.Name + "." + mi.Name + " has no op"); continue; }
                            if (handlers.ContainsKey(a.Op)) { ScLog.Warn("action op '" + a.Op + "' is registered twice (" + t.Name + "." + mi.Name + " ignored)"); continue; }
                            ScActionHandler h;
                            try { h = (ScActionHandler)Delegate.CreateDelegate(typeof(ScActionHandler), mi); }
                            catch (Exception) { ScLog.Warn("action handler " + t.Name + "." + mi.Name + " does not match (EntityPlayer, ScActionArgs, ScActionReply)"); continue; }
                            handlers[a.Op] = new Entry { Handler = h, Access = a.Access, Quiet = a.Quiet };
                        }
                        catch (Exception e) { ScLog.Once("actions-scan-" + t.Name + "." + mi.Name, e); }
                    }
                }
                ScLog.Verbose("actions: " + handlers.Count + " op(s) registered");
            }
            catch (Exception e)
            {
                ScLog.Once("actions-scan", e);
            }
        }

        /// <summary>Whether this build has a handler for op (the server's answer to a client's request).</summary>
        public static bool Has(string op)
        {
            Scan();
            return op != null && handlers.ContainsKey(op);
        }

        // ------------------------------------------------------------------ asking (every machine) ----

        /// <summary>
        /// Asks the server to do op. On the server (single player, the host) the handler runs now with the local player and
        /// onResult is called before this returns; on a client onResult is called when the answer comes (or never, if the
        /// server never answers - the UI must not wait on it). onResult may be null. Main thread only.
        /// </summary>
        public static void Request(string op, int[] ints, string[] strs, Action<ScActionReply> onResult)
        {
            try
            {
                var args = new ScActionArgs(ints, strs);
                if (ScCommunity.IsHost)
                {
                    Deliver(onResult, Dispatch(ScCommunity.LocalPlayer, op, args));
                    return;
                }
                if (!ScNet.IsClient || ScNet.Cm == null)
                {
                    Deliver(onResult, new ScActionReply().Fail("scActionNotReady"));
                    return;
                }
                if (!ScNet.ServerMatches || !ScNet.ServerKnows<NetPackageScAction>())
                {
                    Deliver(onResult, new ScActionReply().Fail(ScNet.ServerProtocol > 0 ? "scNetMismatch" : "scActionNoServer"));
                    return;
                }
                Purge();
                int id = ++nextRequest;
                pending[id] = new Pending { Callback = onResult, At = UnityEngine.Time.realtimeSinceStartup, Op = op };
                // round MP6 (core): the community this client's copy is of (ScRegistry.Current: the mirrored one) - the server only
                // checks it against the sender's, it never picks the community. Round MP6 (core fix): a client names its mirrored
                // community outright, whatever a scope says (review C-m2)
                int community = ScRegistry.Loaded ? ScRegistry.CurrentId : ScRegistry.MirroredCommunity;
                ScNet.Cm.SendToServer(NetPackageManager.GetPackage<NetPackageScAction>().Setup(id, community, op, args.Ints, args.Strs));
            }
            catch (Exception e)
            {
                ScLog.Once("action-request", e);
                Deliver(onResult, new ScActionReply().Fail("scActionFailed"));
            }
        }

        /// <summary>Request with whole numbers only.</summary>
        public static void Request(string op, Action<ScActionReply> onResult, params int[] ints)
        {
            Request(op, ints, null, onResult);
        }

        /// <summary>A client's requests the server never answered (a lost package, a server that went away) are dropped.</summary>
        private static void Purge()
        {
            if (pending.Count == 0) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            stale.Clear();
            foreach (var kv in pending) if (now - kv.Value.At > PendingSeconds) stale.Add(kv.Key);
            for (int i = 0; i < stale.Count; i++)
            {
                ScLog.Verbose("action " + pending[stale[i]].Op + " (request " + stale[i] + ") had no answer from the server");
                pending.Remove(stale[i]);
            }
        }

        /// <summary>Client (NetPackageScResult.ProcessPackage, main thread): the answer to one of this machine's requests.</summary>
        internal static void OnResult(int requestId, ScActionReply reply)
        {
            Pending p;
            if (!pending.TryGetValue(requestId, out p)) return;
            pending.Remove(requestId);
            Deliver(p.Callback, reply);
        }

        private static void Deliver(Action<ScActionReply> cb, ScActionReply reply)
        {
            if (cb == null) return;
            try { cb(reply); }
            catch (Exception e) { ScLog.Once("action-result", e); }
        }

        // ------------------------------------------------------------------ answering (the server) ----

        /// <summary>Server (NetPackageScAction.ProcessPackage, main thread): a remote client's request. Always answers.</summary>
        internal static void OnRequest(ClientInfo sender, int requestId, int community, string op, int[] ints, string[] strs)
        {
            if (sender == null) return;
            ScActionReply r;
            try
            {
                EntityPlayer p = ScPlayers.ByEntity(sender.entityId);
                if (p == null) r = new ScActionReply().Fail("scActionNoPlayer");
                else if (!ScNet.ClientMatches(p)) r = new ScActionReply().Fail("scNetMismatch");
                // round MP6 (core): the package's community is a staleness check - the client acted on a copy of another community
                // than the sender's (they changed community since): refused with a reason, never a choice of community
                else if (community != ScPlayers.CommunityOf(p)) r = new ScActionReply().Fail("scActionCommunityChanged");
                else r = Dispatch(p, op, new ScActionArgs(ints, strs));
            }
            catch (Exception e)
            {
                ScLog.Once("action-request-server", e);
                r = new ScActionReply().Fail("scActionFailed");
            }
            try { sender.SendPackage(NetPackageManager.GetPackage<NetPackageScResult>().Setup(requestId, r)); }
            catch (Exception e) { ScLog.Once("action-reply-send", e); }
        }

        /// <summary>
        /// Server: runs op for player p after the checks (registry loaded, op known, Allowed). A handler that throws answers
        /// scActionFailed (the exception goes to the log once per op). Never null.
        /// </summary>
        public static ScActionReply Dispatch(EntityPlayer p, string op, ScActionArgs args)
        {
            var r = new ScActionReply();
            if (!ScRegistry.Loaded) return r.Fail("scActionNotReady");
            Scan();
            Entry e;
            if (op == null || !handlers.TryGetValue(op, out e))
            {
                // the review's m4: once per op, not per request
                ScLog.Once("action-unknown-" + (op ?? ""), "unknown action '" + (op ?? "") + "' - the sender runs a different SurvivorCommunity version");
                return r.Fail("scActionUnknown");
            }
            if (p == null) return r.Fail("scActionNoPlayer");
            if (!(p is EntityPlayerLocal) && p.IsDead()) return r.Fail("scActionDead");     // the review's m4: as the old UI implied
            // round MP6 (core): every op runs in the sender's community (one scope here covers every handler)
            using (ScRegistry.Use(p))
            {
                // round MP7 (core): a player with only their own relations is refused with scNoCommunity (WhyNot)
                string why = WhyNot(p, op, e.Access);
                if (why != null) return r.Fail(why);
                try
                {
                    e.Handler(p, args ?? new ScActionArgs(null, null), r);
                }
                catch (Exception ex)
                {
                    ScLog.Once("action-" + op, ex);
                    r.Fail("scActionFailed");
                    r.Hint = "";                // the review's n4: no follow-up from a half-run handler
                }
                if (ScConfig.Verbose && !e.Quiet)
                    ScLog.Verbose("action " + op + " by " + p.PlayerDisplayName + ": " + (r.Ok ? "done" : "refused") + (string.IsNullOrEmpty(r.Message) ? "" : " (" + r.Message + ")"));
            }
            return r;
        }

        // ------------------------------------------------------------------ permission (the server) ----

        /// <summary>
        /// The Shared-mode permission matrix: a community player may run the community; Owner ops follow SharedOwnerOnly. Round
        /// MP6 (core): against the sender's own community (ScPlayers.CommunityOf), whatever the scope the caller is in.
        /// Round MP7 (core): WhyNot with no op (so a personal record's player gets no Community access here: facility.do's own check).
        /// </summary>
        public static bool Allowed(EntityPlayer p, ScActionAccess access)
        {
            return WhyNot(p, null, access) == null;
        }

        /// <summary>
        /// Round MP7 (core), the user's decision 4: the ops a player with only their own relations (a personal record, PerPlayer/
        /// PerParty) may use - the relation ops: wild survivors' favors, the enclave talk's gifts, requests, threats and the missing
        /// survivor's search and escort, sending an enlisted person home, and two quiet follow-ups (a bundle opened, a withdrawal's
        /// refund to the stockpile it came out of). Talk, trade and SC's markers need no op. Every other Community op (recruiting,
        /// enlisting, joins, members, stations, radio, linked storage, the stockpile, outposts, runs) and every Owner op answers
        /// scNoCommunity: what needs a home or a community waits for their founding (DEVELOPER.md, the held rewards).
        /// </summary>
        // MP7 (decision needed): enlisting an ally's fighter (enc.enlist) needs no home, but it is no relation op in the design's
        // list, so it waits for the founding with the other community perks
        private static readonly HashSet<string> PersonalOps = new HashSet<string>(StringComparer.Ordinal)
        {
            "favour.start", "favour.poicleared", "favour.items", "favour.given",
            "enc.threat", "enc.gift", "enc.req", "enc.reqgive", "enc.reqgun", "enc.follow", "enc.wait", "enc.seen", "enc.gohome",
            "encremains.search", "bundle.opened", "stock.refund"
        };

        /// <summary>
        /// Round MP7 (core): why player p may not use op (its access level), as a localization key; null = allowed. Against the
        /// sender's own community. Anyone: always. A personal record's player (PerPlayer/PerParty): PersonalOps only, else
        /// scNoCommunity (Admin ops still by IsAdmin). Community: any player of a community. Owner: ScOwnerRule (Shared: SharedOwnerOnly
        /// as before; PerPlayer: owner and admins; PerParty: also the party leader while the owner is offline, part P). Admin:
        /// IsAdmin. With Shared mode, exactly the old matrix.
        /// </summary>
        internal static string WhyNot(EntityPlayer p, string op, ScActionAccess access)
        {
            if (p == null) return "scActionDenied";
            if (access == ScActionAccess.Anyone) return null;
            CommunityState cs = ScCommunities.ById(ScPlayers.CommunityOf(p));
            if (cs == null) return ScMembership.Separate ? "scNoCommunity" : "scActionDenied";
            if (cs.Personal && access != ScActionAccess.Admin)
                return access == ScActionAccess.Community && op != null && PersonalOps.Contains(op) ? null : "scNoCommunity";
            switch (access)
            {
                case ScActionAccess.Community: return null;
                case ScActionAccess.Owner: return ScOwnerRule.May(p, cs) ? null : "scActionDenied";
                case ScActionAccess.Admin: return IsAdmin(p) ? null : "scActionDenied";
                default: return "scActionDenied";
            }
        }

        /// <summary>The host's own player (single player, a listen host), or a remote player with the server's admin level (0).</summary>
        public static bool IsAdmin(EntityPlayer p)
        {
            try
            {
                if (p == null) return false;
                if (p is EntityPlayerLocal) return true;
                ConnectionManager cm = ScNet.Cm;
                ClientInfo ci = cm != null && cm.Clients != null ? cm.Clients.ForEntityId(p.entityId) : null;
                GameManager gm = GameManager.Instance;
                if (ci == null || gm == null || gm.adminTools == null) return false;
                return gm.adminTools.Users.GetUserPermissionLevel(ci) <= 0;
            }
            catch (Exception e)
            {
                ScLog.Once("actions-admin", e);
                return false;
            }
        }

        /// <summary>
        /// The community's owner (by platform id), an admin, or anyone while the community has no owner yet. A save from before
        /// round MP0 knows its owner only by display name until they are online once (ScTicker backfills OwnerId): that name
        /// counts then (the review's m3), as ScPlayers.OwnerOnline matches it. Round MP6 (core): the owner of the player's own
        /// community, whatever the scope the caller is in.
        /// </summary>
        public static bool IsOwnerOrAdmin(EntityPlayer p)
        {
            if (p == null) return false;
            if (IsAdmin(p)) return true;
            CommunityState cs = ScCommunities.ById(ScPlayers.CommunityOf(p));
            Community c = cs != null ? cs.Community : null;
            if (c == null) return true;
            if (!string.IsNullOrEmpty(c.OwnerId)) return string.Equals(ScPlayers.IdOf(p), c.OwnerId, StringComparison.Ordinal);
            if (!string.IsNullOrEmpty(c.Owner)) return string.Equals(p.PlayerDisplayName, c.Owner, StringComparison.Ordinal);
            return true;
        }

        // ------------------------------------------------------------------ text ----

        /// <summary>A localization key in this machine's language, anything else as it is ("" for null).</summary>
        public static string Localize(string message)
        {
            if (string.IsNullOrEmpty(message)) return "";
            try { return Localization.Exists(message) ? Localization.Get(message) : message; }
            catch (Exception) { return message; }
        }
    }
}
