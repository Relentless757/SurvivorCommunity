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
            told = false;
            LastBytes = LastChars = 0;
            Version++;
            lastDerived = null;
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
        /// Client (NetPackageScState.ProcessPackage, main thread): a snapshot (gz, when not null) and the derived block. The copy
        /// is replaced; a derived block alone waits for the first snapshot. Never on the server or the host.
        /// </summary>
        internal static void Receive(byte[] gz, string derivedText)
        {
            try
            {
                if (ScRegistry.Loaded || ScCommunity.IsHost) return;           // the real registry is here: never overwrite it
                if (ScCommunity.World == null) return;                           // a late package after the world closed
                if (gz != null)
                {
                    string text = Unzip(gz);
                    LastBytes = gz.Length;
                    LastChars = text.Length;
                    if (!ScRegistry.ApplyMirror(text))
                    {
                        ScLog.Once("mirror-bad", "the server's community snapshot could not be read (" + gz.Length + " bytes) - the community window stays empty");
                        derived.Clear();
                        Version++;
                        return;
                    }
                }
                else if (!ScRegistry.Mirrored) return;
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
            if (string.IsNullOrEmpty(text)) return;
            foreach (string raw in text.Split('\n'))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                derived[raw.Substring(0, eq)] = raw.Substring(eq + 1).TrimEnd('\r');
            }
        }

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

        private static string lastDerived;
        private static float nextDerived;
        /// <summary>Clients whose hello came while the registry was not loaded yet: their full snapshot goes at the next tick.</summary>
        private static readonly HashSet<int> pendingFull = new HashSet<int>();
        private static readonly List<ClientInfo> targets = new List<ClientInfo>();

        /// <summary>
        /// Server, from ScNet.OnClientHello: a remote client that speaks SC's protocol gets the whole snapshot at once (or at the
        /// next tick, when the registry is not loaded yet).
        /// </summary>
        public static void OnClientMatched(ClientInfo ci)
        {
            try
            {
                if (ci == null || !ScCommunity.IsHost) return;
                if (!ScRegistry.Loaded) { pendingFull.Add(ci.entityId); return; }
                Send(ci, Snapshot(), BuildDerived());
            }
            catch (Exception e) { ScLog.Once("mirror-hello", e); }
        }

        /// <summary>
        /// Server, ScTicker (once a second, registry loaded): the snapshot to every matching remote client when the registry was
        /// saved since the last push, else the derived block when it changed. Nothing to do (and nothing built) with no clients.
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
                    lastDerived = null;
                    pendingFull.Clear();
                    return;
                }
                if (pendingFull.Count > 0)
                {
                    byte[] snap = Snapshot();
                    string d = BuildDerived();
                    foreach (ClientInfo ci in targets) if (pendingFull.Contains(ci.entityId)) Send(ci, snap, d);
                    pendingFull.Clear();
                }
                bool full = ScRegistry.TakeDirty();
                float now = Time.unscaledTime;
                if (!full && now < nextDerived) return;
                nextDerived = now + DerivedSeconds;
                string block = BuildDerived();
                if (!full && block == lastDerived) return;
                lastDerived = block;
                byte[] gz = full ? Snapshot() : null;
                if (full && gz == null) return;        // too big (logged): nothing a client could read
                foreach (ClientInfo ci in targets) Send(ci, gz, block);
            }
            catch (Exception e)
            {
                ScLog.Once("mirror-tick", e);
            }
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

        private static void Send(ClientInfo ci, byte[] gz, string block)
        {
            try
            {
                if (ci == null) return;
                ci.SendPackage(NetPackageManager.GetPackage<NetPackageScState>().Setup(gz, block ?? ""));
            }
            catch (Exception e) { ScLog.Once("mirror-send", e); }
        }

        /// <summary>BuildText(true), gzip'd UTF-8; null when it fails or is too big to send (logged once).</summary>
        private static byte[] Snapshot()
        {
            string text = ScRegistry.BuildText(true);
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
        /// </summary>
        internal static string BuildDerived()
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
            Kv(sb, "beds", ScEncAllies.TotalBeds());
            Kv(sb, "extrabeds", ScEncAllies.ExtraBeds());
            Kv(sb, "allied", ScEncAllies.AlliedCount());
            Kv(sb, "nests", ScInfestations.NearHome().Count);
            Kv(sb, "opslots", ScOutposts.Slots());
            Kv(sb, "scanmissing", ScScan.LastMissing);
            int horde = ScFacilityTab.DefenseHorde(ScPlayers.Anchor());
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
