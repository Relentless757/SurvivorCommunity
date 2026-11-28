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
    /// world saves without a record of SC's - an 0-XNPCCore hire (its Leader cvar), a stranger with a grudge against a player (Persist), a console spawn -
    /// came back from the save with a fresh roll: a new face and new armor. 0-XNPCCore keeps its own look in the body's cvars
    /// (SdcsLookArch, SdcsArmorSet), but that applier is the one SC keeps off these classes.
    ///
    /// What is kept: for each loaded body of a managed class that the world will save (Entity.IsSavedToFile: SCore's rule with
    /// SC's veto, the one Chunk.write applies) and that has no record of SC's, the look it wears now (ScLooks.SessionLook, the
    /// same Look object), by entity id and class. ScLooks.ForEntity reads it back when the body is built again (X1: the entity id
    /// is set before Init, the cvars only after it, so a cvar could not reach the first build). A body that dies, joins, leaves
    /// the world unsaved or is no longer saved loses its entry; one that is not loaded keeps it (it is in its chunk's save).
    /// Entity ids are not reused within a world (WorldState.nextEntityID), and the class must match as well.
    ///
    /// When: every ScanSeconds the table is squared with the world (Tick, from ScTicker on the host whether or not a player is
    /// online) and the registry saved when it changed; a body leaving the world is squared at once (World.EntityUnloadedDelegates,
    /// before the chunk is written) and saved on the next tick; WorldShuttingDown squares it before the registry's last save.
    /// Saved as [world] keptlook=id;class;day;look;gear lines (ScRegistry.WriteBookkeeping, never to a client; a key an older
    /// build ignores and drops at its next save). The day is when the body was last seen loaded: past MaxEntries the
    /// longest-unseen go. Host only, main thread. Both games.
    ///
    /// The backstop: a body's own roll is seeded by its entity id and the world's seed (ScLooks.LookSeed), so a body whose kept
    /// look was lost (a crash between its chunk's save and the registry's, a registry read back from its .bak) rolls the same look
    /// again, while the config and the installed packs are the same. The voice follows the look and the name (ScVoice).
    /// </summary>
    public static class ScLookKeep
    {
        /// <summary>The [world] key of one kept look.</summary>
        public const string Key = "keptlook";
        /// <summary>At most this many kept looks; past it the longest-unseen bodies' looks go (a safety cap, not a working limit).</summary>
        public const int MaxEntries = 1000;
        /// <summary>Real seconds between two passes over the world's bodies.</summary>
        private const float ScanSeconds = 5f;

        private sealed class Kept
        {
            /// <summary>The entity class the look was kept for (a body of another class on the id never takes it).</summary>
            public string Class;
            /// <summary>The look itself: the body's session look, shared, so a change to it is what the next save writes.</summary>
            public Look Look;
            /// <summary>Fingerprint of the look as last noted (Fingerprint): a change from it is saved.</summary>
            public string Written;
            /// <summary>The world day the body was last seen loaded (MaxEntries drops the oldest first).</summary>
            public int Day;
        }

        private static readonly Dictionary<int, Kept> kept = new Dictionary<int, Kept>();
        private static float nextScan;
        private static bool saveDue;
        /// <summary>The world whose EntityUnloadedDelegates this listens to.</summary>
        private static World hooked;
        /// <summary>Bodies built from a kept look this session (sc xnpc counts it too: lk1-look-restored).</summary>
        public static int Restored;

        /// <summary>How many looks are kept.</summary>
        public static int Count { get { return kept.Count; } }

        /// <summary>
        /// ScLooks.ForEntity, a body being built on the host: the look kept for this entity id when it was kept for the same class,
        /// else null. The Look object itself, so the session shares it (as a member's record does).
        /// </summary>
        internal static Look Find(int entityId, string cls)
        {
            Kept k;
            if (!kept.TryGetValue(entityId, out k) || k.Look == null) return null;
            if (!string.Equals(k.Class, cls, StringComparison.OrdinalIgnoreCase)) return null;
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
        /// ScRegistry.ParseWorld: one keptlook= line, "id;class;day;look;gear" (Look.Encode and EncodeGear hold no ';'). A line that
        /// does not read is skipped.
        /// </summary>
        internal static void Parse(string val)
        {
            string[] f = (val ?? "").Split(new[] { ';' }, 5);
            if (f.Length < 4) return;
            int id, day;
            if (!int.TryParse(f[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || id <= 0) return;
            string cls = f[1].Trim();
            if (cls.Length == 0) return;
            if (!int.TryParse(f[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out day)) day = 0;
            var look = new Look();
            if (!look.Decode(f[3].Trim())) return;
            if (f.Length > 4) look.DecodeGear(f[4].Trim());
            kept[id] = new Kept { Class = cls, Look = look, Written = Fingerprint(look), Day = Math.Max(0, day) };
        }

        /// <summary>ScRegistry.WriteBookkeeping (never to a client): one keptlook= line per kept look, by entity id.</summary>
        internal static void Write(StringBuilder sb)
        {
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
                  .Append(';').Append(Safe(k.Look.EncodeGear()))
                  .AppendLine();
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

        // ------------------------------------------------------------------ squaring it with the world ----

        /// <summary>
        /// ScTicker, once a second on the host while the registry is loaded: a change the unload hook noted is saved now; every
        /// ScanSeconds the table is squared with the world's bodies (Sync) and saved when it changed.
        /// </summary>
        public static void Tick(World w)
        {
            if (w == null || !ScRegistry.Loaded || ScRegistry.IsShuttingDown) return;
            try
            {
                Hook(w);
                bool save = saveDue;
                float now = UnityEngine.Time.unscaledTime;
                if (now >= nextScan)
                {
                    nextScan = now + ScanSeconds;
                    if (Sync(w)) save = true;
                }
                if (!save) return;
                saveDue = false;
                ScRegistry.SaveAtomic();
            }
            catch (Exception ex)
            {
                ScLog.Once("keptlook-tick", ex);
            }
        }

        /// <summary>
        /// ModEvents.WorldShuttingDown, before the registry's last save (ScRegistry.FlushAndReset) and the world's: the table squared
        /// with the bodies still in the world, which the world's save is about to write. Then the listener goes.
        /// </summary>
        public static void OnWorldShuttingDown()
        {
            try
            {
                World w = ScCommunity.World;
                if (w != null && ScCommunity.IsHost && ScRegistry.Loaded && !ScRegistry.IsShuttingDown) Sync(w);
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
            Restored = 0;
            Unhook();
        }

        /// <summary>Every loaded body of a managed class noted (Note); then the cap. True when the table changed.</summary>
        private static bool Sync(World w)
        {
            bool changed = false;
            int day = GameUtils.WorldTimeToDays(w.worldTime);
            List<Entity> list = w.Entities.list;
            for (int i = 0; i < list.Count; i++)
            {
                EntityAlive a = list[i] as EntityAlive;
                if ((object)a == null || !ScXnpc.IsBody(a)) continue;
                if (Note(a, day)) changed = true;
            }
            if (Trim()) changed = true;
            return changed;
        }

        /// <summary>
        /// One body: kept with the look it wears (or its entry brought up to date) when Keepable, else its entry dropped. True when
        /// what the save writes changed (a new entry, another look or class, an entry gone); a new last-seen day alone is not.
        /// </summary>
        private static bool Note(EntityAlive a, int day)
        {
            Kept k;
            bool had = kept.TryGetValue(a.entityId, out k);
            string cls;
            Look look;
            if (!Keepable(a, out cls, out look))
            {
                if (!had) return false;
                kept.Remove(a.entityId);
                Patches_Xnpc.Count("lk1-look-dropped");
                return true;
            }
            string fp = Fingerprint(look);
            bool differs = !had || k.Written != fp || !string.Equals(k.Class, cls, StringComparison.OrdinalIgnoreCase);
            if (!had) { k = new Kept(); kept[a.entityId] = k; }
            k.Class = cls;
            k.Look = look;
            k.Day = day;
            if (!differs) return false;
            k.Written = fp;
            Patches_Xnpc.Count(had ? "lk1-look-updated" : "lk1-look-kept");
            if (!had) ScLog.Verbose("kept looks: " + ScCommunity.DisplayName(a) + " (" + cls + ", id " + a.entityId + ") keeps " + ScLooks.Describe(look) + " with the save");
            return true;
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

        /// <summary>Past MaxEntries, the entries of the bodies seen longest ago go. True when any went.</summary>
        private static bool Trim()
        {
            if (kept.Count <= MaxEntries) return false;
            var order = new List<KeyValuePair<int, Kept>>(kept);
            order.Sort((x, y) => x.Value.Day != y.Value.Day ? x.Value.Day.CompareTo(y.Value.Day) : x.Key.CompareTo(y.Key));
            int drop = kept.Count - MaxEntries;
            for (int i = 0; i < drop; i++) kept.Remove(order[i].Key);
            ScLog.Out("kept looks: the " + drop + " longest-unseen bod" + (drop == 1 ? "y's look goes" : "ies' looks go") + " (more than " + MaxEntries + " kept)");
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
        /// (kept when the chunk saves it, dropped when not); for any other reason (killed, despawned, picked up, removed) it is not
        /// coming back, and its entry goes. Saved on the next tick.
        /// </summary>
        private static void OnEntityUnloaded(Entity e, EnumRemoveEntityReason reason)
        {
            try
            {
                EntityAlive a = e as EntityAlive;
                if ((object)a == null || !ScCommunity.IsHost || !ScRegistry.Loaded || ScRegistry.IsShuttingDown) return;
                if (!ScXnpc.IsBody(a)) return;
                bool changed;
                if (reason == EnumRemoveEntityReason.Unloaded)
                {
                    World w = ScCommunity.World;
                    changed = Note(a, w != null ? GameUtils.WorldTimeToDays(w.worldTime) : 0);
                }
                else
                {
                    changed = kept.Remove(a.entityId);
                    if (changed) Patches_Xnpc.Count("lk1-look-dropped");
                }
                if (changed) saveDue = true;
            }
            catch (Exception ex)
            {
                ScLog.Once("keptlook-unloaded", ex);
            }
        }
    }
}
