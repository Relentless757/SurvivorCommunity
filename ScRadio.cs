using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>A survivor heard over the radio: simulated as a record at a real POI until its chunk loads, then spawned.</summary>
    public sealed class Contact
    {
        public int Id;
        public string Class = "";
        public string Label = "";
        public Vector3i PoiKey;
        public string PoiName = "";
        public Vector3i Spawn;
        public int Day;
        public int ExpireDay;
    }

    /// <summary>
    /// The radio station (block scRadioStation, powered). Four jobs:
    ///   Airdrop  - vanilla's own air drop, on request only: the AIDirector's scheduled drops are gated off
    ///              (Patches_Radio) and the sandbox "air drop frequency" (hours) is the radio's cooldown.
    ///   Call     - reach out for survivors: one of our player-body survivors (never a pack NPC - those no longer
    ///              spawn while this mod is installed) at a random real POI a few hundred metres out, marked on
    ///              the map, kept as a registry record and spawned the first time that chunk loads (they then ask
    ///              a favour like any recruit). Fades after RadioContactDays.
    ///   Distress - a survivor on a supply run may radio for help (RadioDistressChance while a radio is
    ///              powered): a rescue quest marks the POI they are pinned at; when the player gets there a
    ///              stand-in of the survivor and a gamestage- and skill-scaled pack of zombies spawn around
    ///              it. Clear them and the run comes home safe with a bonus; ignore them and it ends badly.
    ///   Outposts - radios inside the home zone are remembered in the registry; ScOutposts.RadioOnlineAnywhere
    ///              reads their power from the power graph, so establishing and the daily report-in work from afar.
    /// Host only. Contacts, cooldowns and distress runs persist in the registry.
    /// </summary>
    public static class ScRadio
    {
        public const string BlockName = "scRadioStation";
        public const string WindowGroup = "scradio";
        public const string NavClass = "sc_radio_contact";
        public const string StandInCvar = "sc_standin";

        /// <summary>Set only around our own SpawnAirDrop call; the prefix refuses every other spawn.</summary>
        public static bool DropRequested;

        /// <summary>Radio blocks seen this session (daily scan + activation).</summary>
        public static readonly HashSet<Vector3i> Radios = new HashSet<Vector3i>();

        private static readonly Dictionary<int, Vector3> markers = new Dictionary<int, Vector3>();
        private static bool markersRestored;
        private static float nextTick;
        private static int lastZombieClass = -1;

        public static void Reset()
        {
            Radios.Clear();
            markers.Clear();
            markersRestored = false;
            nextTick = 0f;
            DropRequested = false;
            CurrentRadio = null;
        }

        public static void NoteRadio(Vector3i pos)
        {
            Radios.Add(pos);
            ScOutposts.RememberRadio(pos);      // persisted: outposts check its power from anywhere
        }

        /// <summary>A powered radio inside the community zone (loaded).</summary>
        public static bool Online()
        {
            try
            {
                World w = ScCommunity.World;
                if (w == null) return false;
                foreach (Vector3i pos in Radios)
                {
                    if (ScZone.HasHome && !ScZone.Contains(pos)) continue;
                    TileEntityPowered te = w.GetTileEntity(pos) as TileEntityPowered;
                    if (te != null && te.IsPowered) return true;
                }
                return ScOutposts.RadioOnlineAnywhere();     // the power graph: works with the home unloaded
            }
            catch (Exception e)
            {
                ScLog.Once("radio-online", e);
            }
            return false;
        }

        public static bool IsPoweredAt(World w, Vector3i pos)
        {
            TileEntityPowered te = w != null ? w.GetTileEntity(pos) as TileEntityPowered : null;
            return te != null && te.IsPowered;
        }

        // ------------------------------------------------------------------ airdrop ----

        /// <summary>
        /// The sandbox "air drop frequency" is an index the SandboxOptionManager turns into a day range on the
        /// air drop component (MinDayCount..MaxDayCount; 0 = off, and 6 = a random 1-7 days). The radio's
        /// cooldown is one roll of that range, exactly as vanilla schedules its own drops.
        /// </summary>
        public static int DropMinDays() { return Math.Max(0, AIDirectorAirDropComponent.MinDayCount); }
        public static int DropMaxDays() { return Math.Max(0, AIDirectorAirDropComponent.MaxDayCount); }

        public static string DropDaysText()
        {
            int a = DropMinDays(), b = DropMaxDays();
            return a == b ? a.ToString() : a + "-" + b;
        }

        public static bool CanDrop(out string why)
        {
            why = null;
            if (!ScCommunity.IsHost) { why = "scHostOnly"; return false; }
            if (DropMaxDays() <= 0) { why = "scRadioDropsOff"; return false; }
            World w = ScCommunity.World;
            if (w == null) { why = "scInternal"; return false; }
            if (w.worldTime < ScRegistry.Community.RadioNextDrop) { why = "scRadioDropWait"; return false; }
            return true;
        }

        /// <summary>Hours until the next drop may be requested (0 when ready).</summary>
        public static int DropHoursLeft()
        {
            World w = ScCommunity.World;
            if (w == null) return 0;
            ulong now = w.worldTime, next = ScRegistry.Community.RadioNextDrop;
            return next > now ? (int)((next - now + 999UL) / 1000UL) : 0;
        }

        public static bool RequestDrop(EntityPlayerLocal player)
        {
            string why;
            if (!CanDrop(out why)) { if (player != null) ScCommunity.Tell(player, DropWhy(why), "ui_denied"); return false; }
            try
            {
                World w = ScCommunity.World;
                AIDirectorAirDropComponent comp = (w != null && w.aiDirector != null) ? w.aiDirector.GetComponent<AIDirectorAirDropComponent>() : null;
                if (comp == null) { if (player != null) ScCommunity.Tell(player, Localization.Get("scRadioDropsOff"), "ui_denied"); return false; }
                bool ok;
                DropRequested = true;
                try { ok = comp.SpawnAirDrop(); }
                finally { DropRequested = false; }
                if (!ok) { if (player != null) ScCommunity.Tell(player, Localization.Get("scRadioDropBusy"), "ui_denied"); return false; }
                GameRandom rnd = w.GetGameRandom();
                int days = Math.Max(1, rnd != null ? rnd.RandomRange(DropMinDays(), DropMaxDays() + 1) : DropMaxDays());
                ScRegistry.Community.RadioNextDrop = w.worldTime + (ulong)days * 24000UL;
                ScRegistry.SaveAtomic();
                if (player != null) ScCommunity.Tell(player, Localization.Get("scRadioDropSent"), "");
                ScLog.Out("air drop requested over the radio; next in " + days + " day(s)");
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("radio-drop", e);
                return false;
            }
        }

        public static string DropWhy(string key)
        {
            if (key == "scRadioDropWait") return string.Format(Localization.Get(key), DropHoursLeft());
            return Localization.Get(key ?? "scInternal");
        }

        // ------------------------------------------------------------------ contacts ----

        public static bool CanCall(out string why)
        {
            why = null;
            if (!ScCommunity.IsHost) { why = "scHostOnly"; return false; }
            if (!ScZone.HasHome) { why = "scNeedHome"; return false; }
            Community c = ScRegistry.Community;
            if (ScCommunity.Today < c.RadioNextCallDay) { why = "scRadioCallWait"; return false; }
            if (ScRegistry.Contacts.Count >= ScConfig.RadioMaxContacts) { why = "scRadioContactsFull"; return false; }
            return true;
        }

        public static string CallWhy(string key)
        {
            if (key == "scRadioCallWait") return string.Format(Localization.Get(key), Math.Max(0, ScRegistry.Community.RadioNextCallDay - ScCommunity.Today));
            return Localization.Get(key ?? "scInternal");
        }

        /// <summary>
        /// The class a new radio contact or outpost walk-in gets: one of our player-body survivors, capped by the
        /// player's gamestage tier, ranged and firearm by the same odds as a console spawn (ScLoadout.PickClass). Pack
        /// NPCs (Nurse, Baker, Harley...) are never rolled - they no longer spawn while this mod is installed (the
        /// user's call, 2026-09-30). Null when no scSurvivor* class is loaded.
        /// </summary>
        public static string NewContactClass(EntityPlayerLocal player, GameRandom rnd)
        {
            if (rnd == null && ScCommunity.World != null) rnd = ScCommunity.World.GetGameRandom();   // else PickClass takes the first
            return ScLoadout.PickClass(ScLooks.SurvivorClasses(), rnd, null,
                                       ScLooks.MaxTierFor(player != null ? player.gameStage : 0));
        }

        /// <summary>A random real POI whose centre is MinDist..MaxDist from the home block and outside the zone.</summary>
        public static PrefabInstance PickPoi(int minDist, int maxDist, HashSet<Vector3i> exclude, GameRandom rnd)
        {
            try
            {
                DynamicPrefabDecorator dec = GameManager.Instance != null ? GameManager.Instance.GetDynamicPrefabDecorator() : null;
                if (dec == null || !ScZone.HasHome) return null;
                Vector3i home = ScZone.Home;
                var list = new List<PrefabInstance>();
                dec.GetPrefabsAtXZ(home.x - maxDist, home.x + maxDist, home.z - maxDist, home.z + maxDist, list);
                var ok = new List<PrefabInstance>();
                for (int i = 0; i < list.Count; i++)
                {
                    PrefabInstance pi = list[i];
                    if (!ScSecurity.IsRealPoi(pi)) continue;
                    if (ScSecurity.OverlapsHome(pi)) continue;
                    if (ScOutposts.IsOutpostPoiKey(pi.boundingBoxPosition)) continue;   // never send anyone to our own outpost
                    if (exclude != null && exclude.Contains(pi.boundingBoxPosition)) continue;
                    Vector3i a = pi.boundingBoxPosition, s = pi.boundingBoxSize;
                    float cx = a.x + s.x * 0.5f, cz = a.z + s.z * 0.5f;
                    float d = Mathf.Sqrt((cx - home.x) * (cx - home.x) + (cz - home.z) * (cz - home.z));
                    if (d < minDist || d > maxDist) continue;
                    ok.Add(pi);
                }
                if (ok.Count == 0) return null;
                return ok[rnd != null ? rnd.RandomRange(0, ok.Count) : 0];
            }
            catch (Exception e)
            {
                ScLog.Once("radio-pickpoi", e);
                return null;
            }
        }

        public static Vector3 Centre(PrefabInstance pi)
        {
            Vector3i a = pi.boundingBoxPosition, s = pi.boundingBoxSize;
            return new Vector3(a.x + s.x * 0.5f, a.y, a.z + s.z * 0.5f);
        }

        public static bool Call(EntityPlayerLocal player)
        {
            string why;
            if (!CanCall(out why)) { if (player != null) ScCommunity.Tell(player, CallWhy(why), "ui_denied"); return false; }
            try
            {
                World w = ScCommunity.World;
                GameRandom rnd = w.GetGameRandom();
                string cls = NewContactClass(player, rnd);
                if (cls == null) { ScCommunity.Tell(player, Localization.Get("scRadioNobody"), "ui_denied"); return false; }
                var taken = new HashSet<Vector3i>();
                foreach (var ct in ScRegistry.Contacts) taken.Add(ct.PoiKey);
                PrefabInstance pi = PickPoi(ScConfig.RadioContactMinDist, ScConfig.RadioContactMaxDist, taken, rnd);
                if (pi == null) { ScCommunity.Tell(player, Localization.Get("scRadioNoPoi"), "ui_denied"); return false; }
                Community c = ScRegistry.Community;
                Vector3 centre = Centre(pi);
                int y = (int)w.GetHeightAt(centre.x, centre.z) + 1;      // static heightmap: refined with live blocks at spawn time
                var contact = new Contact
                {
                    Id = ++c.NextContactId,
                    Class = cls,
                    Label = ClassLabel(cls),
                    PoiKey = pi.boundingBoxPosition,
                    PoiName = ScSecurity.DisplayName(pi),
                    Spawn = new Vector3i((int)centre.x, y, (int)centre.z),
                    Day = ScCommunity.Today,
                    ExpireDay = ScCommunity.Today + Math.Max(1, ScConfig.RadioContactDays)
                };
                ScRegistry.Contacts.Add(contact);
                c.RadioNextCallDay = ScCommunity.Today + Math.Max(0, ScConfig.RadioCallDays);
                AddMarker(contact, player);
                ScRegistry.SaveAtomic();
                ScCommunity.Tell(player, string.Format(Localization.Get("scRadioContact"), contact.Label, contact.PoiName, Dist(centre)), "");
                ScLog.Out("radio contact: " + cls + " at " + contact.PoiName + " " + contact.PoiKey + " (fades day " + contact.ExpireDay + ")");
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("radio-call", e);
                return false;
            }
        }

        /// <summary>
        /// A contact whose spawn is a given spot (an outpost's walk-in): the class is rolled like a radio call, the
        /// marker and the registry record are the same, and SpawnContact brings them in when that chunk loads.
        /// </summary>
        public static Contact AddContactAt(Vector3i poiKey, string poiName, Vector3i spawn, EntityPlayerLocal player, GameRandom rnd)
        {
            try
            {
                string cls = NewContactClass(player, rnd);
                if (cls == null) return null;
                Community c = ScRegistry.Community;
                var contact = new Contact
                {
                    Id = ++c.NextContactId,
                    Class = cls,
                    Label = ClassLabel(cls),
                    PoiKey = poiKey,
                    PoiName = poiName ?? "",
                    Spawn = spawn,
                    Day = ScCommunity.Today,
                    ExpireDay = ScCommunity.Today + Math.Max(1, ScConfig.RadioContactDays)
                };
                ScRegistry.Contacts.Add(contact);
                if (player != null) AddMarker(contact, player);
                ScRegistry.SaveAtomic();
                return contact;
            }
            catch (Exception e)
            {
                ScLog.Once("radio-contact-at", e);
                return null;
            }
        }

        private static string Dist(Vector3 p)
        {
            Vector3i home = ScZone.Home;
            return ((int)Mathf.Sqrt((p.x - home.x) * (p.x - home.x) + (p.z - home.z) * (p.z - home.z))).ToString();
        }

        /// <summary>The class's localized name ("Nurse (knife)") with a generic fallback.</summary>
        public static string ClassLabel(string cls)
        {
            try
            {
                string s = Localization.Get(cls);
                if (!string.IsNullOrEmpty(s) && s != cls) return s;
            }
            catch (Exception) { }
            return Localization.Get("scRadioSurvivor");
        }

        private static void AddMarker(Contact ct, EntityPlayer owner)
        {
            try
            {
                NavObjectManager nav = NavObjectManager.Instance;
                if (nav == null || owner == null || NavObjectClass.GetNavObjectClass(NavClass) == null) return;
                Vector3 at = new Vector3(ct.Spawn.x + 0.5f, ct.Spawn.y, ct.Spawn.z + 0.5f);
                NavObject obj = nav.RegisterNavObject(NavClass, at, "", false, -1, owner);
                if (obj != null) obj.name = string.Format(Localization.Get("scRadioMarker"), ct.Label, ct.PoiName);
                markers[ct.Id] = at;
            }
            catch (Exception e)
            {
                ScLog.Once("radio-marker", e);
            }
        }

        private static void RemoveMarker(Contact ct)
        {
            try
            {
                Vector3 at;
                if (!markers.TryGetValue(ct.Id, out at)) return;
                markers.Remove(ct.Id);
                NavObjectManager nav = NavObjectManager.Instance;
                if (nav != null) nav.UnRegisterNavObjectByPosition(at, NavClass);
            }
            catch (Exception e)
            {
                ScLog.Once("radio-unmarker", e);
            }
        }

        // ------------------------------------------------------------------ ticker ----

        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (w == null || player == null) return;
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + 1f;
                if (!markersRestored)
                {
                    markersRestored = true;
                    foreach (var ct in ScRegistry.Contacts) AddMarker(ct, player);
                }
                DespawnLater(w);
                TickContacts(w, player);
                TickDistress(w, player);
            }
            catch (Exception e)
            {
                ScLog.Once("radio-tick", e);
            }
        }

        private static readonly List<Contact> scratch = new List<Contact>();

        private static void TickContacts(World w, EntityPlayerLocal player)
        {
            if (ScRegistry.Contacts.Count == 0) return;
            scratch.Clear();
            int today = ScCommunity.Today;
            foreach (var ct in ScRegistry.Contacts)
            {
                if (today >= ct.ExpireDay) { scratch.Add(ct); continue; }
                if (ScZone.IsLoaded(ct.Spawn) && SpawnContact(w, player, ct)) scratch.Add(ct);
            }
            for (int i = 0; i < scratch.Count; i++)
            {
                Contact ct = scratch[i];
                if (today >= ct.ExpireDay)
                {
                    ScCommunity.Tell(player, string.Format(Localization.Get("scRadioFaded"), ct.Label, ct.PoiName), "");
                    ScLog.Out("radio contact at " + ct.PoiName + " faded");
                }
                RemoveMarker(ct);
                ScRegistry.Contacts.Remove(ct);
            }
            if (scratch.Count > 0) ScRegistry.SaveAtomic();
            scratch.Clear();
        }

        /// <summary>The chunk is here: the simulated survivor becomes a real NPC standing at the POI, waiting for a favour.</summary>
        private static bool SpawnContact(World w, EntityPlayerLocal player, Contact ct)
        {
            try
            {
                if (!ScLooks.IsSurvivorClass(ct.Class))
                {
                    // a pack NPC rolled before pack NPCs stopped spawning: one of ours answers instead
                    string again = NewContactClass(player, w.GetGameRandom());
                    if (again == null) { ScLog.Warn("radio contact class " + ct.Class + " is a pack NPC and no scSurvivor class is loaded - dropped"); return true; }
                    ScLog.Out("radio contact " + ct.Id + ": pack NPC " + ct.Class + " no longer spawns - " + again + " answers instead");
                    ct.Class = again;
                    ct.Label = ClassLabel(again);
                }
                int cid = EntityClass.FromString(ct.Class);
                if (!ScCommunity.ClassExists(cid)) { ScLog.Warn("radio contact class " + ct.Class + " no longer exists - dropped"); return true; }
                Vector3 pos = SpotNear(w, ct.Spawn, 4);
                EntityAliveSDX npc;
                try
                {
                    // a player-body survivor rolls their face and clothes here; the model shim consumes it
                    if (ScLooks.IsSurvivorClass(ct.Class)) ScLooks.Pending = ScLooks.Roll(ScLooks.ClassIsMale(ct.Class), w.GetGameRandom(), true);
                    npc = EntityFactory.CreateEntity(cid, pos) as EntityAliveSDX;
                }
                finally { ScLooks.Pending = null; }
                if (npc == null) { ScLog.Warn("radio contact class " + ct.Class + " is not an EntityAliveSDX - dropped"); return true; }
                w.SpawnEntityInWorld(npc);
                npc.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
                npc.Buffs.SetCustomVar("Persist", 1f);
                Prospect p = ScFavours.Ensure(npc, player);       // favour, guard order, sc_favour cvar, position record
                ScCommunity.Tell(player, string.Format(Localization.Get("scRadioFound"), ScCommunity.DisplayName(npc), ct.PoiName), "");
                ScLog.Out("radio contact spawned: " + ScCommunity.DisplayName(npc) + " (" + ct.Class + ", id " + npc.entityId + ") at " + ct.PoiName + (p != null ? ", favour " + p.Favour : ""));
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("radio-spawn", e);
                return true;   // never retry a throwing spawn every second
            }
        }

        /// <summary>A standable spot within a few blocks of a position (live blocks; the position's own column first).</summary>
        public static Vector3 SpotNear(World w, Vector3i around, int radius)
        {
            int y;
            if (ScCommunity.FindStandable(w, around.x, around.z, around.y, out y)) return new Vector3(around.x + 0.5f, y, around.z + 0.5f);
            for (int r = 1; r <= radius; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dz) != r) continue;
                        if (ScCommunity.FindStandable(w, around.x + dx, around.z + dz, around.y, out y)) return new Vector3(around.x + dx + 0.5f, y, around.z + dz + 0.5f);
                    }
                }
            }
            return new Vector3(around.x + 0.5f, around.y + 1, around.z + 0.5f);
        }

        // ------------------------------------------------------------------ distress ----

        /// <summary>Called from ScRuns.Send: does this run turn into a radio distress call?</summary>
        public static bool RollDistress(Member m, EntityPlayerLocal player, GameRandom rnd)
        {
            if (m == null || !Online()) return false;
            float roll = rnd != null ? rnd.RandomFloat : UnityEngine.Random.value;
            if (roll >= ScConfig.RadioDistressChance) return false;
            return ForceDistress(m, player, rnd);
        }

        /// <summary>The distress call itself (also the console's "sc distress").</summary>
        public static bool ForceDistress(Member m, EntityPlayerLocal player, GameRandom rnd)
        {
            try
            {
                if (m == null || m.Status != MemberStatus.OnRun) return false;
                if (ScRunners.IsPhysical(m))
                {
                    // a physical runner calls from the site they are searching - only once they are there
                    if (m.RunLeg != RunLeg.AtSite) return false;
                    PrefabInstance dpi = ScSecurity.PoiByKey(m.RunDest);
                    m.RunDistress = true;
                    m.RunSafe = false;
                    m.EventSpawned = false;
                    m.StandInId = -1;
                    m.EventZombies.Clear();
                    m.RunPoi = m.RunDest;
                    m.RunPoiName = m.RunDestName;
                    m.RunSpawn = m.RunSite;
                    string perr = null;
                    if (dpi != null) ScQuests.StartRescue(player, m, dpi, out perr);
                    ScCommunity.Tell(player, string.Format(Localization.Get("scRadioDistress"), m.Name, m.RunPoiName, Dist(ScRunners.C(m.RunSite))), "ui_denied");
                    ScLog.Out(m.Name + " radios for help from " + m.RunPoiName + " " + m.RunSite + " (on a physical run)" + (perr != null ? " (quest: " + perr + ")" : ""));
                    return true;
                }
                // sometimes the runner holes up at one of our outposts, where the defenses are
                PrefabInstance pi = null;
                var held = new List<Outpost>();
                foreach (Outpost o in ScRegistry.Outposts) if (o.State == OutpostState.Active) held.Add(o);
                float roll = rnd != null ? rnd.RandomFloat : UnityEngine.Random.value;
                if (held.Count > 0 && roll < ScConfig.OutpostDistressChance)
                    pi = ScSecurity.PoiByKey(held[rnd != null ? rnd.RandomRange(0, held.Count) : 0].Poi);
                if (pi == null) pi = PickPoi(ScConfig.RadioContactMinDist, ScConfig.RadioContactMaxDist, null, rnd);
                if (pi == null) return false;
                World w = ScCommunity.World;
                Vector3 centre = Centre(pi);
                m.RunDistress = true;
                m.RunSafe = false;
                m.EventSpawned = false;
                m.StandInId = -1;
                m.EventZombies.Clear();
                m.RunPoi = pi.boundingBoxPosition;
                m.RunPoiName = ScSecurity.DisplayName(pi);
                m.RunSpawn = new Vector3i((int)centre.x, (int)w.GetHeightAt(centre.x, centre.z) + 1, (int)centre.z);
                string err;
                ScQuests.StartRescue(player, m, pi, out err);
                ScCommunity.Tell(player, string.Format(Localization.Get("scRadioDistress"), m.Name, m.RunPoiName, Dist(centre)), "ui_denied");
                ScLog.Out(m.Name + " radios for help from " + m.RunPoiName + " " + m.RunPoi + (err != null ? " (quest: " + err + ")" : ""));
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("radio-distress", e);
                return false;
            }
        }

        private static void TickDistress(World w, EntityPlayerLocal player)
        {
            foreach (var m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.OnRun || !m.RunDistress || m.RunSafe) continue;
                bool physical = ScRunners.IsPhysical(m);
                if (!m.EventSpawned)
                {
                    if (physical && (m.Offline || ScCommunity.FindNpc(m.Id) == null)) continue;   // they come back into the world first
                    Vector3 centre = new Vector3(m.RunSpawn.x + 0.5f, m.RunSpawn.y, m.RunSpawn.z + 0.5f);
                    float d = Vector3.Distance(new Vector3(player.position.x, 0f, player.position.z), new Vector3(centre.x, 0f, centre.z));
                    if (d <= ScConfig.RescueTriggerRange && ScZone.IsLoaded(m.RunSpawn)) SpawnEvent(w, player, m);
                    continue;
                }
                // the event is live: rescued once every spawned zombie is down - judged only while the player is
                // there and the spot is loaded (walking off until they unload is not a rescue)
                {
                    Vector3 c2 = new Vector3(m.RunSpawn.x + 0.5f, m.RunSpawn.y, m.RunSpawn.z + 0.5f);
                    float d2 = Vector3.Distance(new Vector3(player.position.x, 0f, player.position.z), new Vector3(c2.x, 0f, c2.z));
                    if (d2 > ScConfig.RescueTriggerRange * (physical ? 1f : 2f) || !ScZone.IsLoaded(m.RunSpawn)) continue;
                }
                bool anyAlive = false;
                for (int i = 0; i < m.EventZombies.Count; i++)
                {
                    EntityAlive z = w.GetEntity(m.EventZombies[i]) as EntityAlive;
                    if (z != null && !z.IsDead()) { anyAlive = true; break; }
                }
                if (!anyAlive) Rescued(w, player, m);
            }
        }

        public static int ZombieCount(Member m, EntityPlayerLocal player)
        {
            int gs = 0;
            try { gs = player != null ? player.gameStage : 0; } catch (Exception) { }
            int combat = m.Stars(Skill.Fighting) + m.Stars(Skill.Shooting) - 2;
            int n = Mathf.RoundToInt(ScConfig.DistressZombiesBase + gs * ScConfig.DistressZombiesPerGamestage);
            int min = Math.Max(2, Mathf.RoundToInt(ScConfig.DistressZombiesPerCombatStar * combat));
            return Mathf.Clamp(Math.Max(n, min), 1, Math.Max(1, ScConfig.DistressZombiesMax));
        }

        /// <summary>
        /// The player has arrived: a stand-in of the survivor (their own hidden entity stays parked at home) and
        /// the zombies spawn around the POI centre; the zombies are told to attack the stand-in.
        /// </summary>
        private static void SpawnEvent(World w, EntityPlayerLocal player, Member m)
        {
            try
            {
                GameRandom rnd = w.GetGameRandom();
                int cid = EntityClass.FromString(m.Class);
                // a physical runner is there in person: the zombies come for them, and there is no stand-in
                EntityAliveSDX runner = ScRunners.IsPhysical(m) ? ScCommunity.FindNpc(m.Id) : null;
                if (ScRunners.IsPhysical(m) && runner == null) return;
                Vector3 centre = runner != null ? runner.position : SpotNear(w, m.RunSpawn, 5);
                EntityAliveSDX standIn = null;
                if (runner == null)
                {
                    try
                    {
                        if (m.Look != null) ScLooks.Pending = m.Look.Clone();      // the stand-in IS the survivor
                        if (ScCommunity.ClassExists(cid)) standIn = EntityFactory.CreateEntity(cid, centre) as EntityAliveSDX;
                    }
                    finally { ScLooks.Pending = null; }
                }
                if (standIn != null)
                {
                    w.SpawnEntityInWorld(standIn);
                    standIn.FirstName = m.Name;          // the class rolls a random name at spawn: the stand-in IS this survivor
                    standIn.Title = "";
                    standIn.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
                    standIn.Buffs.SetCustomVar(StandInCvar, m.Id);
                    standIn.Buffs.SetCustomVar("Persist", 1f);
                    ScSkills.StampCvars(m, standIn);                     // fights with the survivor's own skills
                    ScTraits.ApplyBuffs(m, standIn);
                    standIn.Buffs.AddBuff("buffOrderGuard");             // holds this spot
                    m.StandInId = standIn.entityId;
                }
                int count = ZombieCount(m, player);
                m.EventZombies.Clear();
                for (int i = 0; i < count; i++)
                {
                    int zc = EntityGroups.GetRandomFromGroup(ScConfig.DistressZombieGroup, ref lastZombieClass, rnd);
                    if (!ScCommunity.ClassExists(zc)) continue;
                    float ang = rnd.RandomFloat * Mathf.PI * 2f;
                    int dist = rnd.RandomRange(7, 14);
                    Vector3i at = new Vector3i(Mathf.RoundToInt(centre.x + Mathf.Cos(ang) * dist), runner != null ? Mathf.FloorToInt(centre.y) : m.RunSpawn.y, Mathf.RoundToInt(centre.z + Mathf.Sin(ang) * dist));
                    Vector3 pos = SpotNear(w, at, 3);
                    EntityAlive z = EntityFactory.CreateEntity(zc, pos) as EntityAlive;
                    if (z == null) continue;
                    w.SpawnEntityInWorld(z);
                    z.SetSpawnerSource(EnumSpawnerSource.Dynamic);
                    if (runner != null) z.SetAttackTarget(runner, 1200);
                    else if (standIn != null) z.SetAttackTarget(standIn, 1200);
                    m.EventZombies.Add(z.entityId);
                }
                m.EventSpawned = true;
                ScCommunity.Tell(player, string.Format(Localization.Get("scRescueEvent"), m.Name, m.EventZombies.Count), "ui_denied");
                ScLog.Out("rescue event for " + m.Name + " at " + m.RunPoiName + ": " + m.EventZombies.Count + " zombie(s), stand-in " + m.StandInId);
            }
            catch (Exception e)
            {
                ScLog.Once("radio-event", e);
                m.EventSpawned = true;   // never retry a throwing spawn every second
            }
        }

        private static void Rescued(World w, EntityPlayerLocal player, Member m)
        {
            m.RunSafe = true;
            m.EventSpawned = false;                  // the event is over (a live event keeps a physical runner in the world)
            ScLeadership.Count(ScLeadership.RescueEvent);
            m.EventZombies.Clear();
            RemoveStandIn(w, m);
            ScQuests.CloseRescue(player, m, true);
            ScCommunity.Tell(player, string.Format(Localization.Get("scRescued"), m.Name), "");
            ScLog.Out(m.Name + " rescued at " + m.RunPoiName + " - the run comes home safe");
            ScSkills.AddXp(m, Skill.Wits, ScConfig.RunWitsXp);
            if (ScRunners.IsPhysical(m) && m.RunLeg == RunLeg.AtSite)
            {
                EntityAliveSDX r = ScCommunity.FindNpc(m.Id);
                ScRunners.LeaveSite(player, m, r != null ? r.position : ScRunners.C(m.RunSite), w.worldTime, true, 1f);
                ScRunners.S(m.Id).HasWaypoint = false;
            }
            ScRegistry.SaveAtomic();
        }

        private static readonly List<int> despawnLater = new List<int>();

        /// <summary>
        /// A physical runner left the world mid-event: the event resets (walking away is never a rescue) and spawns
        /// afresh when the player comes back. now=false from inside an unload callback, where removing other
        /// entities is unsafe: they go on the next tick.
        /// </summary>
        public static void OnRunnerOffline(World w, Member m, bool now = true)
        {
            try
            {
                if (m == null || !m.EventSpawned) return;
                for (int i = 0; i < m.EventZombies.Count; i++)
                {
                    if (!now) { despawnLater.Add(m.EventZombies[i]); continue; }
                    Entity z = w != null ? w.GetEntity(m.EventZombies[i]) : null;
                    if (z != null && !z.IsDead()) w.RemoveEntity(z.entityId, EnumRemoveEntityReason.Despawned);
                }
                m.EventZombies.Clear();
                m.EventSpawned = false;
            }
            catch (Exception e)
            {
                ScLog.Once("radio-runner-offline", e);
            }
        }

        private static void DespawnLater(World w)
        {
            if (despawnLater.Count == 0) return;
            for (int i = 0; i < despawnLater.Count; i++)
            {
                Entity z = w.GetEntity(despawnLater[i]);
                if (z != null && !z.IsDead()) w.RemoveEntity(z.entityId, EnumRemoveEntityReason.Despawned);
            }
            despawnLater.Clear();
        }

        public static void RemoveStandIn(World w, Member m)
        {
            try
            {
                if (m.StandInId < 0) return;
                EntityAliveSDX s = w != null ? w.GetEntity(m.StandInId) as EntityAliveSDX : null;
                if (s != null) ScRuns.RemoveEntity(s);
                m.StandInId = -1;
            }
            catch (Exception e)
            {
                ScLog.Once("radio-standin", e);
            }
        }

        /// <summary>SetDead postfix: the stand-in fell - that IS the survivor dying out there.</summary>
        public static void OnStandInDied(EntityAliveSDX standIn)
        {
            try
            {
                int id = (int)standIn.Buffs.GetCustomVar(StandInCvar);
                Member m = ScRegistry.GetMember(id);
                EntityPlayerLocal player = ScCommunity.LocalPlayer;
                if (m == null) return;
                if (m.StandInId != standIn.entityId)
                {
                    ScLog.Out("a stale stand-in of " + m.Name + " (entity " + standIn.entityId + ", current " + m.StandInId + ") died - no effect");
                    return;
                }
                if (m.Status == MemberStatus.Garrison) { ScGarrison.OnStandInDied(m, player); return; }   // fell holding an outpost
                m.StandInId = -1;
                ScQuests.CloseRescue(player, m, false);
                World w = ScCommunity.World;
                EntityAliveSDX real = ScCommunity.FindNpc(m.Id);
                if (real != null) ScRuns.RemoveEntity(real); else ScRegistry.Doomed.Add(m.Id);
                ScCommunity.RecordDeath(m, player, "scRescueDied");
            }
            catch (Exception e)
            {
                ScLog.Once("radio-standin-died", e);
            }
        }

        /// <summary>ScRuns.Resolve: the distress run's outcome is decided by whether the player came.</summary>
        public static void EndDistress(World w, EntityPlayerLocal player, Member m)
        {
            RemoveStandIn(w, m);
            ScQuests.CloseRescue(player, m, m.RunSafe);
            m.RunDistress = false;
            m.EventSpawned = false;
            m.EventZombies.Clear();
        }

        // ------------------------------------------------------------------ window ----

        /// <summary>The radio block the window was last opened at (a relay out at an outpost counts too).</summary>
        public static Vector3i? CurrentRadio;

        /// <summary>A home radio with power, or the radio the window is open at.</summary>
        public static bool Usable()
        {
            if (Online()) return true;
            return CurrentRadio.HasValue && IsPoweredAt(ScCommunity.World, CurrentRadio.Value);
        }

        public static void Open(World w, Vector3i pos, EntityPlayerLocal player)
        {
            try
            {
                if (player == null || player.PlayerUI == null || player.PlayerUI.windowManager == null) return;
                if (!ScCommunity.IsHost) { ScCommunity.Tell(player, Localization.Get("scHostOnly"), "ui_denied"); return; }
                NoteRadio(pos);
                CurrentRadio = pos;
                if (!IsPoweredAt(w, pos)) { ScCommunity.Tell(player, Localization.Get("scRadioNoPower"), "ui_denied"); return; }
                GUIWindow win;
                if (!player.PlayerUI.windowManager.TryGetWindow(WindowGroup, out win)) { ScCommunity.Tell(player, Localization.Get("scNoWindow"), "ui_denied"); return; }
                player.PlayerUI.windowManager.Open(WindowGroup, true);
            }
            catch (Exception e)
            {
                ScLog.Once("radio-open", e);
            }
        }
    }
}
