using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP4 (part K, multiplayer Stage 1): every SC map marker reaches every community player.
    /// - Each subsystem still draws its markers for this machine's own player exactly as before (AddMarker / RemoveMarker,
    ///   owner = the host's own EntityPlayerLocal). Single player and the listen host's own map are untouched; a dedicated
    ///   server registers nothing (those AddMarkers return for a player that is not this machine's own).
    /// - The server keeps one replicated set beside them. Once a second, while a remote client that said hello with SC's
    ///   protocol is online, each subsystem's CollectMarkers lists its markers as its AddMarker draws them (kind, key, nav
    ///   class, position, label), from the server's own state - so a dedicated server, which never draws one, has them too.
    ///   Position changes are published throttled per kind (Entry.MoveMin / MoveEvery: bands every 5 s once they walked
    ///   10 m, as the host's own band markers move).
    /// - A client gets the whole set at the server's first tick after its hello (NetPackageScMarkers, full), then deltas:
    ///   put (new, or a new class or label), move, drop. It registers each marker in its own NavObjectManager with its own
    ///   EntityPlayerLocal as the owner, so the IsOwner nav classes (Config/nav_objects.xml) show them unchanged.
    /// - Stage 1 (Shared mode): what the community knows goes to every community player. The one rule that depends on the
    ///   viewer keeps its form, per viewer: the watch posts' horde marker shows to a player within BaseAttackLeaveRange of the
    ///   home zone, as the host's own does.
    /// - Labels are composed on the server, in its language (an enclave's standing word, an outpost's yield).
    /// - While remote players are on, the listen host's own map follows the server's set for the kinds a remote player can
    ///   change (HostFollow): any player's outpost upgrade, repair, mode or staffing re-labels the host's marker too (MP3's
    ///   known gap; until now only the verbs that call ScOutposts.HostMarkersFollow did), and an enclave or infestation a
    ///   client's console revealed shows there.
    /// Main thread only (the ticker, ProcessPackage).
    /// </summary>
    public static class ScMarkers
    {
        // the kinds (the first half of a marker's id; the second is the subsystem's own key)
        public const string KindEnclave = "enclave", KindContact = "contact", KindInfestation = "infest", KindBand = "band",
                            KindOutpost = "outpost", KindOutpostAttack = "opattack", KindScout = "scout", KindRunnerCall = "runner",
                            KindWatch = "watch";

        /// <summary>Vanilla's quest map object class (a runner's call for help, ScRunnerCall.MarkerClass): its recycled fields are cleared.</summary>
        private const string QuestClass = "quest";
        /// <summary>Client: every this many real seconds the registered markers are checked against the server's set (one removed by someone else comes back).</summary>
        private const float HealSeconds = 10f;

        /// <summary>One marker of the replicated set. Server: never changed once published (a change publishes a new one).</summary>
        public sealed class Entry
        {
            public readonly string Kind, Key, Id, Cls, Label;
            public readonly Vector3 Pos;
            /// <summary>Server: a move shorter than this (meters) is not published, nor one sooner than MoveEvery real seconds after the last.</summary>
            public float MoveMin = 0.5f, MoveEvery;
            /// <summary>Server: shown only to a viewer within BaseAttackLeaveRange of the home zone (the watch posts' horde marker).</summary>
            public bool NearBase;
            internal float Published;

            public Entry(string kind, string key, string cls, Vector3 pos, string label)
            {
                Kind = kind ?? "";
                Key = key ?? "";
                Id = Kind + ":" + Key;
                Cls = cls ?? "";
                Pos = pos;
                Label = label ?? "";
            }

            public Entry(string kind, int key, string cls, Vector3 pos, string label)
                : this(kind, key.ToString(CultureInfo.InvariantCulture), cls, pos, label) { }
        }

        /// <summary>One change on the wire (NetPackageScMarkers).</summary>
        public struct Op
        {
            public const byte Put = 0, Move = 1, Drop = 2;
            public byte Code;
            public string Kind, Key, Cls, Label;
            public Vector3 Pos;

            public static Op PutOf(Entry e) { return new Op { Code = Put, Kind = e.Kind, Key = e.Key, Cls = e.Cls, Label = e.Label, Pos = e.Pos }; }
            public static Op MoveOf(Entry e) { return new Op { Code = Move, Kind = e.Kind, Key = e.Key, Cls = "", Label = "", Pos = e.Pos }; }
            public static Op DropOf(Entry e) { return new Op { Code = Drop, Kind = e.Kind, Key = e.Key, Cls = "", Label = "", Pos = Vector3.zero }; }
        }

        // ------------------------------------------------------------------ the server ----

        /// <summary>The set as last published (id -> entry).</summary>
        private static readonly Dictionary<string, Entry> published = new Dictionary<string, Entry>();
        /// <summary>Per remote client (its connection, so a new connection under a reused entity id starts over): what it was sent.</summary>
        private static readonly Dictionary<ClientInfo, Dictionary<string, Entry>> sent = new Dictionary<ClientInfo, Dictionary<string, Entry>>();
        private static readonly List<Entry> fresh = new List<Entry>();
        private static readonly List<ClientInfo> targets = new List<ClientInfo>();
        private static readonly HashSet<string> seen = new HashSet<string>();
        private static readonly List<string> gone = new List<string>();
        private static readonly List<ClientInfo> leftClients = new List<ClientInfo>();
        private static readonly List<Op> ops = new List<Op>();
        private static readonly List<Entry> commitPut = new List<Entry>();
        private static readonly List<string> commitDrop = new List<string>();
        private static World serverWorld;
        private static bool built;
        private static float nextHostFollow;
        /// <summary>The kinds whose markers came, went or changed class or label in the last Publish (moves are not changes here).</summary>
        private static readonly HashSet<string> changedKinds = new HashSet<string>();
        /// <summary>The kinds the listen host's own map is to be drawn again for (HostFollow).</summary>
        private static readonly HashSet<string> followDue = new HashSet<string>();
        /// <summary>The listen host's own markers of a kind that Follows are drawn again at most this often (real seconds).</summary>
        private const float HostFollowSeconds = 3f;

        /// <summary>
        /// Server, ScTicker (once a second, registry loaded): the set collected and published, and each matching remote
        /// client sent what changed for it (the whole set at its first tick). Nothing is built or sent with no such client:
        /// single player and a host alone do no work here.
        /// </summary>
        public static void ServerTick()
        {
            try
            {
                if (!ScCommunity.IsHost || !ScRegistry.Loaded) return;
                World w = ScCommunity.World;
                if (w == null) return;
                Targets(targets);
                if (targets.Count == 0 || w != serverWorld)
                {
                    // a client joining later gets the whole set then
                    published.Clear();
                    sent.Clear();
                    built = false;
                    followDue.Clear();
                    serverWorld = w;
                    if (targets.Count == 0) return;
                }
                float now = Time.unscaledTime;
                Collect(fresh);
                bool first = !built;
                built = true;
                Publish(fresh, now);
                fresh.Clear();
                // clients gone since (a disconnect, a rejoin under a new connection)
                leftClients.Clear();
                foreach (ClientInfo ci in sent.Keys) if (!targets.Contains(ci)) leftClients.Add(ci);
                for (int i = 0; i < leftClients.Count; i++) sent.Remove(leftClients[i]);
                leftClients.Clear();
                for (int i = 0; i < targets.Count; i++)
                {
                    try { SendTo(targets[i]); }
                    catch (Exception e) { ScLog.Once("markers-send", e); }      // one client's fault never stops the others
                }
                if (!first) foreach (string k in changedKinds) if (Follows(k)) followDue.Add(k);
                changedKinds.Clear();
                if (followDue.Count > 0 && now >= nextHostFollow)
                {
                    // at most every HostFollowSeconds: a scavenge outpost's yield follows the stock, which can move often
                    nextHostFollow = now + HostFollowSeconds;
                    HostFollow();
                }
            }
            catch (Exception e)
            {
                ScLog.Once("markers-tick", e);
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

        /// <summary>Every subsystem's markers, each in its own try: one that throws keeps its kind's last published markers (no flicker).</summary>
        private static void Collect(List<Entry> into)
        {
            into.Clear();
            Each(into, KindEnclave, ScEnclaves.CollectMarkers);
            Each(into, KindContact, ScRadio.CollectMarkers);
            Each(into, KindInfestation, ScInfestations.CollectMarkers);
            Each(into, KindBand, ScBands.CollectMarkers);
            Each(into, KindOutpost, ScOutposts.CollectMarkers);
            Each(into, KindOutpostAttack, ScOutpostRisk.CollectMarkers);
            Each(into, KindScout, ScScouting.CollectMarkers);
            Each(into, KindRunnerCall, ScRunnerCall.CollectMarkers);
            Each(into, KindWatch, ScWatch.CollectMarkers);
        }

        private static void Each(List<Entry> into, string kind, Action<List<Entry>> collect)
        {
            int start = into.Count;
            try
            {
                collect(into);
            }
            catch (Exception e)
            {
                into.RemoveRange(start, into.Count - start);
                foreach (Entry old in published.Values) if (old.Kind == kind) into.Add(old);
                ScLog.Once("markers-collect-" + kind, e);
            }
        }

        /// <summary>
        /// The collected list into the published set: a new marker, or a new class or label, at once; a move once it is at least
        /// MoveMin from the published spot and MoveEvery seconds after it was published; a marker no longer listed goes. The
        /// kinds that saw anything but a move go into changedKinds (the listen host's map follows some).
        /// </summary>
        private static void Publish(List<Entry> list, float now)
        {
            changedKinds.Clear();
            seen.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                Entry e = list[i];
                if (!seen.Add(e.Id)) continue;            // a key listed twice: the first wins
                Entry old;
                if (published.TryGetValue(e.Id, out old))
                {
                    if (ReferenceEquals(old, e)) continue;     // a kind kept as it was (its collector threw)
                    if (old.Cls == e.Cls && old.Label == e.Label && old.NearBase == e.NearBase)
                    {
                        float d = Vector3.Distance(old.Pos, e.Pos);
                        if (d < 0.01f || d < e.MoveMin || now - old.Published < e.MoveEvery) continue;
                    }
                    else changedKinds.Add(e.Kind);
                }
                else changedKinds.Add(e.Kind);
                e.Published = now;
                published[e.Id] = e;
            }
            gone.Clear();
            foreach (KeyValuePair<string, Entry> kv in published) if (!seen.Contains(kv.Key)) gone.Add(kv.Key);
            for (int i = 0; i < gone.Count; i++)
            {
                changedKinds.Add(published[gone[i]].Kind);
                published.Remove(gone[i]);
            }
            gone.Clear();
            seen.Clear();
        }

        /// <summary>
        /// One client: the published markers it may see that it was not sent as they are now (put, or move when only the
        /// position changed), and the ones it was sent that it may no longer see (drop). The whole set the first time. Its
        /// record changes only once the package went out.
        /// </summary>
        private static void SendTo(ClientInfo ci)
        {
            EntityPlayer viewer = ScPlayers.ByEntity(ci.entityId);
            if (viewer == null) return;                   // not spawned yet: the whole set once it is
            Dictionary<string, Entry> had;
            bool full = !sent.TryGetValue(ci, out had);
            bool member = ScPlayers.InCommunity(viewer);
            int nearState = -1;                           // -1 not worked out yet, 0 away, 1 near the home zone
            ops.Clear();
            commitPut.Clear();
            commitDrop.Clear();
            if (member)
            {
                foreach (KeyValuePair<string, Entry> kv in published)
                {
                    Entry e = kv.Value;
                    if (e.NearBase && !Near(viewer, ref nearState)) continue;
                    Entry was = null;
                    if (had != null && had.TryGetValue(kv.Key, out was) && ReferenceEquals(was, e)) continue;
                    ops.Add(was != null && was.Cls == e.Cls && was.Label == e.Label ? Op.MoveOf(e) : Op.PutOf(e));
                    commitPut.Add(e);
                }
            }
            if (had != null)
            {
                foreach (KeyValuePair<string, Entry> kv in had)
                {
                    Entry e;
                    bool visible = member && published.TryGetValue(kv.Key, out e) && (!e.NearBase || Near(viewer, ref nearState));
                    if (visible) continue;
                    ops.Add(Op.DropOf(kv.Value));
                    commitDrop.Add(kv.Key);
                }
            }
            if (!full && ops.Count == 0) return;
            NetPackageScMarkers pkg = NetPackageManager.GetPackage<NetPackageScMarkers>().Setup(ScPlayers.SharedCommunity, full, ops);
            ops.Clear();
            if (!ScNet.ToPlayer(viewer, () => pkg, null)) return;    // gone between the list and now: nothing recorded
            if (had == null) { had = new Dictionary<string, Entry>(); sent[ci] = had; }
            for (int i = 0; i < commitDrop.Count; i++) had.Remove(commitDrop[i]);
            for (int i = 0; i < commitPut.Count; i++) had[commitPut[i].Id] = commitPut[i];
            commitPut.Clear();
            commitDrop.Clear();
            if (full) ScLog.Verbose("markers: the whole set (" + had.Count + ") to " + ci.playerName);
        }

        /// <summary>The viewer is within BaseAttackLeaveRange of the home zone (the host's own rule for the watch posts' horde marker), worked out once per send.</summary>
        private static bool Near(EntityPlayer viewer, ref int state)
        {
            if (state < 0) state = ScBaseAttack.OutsideZone(viewer.position) <= ScConfig.BaseAttackLeaveRange ? 1 : 0;
            return state == 1;
        }

        /// <summary>
        /// The kinds a remote player can change today whose host-side markers have a RefreshMarkers: outposts (the MP2 ops:
        /// establish, upgrade, repair, mode, staffing, disband) and enclaves and infestations (a client's sc enclave|infest
        /// reveal|hide). The rest change only from the server's own ticks, which draw the host's markers themselves.
        /// </summary>
        private static bool Follows(string kind)
        {
            return kind == KindOutpost || kind == KindEnclave || kind == KindInfestation;
        }

        /// <summary>
        /// MP3's known gap, made general: a marker of a kind that Follows changed in the server's set (an outpost's yield after
        /// anyone's upgrade, repair, mode or staffing; one established or gone; an enclave or infestation a client revealed) -
        /// the listen host's own map is drawn again from the records, as the host's own verbs do (RefreshMarkers). Only while
        /// remote clients are on (ServerTick builds nothing otherwise): single player and a host alone are untouched.
        /// </summary>
        private static void HostFollow()
        {
            try
            {
                EntityPlayerLocal host = ScCommunity.LocalPlayer;
                if (host == null) { followDue.Clear(); return; }      // a dedicated server has no map of its own
                if (followDue.Contains(KindOutpost)) ScOutposts.RefreshMarkers(host);
                if (followDue.Contains(KindEnclave)) ScEnclaves.RefreshMarkers(host);
                if (followDue.Contains(KindInfestation)) ScInfestations.RefreshMarkers(host);
            }
            catch (Exception e)
            {
                ScLog.Once("markers-host-follow", e);
            }
            followDue.Clear();
        }

        // ------------------------------------------------------------------ the client ----

        /// <summary>Client: what this machine registered for one marker of the server's set.</summary>
        private sealed class Shown
        {
            public NavObject Obj;
            public EntityPlayerLocal Owner;
            public string Cls, Label;
            public Vector3 Pos, Offset;
        }

        /// <summary>Client: the server's set as received (id -> entry).</summary>
        private static readonly Dictionary<string, Entry> wanted = new Dictionary<string, Entry>();
        /// <summary>Client: the markers registered for it (id -> map object).</summary>
        private static readonly Dictionary<string, Shown> shown = new Dictionary<string, Shown>();
        private static readonly List<string> drops = new List<string>();
        private static World clientWorld;
        private static bool clientDirty, clientTold;
        private static float nextHeal;

        /// <summary>Client: the number of markers the server listed and the number registered here (the log).</summary>
        public static int ClientWanted { get { return wanted.Count; } }
        public static int ClientShown { get { return shown.Count; } }

        /// <summary>
        /// Client (NetPackageScMarkers.ProcessPackage, main thread): the server's set (full) or its changes, then this machine's
        /// map objects squared with it. Never on a server or in single player.
        /// </summary>
        internal static void Receive(int community, bool full, Op[] list)
        {
            try
            {
                if (!ScNet.IsClient) return;          // a server draws its own (its subsystems' AddMarker): a package never lands there
                if (ScCommunity.World == null) return;     // a late package after the world closed
                if (community != ScPlayers.SharedCommunity) return;   // Stage 1 has one community
                ClientWorld();
                if (full) wanted.Clear();
                int puts = 0, moves = 0, dropped = 0;
                if (list != null)
                {
                    for (int i = 0; i < list.Length; i++)
                    {
                        Op op = list[i];
                        string id = (op.Kind ?? "") + ":" + (op.Key ?? "");
                        Entry was;
                        switch (op.Code)
                        {
                            case Op.Put:
                                wanted[id] = new Entry(op.Kind, op.Key, op.Cls, op.Pos, op.Label);
                                puts++;
                                break;
                            case Op.Move:
                                if (wanted.TryGetValue(id, out was)) { wanted[id] = new Entry(was.Kind, was.Key, was.Cls, op.Pos, was.Label); moves++; }
                                break;
                            case Op.Drop:
                                if (wanted.Remove(id)) dropped++;
                                break;
                        }
                    }
                }
                clientDirty = true;
                Sync();
                if (full && !clientTold)
                {
                    clientTold = true;
                    ScLog.Out("map markers from the server: " + wanted.Count + " (" + KindCounts() + "), " + shown.Count + " on this map");
                }
                else if (full || puts > 0 || dropped > 0)     // moves alone (a band walking) are not logged
                    ScLog.Verbose("map markers from the server: " + (full ? "the whole set, " : "") + puts + " put, " + moves + " moved, " + dropped
                                  + " dropped - " + wanted.Count + " listed, " + shown.Count + " on this map");
            }
            catch (Exception e)
            {
                ScLog.Once("markers-receive", e);
            }
        }

        /// <summary>
        /// Client, ScTicker (once a second, before its server-only part): the map squared with the server's set when a package
        /// could not be applied yet (no local player), and every HealSeconds (a marker removed by someone else comes back).
        /// </summary>
        public static void ClientTick()
        {
            try
            {
                if (!ScNet.IsClient) return;
                ClientWorld();
                if (wanted.Count == 0 && shown.Count == 0) return;
                if (!clientDirty && Time.unscaledTime < nextHeal) return;
                Sync();
            }
            catch (Exception e)
            {
                ScLog.Once("markers-client-tick", e);
            }
        }

        /// <summary>Client: a new world (a new session) forgets the old set; the server sends the whole new one after the hello.</summary>
        private static void ClientWorld()
        {
            World w = ScCommunity.World;
            if (w == clientWorld) return;
            clientWorld = w;
            if (NavObjectManager.HasInstance) foreach (Shown s in shown.Values) Unshow(NavObjectManager.Instance, s);
            wanted.Clear();
            shown.Clear();
            clientDirty = false;
            clientTold = false;
            nextHeal = 0f;
        }

        /// <summary>Client: this machine's map objects squared with the server's set (registered, moved, re-labeled, removed).</summary>
        private static void Sync()
        {
            World w = ScCommunity.World;
            EntityPlayerLocal me = w != null ? w.GetPrimaryPlayer() : null;
            if (me == null) return;                       // stays dirty: the next tick tries again
            NavObjectManager nav = NavObjectManager.Instance;
            drops.Clear();
            foreach (KeyValuePair<string, Shown> kv in shown) if (!wanted.ContainsKey(kv.Key)) drops.Add(kv.Key);
            for (int i = 0; i < drops.Count; i++)
            {
                Unshow(nav, shown[drops[i]]);
                shown.Remove(drops[i]);
            }
            drops.Clear();
            foreach (KeyValuePair<string, Entry> kv in wanted)
            {
                Entry e = kv.Value;
                Shown s;
                if (shown.TryGetValue(kv.Key, out s))
                {
                    if (s.Owner == me && s.Cls == e.Cls && s.Label == e.Label && Ours(nav, s))
                    {
                        if ((s.Pos - e.Pos).sqrMagnitude > 0.0001f)
                        {
                            s.Obj.TrackedPosition = e.Pos + s.Offset;
                            s.Pos = e.Pos;
                        }
                        continue;
                    }
                    Unshow(nav, s);
                    shown.Remove(kv.Key);
                }
                s = Show(nav, me, e);
                if (s != null) shown[kv.Key] = s;
            }
            clientDirty = false;
            nextHeal = Time.unscaledTime + HealSeconds;
        }

        /// <summary>
        /// Client: the marker registered as the subsystems register theirs (by position, owner = this machine's player, the label
        /// as its name). RegisterNavObject hands back an existing object of the same class and owner at the same spot: one held
        /// by a quest or another of these markers is never taken over - the marker goes 1 cm aside (ScRunnerCall's rule).
        /// </summary>
        private static Shown Show(NavObjectManager nav, EntityPlayerLocal me, Entry e)
        {
            try
            {
                if (nav == null || NavObjectClass.GetNavObjectClass(e.Cls) == null) return null;      // a class this game lacks
                Vector3 off = Vector3.zero;
                NavObject obj = nav.RegisterNavObject(e.Cls, e.Pos, "", false, -1, me);
                if (obj != null && HeldElsewhere(obj, me))
                {
                    off = new Vector3(0.01f, 0f, 0f);
                    obj = nav.RegisterNavObject(e.Cls, e.Pos + off, "", false, -1, me);
                    if (obj != null && HeldElsewhere(obj, me)) obj = null;
                }
                if (obj == null) return null;
                if (e.Cls == QuestClass)
                {
                    // the fields Quest.HandleMapObject fills, plus the ones NavObject.Reset leaves on a recycled object (ScRunnerCall.AddMarker)
                    obj.ExtraData = -1f;
                    obj.hiddenOnMap = false;
                    obj.HiddenDisplayName = null;
                    obj.UseOverrideFontColor = false;
                }
                obj.name = e.Label;
                obj.IsActive = true;
                return new Shown { Obj = obj, Owner = me, Cls = e.Cls, Label = e.Label, Pos = e.Pos, Offset = off };
            }
            catch (Exception ex)
            {
                ScLog.Once("markers-show", ex);
                return null;
            }
        }

        /// <summary>The object is a quest's own (the local journal) or another marker of the set: never ours to take over.</summary>
        private static bool HeldElsewhere(NavObject obj, EntityPlayerLocal me)
        {
            foreach (Shown s in shown.Values) if (s.Obj == obj) return true;
            QuestJournal j = me != null ? me.QuestJournal : null;
            if (j == null || j.quests == null) return false;
            for (int i = 0; i < j.quests.Count; i++) if (j.quests[i] != null && j.quests[i].NavObject == obj) return true;
            return false;
        }

        /// <summary>Still the object this registered: in the manager, the same class, owner and name (the manager pools and reuses removed ones).</summary>
        private static bool Ours(NavObjectManager nav, Shown s)
        {
            NavObject o = s != null ? s.Obj : null;
            return o != null && nav != null && nav.NavObjectList.Contains(o) && o.OwnerEntity == s.Owner && o.NavObjectClass != null
                   && o.NavObjectClass.NavObjectClassName == s.Cls && o.name == s.Label;
        }

        private static void Unshow(NavObjectManager nav, Shown s)
        {
            try
            {
                if (Ours(nav, s)) nav.UnRegisterNavObject(s.Obj);
            }
            catch (Exception e)
            {
                ScLog.Once("markers-unshow", e);
            }
            if (s != null) s.Obj = null;
        }

        /// <summary>"3 enclave, 1 band" for the log.</summary>
        private static string KindCounts()
        {
            var n = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (Entry e in wanted.Values)
            {
                int c;
                n.TryGetValue(e.Kind, out c);
                n[e.Kind] = c + 1;
            }
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in n) parts.Add(kv.Value.ToString(CultureInfo.InvariantCulture) + " " + kv.Key);
            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "none";
        }
    }
}
