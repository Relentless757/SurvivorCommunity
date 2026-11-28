// round lk1: new file - the looks of survivor bodies with no record of SC's, kept with the save
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round lk1 (2026-11-27; a player's report: "a member re-rolled their appearance and gear when reloading a save"): the look
    /// SC gave a survivor body that has no record of SC's, kept with the save.
    ///
    /// SC dresses every body of its managed classes (ScXnpc.IsBody: 0-XNPCCore's six survivor classes and SC's raiders), whoever
    /// spawned it, and keeps 0-XNPCCore's own look applier off them (Config/entityclasses.xml, X1b). Before this round the look
    /// was stored only in a member's or a prospect's record (and an enclave's, a band's or a runner's person, whose bodies are
    /// always re-made from it); anyone else wore a look from ScLooks' session table, cleared at every world load. So a body the
    /// world saves without a record of SC's - an 0-XNPCCore hire (its Leader cvar), a stranger with a grudge against a player
    /// (Persist), a console spawn - came back from the save with a fresh roll: a new face and new armor. 0-XNPCCore keeps its own
    /// look in the body's cvars (SdcsLookArch, SdcsArmorSet), but that applier is the one SC keeps off these classes.
    ///
    /// What is kept: for each loaded body of a managed class that the world will save (Entity.IsSavedToFile: SCore's rule with
    /// SC's veto, the one Chunk.write applies) and that has no record of SC's, the look it wears now (ScLooks.SessionLook, the
    /// same Look object), by entity id and class. ScLooks.ForEntity reads it back when the body is built again (X1: the entity id
    /// is set before Init, the cvars only after it, so a cvar could not reach the first build). A raider is kept too, but only
    /// when the world saves it (a console spawn): raid, den and wild raiders are Dynamic or Biome bodies and never are. Entity
    /// ids are not reused within a world (WorldState.nextEntityID), and the class must match as well.
    ///
    /// Round lk1 (review, LK1-m1): an entry whose body dies, joins, leaves the world any other way than with its saved chunk
    /// (killed, despawned, picked up) or is no longer saved is only marked Gone - still written, still found. Marked entries go
    /// at the world-save beat (OnWorldSave, from the PersistentPlayerList.Write postfix, and at WorldShuttingDown), so a crash
    /// before the world's next save, which brings the body back, never costs it its look. A body that is not loaded keeps its
    /// entry (it is in its chunk's save).
    ///
    /// When: every ScanSeconds the table is squared with the world (Tick, from ScTicker on the host whether or not a player is
    /// online); a body leaving the world is squared at once (World.EntityUnloadedDelegates, before the chunk is written);
    /// WorldShuttingDown squares it before the registry's last save. Round lk1 (review, LK1-n4): a change is written by the next
    /// registry save anyone makes, and by Tick's own SaveAtomic only when none came within SaveDelaySeconds.
    /// Saved as [world] keptlook=id;class;day;look;gear[;h] lines (ScRegistry.WriteBookkeeping, never to a client; a key an older
    /// build ignores and drops at its next save). The day is when the body was last seen loaded; "h": it had a hire link
    /// (Leader or Owner) then. Past MaxEntries the cap evicts (Trim, LK1-m3): Gone entries first, then bodies nobody hired, then
    /// the longest-unseen - never a body seen in the current pass. Host only, main thread. Both games.
    ///
    /// The backstop: a body's own roll is seeded by its entity id and the world's seed (ScLooks.LookSeed), so a body whose kept
    /// look was lost (a crash between its chunk's save and the registry's, a registry read back from its .bak) rolls the same look
    /// again, while the config and the installed packs are the same. The voice follows the look and the name (ScVoice).
    ///
    /// Known gap: a hire picked up as an item and placed again is a new entity id, so it gets a new look (a later round).
    /// </summary>
    public static class ScLookKeep
    {
        /// <summary>The [world] key of one kept look.</summary>
        public const string Key = "keptlook";
        /// <summary>At most this many kept looks; past it Trim evicts (a safety cap, not a working limit).</summary>
        public const int MaxEntries = 1000;
        /// <summary>Real seconds between two passes over the world's bodies.</summary>
        private const float ScanSeconds = 5f;
        /// <summary>Round lk1 (review, LK1-n4): real seconds a change waits for somebody else's registry save before Tick saves it.</summary>
        private const float SaveDelaySeconds = 30f;
        /// <summary>Round lk1 (review, LK1-m3): the line's last field when the body had a hire link at its last note.</summary>
        private const string HiredMark = "h";

        private sealed class Kept
        {
            /// <summary>The entity class the look was kept for (a body of another class on the id never takes it).</summary>
            public string Class;
            /// <summary>The look itself: the body's session look, shared, so a change to it is what the next save writes.</summary>
            public Look Look;
            /// <summary>Fingerprint of the look as last noted (Fingerprint): a change from it is saved.</summary>
            public string Written;
            /// <summary>The world day the body was last seen loaded (the cap's order).</summary>
            public int Day;
            /// <summary>Round lk1 (review, LK1-m3): the body had a hire link (Leader or Owner) at its last note - evicted last.</summary>
            public bool Hired;
            /// <summary>Round lk1 (review, LK1-m1): its body died, joined, left unsaved or is no longer saved - removed at the world save.</summary>
            public bool Gone;
            /// <summary>Round lk1 (review, LK1-m3): the Sync pass that last saw the body loaded (the cap never evicts the current one's).</summary>
            public int Pass;
        }

        private static readonly Dictionary<int, Kept> kept = new Dictionary<int, Kept>();
        private static float nextScan;
        /// <summary>A change not written yet. Round lk1 (review, LK1-n4): cleared by Write - any registry save carries it.</summary>
        private static bool saveDue;
        /// <summary>Round lk1 (review, LK1-n4): when saveDue was set (real seconds).</summary>
        private static float dueSince;
        /// <summary>Round lk1 (review, LK1-m3): the current Sync pass.</summary>
        private static int pass;
        /// <summary>The world whose EntityUnloadedDelegates this listens to.</summary>
        private static World hooked;
        /// <summary>Bodies built from a kept look this session (sc xnpc counts it too: lk1-look-restored).</summary>
        public static int Restored;

        /// <summary>How many looks are kept (Gone ones included until the world save).</summary>
        public static int Count { get { return kept.Count; } }

        /// <summary>
        /// ScLooks.ForEntity, a body being built on the host: the look kept for this entity id when it was kept for the same class,
        /// else null - a Gone entry too (its body is back: a crash before the world save brought it). The Look object itself, so the
        /// session shares it (as a member's record does).
        /// </summary>
        internal static Look Find(int entityId, string cls)
        {
            Kept k;
            if (!kept.TryGetValue(entityId, out k) || k.Look == null) return null;
            if (!string.Equals(k.Class, cls, StringComparison.OrdinalIgnoreCase)) return null;
            k.Gone = false;         // round lk1 (review, LK1-m1): the body exists after all
            Restored++;
            Patches_Xnpc.Count("lk1-look-restored");
            return k.Look;
        }

        // ------------------------------------------------------------------ the save (ScRegistry) ----

        /// <summary>ScRegistry.ClearContainers: the registry is emptied (a load, the world's end, a client's copy).</summary>
        internal static void Clear()
        {
            kept.Clear();
            saveDue = false;
        }

        /// <summary>
        /// ScRegistry.ParseWorld: one keptlook= line, "id;class;day;look;gear", and ";h" when hired (Look.Encode and EncodeGear hold
        /// no ';'). A line that does not read is skipped.
        /// </summary>
        internal static void Parse(string val)
        {
            string[] f = (val ?? "").Split(new[] { ';' }, 6);       // round lk1 (review, LK1-m3): the hired mark is a sixth field
            if (f.Length < 4) return;
            int id, day;
            if (!int.TryParse(f[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || id <= 0) return;
            string cls = f[1].Trim();
            if (cls.Length == 0) return;
            if (!int.TryParse(f[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out day)) day = 0;
            var look = new Look();
            if (!look.Decode(f[3].Trim())) return;
            if (f.Length > 4) look.DecodeGear(f[4].Trim());
            bool hired = f.Length > 5 && string.Equals(f[5].Trim(), HiredMark, StringComparison.OrdinalIgnoreCase);
            kept[id] = new Kept { Class = cls, Look = look, Written = Fingerprint(look), Day = Math.Max(0, day), Hired = hired, Pass = -1 };
        }

        /// <summary>
        /// ScRegistry.WriteBookkeeping (never to a client): one keptlook= line per kept look, by entity id, Gone ones included (they
        /// go at the world save). Round lk1 (review, LK1-n4): every registry save comes through here, so whatever was due is written.
        /// </summary>
        internal static void Write(StringBuilder sb)
        {
            saveDue = false;
            if (kept.Count == 0) return;
            var ids = new List<int>(kept.Keys);
            ids.Sort();
            foreach (int id in ids)
            {
                Kept k = kept[id];
                if (k.Look == null || string.IsNullOrEmpty(k.Class)) continue;
                sb.Append(Key).Append('=').Append(id.ToString(CultureInfo.InvariantCulture))
                  .Append(';').Append(Safe(k.Class))
                  .Append(';').Append(k.Day.ToString(CultureInfo.InvariantCulture))
                  .Append(';').Append(Safe(k.Look.Encode()))
                  .Append(';').Append(Safe(k.Look.EncodeGear()));
                if (k.Hired) sb.Append(';').Append(HiredMark);     // round lk1 (review, LK1-m3)
                sb.AppendLine();
            }
        }

        /// <summary>A field of the line: no separator and no line break.</summary>
        private static string Safe(string s)
        {
            return (s ?? "").Replace(";", ",").Replace("\r", "").Replace("\n", " ").Trim();
        }

        /// <summary>The look as the save writes it (face and gear).</summary>
        private static string Fingerprint(Look l)
        {
            return l == null ? "" : l.Encode() + "#" + l.EncodeGear();
        }

        /// <summary>Round lk1 (review, LK1-n4): a change to write - the next registry save carries it, Tick's own after SaveDelaySeconds.</summary>
        private static void Due()
        {
            if (saveDue) return;
            saveDue = true;
            dueSince = UnityEngine.Time.unscaledTime;
        }

        // ------------------------------------------------------------------ squaring it with the world ----

        /// <summary>
        /// ScTicker, once a second on the host while the registry is loaded: every ScanSeconds the table is squared with the world's
        /// bodies (Sync); a change no registry save has written within SaveDelaySeconds is saved here.
        /// </summary>
        public static void Tick(World w)
        {
            if (w == null || !ScRegistry.Loaded || ScRegistry.IsShuttingDown) return;
            try
            {
                Hook(w);
                float now = UnityEngine.Time.unscaledTime;
                if (now >= nextScan)
                {
                    nextScan = now + ScanSeconds;
                    if (Sync(w)) Due();
                }
                // round lk1 (review, LK1-n4): at most one save of its own per SaveDelaySeconds, and none when another save wrote it
                if (!saveDue || now - dueSince < SaveDelaySeconds) return;
                ScRegistry.SaveAtomic();      // its Write clears saveDue
                saveDue = false;              // a save that never reached Write (no save directory yet) waits for the next change
            }
            catch (Exception ex)
            {
                ScLog.Once("keptlook-tick", ex);
            }
        }

        /// <summary>
        /// Round lk1 (review, LK1-m1): the world-save beat (the PersistentPlayerList.Write postfix, right before its registry save):
        /// the Gone entries go - the world's save no longer holds their bodies.
        /// </summary>
        public static void OnWorldSave()
        {
            try
            {
                if (!ScCommunity.IsHost || !ScRegistry.Loaded || ScRegistry.IsShuttingDown) return;
                PurgeGone();
            }
            catch (Exception ex)
            {
                ScLog.Once("keptlook-worldsave", ex);
            }
        }

        /// <summary>
        /// ModEvents.WorldShuttingDown, before the registry's last save (ScRegistry.FlushAndReset) and the world's: the table squared
        /// with the bodies still in the world, which the world's save is about to write, and the Gone entries removed. Then the
        /// listener goes.
        /// </summary>
        public static void OnWorldShuttingDown()
        {
            try
            {
                World w = ScCommunity.World;
                if (w != null && ScCommunity.IsHost && ScRegistry.Loaded && !ScRegistry.IsShuttingDown)
                {
                    Sync(w);
                    PurgeGone();          // round lk1 (review, LK1-m1)
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("keptlook-shutdown", ex);
            }
            saveDue = false;
            Unhook();
        }

        /// <summary>ScLooks.Reset (GameStarting): the session's timers and listener. The table is the registry's (Clear).</summary>
        public static void Reset()
        {
            nextScan = 0f;
            saveDue = false;
            pass = 0;
            Restored = 0;
            Unhook();
        }

        /// <summary>Round lk1 (review, LK1-m1): the Gone entries removed. How many went.</summary>
        private static int PurgeGone()
        {
            List<int> gone = null;
            foreach (KeyValuePair<int, Kept> kv in kept)
            {
                if (!kv.Value.Gone) continue;
                if (gone == null) gone = new List<int>();
                gone.Add(kv.Key);
            }
            if (gone == null) return 0;
            foreach (int id in gone) kept.Remove(id);
            Patches_Xnpc.Count("lk1-look-purged");
            ScLog.Verbose("kept looks: " + gone.Count + " entr" + (gone.Count == 1 ? "y" : "ies") + " of bodies gone removed at the world save");
            return gone.Count;
        }

        /// <summary>Every loaded body of a managed class noted (Note); then the cap. True when what the save writes changed.</summary>
        private static bool Sync(World w)
        {
            bool changed = false;
            pass++;                     // round lk1 (review, LK1-m3)
            int day = GameUtils.WorldTimeToDays(w.worldTime);
            List<Entity> list = w.Entities.list;
            for (int i = 0; i < list.Count; i++)
            {
                // round lk1 (review, LK1-n5): one body that throws costs only itself, not the rest of the pass
                try
                {
                    EntityAlive a = list[i] as EntityAlive;
                    if ((object)a == null || !ScXnpc.IsBody(a)) continue;
                    if (Note(a, day)) changed = true;
                }
                catch (Exception ex)
                {
                    ScLog.Once("keptlook-note", ex);
                }
            }
            if (Trim()) changed = true;
            return changed;
        }

        /// <summary>
        /// One body: kept with the look it wears (or its entry brought up to date) when Keepable, else its entry marked Gone. True
        /// when what the save writes changed (a new entry, another look, class or hired mark); a new last-seen day or a Gone mark
        /// alone is not.
        /// </summary>
        private static bool Note(EntityAlive a, int day)
        {
            Kept k;
            bool had = kept.TryGetValue(a.entityId, out k);
            string cls;
            Look look;
            if (!Keepable(a, out cls, out look))
            {
                // round lk1 (review, LK1-m1): marked, not removed - the line stays until the world save
                if (!had || k.Gone) return false;
                k.Gone = true;
                Patches_Xnpc.Count("lk1-look-dropped");
                return false;
            }
            string fp = Fingerprint(look);
            bool hired = HasHireLink(a);
            bool differs = !had || k.Written != fp || k.Hired != hired || !string.Equals(k.Class, cls, StringComparison.OrdinalIgnoreCase);
            if (!had) { k = new Kept(); kept[a.entityId] = k; }
            k.Class = cls;
            k.Look = look;
            k.Day = day;
            k.Hired = hired;            // round lk1 (review, LK1-m3)
            k.Gone = false;             // round lk1 (review, LK1-m1): kept again (re-hired, saved again)
            k.Pass = pass;
            if (!differs) return false;
            k.Written = fp;
            Patches_Xnpc.Count(had ? "lk1-look-updated" : "lk1-look-kept");
            if (!had) ScLog.Verbose("kept looks: " + ScCommunity.DisplayName(a) + " (" + cls + ", id " + a.entityId + ") keeps " + ScLooks.Describe(look) + " with the save");
            return true;
        }

        /// <summary>Round lk1 (review, LK1-m3): SCore's hire link (Leader, else Owner) names somebody.</summary>
        private static bool HasHireLink(EntityAlive a)
        {
            return a.Buffs != null && (a.Buffs.GetCustomVar("Leader") > 0f || a.Buffs.GetCustomVar("Owner") > 0f);
        }

        /// <summary>
        /// Should this body's look be kept with the save? A living body on this machine that the world saves (IsSavedToFile, SC's
        /// veto included: runners, enclave and band bodies), with no record of SC's (a member's or a prospect's record keeps it, a
        /// stand-in, an enclave's or a band's person is made again from one) and a look of this session's for its class.
        /// </summary>
        private static bool Keepable(EntityAlive a, out string cls, out Look look)
        {
            cls = null;
            look = null;
            if (a.isEntityRemote || a.IsDead() || a.Health <= 0) return false;
            if (ScRegistry.GetMember(a.entityId) != null || ScRegistry.GetProspect(a.entityId) != null) return false;
            if (ScXnpc.IsCommunity(a) || ScEnclavePeople.IsEnclaveNpc(a) || ScBands.IsBandNpc(a)) return false;
            if (!a.IsSavedToFile()) return false;
            cls = ScCommunity.ClassName(a);
            look = ScLooks.SessionLook(a.entityId, cls);
            return look != null;
        }

        /// <summary>
        /// Past MaxEntries the cap evicts. Round lk1 (review, LK1-m3): never an entry seen in the current pass (a loaded body, also
        /// after the world time went back); of the rest the Gone ones first, then bodies with no hire link, then the longest-unseen
        /// (stale saved bodies that never load again age out before a hire parked far away). True when any went.
        /// </summary>
        private static bool Trim()
        {
            if (kept.Count <= MaxEntries) return false;
            var order = new List<KeyValuePair<int, Kept>>();
            foreach (KeyValuePair<int, Kept> kv in kept) if (kv.Value.Pass != pass) order.Add(kv);
            order.Sort((x, y) =>
            {
                if (x.Value.Gone != y.Value.Gone) return x.Value.Gone ? -1 : 1;
                if (x.Value.Hired != y.Value.Hired) return x.Value.Hired ? 1 : -1;
                if (x.Value.Day != y.Value.Day) return x.Value.Day.CompareTo(y.Value.Day);
                return x.Key.CompareTo(y.Key);
            });
            int drop = Math.Min(kept.Count - MaxEntries, order.Count);
            if (drop <= 0) return false;
            for (int i = 0; i < drop; i++) kept.Remove(order[i].Key);
            ScLog.Out("kept looks: " + drop + " entr" + (drop == 1 ? "y" : "ies") + " evicted - gone, never hired or longest unseen first (more than " + MaxEntries + " kept)");
            return true;
        }

        // ------------------------------------------------------------------ a body leaving the world ----

        private static void Hook(World w)
        {
            if (w == null || ReferenceEquals(hooked, w)) return;
            Unhook();
            w.EntityUnloadedDelegates += OnEntityUnloaded;             // World.unloadEntity, before OnEntityUnload: the body intact
            hooked = w;
        }

        private static void Unhook()
        {
            if (hooked != null) { try { hooked.EntityUnloadedDelegates -= OnEntityUnloaded; } catch (Exception) { } }
            hooked = null;
        }

        /// <summary>
        /// A body leaving the world (World.unloadEntity): with its chunk (Unloaded) it is noted as the chunk is about to write it
        /// (kept when the chunk saves it, marked Gone when not). For any other reason (killed, despawned, picked up, removed) it is
        /// not coming back unless a crash undoes it: round lk1 (review, LK1-m1) - its entry is marked Gone, removed at the world save.
        /// </summary>
        private static void OnEntityUnloaded(Entity e, EnumRemoveEntityReason reason)
        {
            try
            {
                EntityAlive a = e as EntityAlive;
                if ((object)a == null || !ScCommunity.IsHost || !ScRegistry.Loaded || ScRegistry.IsShuttingDown) return;
                if (!ScXnpc.IsBody(a)) return;
                if (reason == EnumRemoveEntityReason.Unloaded)
                {
                    World w = ScCommunity.World;
                    if (Note(a, w != null ? GameUtils.WorldTimeToDays(w.worldTime) : 0)) Due();
                    return;
                }
                Kept k;
                if (kept.TryGetValue(a.entityId, out k) && !k.Gone)
                {
                    k.Gone = true;
                    Patches_Xnpc.Count("lk1-look-dropped");
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("keptlook-unloaded", ex);
            }
        }
    }
}
