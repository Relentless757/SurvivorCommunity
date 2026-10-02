using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SurvivorCommunity
{
    /// <summary>
    /// The community's source of truth: &lt;save&gt;/survivor_community.txt (atomic tmp + File.Replace + .bak).
    ///
    /// Lifecycle mirrors LandClaimUpgrade's registry: loaded from the PersistentPlayerList.ReadXML postfix
    /// (the players.xml directory IS the save directory), saved from the PersistentPlayerList.Write(string)
    /// postfix and explicitly after every mutation that matters, and flushed + latched from
    /// ModEvents.WorldShuttingDown - which fires BEFORE vanilla writes players.xml, so without the latch the
    /// Write postfix would save the intentionally emptied tables over the good file.
    ///
    /// Format (key=value lines; a line starting with '[' opens a section):
    ///   SCOM 1
    ///   [community]  home=x,y,z  owner=..  morale=63  lastday=12  stock=food:5,meds:2,ammo:3,mats:4,fuel:1
    ///                deaths=11:Nancy;9:Harley  ledger=..;..  lowmorale=0  threat=-1  hungry=0  beds=3  facilities=2
    ///   [member]     id=1234 class=npcNurseSMG name=Nancy status=Resident post=x,y,z join=4 mood=55 injured=0
    ///                ranged=1 missing=0 skills=cardio:2:15,wits:1:0,fighting:3:40,shooting:2:5 spec=Medicine:1:0
    ///                traits=Hoarder,Hardy run=food:13 pendingunhide=0 loadout=gunBowT0PrimitiveBow,meleeWpnClubT0WoodenClub
    ///                ammo=ammoArrowStone:14 (or ammo=none: recorded, carrying nothing)
    ///   [prospect]   id=1300 class=.. name=.. favour=poi|supplies poi=x,y,z poiname=.. poisize=w,d items=food:15 done=1
    ///                loadout=ranged,melee
    ///   [contact]    id=3 class=.. label=.. poi=x,y,z poiname=.. spawn=x,y,z day=12 expire=19 loadout=ranged,melee
    ///   [outpost]    id=2 flag=x,y,z poi=x,y,z size=w,h,d prefab=.. name=.. row=grocery weight=food:1.2 tier=4 day=20
    ///                carry=f,f,f,f,f pending=n,n,n,n,n state=Active lost=-1 last=f,f,f,f,f
    /// Main thread only.
    /// </summary>
    public static class ScRegistry
    {
        public const string FileName = "survivor_community.txt";
        private const string Header = "SCOM 1";

        public static Community Community = new Community();
        public static readonly Dictionary<int, Member> Members = new Dictionary<int, Member>();
        public static readonly Dictionary<int, Prospect> Prospects = new Dictionary<int, Prospect>();
        /// <summary>Survivors heard over the radio, not yet met (ScRadio).</summary>
        public static readonly List<Contact> Contacts = new List<Contact>();
        /// <summary>Cleared POIs held as outposts (ScOutposts), in establishment order.</summary>
        public static readonly List<Outpost> Outposts = new List<Outpost>();
        /// <summary>Entity ids whose record is gone but whose NPC still carries the mod's cvars/buffs; stripped when they next load.</summary>
        public static readonly HashSet<int> Cleanup = new HashSet<int>();
        /// <summary>Entity ids of survivors who died on a supply run while unloaded: their hidden entity is removed when it next loads.</summary>
        public static readonly HashSet<int> Doomed = new HashSet<int>();

        private static string saveDir;
        private static volatile bool shuttingDown;
        private static bool loaded;
        private static bool degraded;

        public static bool IsShuttingDown { get { return shuttingDown; } }
        public static bool Loaded { get { return loaded; } }
        /// <summary>True when the last load had to skip lines or fall back to the .bak.</summary>
        public static bool Degraded { get { return degraded; } }
        public static string SaveDir { get { return saveDir; } }

        public static void SetDir(string dir)
        {
            if (!string.IsNullOrEmpty(dir)) saveDir = dir;
        }

        // ---- queries ----

        public static Member GetMember(int entityId)
        {
            Member m;
            return Members.TryGetValue(entityId, out m) ? m : null;
        }

        public static Prospect GetProspect(int entityId)
        {
            Prospect p;
            return Prospects.TryGetValue(entityId, out p) ? p : null;
        }

        public static int CountStatus(MemberStatus s)
        {
            int n = 0;
            foreach (var m in Members.Values) if (m.Status == s) n++;
            return n;
        }

        public static int FollowerCount
        {
            get { return CountStatus(MemberStatus.Following) + CountStatus(MemberStatus.PendingStation); }
        }

        // ---- load ----

        public static void Load(string dir)
        {
            OnMain(() =>
            {
                SetDir(dir);
                shuttingDown = false;
                loaded = false;
                degraded = false;
                Community = new Community();
                Members.Clear();
                Prospects.Clear();
                Contacts.Clear(); Outposts.Clear();
                Cleanup.Clear();
                Doomed.Clear();
                if (string.IsNullOrEmpty(saveDir)) { ScLog.Warn("registry load: no save directory"); return; }
                string real = Path.Combine(saveDir, FileName);
                string bak = real + ".bak";
                bool ok = false;
                if (File.Exists(real)) ok = TryLoadFile(real);
                if (!ok && File.Exists(bak))
                {
                    ScLog.Warn("registry: primary file unreadable, loading the backup");
                    degraded = true;
                    Community = new Community();
                    Members.Clear();
                    Prospects.Clear();
                Contacts.Clear(); Outposts.Clear();
                    Cleanup.Clear();
                Doomed.Clear();
                    ok = TryLoadFile(bak);
                }
                if (!ok && !File.Exists(real) && !File.Exists(bak))
                {
                    ScLog.Out("registry: no " + FileName + " yet (first run in this save)");
                }
                loaded = true;
                foreach (var m in Members.Values) m.MissingDays = Math.Max(m.MissingDays, 0);
                WarnPreMigration();
                ScRunners.OnRegistryLoaded();          // a runner left in the world by a crash resumes from its snapshot
                ScOutposts.OnRegistryChanged();
                ScLog.Out("registry loaded: " + Members.Count + " member(s), " + Prospects.Count + " prospect(s), " + Outposts.Count + " outpost(s), morale "
                          + Community.Morale + (Community.HasHome ? ", home " + Community.Home : ", no home yet")
                          + (degraded ? " (DEGRADED)" : ""));
            });
        }

        /// <summary>
        /// What an old save is told (XNPCCore migration M9; decision D5: the migration needs a new game, so nothing is
        /// converted). SurvivorCommunity no longer defines its own survivor classes (D11), so a member recorded as one of
        /// them can never be re-created or found again: the relink never matches their entity, and ScDaily removes them
        /// after MissingDaysToPurge days. This line names the cause wherever that shows.
        /// </summary>
        public const string PreMigrationLine = "this save predates the XNPCCore migration: start a new game";

        /// <summary>
        /// Added to the old-save warning (M10 round 2): raiders saved by such a world (console raiders are saved, as
        /// StaticSpawner entities) belong to the old per-weapon scRaider&lt;Sex&gt;&lt;Weapon&gt; classes, which are gone too
        /// (since M10 round 1), so the world cannot bring them back either.
        /// </summary>
        public const string PreM10RaiderLine = "Raiders it saved (the old per-weapon scRaider<Sex><Weapon> classes, e.g. console spawns) are of classes that no longer exist either.";

        /// <summary>Is this member recorded as an entity class the game no longer defines (an old save's scSurvivor member)?</summary>
        public static bool PredatesMigration(Member m)
        {
            // an empty class table means the configs are not loaded yet: that says nothing about the save
            if (m == null || string.IsNullOrEmpty(m.Class) || EntityClass.list == null || EntityClass.list.Count == 0) return false;
            return !ScCommunity.ClassExists(EntityClass.FromString(m.Class));
        }

        /// <summary>Logs PreMigrationLine once per world when the registry holds members of classes that are gone.</summary>
        private static void WarnPreMigration()
        {
            try
            {
                int n = 0;
                string sample = null;
                foreach (var m in Members.Values)
                {
                    if (!PredatesMigration(m)) continue;
                    n++;
                    if (sample == null) sample = m.Name + " (" + m.Class + ")";
                }
                if (n > 0)
                    ScLog.Once("pre-migration-save", PreMigrationLine + ". " + n + " member(s) of entity classes that no longer exist, e.g. "
                               + sample + ", cannot be brought back and are removed after " + ScConfig.MissingDaysToPurge + " days unseen"
                               + " (or their class came from a character pack that is no longer installed). " + PreM10RaiderLine);
            }
            catch (Exception e)
            {
                ScLog.Once("pre-migration-check", e);
            }
        }

        private static bool TryLoadFile(string path)
        {
            try
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0 || !lines[0].StartsWith("SCOM")) { ScLog.Err("registry: bad header in " + path); return false; }
                string section = "";
                Member m = null;
                Prospect p = null;
                Contact ct = null;
                Outpost op = null;
                int bad = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    if (line[0] == '[')
                    {
                        Commit(ref m, ref p); CommitContact(ref ct); CommitOutpost(ref op);
                        section = line.Trim('[', ']').ToLowerInvariant();
                        if (section == "member") m = new Member();
                        else if (section == "prospect") p = new Prospect();
                        else if (section == "contact") ct = new Contact();
                        else if (section == "outpost") op = new Outpost();
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) { bad++; continue; }
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    try
                    {
                        if (section == "community") ParseCommunity(key, val);
                        else if (section == "member" && m != null) ParseMember(m, key, val);
                        else if (section == "prospect" && p != null) ParseProspect(p, key, val);
                        else if (section == "contact" && ct != null) ParseContact(ct, key, val);
                        else if (section == "outpost" && op != null) ParseOutpost(op, key, val);
                        else bad++;
                    }
                    catch (Exception)
                    {
                        bad++;
                    }
                }
                Commit(ref m, ref p); CommitContact(ref ct); CommitOutpost(ref op);
                if (bad > 0) { degraded = true; ScLog.Warn("registry: " + bad + " unreadable line(s) skipped in " + path); }
                return true;
            }
            catch (Exception e)
            {
                ScLog.Err("registry: cannot read " + path + ": " + e.Message);
                return false;
            }
        }

        private static void CommitOutpost(ref Outpost op)
        {
            if (op != null)
            {
                if (op.Id > 0 && !string.IsNullOrEmpty(op.Row)) Outposts.Add(op); else degraded = true;
            }
            op = null;
        }

        private static void ParseOutpost(Outpost o, string key, string val)
        {
            switch (key)
            {
                case "id": o.Id = I(val); break;
                case "flag": { Vector3i v; if (TryVec(val, out v)) o.Flag = v; break; }
                case "poi": { Vector3i v; if (TryVec(val, out v)) o.Poi = v; break; }
                case "size": { Vector3i v; if (TryVec(val, out v)) o.Size = v; break; }
                case "prefab": o.Prefab = val; break;
                case "name": o.Name = val; break;
                case "row": o.Row = val; break;
                case "weight":
                {
                    float[] w = ScPoiTypes.ParseYields(val);
                    for (int i = 0; i < 5; i++) o.Weight[i] = w[i];
                    break;
                }
                case "tier": o.Tier = I(val); break;
                case "day": o.Day = I(val); break;
                case "carry": Floats(val, o.Carry); break;
                case "last": Floats(val, o.LastYield); break;
                case "pending":
                {
                    string[] f = val.Split(',');
                    for (int i = 0; i < 5 && i < f.Length; i++) o.Pending[i] = Math.Max(0, I(f[i]));
                    break;
                }
                case "state": o.State = (OutpostState)Enum.Parse(typeof(OutpostState), val, true); break;
                case "lost": o.LostDay = I(val); break;
                case "level": o.Level = Clamp(I(val), 1, 3); break;
                case "mode": o.Mode = Clamp(I(val), 0, 2); break;
                case "attack":
                {
                    string[] f = val.Split(':');
                    ulong u;
                    if (f.Length >= 1 && ulong.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out u)) o.AttackUntil = u;
                    o.AttackRaiders = f.Length >= 2 && f[1] == "1";
                    break;
                }
                case "damaged": o.DamagedUntil = I(val); break;
                case "secbonus": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) o.SecBonus = Math.Max(0f, f); break; }
                case "demand":
                {
                    string[] f = val.Split(':');
                    if (f.Length == 3) { o.DemandUntil = I(f[0]); o.DemandAmmo = I(f[1]); o.DemandFood = I(f[2]); }
                    break;
                }
                case "invest":
                {
                    string[] f = val.Split(',');
                    for (int i = 0; i < 4 && i < f.Length; i++) o.Invest[i] = Math.Max(0, I(f[i]));
                    break;
                }
                case "investbase":
                {
                    string[] f = val.Split(',');
                    for (int i = 0; i < 4 && i < f.Length; i++) o.InvestBase[i] = Math.Max(0, I(f[i]));
                    o.HasInvestBase = true;
                    break;
                }
                case "neglect": o.NeglectDay = I(val); break;
                case "heard": o.AttackHeard = val == "1"; break;
                case "stash":
                {
                    string[] f = val.Split(',');
                    for (int i = 0; i < 5 && i < f.Length; i++) o.Stash[i] = Math.Max(0, I(f[i]));
                    break;
                }
                case "produced": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) o.Produced = Math.Max(0f, f); break; }
                case "levelday": o.LevelDay = I(val); break;
                case "visit": o.LastVisitDay = I(val); break;
            }
        }

        private static void Floats(string val, float[] into)
        {
            string[] f = val.Split(',');
            for (int i = 0; i < into.Length && i < f.Length; i++)
            {
                float v;
                if (float.TryParse(f[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) into[i] = Math.Max(0f, v);
            }
        }

        private static string Floats(float[] v)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < v.Length; i++) { if (i > 0) sb.Append(','); sb.Append(v[i].ToString("0.###", CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }

        private static void CommitContact(ref Contact ct)
        {
            if (ct != null && ct.Id > 0 && !string.IsNullOrEmpty(ct.Class)) Contacts.Add(ct);
            ct = null;
        }

        private static void ParseContact(Contact ct, string key, string val)
        {
            switch (key)
            {
                case "id": ct.Id = I(val); break;
                case "class": ct.Class = val; break;
                case "label": ct.Label = val; break;
                case "poi": { Vector3i v; if (TryVec(val, out v)) ct.PoiKey = v; break; }
                case "poiname": ct.PoiName = val; break;
                case "spawn": { Vector3i v; if (TryVec(val, out v)) ct.Spawn = v; break; }
                case "day": ct.Day = I(val); break;
                case "expire": ct.ExpireDay = I(val); break;
                case "loadout": ct.Loadout = Loadout.Decode(val); break;
            }
        }

        private static void Commit(ref Member m, ref Prospect p)
        {
            if (m != null)
            {
                if (m.Id > 0) Members[m.Id] = m; else degraded = true;
                m = null;
            }
            if (p != null)
            {
                if (p.Id > 0) Prospects[p.Id] = p; else degraded = true;
                p = null;
            }
        }

        private static void ParseCommunity(string key, string val)
        {
            Community c = Community;
            switch (key)
            {
                case "home": { Vector3i v; if (TryVec(val, out v)) { c.Home = v; c.HasHome = true; } break; }
                case "owner": c.Owner = val; break;
                case "morale": c.Morale = Clamp(I(val), 0, 100); break;
                case "lastday": c.LastDay = I(val); break;
                case "lowmorale": c.LowMoraleDays = I(val); break;
                case "threat": c.ThreatMemberId = I(val); break;
                case "hungry": c.Hungry = val == "1"; break;
                case "deathpenalty": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c.DeathPenalty = f; break; }
                case "foodcarry": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c.FoodCarry = f; break; }
                // "ammocarry" (the old daily ammo upkeep's fraction) is no longer read: there is no upkeep (decision D3)
                case "ammoopen": c.AmmoOpenPoints = Math.Max(0, I(val)); break;
                case "ammoopened": c.AmmoOpened = Math.Max(0, I(val)); break;
                case "ammoshort": c.AmmoShort = val == "1"; break;
                case "beds": c.Beds = I(val); break;
                case "facilities": c.Facilities = I(val); break;
                case "crops": c.Crops = I(val); break;
                case "scanhome": { Vector3i v; if (TryVec(val, out v)) { c.ScanHome = v; c.HasScanHome = true; } break; }
                case "scanmap":
                    // cx:cz:beds:facilities:crops;...
                    foreach (string part in val.Split(';'))
                    {
                        string[] f = part.Split(':');
                        if (f.Length != 5) continue;
                        var e = new int[] { I(f[0]), I(f[1]), I(f[2]), I(f[3]), I(f[4]) };
                        c.ScanChunks[WorldChunkCache.MakeChunkKey(e[0], e[1])] = e;
                    }
                    break;
                case "radiodrop": { ulong u; if (ulong.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out u)) c.RadioNextDrop = u; break; }
                case "radiocall": c.RadioNextCallDay = I(val); break;
                case "contactid": c.NextContactId = I(val); break;
                case "radios":
                    c.Radios.Clear();
                    foreach (string part in val.Split(';')) { Vector3i v; if (part.Length > 0 && TryVec(part, out v) && !c.Radios.Contains(v)) c.Radios.Add(v); }
                    break;
                case "radioday": c.RadioOnlineDay = I(val); break;
                case "outpostid": c.NextOutpostId = I(val); break;
                case "upkeepdebt": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c.UpkeepDebt = Math.Max(0f, f); break; }
                case "hauldebt": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c.HaulDebt = Math.Max(0f, f); break; }
                case "rundown": c.OutpostRunDown = val == "1"; break;
                case "onfoot": c.OutpostOnFoot = val == "1"; break;
                case "outpostloss": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c.OutpostLoss = Math.Max(0f, f); break; }
                case "scouts": ScScouting.Decode(val, c.Scouts); break;
                case "scoutday": c.RadioNextScoutDay = I(val); break;
                case "relays":
                    c.Relays.Clear();
                    foreach (string part in val.Split(';')) { Vector3i v; if (part.Length > 0 && TryVec(part, out v) && !c.Relays.Contains(v)) c.Relays.Add(v); }
                    break;
                case "barterday": c.RadioNextBarterDay = I(val); break;
                case "triumph": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c.OutpostTriumph = Math.Max(0f, f); break; }
                case "harvestcarry": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c.HarvestCarry = f; break; }
                case "stock":
                    foreach (string part in val.Split(','))
                    {
                        string[] kv = part.Split(':');
                        Resource r;
                        if (kv.Length == 2 && ScNames.TryResource(kv[0], out r)) c.Set(r, I(kv[1]));
                    }
                    break;
                case "deaths":
                    c.Deaths.Clear();
                    foreach (string part in val.Split(';'))
                    {
                        int colon = part.IndexOf(':');
                        if (colon > 0) c.Deaths.Add(new KeyValuePair<int, string>(I(part.Substring(0, colon)), part.Substring(colon + 1)));
                    }
                    break;
                case "ledger":
                    c.Ledger.Clear();
                    foreach (string part in val.Split(';')) if (part.Length > 0) c.Ledger.Add(part.Replace("\\n", "\n"));
                    break;
                case "cleanup":
                    foreach (string part in val.Split(',')) { int id = I(part); if (id > 0) Cleanup.Add(id); }
                    break;
                case "doomed":
                    foreach (string part in val.Split(',')) { int id = I(part); if (id > 0) Doomed.Add(id); }
                    break;
            }
        }

        private static void ParseMember(Member m, string key, string val)
        {
            switch (key)
            {
                case "id": m.Id = I(val); break;
                case "class": m.Class = val; break;
                case "name": m.Name = val; break;
                case "born": { ulong b; if (ulong.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out b)) m.Born = b; break; }
                case "status": m.Status = (MemberStatus)Enum.Parse(typeof(MemberStatus), val, true); break;
                case "post": { Vector3i v; if (TryVec(val, out v)) m.Post = v; break; }
                case "join": m.JoinDay = I(val); break;
                case "mood": m.Mood = Clamp(I(val), 0, 100); break;
                case "injured": m.Injured = val == "1"; break;
                case "ranged": m.Ranged = val == "1"; break;
                case "missing": m.MissingDays = I(val); break;
                case "pendingunhide": m.PendingUnhide = val == "1"; break;
                case "skills":
                    foreach (string part in val.Split(','))
                    {
                        string[] kv = part.Split(':');
                        Skill s;
                        if (kv.Length == 3 && ScNames.TrySkill(kv[0], out s))
                        {
                            m.Skill(s).Level = Clamp(I(kv[1]), 1, Member.MaxStars);
                            m.Skill(s).Xp = Math.Max(0, I(kv[2]));
                        }
                    }
                    break;
                case "spec":
                {
                    string[] kv = val.Split(':');
                    Specialty sp;
                    if (kv.Length >= 1 && ScNames.TrySpecialty(kv[0], out sp)) m.Specialty = sp;
                    if (kv.Length >= 2) m.SpecLevel = Clamp(I(kv[1]), 1, Member.MaxStars);
                    if (kv.Length >= 3) m.SpecXp = Math.Max(0, I(kv[2]));
                    break;
                }
                case "traits":
                    m.Traits.Clear();
                    foreach (string t in val.Split(',')) if (t.Trim().Length > 0) m.Traits.Add(t.Trim());
                    break;
                case "run":
                {
                    string[] kv = val.Split(':');
                    Resource r;
                    if (kv.Length == 2 && ScNames.TryResource(kv[0], out r)) { m.RunKind = r; m.RunEndDay = I(kv[1]); }
                    break;
                }
                case "distresspoi": { Vector3i v; if (TryVec(val, out v)) { m.RunPoi = v; m.RunDistress = true; } break; }
                case "distressspawn": { Vector3i v; if (TryVec(val, out v)) m.RunSpawn = v; break; }
                case "distressname": m.RunPoiName = val; break;
                case "distresssafe": m.RunSafe = val == "1"; break;
                case "look": { Look l = m.Look ?? new Look(); if (l.Decode(val)) m.Look = l; break; }
                case "gear": { if (m.Look == null) m.Look = new Look(); m.Look.DecodeGear(val); break; }
                case "loadout": m.Loadout = Loadout.Decode(val); break;
                case "ammo": m.Ammo = ScAmmo.Decode(val, out m.Mags); break;
                case "outpost": m.OutpostId = I(val); break;
                case "leg": { RunLeg l; if (Enum.TryParse(val, true, out l)) m.RunLeg = l; break; }
                case "dest": { Vector3i v; if (TryVec(val, out v)) m.RunDest = v; break; }
                case "destname": m.RunDestName = val; break;
                case "site": { Vector3i v; if (TryVec(val, out v)) m.RunSite = v; break; }
                case "legfrom": { Vector3i v; if (TryVec(val, out v)) m.LegFrom = v; break; }
                case "legto": { Vector3i v; if (TryVec(val, out v)) m.LegTo = v; break; }
                case "legt":
                {
                    string[] kv = val.Split(':');
                    ulong a, b;
                    if (kv.Length == 2 && ulong.TryParse(kv[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out a)
                        && ulong.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out b)) { m.LegStart = a; m.LegEnd = b; }
                    break;
                }
                case "offline": m.Offline = val == "1"; break;
                case "carry": m.RunCarry = Math.Max(0, I(val)); break;
                case "rollpending": m.RollPending = val == "1"; break;
                case "snap": m.Snap = val; break;
            }
        }

        private static void ParseProspect(Prospect p, string key, string val)
        {
            switch (key)
            {
                case "id": p.Id = I(val); break;
                case "class": p.Class = val; break;
                case "name": p.Name = val; break;
                case "favour": p.Favour = val == "poi" ? FavourKind.Poi : (val == "supplies" ? FavourKind.Supplies : FavourKind.None); break;
                case "poi": { Vector3i v; if (TryVec(val, out v)) p.PoiPos = v; break; }
                case "npcpos": { Vector3i v; if (TryVec(val, out v)) p.NpcPos = v; break; }
                case "poiname": p.PoiName = val; break;
                case "poisize":
                {
                    string[] kv = val.Split(',');
                    if (kv.Length == 2) { p.PoiWidth = I(kv[0]); p.PoiDepth = I(kv[1]); }
                    break;
                }
                case "items":
                {
                    string[] kv = val.Split(':');
                    Resource r;
                    if (kv.Length == 2 && ScNames.TryResource(kv[0], out r)) { p.ItemCategory = r; p.ItemCount = I(kv[1]); }
                    break;
                }
                case "done": p.Done = val == "1"; break;
                case "look": { Look l = p.Look ?? new Look(); if (l.Decode(val)) p.Look = l; break; }
                case "gear": { if (p.Look == null) p.Look = new Look(); p.Look.DecodeGear(val); break; }
                case "loadout": p.Loadout = Loadout.Decode(val); break;
            }
        }

        // ---- save ----

        /// <summary>Atomic write. Host only. Never throws.</summary>
        public static void SaveAtomic()
        {
            try
            {
                if (shuttingDown) return;
                var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
                if (cm == null || !cm.IsServer) return;
                if (string.IsNullOrEmpty(saveDir)) { ScLog.Once("save-nodir", "registry save skipped: no save directory yet"); return; }
                if (!ThreadManager.IsMainThread()) { OnMain(SaveAtomic); return; }

                var sb = new StringBuilder();
                sb.AppendLine(Header);
                sb.AppendLine("# SurvivorCommunity registry - edit only with the game closed");
                Community c = Community;
                sb.AppendLine("[community]");
                if (c.HasHome) sb.AppendLine("home=" + Vec(c.Home));
                sb.AppendLine("owner=" + Clean(c.Owner));
                sb.AppendLine("morale=" + c.Morale);
                sb.AppendLine("lastday=" + c.LastDay);
                sb.AppendLine("lowmorale=" + c.LowMoraleDays);
                sb.AppendLine("threat=" + c.ThreatMemberId);
                sb.AppendLine("hungry=" + (c.Hungry ? 1 : 0));
                sb.AppendLine("deathpenalty=" + c.DeathPenalty.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("foodcarry=" + c.FoodCarry.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("ammoopen=" + c.AmmoOpenPoints);
                sb.AppendLine("ammoopened=" + c.AmmoOpened);
                sb.AppendLine("ammoshort=" + (c.AmmoShort ? 1 : 0));
                sb.AppendLine("beds=" + c.Beds);
                sb.AppendLine("facilities=" + c.Facilities);
                sb.AppendLine("crops=" + c.Crops);
                if (c.HasScanHome) sb.AppendLine("scanhome=" + Vec(c.ScanHome));
                if (c.ScanChunks.Count > 0)
                {
                    var sm = new StringBuilder();
                    foreach (int[] e in c.ScanChunks.Values)
                    {
                        if (sm.Length > 0) sm.Append(';');
                        sm.Append(e[0].ToString(CultureInfo.InvariantCulture)).Append(':').Append(e[1].ToString(CultureInfo.InvariantCulture)).Append(':')
                          .Append(e[2]).Append(':').Append(e[3]).Append(':').Append(e[4]);
                    }
                    sb.AppendLine("scanmap=" + sm);
                }
                sb.AppendLine("radiodrop=" + c.RadioNextDrop.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("radiocall=" + c.RadioNextCallDay);
                sb.AppendLine("contactid=" + c.NextContactId);
                if (c.Radios.Count > 0)
                {
                    var rd = new StringBuilder();
                    foreach (Vector3i v in c.Radios) { if (rd.Length > 0) rd.Append(';'); rd.Append(Vec(v)); }
                    sb.AppendLine("radios=" + rd);
                }
                sb.AppendLine("radioday=" + c.RadioOnlineDay);
                sb.AppendLine("outpostid=" + c.NextOutpostId);
                sb.AppendLine("upkeepdebt=" + c.UpkeepDebt.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("hauldebt=" + c.HaulDebt.ToString("0.###", CultureInfo.InvariantCulture));
                sb.AppendLine("rundown=" + (c.OutpostRunDown ? 1 : 0));
                sb.AppendLine("onfoot=" + (c.OutpostOnFoot ? 1 : 0));
                sb.AppendLine("outpostloss=" + c.OutpostLoss.ToString("0.###", CultureInfo.InvariantCulture));
                if (c.Scouts.Count > 0) sb.AppendLine("scouts=" + ScScouting.Encode(c.Scouts));   // already sanitised: ';' and '|' separate
                sb.AppendLine("scoutday=" + c.RadioNextScoutDay);
                if (c.Relays.Count > 0)
                {
                    var rl = new StringBuilder();
                    foreach (Vector3i v in c.Relays) { if (rl.Length > 0) rl.Append(';'); rl.Append(Vec(v)); }
                    sb.AppendLine("relays=" + rl);
                }
                sb.AppendLine("barterday=" + c.RadioNextBarterDay);
                if (c.OutpostTriumph > 0f) sb.AppendLine("triumph=" + c.OutpostTriumph.ToString("0.##", CultureInfo.InvariantCulture));
                sb.AppendLine("harvestcarry=" + c.HarvestCarry.ToString("0.###", CultureInfo.InvariantCulture));
                var st = new StringBuilder();
                for (int i = 0; i < 5; i++) { if (i > 0) st.Append(','); st.Append(ScNames.ResourceKeys[i]).Append(':').Append(c.Stock[i]); }
                sb.AppendLine("stock=" + st);
                if (c.Deaths.Count > 0)
                {
                    var d = new StringBuilder();
                    for (int i = 0; i < c.Deaths.Count && i < 5; i++) { if (i > 0) d.Append(';'); d.Append(c.Deaths[i].Key).Append(':').Append(Clean(c.Deaths[i].Value)); }
                    sb.AppendLine("deaths=" + d);
                }
                if (c.Ledger.Count > 0)
                {
                    var l = new StringBuilder();
                    for (int i = 0; i < c.Ledger.Count && i < 7; i++) { if (i > 0) l.Append(';'); l.Append(Clean(c.Ledger[i]).Replace("\n", "\\n")); }
                    sb.AppendLine("ledger=" + l);
                }
                if (Cleanup.Count > 0)
                {
                    var cl = new StringBuilder();
                    foreach (int id in Cleanup) { if (cl.Length > 0) cl.Append(','); cl.Append(id); }
                    sb.AppendLine("cleanup=" + cl);
                }
                if (Doomed.Count > 0)
                {
                    var dm = new StringBuilder();
                    foreach (int id in Doomed) { if (dm.Length > 0) dm.Append(','); dm.Append(id); }
                    sb.AppendLine("doomed=" + dm);
                }
                foreach (var m in Members.Values)
                {
                    sb.AppendLine("[member]");
                    sb.AppendLine("id=" + m.Id);
                    sb.AppendLine("class=" + Clean(m.Class));
                    sb.AppendLine("name=" + Clean(m.Name));
                    sb.AppendLine("born=" + m.Born.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("status=" + m.Status);
                    sb.AppendLine("post=" + Vec(m.Post));
                    sb.AppendLine("join=" + m.JoinDay);
                    sb.AppendLine("mood=" + m.Mood);
                    sb.AppendLine("injured=" + (m.Injured ? 1 : 0));
                    sb.AppendLine("ranged=" + (m.Ranged ? 1 : 0));
                    sb.AppendLine("missing=" + m.MissingDays);
                    var sk = new StringBuilder();
                    for (int i = 0; i < Member.SkillCount; i++) { if (i > 0) sk.Append(','); sk.Append(ScNames.SkillKeys[i]).Append(':').Append(m.Skills[i].Level).Append(':').Append(m.Skills[i].Xp); }
                    sb.AppendLine("skills=" + sk);
                    sb.AppendLine("spec=" + m.Specialty + ":" + m.SpecLevel + ":" + m.SpecXp);
                    if (m.Traits.Count > 0) sb.AppendLine("traits=" + string.Join(",", m.Traits.ToArray()));
                    if (m.Status == MemberStatus.OnRun) sb.AppendLine("run=" + ScNames.Key(m.RunKind) + ":" + m.RunEndDay);
                    if (m.Status == MemberStatus.Garrison) sb.AppendLine("outpost=" + m.OutpostId);
                    if (m.Status == MemberStatus.OnRun && m.RunLeg != RunLeg.None)
                    {
                        sb.AppendLine("leg=" + m.RunLeg);
                        sb.AppendLine("dest=" + Vec(m.RunDest));
                        sb.AppendLine("destname=" + Clean(m.RunDestName));
                        sb.AppendLine("site=" + Vec(m.RunSite));
                        sb.AppendLine("legfrom=" + Vec(m.LegFrom));
                        sb.AppendLine("legto=" + Vec(m.LegTo));
                        sb.AppendLine("legt=" + m.LegStart.ToString(CultureInfo.InvariantCulture) + ":" + m.LegEnd.ToString(CultureInfo.InvariantCulture));
                        sb.AppendLine("offline=" + (m.Offline ? 1 : 0));
                        sb.AppendLine("carry=" + m.RunCarry);
                        sb.AppendLine("rollpending=" + (m.RollPending ? 1 : 0));
                        if (!string.IsNullOrEmpty(m.Snap)) sb.AppendLine("snap=" + m.Snap);   // base64url: never Clean()ed (it has no '=' or ';')
                    }
                    if (m.Status == MemberStatus.OnRun && m.RunDistress)
                    {
                        sb.AppendLine("distresspoi=" + Vec(m.RunPoi));
                        sb.AppendLine("distressspawn=" + Vec(m.RunSpawn));
                        sb.AppendLine("distressname=" + Clean(m.RunPoiName));
                        sb.AppendLine("distresssafe=" + (m.RunSafe ? 1 : 0));
                    }
                    if (m.Look != null)
                    {
                        sb.AppendLine("look=" + Clean(m.Look.Encode()));
                        sb.AppendLine("gear=" + Clean(m.Look.EncodeGear()));
                    }
                    if (m.Loadout != null) sb.AppendLine("loadout=" + Clean(m.Loadout.Encode()));
                    if (m.Ammo != null) sb.AppendLine("ammo=" + Clean(ScAmmo.Encode(m.Ammo, m.Mags)));   // magazines %-escaped inside (M11)
                }
                foreach (var ct in Contacts)
                {
                    sb.AppendLine("[contact]");
                    sb.AppendLine("id=" + ct.Id);
                    sb.AppendLine("class=" + Clean(ct.Class));
                    sb.AppendLine("label=" + Clean(ct.Label));
                    sb.AppendLine("poi=" + Vec(ct.PoiKey));
                    sb.AppendLine("poiname=" + Clean(ct.PoiName));
                    sb.AppendLine("spawn=" + Vec(ct.Spawn));
                    sb.AppendLine("day=" + ct.Day);
                    sb.AppendLine("expire=" + ct.ExpireDay);
                    if (ct.Loadout != null) sb.AppendLine("loadout=" + Clean(ct.Loadout.Encode()));
                }
                foreach (var o in Outposts)
                {
                    sb.AppendLine("[outpost]");
                    sb.AppendLine("id=" + o.Id);
                    sb.AppendLine("flag=" + Vec(o.Flag));
                    sb.AppendLine("poi=" + Vec(o.Poi));
                    sb.AppendLine("size=" + Vec(o.Size));
                    sb.AppendLine("prefab=" + Clean(o.Prefab));
                    sb.AppendLine("name=" + Clean(o.Name));
                    sb.AppendLine("row=" + Clean(o.Row));
                    var wt = new StringBuilder();
                    for (int i = 0; i < 5; i++)
                    {
                        if (o.Weight[i] <= 0f) continue;
                        if (wt.Length > 0) wt.Append(',');
                        wt.Append(ScNames.ResourceKeys[i]).Append(':').Append(o.Weight[i].ToString("0.###", CultureInfo.InvariantCulture));
                    }
                    if (wt.Length > 0) sb.AppendLine("weight=" + wt);
                    sb.AppendLine("tier=" + o.Tier);
                    sb.AppendLine("day=" + o.Day);
                    sb.AppendLine("carry=" + Floats(o.Carry));
                    sb.AppendLine("pending=" + string.Join(",", Array.ConvertAll(o.Pending, x => x.ToString(CultureInfo.InvariantCulture))));
                    sb.AppendLine("last=" + Floats(o.LastYield));
                    sb.AppendLine("state=" + o.State);
                    sb.AppendLine("lost=" + o.LostDay);
                    sb.AppendLine("level=" + o.Level);
                    sb.AppendLine("mode=" + o.Mode);
                    if (o.AttackUntil > 0) sb.AppendLine("attack=" + o.AttackUntil.ToString(CultureInfo.InvariantCulture) + ":" + (o.AttackRaiders ? 1 : 0));
                    if (o.DamagedUntil >= 0) sb.AppendLine("damaged=" + o.DamagedUntil);
                    if (o.SecBonus > 0f) sb.AppendLine("secbonus=" + o.SecBonus.ToString("0.##", CultureInfo.InvariantCulture));
                    if (o.DemandUntil >= 0) sb.AppendLine("demand=" + o.DemandUntil + ":" + o.DemandAmmo + ":" + o.DemandFood);
                    sb.AppendLine("invest=" + string.Join(",", Array.ConvertAll(o.Invest, x => x.ToString(CultureInfo.InvariantCulture))));
                    if (o.HasInvestBase) sb.AppendLine("investbase=" + string.Join(",", Array.ConvertAll(o.InvestBase, x => x.ToString(CultureInfo.InvariantCulture))));
                    if (o.NeglectDay >= 0) sb.AppendLine("neglect=" + o.NeglectDay);
                    if (o.AttackUntil > 0) sb.AppendLine("heard=" + (o.AttackHeard ? 1 : 0));
                    if (o.StashTotal > 0) sb.AppendLine("stash=" + string.Join(",", Array.ConvertAll(o.Stash, x => x.ToString(CultureInfo.InvariantCulture))));
                    sb.AppendLine("produced=" + o.Produced.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.AppendLine("levelday=" + o.LevelDay);
                    sb.AppendLine("visit=" + o.LastVisitDay);
                }
                foreach (var p in Prospects.Values)
                {
                    sb.AppendLine("[prospect]");
                    sb.AppendLine("id=" + p.Id);
                    sb.AppendLine("class=" + Clean(p.Class));
                    sb.AppendLine("name=" + Clean(p.Name));
                    sb.AppendLine("favour=" + (p.Favour == FavourKind.Poi ? "poi" : (p.Favour == FavourKind.Supplies ? "supplies" : "none")));
                    if (p.Favour == FavourKind.Poi)
                    {
                        sb.AppendLine("poi=" + Vec(p.PoiPos));
                        sb.AppendLine("poiname=" + Clean(p.PoiName));
                        sb.AppendLine("poisize=" + p.PoiWidth + "," + p.PoiDepth);
                    }
                    if (p.Favour == FavourKind.Supplies) sb.AppendLine("items=" + ScNames.Key(p.ItemCategory) + ":" + p.ItemCount);
                    if (p.NpcPos != Vector3i.zero) sb.AppendLine("npcpos=" + Vec(p.NpcPos));
                    sb.AppendLine("done=" + (p.Done ? 1 : 0));
                    if (p.Look != null)
                    {
                        sb.AppendLine("look=" + Clean(p.Look.Encode()));
                        sb.AppendLine("gear=" + Clean(p.Look.EncodeGear()));
                    }
                    if (p.Loadout != null) sb.AppendLine("loadout=" + Clean(p.Loadout.Encode()));
                }

                string real = Path.Combine(saveDir, FileName);
                string tmp = real + ".tmp";
                string bak = real + ".bak";
                Directory.CreateDirectory(saveDir);
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(real)) File.Replace(tmp, real, bak);
                else File.Move(tmp, real);
                ScLog.Verbose("registry saved: " + Members.Count + " member(s), " + Prospects.Count + " prospect(s)");
            }
            catch (Exception e)
            {
                ScLog.Err("registry save failed: " + e.Message);
                Log.Exception(e);
            }
        }

        /// <summary>World shutdown: save, then latch and forget so the next world starts clean.</summary>
        public static void FlushAndReset()
        {
            try
            {
                SaveAtomic();
            }
            finally
            {
                OnMain(() =>
                {
                    shuttingDown = true;
                    loaded = false;
                    Community = new Community();
                    Members.Clear();
                    Prospects.Clear();
                Contacts.Clear(); Outposts.Clear();
                    Cleanup.Clear();
                Doomed.Clear();
                    saveDir = null;
                    ScOutposts.OnRegistryChanged();
                    ScRunners.Reset();
                });
            }
        }

        // ---- helpers ----

        private static void OnMain(Action a)
        {
            if (ThreadManager.IsMainThread()) { a(); return; }
            ScLog.Once("offthread-registry", "registry mutation requested from a background thread - deferring to the main thread");
            ThreadManager.AddSingleTaskMainThread("sc-registry", a);
        }

        private static string Clean(string s)
        {
            if (s == null) return "";
            return s.Replace("\r", "").Replace("\n", " ").Replace("=", "-").Replace(";", ",").Trim();
        }

        private static string Vec(Vector3i v)
        {
            return v.x.ToString(CultureInfo.InvariantCulture) + "," + v.y.ToString(CultureInfo.InvariantCulture) + "," + v.z.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryVec(string s, out Vector3i v)
        {
            v = Vector3i.zero;
            string[] p = s.Split(',');
            int x, y, z;
            if (p.Length != 3) return false;
            if (!int.TryParse(p[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) return false;
            if (!int.TryParse(p[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out y)) return false;
            if (!int.TryParse(p[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out z)) return false;
            v = new Vector3i(x, y, z);
            return true;
        }

        private static int I(string s)
        {
            int v;
            return int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
    }
}
