using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>A survivor heard over the radio: simulated as a record at a real POI until its chunk loads, then spawned.</summary>
    public sealed class Contact : IScCommunityRecord      // round MP6 (core): a contact belongs to one community
    {
        public int Id;
        /// <summary>Round MP6 (core): the community whose radio heard them (stamped by ScRecordList; cid= in the save).</summary>
        public int CommunityId { get; set; }
        public string Class = "";
        public string Label = "";
        public Vector3i PoiKey;
        public string PoiName = "";
        public Vector3i Spawn;
        public int Day;
        public int ExpireDay;
        /// <summary>The two weapons rolled with the contact (ScLoadout), handed to the body when it spawns. Null: rolled then.</summary>
        public Loadout Loadout;
    }

    /// <summary>
    /// The radio station (block scRadioStation, powered). Four jobs:
    ///   Airdrop  - vanilla's own air drop, on request only: the AIDirector's scheduled drops are gated off
    ///              (Patches_Radio) and the sandbox "air drop frequency" (hours) is the radio's cooldown.
    ///   Call     - reach out for survivors: one of the community's 0-XNPCCore survivors (never a pack NPC - those no
    ///              longer spawn while this mod is installed) at a random real POI a few hundred metres out, marked on
    ///              the map, kept as a registry record and spawned the first time that chunk loads (they then ask
    ///              a favour like any recruit). Fades after RadioContactDays.
    ///   Distress - a survivor on a supply run may radio for help (RadioDistressChance while a radio is
    ///              powered): a rescue quest marks the POI they are pinned at; when the player gets there a
    ///              stand-in of the survivor and a gamestage- and skill-scaled pack of zombies spawn around
    ///              it. Clear them and the run comes home safe with a bonus; ignore them and it ends badly.
    ///              R2 (3.4): only a runner carrying a handheld radio calls, and the call needs the player's handheld or a
    ///              powered station at home to land (ScHandheld.DistressAllowed); the window opens over a handheld too
    ///              (the Radio tab: "remote", relayed through the home station - XUiC_ScRadio).
    ///   Outposts - radios inside the home zone are remembered in the registry; ScOutposts.RadioOnlineAnywhere
    ///              reads their power from the power graph, so establishing and the daily report-in work from afar.
    /// Host only. Contacts, cooldowns and distress runs persist in the registry.
    /// Round MP5 (part R): any community player uses the window - at a powered station, or over a handheld radio where the
    /// host can. A client opens it on its own machine (Open) and asks the server for every act and for what its copy of the
    /// registry lacks (ScActionsRadio); the server keeps each remote player's station (StationFor), and the radio's air drop
    /// falls near the player who asked (DropFor, Patches_Radio).
    /// </summary>
    public static class ScRadio
    {
        public const string BlockName = "scRadioStation";
        public const string WindowGroup = "scradio";
        public const string NavClass = "sc_radio_contact";
        public const string StandInCvar = "sc_standin";

        /// <summary>Set only around our own SpawnAirDrop call; the prefix refuses every other spawn.</summary>
        public static bool DropRequested;
        /// <summary>
        /// Round MP5 (part R): set with DropRequested - the player who asked for the drop (the caller), whom the planes fly to
        /// (Patches_Radio); null: vanilla's own pick among every living player.
        /// </summary>
        public static EntityPlayer DropFor;

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
            DropFor = null;                 // round MP5 (part R)
            CurrentRadio = null;
            stations.Clear();               // round MP5 (part R): the remote players' stations
#if SC_V34
            OpeningAtBlock = false;
#endif
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

        public static bool RequestDrop(EntityPlayer player)      // round MP5 (part R): any player (the radio's request runs on the server)
        {
            string why;
            if (!CanDrop(out why)) { if (player != null) ScCommunity.Tell(player, DropWhy(why), "ui_denied"); return false; }
            try
            {
                World w = ScCommunity.World;
                AIDirectorAirDropComponent comp = (w != null && w.aiDirector != null) ? w.aiDirector.GetComponent<AIDirectorAirDropComponent>() : null;
                // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
                if (comp == null) { if (player != null) ScCommunity.TellKey(player, "scRadioDropsOff", "ui_denied"); return false; }
                bool ok;
                DropRequested = true;
                DropFor = player;                    // round MP5 (part R): the planes fly to the player who asked (Patches_Radio)
                try { ok = comp.SpawnAirDrop(); }
                finally { DropRequested = false; DropFor = null; }
                if (!ok) { if (player != null) ScCommunity.TellKey(player, "scRadioDropBusy", "ui_denied"); return false; }
                GameRandom rnd = w.GetGameRandom();
                int days = Math.Max(1, rnd != null ? rnd.RandomRange(DropMinDays(), DropMaxDays() + 1) : DropMaxDays());
                ScRegistry.Community.RadioNextDrop = w.worldTime + (ulong)days * 24000UL;
                ScRegistry.SaveAtomic();
                if (player != null) ScCommunity.TellKey(player, "scRadioDropSent", "");
                ScLog.Out("air drop requested over the radio" + (player != null && !(player is EntityPlayerLocal) ? " by " + player.PlayerDisplayName : "") + "; next in " + days + " day(s)");
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
        /// The class a new radio contact or outpost walk-in gets: one of the community's 0-XNPCCore survivor classes
        /// (migration M6), either sex, at the player's gamestage tier (T1 early, T2 middle, T3 late). Its weapons are not
        /// in the class: the contact rolls a pair (NewContactLoadout). Pack NPCs (Nurse, Baker, Harley...) are never
        /// rolled - they no longer spawn while this mod is installed (the user's call, 2026-09-30). Null when none of
        /// those classes is loaded.
        /// </summary>
        public static string NewContactClass(EntityPlayer player, GameRandom rnd)      // round MP0: any player's gamestage (the ticker's anchor)
        {
            if (rnd == null && ScCommunity.World != null) rnd = ScCommunity.World.GetGameRandom();
            bool male = rnd == null || rnd.RandomFloat < 0.5f;
            return ScXnpc.ClassFor(male, ScLooks.MaxTierFor(player != null ? player.gameStage : 0));
        }

        /// <summary>
        /// The pair a new contact will carry, rolled now (the class's tier cap at today's gamestage) so the radio can
        /// say what they are armed with and the body spawns with exactly that (ScLoadout.Pending).
        /// </summary>
        private static Loadout NewContactLoadout(string cls, GameRandom rnd)
        {
            try { return ScLoadout.RollXnpc(cls, null, rnd); }
            catch (Exception e) { ScLog.Once("radio-contact-loadout", e); return null; }
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
                    if (ScEnclaves.IsEnclavePoi(pi)) continue;                          // nor into an enclave's building
                    if (exclude != null && exclude.Contains(pi.boundingBoxPosition)) continue;
                    if (ScEncRequests.IsTarget(pi.boundingBoxPosition)) continue;     // E5: nor where an enclave's request sends the player
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

        public static bool Call(EntityPlayer player)            // round MP5 (part R): any player - the caller's gamestage, their tells (the marker: the host's own, ScMarkers for the rest)
        {
            string why;
            if (!CanCall(out why)) { if (player != null) ScCommunity.Tell(player, CallWhy(why), "ui_denied"); return false; }
            try
            {
                World w = ScCommunity.World;
                GameRandom rnd = w.GetGameRandom();
                string cls = NewContactClass(player, rnd);
                if (cls == null) { ScCommunity.TellKey(player, "scRadioNobody", "ui_denied"); return false; }
                var taken = new HashSet<Vector3i>();
                foreach (CommunityState cs in ScCommunities.All)      // round MP6 (part G): anyone's contact's building
                    foreach (var ct in cs.Contacts) taken.Add(ct.PoiKey);
                PrefabInstance pi = PickPoi(ScConfig.RadioContactMinDist, ScConfig.RadioContactMaxDist, taken, rnd);
                if (pi == null) { ScCommunity.TellKey(player, "scRadioNoPoi", "ui_denied"); return false; }
                Community c = ScRegistry.Community;
                Vector3 centre = Centre(pi);
                int y = (int)w.GetHeightAt(centre.x, centre.z) + 1;      // static heightmap: refined with live blocks at spawn time
                Loadout pair = NewContactLoadout(cls, rnd);
                var contact = new Contact
                {
                    Id = ++c.NextContactId,
                    Class = cls,
                    Label = ContactLabel(cls, pair),
                    Loadout = pair,
                    PoiKey = pi.boundingBoxPosition,
                    PoiName = ScSecurity.DisplayName(pi),
                    Spawn = new Vector3i((int)centre.x, y, (int)centre.z),
                    Day = ScCommunity.Today,
                    ExpireDay = ScCommunity.Today + Math.Max(1, ScConfig.RadioContactDays)
                };
                ScRegistry.Contacts.Add(contact);
                c.RadioNextCallDay = ScCommunity.Today + Math.Max(0, ScConfig.RadioCallDays);
                // round MP5 (part R): the marker is this machine's own player's - a listen host's own map too when a client called
                // (the client's comes from ScMarkers)
                AddMarker(contact, player is EntityPlayerLocal ? player : ScCommunity.LocalPlayer);
                ScRegistry.SaveAtomic();
                ScCommunity.TellKey(player, "scRadioContact", "", contact.Label, contact.PoiName, Dist(centre));
                ScLog.Out("radio contact" + (player != null && !(player is EntityPlayerLocal) ? " (called by " + player.PlayerDisplayName + ")" : "") + ": " + cls + (pair != null ? " (" + pair + ")" : "") + " at " + contact.PoiName + " " + contact.PoiKey + " (fades day " + contact.ExpireDay + ")");
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
        public static Contact AddContactAt(Vector3i poiKey, string poiName, Vector3i spawn, EntityPlayer player, GameRandom rnd)   // round MP0: any player (outpost ticks)
        {
            try
            {
                string cls = NewContactClass(player, rnd);
                if (cls == null) return null;
                Community c = ScRegistry.Community;
                Loadout pair = NewContactLoadout(cls, rnd);
                var contact = new Contact
                {
                    Id = ++c.NextContactId,
                    Class = cls,
                    Label = ContactLabel(cls, pair),
                    Loadout = pair,
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

        /// <summary>
        /// What the radio calls a contact: the class's name and, when the contact rolled a pair, the ranged weapon - the one
        /// that tells a bow from a rare firearm ("a survivor (Pipe Rifle)").
        /// </summary>
        private static string ContactLabel(string cls, Loadout pair)
        {
            string label = ClassLabel(cls);
            try
            {
                ItemClass ic = pair != null && pair.HasRanged ? ItemClass.GetItemClass(pair.Ranged, true) : null;
                string weapon = ic != null ? ic.GetLocalizedItemName() : null;
                if (!string.IsNullOrEmpty(weapon)) label += " (" + weapon + ")";
            }
            catch (Exception e) { ScLog.Once("radio-contact-label", e); }
            return label;
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
                // round MP0: markers are this machine's own, for its own player - none on a dedicated server. Round MP4: remote players' come from ScMarkers (CollectMarkers)
                if (!(owner is EntityPlayerLocal)) return;
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

        /// <summary>
        /// Round MP4 (part K): every radio contact's marker as AddMarker draws it, for ScMarkers - the set each remote community
        /// player registers for themselves. Server side; registers nothing.
        /// </summary>
        internal static void CollectMarkers(List<ScMarkers.Entry> into)
        {
            foreach (Contact ct in ScRegistry.Contacts)
                into.Add(new ScMarkers.Entry(ScMarkers.KindContact, ct.Id, NavClass, new Vector3(ct.Spawn.x + 0.5f, ct.Spawn.y, ct.Spawn.z + 0.5f),
                                             string.Format(Localization.Get("scRadioMarker"), ct.Label, ct.PoiName)));
        }

        // ------------------------------------------------------------------ ticker ----

        public static void Tick(World w, EntityPlayer player)          // round MP0: the ticker's anchor - any server, a dedicated one too
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
                    foreach (var ct in ScRegistry.Contacts) AddMarker(ct, player);     // round MP0: the host's own only (AddMarker). Round MP4: remote players' come from ScMarkers (CollectMarkers)
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

        private static void TickContacts(World w, EntityPlayer player)  // round MP0: any player
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
                    ScPlayers.TellCommunityKey("scRadioFaded", "", ct.Label, ct.PoiName);    // round MP0: the community's radio news
                    ScLog.Out("radio contact at " + ct.PoiName + " faded");
                }
                RemoveMarker(ct);
                ScRegistry.Contacts.Remove(ct);
            }
            if (scratch.Count > 0) ScRegistry.SaveAtomic();
            scratch.Clear();
        }

        /// <summary>The chunk is here: the simulated survivor becomes a real NPC standing at the POI, waiting for a favour.</summary>
        private static bool SpawnContact(World w, EntityPlayer player, Contact ct)     // round MP0: any player (the gamestage)
        {
            try
            {
                if (!ScXnpc.IsSurvivorClass(ct.Class))
                {
                    // a pack NPC rolled before pack NPCs stopped spawning, or one of SC's own survivor classes from before
                    // the XNPCCore migration (decision D11): one of the community's 0-XNPCCore survivors answers instead.
                    // Never a raider class (M10): those are managed bodies too, but no contact may be hostile
                    string again = NewContactClass(player, w.GetGameRandom());
                    if (again == null) { ScLog.Warn("radio contact class " + ct.Class + " no longer spawns and no 0-XNPCCore survivor class is loaded - dropped"); return true; }
                    ScLog.Out("radio contact " + ct.Id + ": " + ct.Class + " no longer spawns - " + again + " answers instead");
                    ct.Class = again;
                    ct.Loadout = NewContactLoadout(again, w.GetGameRandom());
                    ct.Label = ContactLabel(again, ct.Loadout);
                }
                int cid = EntityClass.FromString(ct.Class);
                if (!ScCommunity.ClassExists(cid)) { ScLog.Warn("radio contact class " + ct.Class + " no longer exists - dropped"); return true; }
                Vector3 pos = SpotNear(w, ct.Spawn, 4);
                // a survivor wearing one of our looks rolls their face and clothes here; the model consumes it while it
                // is built (Patches_Xnpc X1)
                Look look = ScSpawn.IsSurvivorBodyClass(ct.Class) ? ScLooks.Roll(ScLooks.ClassIsMale(ct.Class), w.GetGameRandom(), true, false, ScLoadout.TierCap(ct.Class)) : null;
                EntityAlive npc = ScSpawn.Create(cid, pos, Vector3.zero, look, ct.Loadout, kind: "radio contact");
                if (npc == null) { ScLog.Warn("radio contact class " + ct.Class + " is not an SCore NPC class - dropped"); return true; }
                w.SpawnEntityInWorld(npc);
                npc.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
                npc.Buffs.SetCustomVar("Persist", 1f);
                Prospect p = ScFavours.Ensure(npc, player);       // favour, guard order, sc_favour cvar, position record
                // round MP0: told to the player whose coming loaded the spot (the nearest), the anchor when none is alive
                // round MP6 (part G): the nearest of the community whose radio heard them (the contact's)
                ScCommunity.TellKey(ScPlayers.Nearest(pos, 0f, ScPlayers.CurrentCommunity) ?? player, "scRadioFound", "", ScCommunity.DisplayName(npc), ct.PoiName);
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
        public static bool RollDistress(Member m, EntityPlayer player, GameRandom rnd)     // round MP0: any player (runner ticks)
        {
#if SC_V34
            // R2 (decision 5): only a runner carrying a radio, and only with someone to hear it - the player's handheld or a
            // powered station at home (ScHandheld.DistressAllowed; DistressNeedsRadio=false: the rule above's old one).
            // Round MP0: the handheld is the host's own (a remote bag is not readable here) - else the station alone.
            // Round MP5 (part N), the handheld rule: callers pass the real player (the runner tick's anchor, a run's sender). The
            // handheld that can hear the call is one this machine can read: that player's when it is this machine's own, else the
            // host's own player's on a listen host (the community's radio: any community player may answer the call), else none -
            // a remote player's bag lives on their own machine (the server only keeps a save copy), so on a dedicated server a
            // call needs a powered station at home. Single player and the host's own sends: the host's handheld, as before
            EntityPlayerLocal ear = player as EntityPlayerLocal ?? ScCommunity.LocalPlayer;
            if (m == null || !ScHandheld.DistressAllowed(m, ear)) return false;
            // round MP0 kept distress off a dedicated server (no local player to take the quest). Round MP4 review M2: every
            // community player online gets the rescue quest and its marker (ScQuestNet), and any of them answers it (TickDistress)
#else
            if (m == null || !Online()) return false;
#endif
            float roll = rnd != null ? rnd.RandomFloat : UnityEngine.Random.value;
            if (roll >= ScConfig.RadioDistressChance) return false;
            return ForceDistress(m, player, rnd);
        }

        /// <summary>The distress call itself (also the console's "sc distress").</summary>
        public static bool ForceDistress(Member m, EntityPlayer player, GameRandom rnd)    // round MP0: any player
        {
            try
            {
                if (m == null || m.Status != MemberStatus.OnRun) return false;
                // round MP4: the rescue quest goes to EVERY community player online (the user, 2026-11-25) - the host's own journal
                // here (whoever the caller reasons from: in single player and on a listen host that was always the host's own),
                // every client's through the quest op (ScQuestNet.RescueStarted, below)
                EntityPlayerLocal lp = ScCommunity.LocalPlayer;
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
                    if (dpi != null) { if (lp != null) ScQuests.StartRescue(lp, m, dpi, out perr); }
#if SC_V34
                    // round 33c (M33): a mining runner calls from a natural spot - the rescue quest marks the ground around it
                    else if (ScMining.IsDig(m)) { if (lp != null) ScQuests.StartRescueAt(lp, m, new Vector3i(m.RunSite.x - 8, m.RunSite.y, m.RunSite.z - 8), new Vector3i(16, 1, 16), out perr); }
#endif
                    ScQuestNet.RescueStarted(m);          // round MP4: every client's own copy (the same place: ScQuestNet.RescueBox)
                    ScPlayers.TellCommunityKey("scRadioDistress", "ui_denied", m.Name, m.RunPoiName, Dist(ScRunners.C(m.RunSite)));   // round MP0: the community's radio news
                    ScLog.Out(m.Name + " radios for help from " + m.RunPoiName + " " + m.RunSite + " (on a physical run)" + (perr != null ? " (quest: " + perr + ")" : ""));
#if SC_V34
                    // R2: their own words on the radio. Round MP5 (part N): the host's own player hears it here whoever the caller
                    // reasons from (a client's send passes the client; ScRadioTalk plays the local queue for this machine's own only)
                    ScRadioTalk.Say(m, RadioTalk.Trouble, lp ?? player, false);
#endif
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
                string err = null;
                if (lp != null) ScQuests.StartRescue(lp, m, pi, out err);
                ScQuestNet.RescueStarted(m);              // round MP4: every client's own copy
                ScPlayers.TellCommunityKey("scRadioDistress", "ui_denied", m.Name, m.RunPoiName, Dist(centre));   // round MP0: the community's radio news
                ScLog.Out(m.Name + " radios for help from " + m.RunPoiName + " " + m.RunPoi + (err != null ? " (quest: " + err + ")" : ""));
#if SC_V34
                ScRadioTalk.Say(m, RadioTalk.Trouble, lp ?? player, false);                   // R2: their own words on the radio (round MP5, part N: the host hears it, as above)
#endif
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("radio-distress", e);
                return false;
            }
        }

        private static void TickDistress(World w, EntityPlayer player)  // round MP0: any player
        {
            foreach (var m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.OnRun || !m.RunDistress || m.RunSafe) continue;
                bool physical = ScRunners.IsPhysical(m);
                if (!m.EventSpawned)
                {
                    if (physical && (m.Offline || ScCommunity.FindNpc(m.Id) == null)) continue;   // they come back into the world first
                    // round MP4 review M1: the trigger and the judging below are any community player's position - the anchor first (the
                    // host's own player on a host: single player as before), else the first living community player there - so every
                    // player's rescue copy can be the one completed (was the anchor's alone)
                    Vector3 centre = new Vector3(m.RunSpawn.x + 0.5f, m.RunSpawn.y, m.RunSpawn.z + 0.5f);
                    EntityPlayer arrived = RescuerNear(player, centre, ScConfig.RescueTriggerRange);
                    if (arrived != null && ScZone.IsLoaded(m.RunSpawn)) SpawnEvent(w, arrived, m);
                    continue;
                }
                // the event is live: rescued once every spawned zombie is down - judged only while a player is
                // there and the spot is loaded (walking off until they unload is not a rescue)
                Vector3 c2 = new Vector3(m.RunSpawn.x + 0.5f, m.RunSpawn.y, m.RunSpawn.z + 0.5f);
                EntityPlayer there = RescuerNear(player, c2, ScConfig.RescueTriggerRange * (physical ? 1f : 2f));
                if (there == null || !ScZone.IsLoaded(m.RunSpawn)) continue;
                bool anyAlive = false;
                for (int i = 0; i < m.EventZombies.Count; i++)
                {
                    EntityAlive z = w.GetEntity(m.EventZombies[i]) as EntityAlive;
                    if (z != null && !z.IsDead()) { anyAlive = true; break; }
                }
                if (!anyAlive) Rescued(w, there, m);
            }
        }

        /// <summary>
        /// Round MP4 review M1: who is at a rescue (XZ distance within range of centre): the anchor first, as before (the host's own
        /// player on a host, dead or alive - the old test), else the first living community player there; or null. Round MP5 (part
        /// N): internal - ScRunners' escort test ("never a distress call with a player right beside her") asks it too, so the
        /// escort and the rescue are one rule. The anchor may be null (any living community player then).
        /// </summary>
        internal static EntityPlayer RescuerNear(EntityPlayer anchor, Vector3 centre, float range)
        {
            Vector2 c = new Vector2(centre.x, centre.z);
            if (anchor != null && Vector2.Distance(new Vector2(anchor.position.x, anchor.position.z), c) <= range) return anchor;
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
            {
                EntityPlayer q = all[i];
                if (q == null || ReferenceEquals(q, anchor) || q.IsDead() || !ScPlayers.InCommunity(q)) continue;
                if (Vector2.Distance(new Vector2(q.position.x, q.position.z), c) <= range) return q;
            }
            return null;
        }

        public static int ZombieCount(Member m, EntityPlayer player)   // round MP0: any player's gamestage. Round MP5 (part N): SpawnEvent passes the strongest player at the call
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
        private static void SpawnEvent(World w, EntityPlayer player, Member m)        // round MP0: the player who arrived (the anchor)
        {
            try
            {
                GameRandom rnd = w.GetGameRandom();
                // a physical runner is there in person: the zombies come for them, and there is no stand-in
                EntityAlive runner = ScRunners.IsPhysical(m) ? ScCommunity.FindNpc(m.Id) : null;
                if (ScRunners.IsPhysical(m) && runner == null) return;
                Vector3 centre = runner != null ? runner.position : SpotNear(w, m.RunSpawn, 5);
                // the stand-in IS the survivor: their class, look and name (ScSpawn); none when the class is gone
                EntityAlive standIn = runner == null ? ScSpawn.Body(w, m, centre, Vector3.zero, true) : null;
                if (standIn != null)
                {
                    standIn.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
                    standIn.Buffs.SetCustomVar(StandInCvar, m.Id);
                    standIn.Buffs.SetCustomVar("Persist", 1f);
                    ScSkills.StampCvars(m, standIn);                     // fights with the survivor's own skills
                    ScTraits.ApplyBuffs(m, standIn);
                    standIn.Buffs.AddBuff("buffOrderGuard");             // holds this spot
                    m.StandInId = standIn.entityId;
                }
                // round MP5 (part N): the players involved - the one who arrived and every living community player at the call
                // (within RescueTriggerRange of it): told it is on (below), and the strongest of them sets the zombie count (the
                // gamestage, as vanilla meets a party). Single player: the player, as before
                Vector3 call = new Vector3(m.RunSpawn.x + 0.5f, m.RunSpawn.y, m.RunSpawn.z + 0.5f);
                var party = new List<EntityPlayer>();
                if (player != null) party.Add(player);
                List<EntityPlayer> all = ScPlayers.All;
                for (int i = 0; i < all.Count; i++)
                {
                    EntityPlayer q = all[i];
                    if (q == null || ReferenceEquals(q, player) || q.IsDead() || !ScPlayers.InCommunity(q)) continue;
                    if (Vector2.Distance(new Vector2(q.position.x, q.position.z), new Vector2(call.x, call.z)) <= ScConfig.RescueTriggerRange) party.Add(q);
                }
                EntityPlayer strongest = player;
                for (int i = 0; i < party.Count; i++)
                {
                    try { if (strongest == null || party[i].gameStage > strongest.gameStage) strongest = party[i]; }
                    catch (Exception) { }
                }
                int count = ZombieCount(m, strongest);
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
                for (int i = 0; i < party.Count; i++) ScCommunity.TellKey(party[i], "scRescueEvent", "ui_denied", m.Name, m.EventZombies.Count);   // round MP5 (part N): everyone at the call
                ScLog.Out("rescue event for " + m.Name + " at " + m.RunPoiName + ": " + m.EventZombies.Count + " zombie(s), stand-in " + m.StandInId
                          + (party.Count > 1 ? ", " + party.Count + " players there" : ""));
            }
            catch (Exception e)
            {
                ScLog.Once("radio-event", e);
                m.EventSpawned = true;   // never retry a throwing spawn every second
            }
        }

        /// <summary>
        /// The rescue is over and counts (TickDistress: every spawned zombie down with the player there). Round r2: also a pinned
        /// runner too hurt to go on who breaks for home while the player fights beside her (ScRunners.TurnHomeHurt).
        /// </summary>
        internal static void Rescued(World w, EntityPlayer player, Member m)           // round MP0: the player there (the anchor)
        {
            m.RunSafe = true;
            m.EventSpawned = false;                  // the event is over (a live event keeps a physical runner in the world)
            ScLeadership.Count(ScLeadership.RescueEvent);
            m.EventZombies.Clear();
            RemoveStandIn(w, m);
            ScQuestNet.CloseRescueAll(m, true);       // round MP4: the first to complete it closes it for everyone - the host's own, every client's
            ScCommunity.TellKey(player, "scRescued", "", m.Name);
            // round MP5 (part N): every other living community player at the call fought it too - told as well (within twice the
            // trigger range, TickDistress' judging range for a hidden run's call)
            Vector3 call = new Vector3(m.RunSpawn.x + 0.5f, m.RunSpawn.y, m.RunSpawn.z + 0.5f);
            List<EntityPlayer> all = ScPlayers.All;
            EntityPlayer[] others = all.ToArray();                                            // copied: a tell can re-enter code that reuses the list
            for (int i = 0; i < others.Length; i++)
            {
                EntityPlayer q = others[i];
                if (q == null || ReferenceEquals(q, player) || q.IsDead() || !ScPlayers.InCommunity(q)) continue;
                if (Vector2.Distance(new Vector2(q.position.x, q.position.z), new Vector2(call.x, call.z)) <= ScConfig.RescueTriggerRange * 2f)
                    ScCommunity.TellKey(q, "scRescued", "", m.Name);
            }
            ScLog.Out(m.Name + " rescued at " + m.RunPoiName + " - the run comes home safe");
            ScSkills.AddXp(m, Skill.Wits, ScConfig.RunWitsXp);
            if (ScRunners.IsPhysical(m) && m.RunLeg == RunLeg.AtSite)
            {
                EntityAlive r = ScCommunity.FindNpc(m.Id);
                // round MP0: null-safe. Round MP5 (part N): the real player (the gate is every player's now, ScRunners.GatePoint)
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
                EntityAlive s = w != null ? ScNpc.As(w.GetEntity(m.StandInId)) : null;
                if (s != null)
                {
                    if (!s.IsDead()) ScAmmo.Record(m, s);   // what the rescue left them with (ScAmmo: re-created bodies start from it)
                    ScRuns.RemoveEntity(s);
                }
                m.StandInId = -1;
            }
            catch (Exception e)
            {
                ScLog.Once("radio-standin", e);
            }
        }

        /// <summary>SetDead postfix: the stand-in fell - that IS the survivor dying out there.</summary>
        public static void OnStandInDied(EntityAlive standIn)
        {
            try
            {
                int id = (int)standIn.Buffs.GetCustomVar(StandInCvar);
                Member m = ScRegistry.GetMember(id);
                EntityPlayer player = ScPlayers.Anchor();      // round MP0: any server (round MP4: the rescue quest below closes for every player)
                if (m == null) return;
                if (m.StandInId != standIn.entityId)
                {
                    ScLog.Out("a stale stand-in of " + m.Name + " (entity " + standIn.entityId + ", current " + m.StandInId + ") died - no effect");
                    return;
                }
                // fell holding an outpost - or (round op1 review, M1) in the seconds a recalled member's stand-in waits for their body
                if (m.Status == MemberStatus.Garrison || m.Status == MemberStatus.Transit) { ScGarrison.OnStandInDied(m, player); return; }
                m.StandInId = -1;
                ScQuestNet.CloseRescueAll(m, false);      // round MP4: every player's copy fails - the host's own, every client's
                World w = ScCommunity.World;
                EntityAlive real = ScCommunity.FindNpc(m.Id);
                if (real != null) ScRuns.RemoveEntity(real); else ScRegistry.Doomed.Add(m.Id);
                ScCommunity.RecordDeath(m, player, "scRescueDied");
            }
            catch (Exception e)
            {
                ScLog.Once("radio-standin-died", e);
            }
        }

        /// <summary>ScRuns.Resolve: the distress run's outcome is decided by whether the player came.</summary>
        public static void EndDistress(World w, EntityPlayer player, Member m)         // round MP0: any player (runs, deaths)
        {
            RemoveStandIn(w, m);
            // round MP4: every player's copy closes - the host's own (whichever player the caller passed), every client's
            ScQuestNet.CloseRescueAll(m, m.RunSafe);
            m.RunDistress = false;
            m.EventSpawned = false;
            m.EventZombies.Clear();
        }

        // ------------------------------------------------------------------ window ----

        /// <summary>The radio block the window was last opened at (a relay out at an outpost counts too).</summary>
        public static Vector3i? CurrentRadio;
#if SC_V34
        /// <summary>
        /// R2: true only inside Open's own window open - the window is at a radio block. Opened any other way (the Radio tab,
        /// over a handheld radio) it is "remote": it relays through a powered station at home (XUiC_ScRadio).
        /// </summary>
        public static bool OpeningAtBlock;
#endif

        /// <summary>A home radio with power, or the radio the window is open at.</summary>
        public static bool Usable()
        {
            if (Online()) return true;
            return CurrentRadio.HasValue && IsPoweredAt(ScCommunity.World, CurrentRadio.Value);
        }

        /// <summary>
        /// Round MP5 (part R), the server: the radio blocks remote players use the window at (entity id -> block), from their last
        /// radio request (ScActionsRadio: at a station within reach; a handheld request clears it). The host's own player keeps
        /// CurrentRadio, set by its own window as before. Session only.
        /// </summary>
        private static readonly Dictionary<int, Vector3i> stations = new Dictionary<int, Vector3i>();

        /// <summary>Round MP5 (part R), the server: a remote player's radio block (null: a handheld, or none known).</summary>
        public static void NoteStationFor(EntityPlayer p, Vector3i? at)
        {
            if (p == null || p is EntityPlayerLocal) return;
            if (at.HasValue) stations[p.entityId] = at.Value;
            else stations.Remove(p.entityId);
            if (stations.Count > 64) stations.Clear();       // never grows: a player's next request notes theirs again
        }

        /// <summary>
        /// Round MP5 (part R): the radio block a player's window is open at - CurrentRadio for this machine's own player (or
        /// nobody named), a remote player's from their last request (NoteStationFor) - or null (over a handheld radio).
        /// </summary>
        public static Vector3i? StationFor(EntityPlayer p)
        {
            if (p == null || p is EntityPlayerLocal) return CurrentRadio;
            Vector3i at;
            return stations.TryGetValue(p.entityId, out at) ? at : (Vector3i?)null;
        }

        /// <summary>
        /// Round MP5 (part R): Usable for one player - a home radio with power, or the radio their window is open at
        /// (StationFor). Null or this machine's own player: Usable() exactly.
        /// </summary>
        public static bool Usable(EntityPlayer who)
        {
            if (who == null || who is EntityPlayerLocal) return Usable();
            if (Online()) return true;
            Vector3i? at = StationFor(who);
            return at.HasValue && IsPoweredAt(ScCommunity.World, at.Value);
        }

        public static void Open(World w, Vector3i pos, EntityPlayerLocal player)
        {
            try
            {
                if (player == null || player.PlayerUI == null || player.PlayerUI.windowManager == null) return;
                // round MP5 (part R): a client opens the window on its own machine and asks the server for every act (ScActionsRadio)
                // - it was "host only"; a client that cannot ask (no SC on the server, another version, no copy yet) says why
                string refusal = ScActionsWindow.RequestRefusal();
                if (refusal != null) { ScCommunity.TellKey(player, refusal, "ui_denied"); return; }
                if (ScCommunity.IsHost) NoteRadio(pos);      // round MP5: never on a client's copy - the server notes a client's station at its first request
                CurrentRadio = pos;
                if (!IsPoweredAt(w, pos)) { ScCommunity.TellKey(player, "scRadioNoPower", "ui_denied"); return; }
                GUIWindow win;
                if (!player.PlayerUI.windowManager.TryGetWindow(WindowGroup, out win)) { ScCommunity.TellKey(player, "scNoWindow", "ui_denied"); return; }
#if SC_V34
                // R2: the window reads this in its OnOpen (an open is synchronous): at the station, not over a handheld radio
                OpeningAtBlock = true;
                try { player.PlayerUI.windowManager.Open(WindowGroup, true); }
                finally { OpeningAtBlock = false; }
#else
                player.PlayerUI.windowManager.Open(WindowGroup, true);
#endif
            }
            catch (Exception e)
            {
                ScLog.Once("radio-open", e);
            }
        }
    }
}
