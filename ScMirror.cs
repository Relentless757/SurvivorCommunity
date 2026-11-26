using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP1 (multiplayer, part mirror): clients see the community's data.
    /// - The server's registry lives only on the server. A client holds a read-only copy in ScRegistry's own containers
    ///   (ScRegistry.ApplyMirror), filled from the server's registry text (ScRegistry.BuildText(true)), so the community window,
    ///   the Home Status HUD, the Facilities tab and the talk card - and the helpers under them - read it unchanged.
    ///   ScRegistry.Loaded stays false there (it means "the authoritative registry"); ScRegistry.Mirrored / Ready say a copy is
    ///   here. Nothing that writes may act on the copy: those paths keep their Loaded / IsHost gates (research note
    ///   mp1_client_paths.md lists them).
    /// - A "derived" block rides along: facts a client cannot work out - the server's runtime state, unloaded chunks, power, live
    ///   bodies beyond the client's 80 m (radio online, the defense rating, morale parts, the next rollover's needs, members'
    ///   health, downed and missing state ...). key=value lines; the helpers the UI calls read it while Active.
    /// - Push (server): a full snapshot to a client at its hello; then from the ticker, at most once a second, the snapshot to
    ///   every remote client that said hello with SC's protocol when the registry was saved since, or the derived block alone when
    ///   only that changed. The host's own player reads the real registry: nothing is sent to it. Single player has no clients,
    ///   so nothing is built or sent.
    /// - Round MP6 (part N), several communities: each client gets its own community's copy (ScPlayers.CommunityOf, from its
    ///   platform id - never a number it sent). The push groups the clients by community and builds each community's snapshot
    ///   (ScRegistry.BuildText(true, cid)) and derived block (BuildDerived(cid)) inside that community's scope, with its own
    ///   last text, last block and comparison; a client whose community changed (sc community join) gets its new community's
    ///   whole snapshot at the next tick. The package names the community (NetPackageScState version 2) and the client's copy is
    ///   parsed into it (ScRegistry.MirroredCommunity). With two or more communities the derived block also lists the
    ///   communities of the players online outside community 0 ("pcom", PlayerCommunity). With one community every byte sent is
    ///   as before.
    /// Main thread only (the ticker, ProcessPackage).
    /// </summary>
    public static class ScMirror
    {
        /// <summary>A gzip'd snapshot above this is not sent (a package's limit is about 2 MB; real ones are 3-15 KB).</summary>
        private const int MaxBytes = 1500000;
        /// <summary>Real seconds between the server's derived blocks (the snapshot itself goes whenever the registry was saved).</summary>
        private const float DerivedSeconds = 2f;

        // ------------------------------------------------------------------ the client ----

        private static readonly Dictionary<string, string> derived = new Dictionary<string, string>();
        private static bool told;
        /// <summary>
        /// Round MP6 (part N), client: the derived block's "pcom" (another player's entity id -> community), parsed once per block
        /// into a new table that replaces this one whole (never changed after), so a reader on any thread sees one consistent table.
        /// </summary>
        private static readonly Dictionary<int, int> NoPlayers = new Dictionary<int, int>();     // before playerCommunity: initializers run in order
        private static volatile Dictionary<int, int> playerCommunity = NoPlayers;

        /// <summary>Client: bumped at each snapshot or derived block received (the community window refreshes on a change).</summary>
        public static int Version { get; private set; }
        /// <summary>Client: the last snapshot's gzip'd size and its text's length (sc state, the log).</summary>
        public static int LastBytes, LastChars;

        /// <summary>A client reading the server's copy: the UI's helpers answer from the derived block.</summary>
        public static bool Active { get { return !ScRegistry.Loaded && ScRegistry.Mirrored; } }

        /// <summary>Every machine, at world start and shutdown: the client's copy and the server's push state go.</summary>
        public static void Reset()
        {
            derived.Clear();
            playerCommunity = NoPlayers;          // round MP6 (part N)
            told = false;
            LastBytes = LastChars = 0;
            Version++;
            sets.Clear();                         // round MP6 (part N): every community's last text and block
            sentCommunity.Clear();
            nextDerived = 0f;
            pendingFull.Clear();
        }

        /// <summary>Client, world shutdown: the copy is forgotten (ScRegistry.FlushAndReset clears the containers too).</summary>
        public static void OnWorldShuttingDown()
        {
            try
            {
                if (ScCommunity.IsHost) { Reset(); return; }
                ScRegistry.ClearMirror();
                Reset();
            }
            catch (Exception e) { ScLog.Once("mirror-shutdown", e); }
        }

        public static bool Has(string key) { return Active && derived.ContainsKey(key); }

        public static string Str(string key, string def = "")
        {
            string v;
            return derived.TryGetValue(key, out v) ? v : def;
        }

        public static int Int(string key, int def = 0)
        {
            string v;
            int n;
            return derived.TryGetValue(key, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : def;
        }

        public static float Float(string key, float def = 0f)
        {
            string v;
            float f;
            return derived.TryGetValue(key, out v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out f) ? f : def;
        }

        public static bool Bool(string key) { return Int(key) != 0; }

        /// <summary>A comma list of whole numbers (n of them, missing ones 0); null when the key is absent.</summary>
        public static int[] Ints(string key, int n)
        {
            string v;
            if (!derived.TryGetValue(key, out v)) return null;
            var a = new int[n];
            string[] f = v.Split(',');
            for (int i = 0; i < n && i < f.Length; i++) int.TryParse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out a[i]);
            return a;
        }

        /// <summary>Is id in the comma list under key ("missing")?</summary>
        public static bool InList(string key, int id)
        {
            string v;
            if (!derived.TryGetValue(key, out v) || v.Length == 0) return false;
            string s = id.ToString(CultureInfo.InvariantCulture);
            foreach (string part in v.Split(',')) if (part == s) return true;
            return false;
        }

        /// <summary>
        /// Round MP6 (part N), client: another player's community as the server last listed it (the derived block's "pcom"); 0 when
        /// not listed - community 0, everyone with one community. This machine's own player is ScRegistry.MirroredCommunity.
        /// </summary>
        public static int PlayerCommunity(int entityId)
        {
            Dictionary<int, int> t = playerCommunity;
            int c;
            return t != null && t.TryGetValue(entityId, out c) ? c : ScPlayers.SharedCommunity;
        }

        /// <summary>
        /// Client (NetPackageScState.ProcessPackage, main thread): a snapshot (gz, when not null) and the derived block. The copy
        /// is replaced; a derived block alone waits for the first snapshot. Never on the server or the host.
        /// Round MP6 (part N): community is the server's word for this client's community: a snapshot sets
        /// ScRegistry.MirroredCommunity before the copy is parsed (into it); a derived block of another community than the copy's
        /// is dropped (sent before a community change - the new community's snapshot is on its way).
        /// </summary>
        internal static void Receive(int community, byte[] gz, string derivedText)
        {
            try
            {
                if (ScRegistry.Loaded || ScCommunity.IsHost) return;           // the real registry is here: never overwrite it
                if (ScCommunity.World == null) return;                           // a late package after the world closed
                if (community < 0) community = ScPlayers.SharedCommunity;       // round MP6 (part N): never a negative id
                if (gz != null)
                {
                    string text = Unzip(gz);
                    LastBytes = gz.Length;
                    LastChars = text.Length;
                    // round MP6 (part N): the copy is this community's (ApplyMirror parses it into MirroredCommunity)
                    if (community != ScRegistry.MirroredCommunity)
                        ScLog.Out("community snapshot from the server: you are in community " + community + " (was " + ScRegistry.MirroredCommunity + ")");
                    ScRegistry.MirroredCommunity = community;
                    if (!ScRegistry.ApplyMirror(text))
                    {
                        ScLog.Once("mirror-bad", "the server's community snapshot could not be read (" + gz.Length + " bytes) - the community window stays empty");
                        derived.Clear();
                        Version++;
                        return;
                    }
                }
                else if (!ScRegistry.Mirrored) return;
                else if (community != ScRegistry.MirroredCommunity) return;     // round MP6 (part N): another community's block (a change is on its way)
                ParseDerived(derivedText);
                Overlay();
                Version++;
                TintHud();
                if (gz != null && !told)
                {
                    told = true;
                    ScLog.Out("community snapshot from the server: " + ScRegistry.Members.Count + " member(s), " + ScRegistry.Outposts.Count + " outpost(s), "
                              + LastBytes + " bytes (" + LastChars + " characters of registry text)");
                }
                else if (gz != null) ScLog.Verbose("community snapshot from the server: " + ScRegistry.Members.Count + " member(s), " + LastBytes + " bytes");
            }
            catch (Exception e)
            {
                ScLog.Once("mirror-receive", e);
            }
        }

        private static void ParseDerived(string text)
        {
            derived.Clear();
            playerCommunity = NoPlayers;          // round MP6 (part N)
            if (string.IsNullOrEmpty(text)) return;
            foreach (string raw in text.Split('\n'))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                derived[raw.Substring(0, eq)] = raw.Substring(eq + 1).TrimEnd('\r');
            }
            // round MP6 (part N): "pcom" = entityid:cid,... - the players online outside community 0 (two or more communities only)
            string pc;
            if (!derived.TryGetValue(PlayerCommunitiesKey, out pc) || pc.Length == 0) return;
            var next = new Dictionary<int, int>();
            foreach (string part in pc.Split(','))
            {
                int colon = part.IndexOf(':');
                int id, cid;
                if (colon > 0 && int.TryParse(part.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)
                    && int.TryParse(part.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out cid) && cid >= 0)
                    next[id] = cid;
            }
            playerCommunity = next;
        }

        /// <summary>Round MP6 (part N): the derived block's key for the players' communities (PlayerCommunity).</summary>
        private const string PlayerCommunitiesKey = "pcom";

        /// <summary>
        /// The few live figures the server changes between saves (morale and moods every 5 s, the attention, the activities'
        /// countdowns), written into the client's copy so every reader of the copy shows the server's current value. The copy is
        /// this machine's own (never saved, replaced by the next snapshot).
        /// </summary>
        private static void Overlay()
        {
            Community c = ScRegistry.Community;
            string v;
            if (derived.ContainsKey("morale")) c.Morale = Mathf.Clamp(Int("morale", c.Morale), 0, 100);
            if (derived.ContainsKey("attention")) c.Attention = Mathf.Clamp(Float("attention", c.Attention), 0f, 100f);
            c.DrinksLeft = Math.Max(0f, Float("drinks", 0f));
            c.FeastLeft = Math.Max(0f, Float("feast", 0f));
            c.RationLeft = Math.Max(0f, Float("ration", 0f));
            foreach (Member m in ScRegistry.Members.Values)
                if (derived.TryGetValue("mood." + m.Id.ToString(CultureInfo.InvariantCulture), out v))
                {
                    int mood;
                    if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out mood)) m.Mood = Mathf.Clamp(mood, 0, 100);
                }
        }

        /// <summary>The morale HUD icon's band colour (the server tints only its own machine's BuffClass; ScMorale.SyncHud).</summary>
        private static void TintHud()
        {
            try
            {
                BuffClass hud = BuffManager.GetBuff(ScMorale.HudBuff);
                if (hud != null && ScRegistry.Members.Count > 0) hud.IconColor = ScMorale.MoodColor32(ScRegistry.Community.Morale);
            }
            catch (Exception e) { ScLog.Once("mirror-hud-tint", e); }
        }

        /// <summary>Client: the morale terms the server computed (ScMorale.Compute answers them on a client).</summary>
        public static bool TryParts(out ScMorale.Parts p)
        {
            p = new ScMorale.Parts();
            int[] a = Ints("parts", PartCount);
            if (a == null) return false;
            p.Food = a[0]; p.Meds = a[1]; p.Beds = a[2]; p.Facilities = a[3]; p.Supplies = a[4]; p.Traits = a[5]; p.Deaths = a[6];
            p.Leadership = a[7]; p.Territory = a[8]; p.Defence = a[9]; p.Water = a[10]; p.Activity = a[11]; p.Feast = a[12];
            p.Rationing = a[13]; p.Allies = a[14]; p.Comfort = a[15]; p.EnclaveLost = a[16]; p.Nests = a[17]; p.FoodDays = a[18];
            p.WaterDays = a[19]; p.Total = a[20]; p.FedTotal = a[21];
            return true;
        }

        private const int PartCount = 22;

        private static string EncodeParts(ScMorale.Parts p)
        {
            int[] a =
            {
                p.Food, p.Meds, p.Beds, p.Facilities, p.Supplies, p.Traits, p.Deaths, p.Leadership, p.Territory, p.Defence, p.Water,
                p.Activity, p.Feast, p.Rationing, p.Allies, p.Comfort, p.EnclaveLost, p.Nests, p.FoodDays, p.WaterDays, p.Total, p.FedTotal
            };
            return Join(a);
        }

        // ------------------------------------------------------------------ the server ----

        /// <summary>
        /// Round MP6 (part N): one community's push state - the derived block and the clients' text last broadcast to its clients
        /// (the review's N1 comparison, per community).
        /// </summary>
        private sealed class PushState
        {
            public string LastDerived;
            /// <summary>The review's N1: the clients' text last broadcast (null: none since the last reset).</summary>
            public string LastSnapText;
        }

        /// <summary>Round MP6 (part N): each community's push state, by community id (only communities with a client online).</summary>
        private static readonly Dictionary<int, PushState> sets = new Dictionary<int, PushState>();
        /// <summary>Round MP6 (part N): the community whose whole snapshot each client (entity id) was last sent.</summary>
        private static readonly Dictionary<int, int> sentCommunity = new Dictionary<int, int>();
        private static float nextDerived;
        /// <summary>Clients whose hello came while the registry was not loaded yet: their full snapshot goes at the next tick.</summary>
        private static readonly HashSet<int> pendingFull = new HashSet<int>();
        private static readonly List<ClientInfo> targets = new List<ClientInfo>();
        /// <summary>Round MP6 (part N): the tick's clients by community, lowest id first (reused).</summary>
        private static readonly SortedDictionary<int, List<ClientInfo>> groups = new SortedDictionary<int, List<ClientInfo>>();
        private static readonly List<ClientInfo> joiners = new List<ClientInfo>();
        private static readonly List<int> scratchIds = new List<int>();

        /// <summary>
        /// Server, from ScNet.OnClientHello: a remote client that speaks SC's protocol gets the whole snapshot at once (or at the
        /// next tick, when the registry is not loaded yet). Round MP6 (part N): its own community's (from its platform id).
        /// </summary>
        public static void OnClientMatched(ClientInfo ci)
        {
            try
            {
                if (ci == null || !ScCommunity.IsHost) return;
                if (!ScRegistry.Loaded) { pendingFull.Add(ci.entityId); return; }
                int cid = ScPlayers.CommunityOf(ci);
                byte[] snap;
                string block;
                using (ScRegistry.Use(cid))
                {
                    snap = Snapshot(ScRegistry.BuildText(true, cid));
                    block = BuildDerived(cid, PlayerCommunities());
                }
                Send(ci, cid, snap, block);
                sentCommunity[ci.entityId] = cid;
            }
            catch (Exception e) { ScLog.Once("mirror-hello", e); }
        }

        /// <summary>
        /// Server, ScTicker (once a second, registry loaded): the snapshot to every matching remote client when the registry was
        /// saved since the last push, else the derived block when it changed. Nothing to do (and nothing built) with no clients.
        /// Round MP6 (part N): per community - the clients grouped by their community, each group's snapshot and block built in
        /// its scope and compared with its own last ones; a client whose community changed (or whose hello came before the load)
        /// gets its community's whole snapshot first. With one community: exactly the old push.
        /// </summary>
        public static void ServerTick()
        {
            try
            {
                if (!ScCommunity.IsHost || !ScRegistry.Loaded) return;
                Targets(targets);
                if (targets.Count == 0)
                {
                    ScRegistry.TakeDirty();            // a client joining later gets the whole snapshot at its hello
                    sets.Clear();
                    sentCommunity.Clear();
                    pendingFull.Clear();
                    return;
                }
                Group(targets);
                bool dirty = ScRegistry.TakeDirty();
                float now = Time.unscaledTime;
                bool due = dirty || now >= nextDerived;
                if (due) nextDerived = now + DerivedSeconds;
                string pcom = PlayerCommunities();
                foreach (KeyValuePair<int, List<ClientInfo>> g in groups)
                {
                    try
                    {
                        using (ScRegistry.Use(g.Key)) Push(g.Key, g.Value, dirty, due, pcom);
                    }
                    catch (Exception e) { ScLog.Once("mirror-tick-community", e); }    // one community's fault never stops the others'
                }
                pendingFull.Clear();                   // as before: a pending hello is served at the first tick after the load
                // a community with no client online any more: its state goes (a client of it later starts from its hello's snapshot)
                scratchIds.Clear();
                foreach (int cid in sets.Keys) if (!groups.ContainsKey(cid)) scratchIds.Add(cid);
                for (int i = 0; i < scratchIds.Count; i++) sets.Remove(scratchIds[i]);
                scratchIds.Clear();
            }
            catch (Exception e)
            {
                ScLog.Once("mirror-tick", e);
            }
        }

        /// <summary>
        /// Round MP6 (part N): the targets into groups by community (ScPlayers.CommunityOf: the server's membership map, from the
        /// client's platform id), and the clients gone since forgotten.
        /// </summary>
        private static void Group(List<ClientInfo> list)
        {
            foreach (List<ClientInfo> l in groups.Values) l.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                int cid = ScPlayers.CommunityOf(list[i]);
                List<ClientInfo> l;
                if (!groups.TryGetValue(cid, out l)) groups[cid] = l = new List<ClientInfo>();
                l.Add(list[i]);
            }
            scratchIds.Clear();
            foreach (KeyValuePair<int, List<ClientInfo>> g in groups) if (g.Value.Count == 0) scratchIds.Add(g.Key);
            for (int i = 0; i < scratchIds.Count; i++) groups.Remove(scratchIds[i]);
            scratchIds.Clear();
            foreach (int id in sentCommunity.Keys)
            {
                bool on = false;
                for (int i = 0; i < list.Count && !on; i++) on = list[i].entityId == id;
                if (!on) scratchIds.Add(id);
            }
            for (int i = 0; i < scratchIds.Count; i++) sentCommunity.Remove(scratchIds[i]);
            scratchIds.Clear();
        }

        /// <summary>
        /// Round MP6 (part N): one community's push, inside its scope (the caller's Use) - the old ServerTick's body for that
        /// community's clients: first the whole snapshot to a client that has none of this community's (a hello before the load,
        /// a community change), then the snapshot when the registry was saved and this community's text changed, else the
        /// derived block when it changed.
        /// </summary>
        private static void Push(int cid, List<ClientInfo> list, bool dirty, bool due, string pcom)
        {
            PushState st;
            if (!sets.TryGetValue(cid, out st)) sets[cid] = st = new PushState();
            joiners.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                ClientInfo ci = list[i];
                int had;
                bool pending = pendingFull.Remove(ci.entityId);
                if (pending || !sentCommunity.TryGetValue(ci.entityId, out had) || had != cid) joiners.Add(ci);
            }
            if (joiners.Count > 0)
            {
                byte[] snap = Snapshot(ScRegistry.BuildText(true, cid));
                string d = BuildDerived(cid, pcom);
                for (int i = 0; i < joiners.Count; i++)
                {
                    Send(joiners[i], cid, snap, d);
                    sentCommunity[joiners[i].entityId] = cid;      // too big (snap null, logged): tried again only after the next save, as before
                }
            }
            if (!due) return;
            bool full = dirty;
            string block = BuildDerived(cid, pcom);
            // the review's N1: a save that left the clients' text as it was (a field they never get) sends no snapshot
            string text = full ? ScRegistry.BuildText(true, cid) : null;
            if (full && text == st.LastSnapText) full = false;
            if (!full && block == st.LastDerived) return;
            st.LastDerived = block;
            byte[] gz = full ? Snapshot(text) : null;
            if (full && gz == null) return;        // too big (logged): nothing a client could read
            if (full) st.LastSnapText = text;
            for (int i = 0; i < list.Count; i++)
                if (!joiners.Contains(list[i])) Send(list[i], cid, gz, block);     // a joiner has this second's copy already
        }

        /// <summary>
        /// Round MP6 (part N), server: the derived block's "pcom" value - entityid:cid for every player online outside community 0
        /// (a client's ScPlayers.CommunityOf for the players it sees: ScMirror.PlayerCommunity). "" with one community, so the block
        /// is as before MP6; a player whose community cannot be told yet is left out (community 0 on the client until the next).
        /// </summary>
        private static string PlayerCommunities()
        {
            if (ScCommunities.Count < 2) return "";
            var sb = new StringBuilder();
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
            {
                int c;
                if (all[i] == null || !ScPlayers.TryCommunityOf(all[i], out c) || c == ScPlayers.SharedCommunity) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(all[i].entityId.ToString(CultureInfo.InvariantCulture)).Append(':').Append(c.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>The remote clients that said hello with SC's protocol (never the host's own player: it has no ClientInfo).</summary>
        private static void Targets(List<ClientInfo> into)
        {
            into.Clear();
            ConnectionManager cm = ScNet.Cm;
            if (cm == null || cm.Clients == null) return;
            foreach (ClientInfo ci in cm.Clients.List)
                if (ci != null && ci.entityId != -1 && ScNet.ClientMatches(ci.entityId)) into.Add(ci);
        }

        /// <summary>Round MP6 (part N): with the community the snapshot and block are of (the client's own).</summary>
        private static void Send(ClientInfo ci, int cid, byte[] gz, string block)
        {
            try
            {
                if (ci == null) return;
                ci.SendPackage(NetPackageManager.GetPackage<NetPackageScState>().Setup(cid, gz, block ?? ""));
            }
            catch (Exception e) { ScLog.Once("mirror-send", e); }
        }

        /// <summary>BuildText(true)'s text, gzip'd UTF-8; null when it fails or is too big to send (logged once).</summary>
        private static byte[] Snapshot(string text)
        {
            if (text == null) return null;
            byte[] gz = Zip(text);
            if (gz.Length > MaxBytes)
            {
                ScLog.Once("mirror-too-big", "the community snapshot for clients is " + gz.Length + " bytes gzip'd (" + text.Length
                           + " characters) - over the " + MaxBytes + " a package may carry: clients keep their last copy");
                return null;
            }
            return gz;
        }

        /// <summary>
        /// The derived block (key=value lines): what the community window, the HUD and the Facilities tab show that a client
        /// cannot work out from the registry's text. Every value is written so a whole number stays whole (no churn).
        /// Round MP6 (part N): community cid's, built inside its scope - so "beds", "extrabeds" and "allied" (ScEncAllies: its
        /// allies through each enclave's standing as that community sees it), "nests" (ScInfestations.NearHome: its home) and
        /// "defhorde" (its anchor: ScPlayers.Anchor(cid)) are its own, as is every other key read through the registry. pcom: the
        /// players' communities (PlayerCommunities; "" with one community, so the block is as before MP6).
        /// </summary>
        internal static string BuildDerived(int cid, string pcom)
        {
            using (ScRegistry.Use(cid)) return BuildDerivedIn(cid, pcom);
        }

        /// <summary>Round MP6 (part N): BuildDerived's body, in cid's scope (the old BuildDerived, its five community-blind keys given the community).</summary>
        private static string BuildDerivedIn(int cid, string pcom)
        {
            var sb = new StringBuilder();
            Community c = ScRegistry.Community;
            World w = ScCommunity.World;
            Kv(sb, "morale", c.Morale);
            Kv(sb, "attention", c.Attention.ToString("0.#", CultureInfo.InvariantCulture));
            if (c.DrinksLeft > 0f) Kv(sb, "drinks", Mathf.CeilToInt(c.DrinksLeft));
            if (c.FeastLeft > 0f) Kv(sb, "feast", Mathf.CeilToInt(c.FeastLeft));
            if (c.RationLeft > 0f) Kv(sb, "ration", Mathf.CeilToInt(c.RationLeft));
            Kv(sb, "parts", EncodeParts(ScMorale.Compute()));
            Kv(sb, "radio", ScOutposts.RadioOnlineAnywhere() ? 1 : 0);
            Kv(sb, "noise", ScNoise.Current().ToString("0.#", CultureInfo.InvariantCulture));
            Kv(sb, "need", Join(ScStockpile.NeedForRollover()));
            var gain = new int[ScNames.ResourceCount];
            var use = new int[ScNames.ResourceCount];
            ScEconomy.DailyFlow(ScCommunity.Today + 1, gain, use);
            Kv(sb, "gain", Join(gain));
            Kv(sb, "use", Join(use));
            Kv(sb, "food", ScEconomy.FoodEaten());
            // round MP6 (part N): these five read the community in scope (cid's): its standing with each enclave, its home, its anchor
            Kv(sb, "beds", ScEncAllies.TotalBeds());
            Kv(sb, "extrabeds", ScEncAllies.ExtraBeds());
            Kv(sb, "allied", ScEncAllies.AlliedCount());
            Kv(sb, "nests", ScInfestations.NearHome().Count);
            Kv(sb, "opslots", ScOutposts.Slots());
            Kv(sb, "scanmissing", ScScan.LastMissing);
            int horde = ScFacilityTab.DefenseHorde(ScPlayers.Anchor(cid));
            Kv(sb, "defhorde", horde);
            Kv(sb, "defrating", ScDefence.Rating(horde, null));
            Kv(sb, "share", ScBundleOpen.ShareNow().ToString("0.###", CultureInfo.InvariantCulture));
            foreach (Outpost o in ScRegistry.Outposts)
            {
                string left = ScOutposts.ContestedLeftShort(o);
                if (left.Length > 0) Kv(sb, "opleft." + o.Id.ToString(CultureInfo.InvariantCulture), left);
            }
            var missing = new StringBuilder();
            foreach (Member m in ScRegistry.Members.Values)
            {
                string id = m.Id.ToString(CultureInfo.InvariantCulture);
                Kv(sb, "mood." + id, m.Mood);
                if (ScMissing.IsMissing(m)) { if (missing.Length > 0) missing.Append(','); missing.Append(id); }
                EntityAlive npc = ScCommunity.FindNpc(m.Id);
                if (npc != null && !npc.IsDead() && npc.Stats != null && npc.Stats.Health != null)
                    Kv(sb, "hp." + id, npc.Health.ToString(CultureInfo.InvariantCulture) + "," + npc.GetMaxHealth().ToString(CultureInfo.InvariantCulture) + ","
                                       + npc.Stats.Health.ValuePercentUI.ToString("0.##", CultureInfo.InvariantCulture));
#if SC_V34
                int secs, medic;
                string medicName;
                if (ScDowned.DownFacts(m, out secs, out medic, out medicName))
                {
                    Kv(sb, "down." + id, secs.ToString(CultureInfo.InvariantCulture) + "," + medic.ToString(CultureInfo.InvariantCulture));
                    if (medicName.Length > 0) Kv(sb, "downmedic." + id, medicName);
                }
                float fragile = npc != null ? ScDowned.FragileSeconds(npc) : 0f;
                if (fragile > 0f) Kv(sb, "fragile." + id, Mathf.CeilToInt(fragile / 60f) * 60);     // whole minutes: the text shows minutes
#endif
                if (m.Status == MemberStatus.Following || m.Status == MemberStatus.PendingStation)
                {
                    bool guarding = npc != null && npc.Buffs != null && npc.Buffs.GetCustomVar("Guarding") >= 1f;
                    Kv(sb, "held." + id, ((int)m.HeldOrder).ToString(CultureInfo.InvariantCulture) + "," + (guarding ? 1 : 0));
                }
            }
            if (missing.Length > 0) Kv(sb, "missing", missing.ToString());
            if (!string.IsNullOrEmpty(pcom)) Kv(sb, PlayerCommunitiesKey, pcom);     // round MP6 (part N): two or more communities only
            return sb.ToString();
        }

        private static void Kv(StringBuilder sb, string key, int v) { Kv(sb, key, v.ToString(CultureInfo.InvariantCulture)); }

        private static void Kv(StringBuilder sb, string key, string v)
        {
            sb.Append(key).Append('=').Append((v ?? "").Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
        }

        private static string Join(int[] a)
        {
            if (a == null) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < a.Length; i++) { if (i > 0) sb.Append(','); sb.Append(a[i].ToString(CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ gzip ----

        private static byte[] Zip(string text)
        {
            byte[] raw = Encoding.UTF8.GetBytes(text ?? "");
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionMode.Compress, true)) gz.Write(raw, 0, raw.Length);
                return ms.ToArray();
            }
        }

        private static string Unzip(byte[] gz)
        {
            using (var ms = new MemoryStream(gz))
            using (var z = new GZipStream(ms, CompressionMode.Decompress))
            using (var outMs = new MemoryStream())
            {
                z.CopyTo(outMs);
                return Encoding.UTF8.GetString(outMs.ToArray());
            }
        }
    }
}
