using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SurvivorCommunity
{
    /// <summary>
    /// The community's source of truth: &lt;save&gt;/survivor_communities.txt (atomic tmp + File.Replace + .bak). Round MP6 (core):
    /// before MP6 it was &lt;save&gt;/survivor_community.txt (header SCOM 1, one community); a save with only that file is migrated
    /// at load (its community becomes community 0, owner kept; its world keys go to [world]; every id counter keeps its value)
    /// and the old file is never written again, so an older build still loads the pre-MP6 state and never touches the new file.
    ///
    /// Round MP6 (core) layout (header SCOM 2):
    ///   [world]      lastday=N (the world daily's day guard) communityid=N (the community id counter) contactid= outpostid=
    ///                enclaveid= enclaveseed= infestid= encpersonid= bandid= bandpersonid= encrequestid= encreqday= encremains=
    ///                encrefill= cleanup= doomed= streamname=... - every world key below that used to sit in [community];
    ///                keptlook=... (round lk1)
    ///   [membership] player=platformid:cid, one line per player in a community other than 0 (anyone not listed is in 0)
    ///                round MP7 (part AP), PerPlayer/PerParty only: joined=platformid:admin|ally|party[:link] (how a player joined
    ///                their founded community) and optout=platformid:cid (a community they left: Leave sticks) - ScJoins
    ///   [community]  cid=N first, then name= (when set) and the community keys below; one section per community
    ///   [member], [prospect], [contact], [outpost], [encrequest]: cid=N first, then the keys below (no cid: community 0)
    ///   [enclave], [encperson], [infestation], [band], [bandperson]: world records, unchanged (their relation fields stay for now)
    /// A client's snapshot (BuildText(true, cid)) is its own community's text in the SCOM 1 layout (round MP6 (part N); with one
    /// community exactly as before MP6).
    /// The keys below are each section's, as before MP6 (SCOM 1):
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
    ///                n12: handed=ranged|gunPistol9mm|1:N:base64url, one line per item she was handed exactly (ScHanded: slots
    ///                ranged, melee, head, chest, hands, feet, or spare; ItemValue.Write's N bytes; a key an older build ignores;
    ///                a line whose item is missing this session is written back verbatim) and wardrobe=helmetX,,, (the pieces
    ///                0-XNPCCore's Wardrobe dresses her in, by look slot: ScWardrobe)
    ///                md1: meds=worldtime (her last medical restock, ScMedic.Restock; written when set, a key an older build
    ///                ignores) and [community] medsrestocked=N (today's restocks, the daily ledger)
    ///                U1: ammopick=gunName|ammoName (the ammo type the player chose for her gun in the member panel,
    ///                ScAmmoChoice; written when set, a key an older build ignores)
    ///                wq1: wq=gunBowT1IronCrossbow:6,meleeWpnBatonT0PipeBaton:2 (the quality her weapons really have, by item name:
    ///                Member.WeaponQuality; written when set, never to a client, a key an older build ignores)
    ///   [prospect]   id=1300 class=.. name=.. favour=poi|supplies poi=x,y,z poiname=.. poisize=w,d items=food:15 done=1
    ///                loadout=ranged,melee; E3 encp=12 (an enclave person's favour: id is then their body's, parked at load)
    ///   [contact]    id=3 class=.. label=.. poi=x,y,z poiname=.. spawn=x,y,z day=12 expire=19 loadout=ranged,melee
    ///   [outpost]    id=2 flag=x,y,z poi=x,y,z size=w,h,d prefab=.. name=.. row=grocery weight=food:1.2 tier=4 day=20
    ///                carry=f,f,f,f,f pending=n,n,n,n,n state=Active lost=-1 last=f,f,f,f,f
    ///   [enclave]    id=4 name=.. poi=x,y,z size=w,h,d prefab=.. poiname=.. biome=pine_forest row=farm tier=2 hostile=0
    ///                standing=0 known=1 knownday=6 seeded=1:new (forward-only: an older build drops the section)
    ///                E2 adds kind=growers turned=-1 (the day it turned hostile, -1 = never) people=1 (its group was made)
    ///                E3 adds stock=1:N:base64url (the leader's trade stock, TraderData.Write's N bytes) recruited=-1 (the
    ///                day one of them last left to join the player)
    ///   [encperson]  pid=12 enclave=4 role=leader|member state=home|dead|enlisted died=-1 spot=x,y,z yaw=90 health=0.8
    ///                ammostart=.. (ScAmmo's form), then the person as a [member] writes them (WriteMemberFields: class, name,
    ///                look, loadout, ammo, skills, traits ...; id and born are 0, a body is never saved). Forward-only, as
    ///                [enclave]: an older build drops the section and the encpersonid= key. A person whose enclave is
    ///                missing is dropped (a degraded load). E3, while enlisted: enlisted=player:until:body (entity ids and the
    ///                world time) - a load finds them home all the same (bodies never outlive one) and clears that body's hire.
    ///   [infestation] id=3 poi=x,y,z size=w,h,d prefab=.. poiname=.. tier=2 start=12 how=roll source=0 known=1 knownday=13
    ///                special=owed (none | pending:x,y,z | met:class) spread=-1 rearm=x,y,z;.. (forward-only, as [enclave])
    ///   [encrequest] id=3 enclave=4 kind=resource|generator|gun state=offered|accepted offered=12 lapse=14 accepted=-1 due=-1
    ///                res=meds:2 (a resource request's resource and units) quest=QuestCode (E4, ScEncRequests; forward-only, as
    ///                [enclave], with [community] encrequestid= and encreqday= and [enclave] nextreq=). E5's kinds add
    ///                target=x,y,z infest=id person=pid deadline=worldtime stage=n targetname=.. spot=x,y,z spotyaw=90
    ///                escort=player:body siege=size:spawned:killed:started (each written when set), with [enclave] thanks=kind,
    ///                [encperson] state=missing and [community] encsiegeat=worldtime encremains=x,y,z;.. A request whose
    ///                enclave is missing is dropped (a degraded load); one of a kind this build does not know is dropped at
    ///                OnRegistryChanged. Round MP4: acceptedby=platform id (whose journal holds its quest; absent: the host's own,
    ///                as every save from before MP4; never in a client's copy).
    ///   E6 (ScEncAllies; keys an older build ignores): [enclave] benefit=harvest (the group's benefit) allyreveal=N (the
    ///                request-clock day an ally may next tell of another); [community] encloss=F (the Enclave lost morale
    ///                part) encrefill=day:x,y,z:name;.. (the refills owed: the request-clock day, the lost group's POI and name).
    ///   E7 (ScEncGifts, ScEncBarter; keys an older build ignores): [enclave] gift=N (the request-clock day the leader takes
    ///                another gift) barter=N (the request-clock day an ally trades over the radio again), each written when set.
    ///   E9 (ScEncCalls; keys an older build ignores): [enclave] callday=N (the request-clock day an ally answers another radio
    ///                call) call=kind:at:pid:until (the call that is on: fighter or drop, the world time it arrives, the
    ///                fighter's person, the world time they go home once they came - 0 before), each written when set.
    ///   E10 (ScEncRaids; keys an older build ignores): [outpost] raidenc=N (the hostile enclave behind its open raider demand or
    ///                raid) raidlost=N (how many of them died raiding it so far; with raidenc only) and [enclave] raidday=N (the
    ///                request-clock day they may raid again) raidlost=N (people a held raid cost them before they were made),
    ///                each written when set. A person out raiding is written state=home (bodies never outlive a load, and an
    ///                older build knows no other state for them).
    ///   E11 (ScEncMoves; keys an older build ignores): [enclave] moved=day:x,y,z:dead:name (the request-clock day they last
    ///                moved of their own accord, the building they left, their dead then - left out when not known - and the
    ///                building's name; seeded= keeps the mode "moved"), lossday=N (the request-clock day of their last death)
    ///                and nestday=N (the request-clock day an infestation has been next door since), each written when set.
    ///   E12 (ScEncJoin; a key an older build ignores): [member] arrive=at:enclave:x,y,z:name (one of a whole enclave that joined,
    ///                still on the way: the world time they may turn up at home from, the group they left - its id and its
    ///                building's centre - and its name), written only for them, with status=Resident: an older build has a resident
    ///                with no body (missing; "Call back" brings them). Their id is ScEncJoin.Key until a body is made for them.
    ///   E13a (ScBands; two forward-only sections, as [enclave]: an older build counts their lines as unreadable, loads the rest
    ///                - a "degraded" load, which only holds off its portrait pruning - and drops them at its next save):
    ///   [band]       id=3 kind=drifters|raiders name=.. size=4 made=1 from=x,y,z fromname=.. to=x,y,z toname=.. toenc=N
    ///                toat=x,y,z at=x,y,z depart=worldtime via=x,y,z (only while ahead) day=N (request clock) known=1 knownday=N
    ///                seed=N trust=-1|0|1 trouble=0|1|3 arrived=1 (the walk is derived from at, depart, via and toat: ScBands)
    ///   [bandperson] pid=7 band=3 role=leader|member state=alive|dead died=-1 health=0.9, then the person as a [member] writes
    ///                them (WriteMemberFields, id and born 0). A person whose band is missing is dropped (a degraded load).
    ///                [community] gains bandid= and bandpersonid= (keys an older build ignores). A drifter who took up "Come to
    ///                our place." is a [member] on the way (E12's arrive=, the enclave part the band's id negated).
    ///   R2 (ScHandheld, ScRadioTalk; 3.4 build, keys an older build ignores): [member] runradio=1 (the runner carries a handheld
    ///                radio) runtalk=N (the RadioTalk moments said on it this run), each written while OnRun and set.
    ///   Round r2 (ScRunners.TurnHomeHurt; a key an older build ignores): [member] runhurt=1 (a physical runner gave the job up
    ///                badly hurt with no way to heal and is on her way home), written with the run's leg= keys while set.
    ///   Round 30 (ScStreamerNames; both games, a key an older build ignores and drops at its next save): [community]
    ///                streamname=Sarah, one line for each streamer name given out in this save, in the order given.
    ///   Round lk1 (ScLookKeep; both games, a key an older build ignores and drops at its next save; [world] since MP6, never
    ///                to a client): keptlook=id;class;day;look;gear, one line for each body the world saves with no record of SC's
    ///                (an 0-XNPCCore hire, a stranger with a grudge): its entity id and class, the world day it was last seen loaded,
    ///                Look.Encode and EncodeGear - the look it gets back at the next load.
    ///   Round op1 (ScGarrison's journeys; keys an older build ignores): [member] transit=leg:outpost:x,y,z:x,y,z:start:end:offline:name
    ///                (on the road to or from an outpost - MemberStatus.Transit), posted=N (posted, the body is the snapshot) and
    ///                postsnap=1:N:base64url (that snapshot, or a traveler's), each with status=Resident: an older build has a resident
    ///                with no body (missing; "Call back" brings them) or, for a parked traveler, a resident whose hidden body is home.
    ///                The op1 review: postsnap= is "2:<bytes>:<base64url of gzip>" (Unpack reads "1:" too); bodyat=x,y,z, any status,
    ///                where the member's body last stood while loaded (the brief load of a parked body looks there).
    ///   Round MP6 (part relations; SCOM 2 only): [enclave], [infestation] and [band] keep their relation keys as community 0's row
    ///                (standing=, known=, knownday=, turned=, nextreq=, thanks=, allyreveal=, gift=, barter=, callday=, call=; known=,
    ///                knownday=; known=, knownday=, trust=); each other community's row is one rel= line in the same section:
    ///                [enclave] rel=cid:standing:known:knownday:turned:nextreq:allyreveal:gift:barter:callday:thanks:callkind:callat:callpid:calluntil,
    ///                [infestation] rel=cid:known:knownday, [band] rel=cid:known:knownday:trust (ScRelations). [community] grudge=&lt;platform
    ///                id&gt;, one line per player its people hold a grudge against (never to a client). A client's snapshot writes the
    ///                scope's row into the record's keys and no rel= line.
    /// Main thread only.
    /// </summary>
    public static class ScRegistry
    {
        /// <summary>Round MP6 (core): the save file since MP6 (several communities, SCOM 2).</summary>
        public const string FileName = "survivor_communities.txt";
        /// <summary>Round MP6 (core): the file before MP6 (one community, SCOM 1): read once to migrate, never written again.</summary>
        public const string LegacyFileName = "survivor_community.txt";
        private const string Header = "SCOM 2";
        /// <summary>Round MP6 (core): the old layout's header - a client's snapshot (community 0's text) still uses it.</summary>
        private const string LegacyHeader = "SCOM 1";
        /// <summary>
        /// Round MP1: a client's snap= / postsnap= (BuildText(true)): the snapshot itself stays on the server, but its presence
        /// says the member is held offline (ScRunners.HeldOffline, IsPhysical, Commit's Garrison) - so it is there, as "-", which
        /// never deserializes (ScRunners.Deserialize answers null).
        /// </summary>
        public const string SnapStub = "-";

        // ---- round MP6 (core): the community in scope. Community, Members, Prospects, Contacts, Outposts and EncRequests used to
        // be the one community's containers; they are read-only properties of Current now, so every reference reads the
        // community in scope. With one community (everyone in community 0) Current is always community 0.

        /// <summary>The community in scope (see Current): its Community record (the community fields; the world's are WorldState's).</summary>
        public static Community Community { get { return Current.Community; } }
        /// <summary>The members of the community in scope, by entity id. GetMember answers across every community.</summary>
        public static ScRecordTable<Member> Members { get { return Current.Members; } }
        /// <summary>The prospects of the community in scope, by NPC entity id. GetProspect answers across every community.</summary>
        public static ScRecordTable<Prospect> Prospects { get { return Current.Prospects; } }
        /// <summary>Survivors heard over the radio of the community in scope, not yet met (ScRadio).</summary>
        public static ScRecordList<Contact> Contacts { get { return Current.Contacts; } }
        /// <summary>Cleared POIs the community in scope holds as outposts (ScOutposts), in establishment order.</summary>
        public static ScRecordList<Outpost> Outposts { get { return Current.Outposts; } }
        /// <summary>The enclaves' open requests to the community in scope (ScEncRequests, E4), in the order they were offered.</summary>
        public static ScRecordList<EnclaveRequest> EncRequests { get { return Current.EncRequests; } }
        /// <summary>
        /// Round MP6 (core): the world record - every id counter, the enclaves' seeding, the request clock, refills, remains,
        /// streamer names, the world daily's day guard and the membership map (ScWorldState). One per save.
        /// </summary>
        public static ScWorldState WorldState { get; private set; } = new ScWorldState();
        /// <summary>
        /// Round MP6 (core), client: the community this machine's copy is of (Current with no scope). 0 until the network part
        /// sends another; back to 0 with the copy.
        /// </summary>
        public static int MirroredCommunity;

        /// <summary>
        /// Round MP6 (core): the community the registry's community properties read. In order: the top of the scope stack (Use);
        /// on a client the mirrored community; on a host with a local player (single player, a listen host) that player's
        /// community; else community 0 - and, while two or more communities exist, a diagnostic once per distinct stack trace
        /// (a dedicated server has no viewer default: every read there should be scoped). With one community it is community 0
        /// whatever the scope. Main thread only (another thread ignores the stack and gets the default).
        /// </summary>
        public static CommunityState Current
        {
            get
            {
                // one community: always it (nothing changes). Round MP6 (core fix): not on a client whose copy is of another
                // community (review C-m2) - its Current is the mirrored community even while it holds that one community only
                if (ScCommunities.Count == 1 && (loaded || MirroredCommunity == ScPlayers.SharedCommunity))
                {
                    // round MP7 (core): in PerPlayer/PerParty a scope naming no community (ScPlayers.NoCommunity) or one that is gone
                    // reads the empty record, never community 0 (its state is its own players': the research's risk 1)
                    if (scopeTop == 0 || !loaded || scopeStack[scopeTop - 1] == ScPlayers.SharedCommunity || !ScMembership.Separate || !ThreadManager.IsMainThread())
                        return ScCommunities.Zero;
                    return ScCommunities.None;
                }
                int n = scopeTop;
                if (n > 0 && ThreadManager.IsMainThread())
                {
                    int named = scopeStack[n - 1];
                    CommunityState cs = ScCommunities.ById(named);
                    if (cs != null) return cs;
                    // round MP7 (core): PerPlayer/PerParty - the empty record (a scope of NoCommunity is no fault: not logged)
                    if (loaded && ScMembership.Separate)
                    {
                        if (named != ScPlayers.NoCommunity && named != goneReported)
                        {
                            goneReported = named;
                            ScLog.Once("mp7-scope-gone-" + named, "a scope names community " + named + ", which does not exist - the empty record read (not community 0)");
                        }
                        return ScCommunities.None;
                    }
                    // round MP6 (core fix): the log key is built once per missing id, not on every read (review C-n2)
                    if (named != goneReported)
                    {
                        goneReported = named;
                        ScLog.Once("mp6-scope-gone-" + named, "a scope names community " + named + ", which does not exist - community 0 assumed");
                    }
                    return ScCommunities.Zero;
                }
                return Unscoped();
            }
        }

        /// <summary>
        /// Round MP6 (test fix t1): the community in scope when one is open, else the listen host's own player's, else null - with no
        /// "[MP6 scope]" diagnostic (GetProspect only prefers it and falls back to every community). Main thread for the scope.
        /// </summary>
        private static CommunityState PreferredQuiet()
        {
            if (scopeTop > 0 && ThreadManager.IsMainThread()) return ScCommunities.ById(scopeStack[scopeTop - 1]);
            EntityPlayerLocal lp = ScCommunity.LocalPlayer;
            return lp != null ? ScCommunities.ById(ScPlayers.CommunityOf(lp)) : null;
        }

        /// <summary>Round MP6 (core fix): the last missing community a scope named that Current reported (review C-n2).</summary>
        private static int goneReported = int.MinValue;

        /// <summary>Round MP6 (core): the id of the community in scope (Current.Id).</summary>
        public static int CurrentId { get { return Current.Id; } }

        /// <summary>
        /// Round MP6 (core): makes cid the community in scope until the returned handle is disposed:
        /// using (ScRegistry.Use(cid)) { ... }. Scopes nest; Dispose restores the previous one, exceptions included. Never keep a
        /// scope open across a coroutine's yield. Main thread only: from another thread it does nothing (logged once).
        /// </summary>
        public static ScScope Use(int cid)
        {
            if (!ThreadManager.IsMainThread())
            {
                ScLog.Once("mp6-scope-offthread", "ScRegistry.Use called off the main thread - ignored (the registry is main-thread only)");
                return default(ScScope);
            }
            if (scopeTop == scopeStack.Length)
            {
                var grown = new int[scopeStack.Length * 2];
                Array.Copy(scopeStack, grown, scopeStack.Length);
                scopeStack = grown;
            }
            int depth = scopeTop;
            scopeStack[depth] = cid;
            scopeTop = depth + 1;
            return new ScScope(depth);
        }

        /// <summary>Round MP6 (core): the member's community (her record's CommunityId).</summary>
        public static ScScope Use(Member m) { return Use(m != null ? m.CommunityId : ScPlayers.SharedCommunity); }

        /// <summary>Round MP6 (core): the outpost's community (its record's CommunityId).</summary>
        public static ScScope Use(Outpost o) { return Use(o != null ? o.CommunityId : ScPlayers.SharedCommunity); }

        /// <summary>Round MP6 (core): the player's community (ScPlayers.CommunityOf; community 0 for null).</summary>
        public static ScScope Use(EntityPlayer p) { return Use(ScPlayers.CommunityOf(p)); }

        /// <summary>Round MP6 (core), ScScope.Dispose: back to the given depth (this scope and any inner one left open go).</summary>
        internal static void PopScope(int depth)
        {
            if (depth >= 0 && depth < scopeTop) scopeTop = depth;
        }

        private static int[] scopeStack = new int[16];
        private static int scopeTop;

        /// <summary>
        /// Round MP6 (core fix), ScTicker.OnGameUpdate at the top of every frame (review C-m3): no SC scope is open between frames,
        /// so one still open here leaked (a Use without "using", or held across a coroutine's yield) - it would be the default of
        /// every later unscoped read and hide the "[MP6 scope]" diagnostic. Logged once per set of ids, then the stack is reset
        /// (a late Dispose of a leaked handle is then a no-op: PopScope ignores a depth past the top).
        /// </summary>
        internal static void ResetLeakedScopes()
        {
            int n = scopeTop;
            if (n == 0 || !ThreadManager.IsMainThread()) return;
            var ids = new StringBuilder();
            for (int i = 0; i < n && i < 8; i++) ids.Append(i > 0 ? "," : "").Append(scopeStack[i]);
            if (n > 8) ids.Append(",...");
            ScLog.Once("mp6-scope-leak-" + ids, "[MP6 scope] " + n + " scope(s) still open at the start of a frame (communities " + ids
                       + ") - a ScRegistry.Use without \"using\", or one held across a yield; reset");
            scopeTop = 0;
        }

        /// <summary>Round MP6 (core): Current with nothing in scope and two or more communities (see Current).</summary>
        private static CommunityState Unscoped()
        {
            if (!loaded) return ScCommunities.ById(MirroredCommunity) ?? ScCommunities.Zero;     // a client's copy (or no world)
            EntityPlayerLocal local = ScCommunity.LocalPlayer;
            if (local != null)
            {
                // round MP6 (core fix): a listen host's unscoped read takes the host's community - counted, and logged (verbose)
                // while remote clients are connected: a patch acting for a client's community would work on the host's (review C-m6)
                NoteHostUnscoped();
                return ScCommunities.OrNone(ScPlayers.CommunityOf(local));     // round MP7 (core): the empty record for none (PerPlayer/PerParty)
            }
            NoteUnscoped();
            return ScCommunities.Zero;
        }

        /// <summary>Round MP6 (core): unscoped reads seen on a server with no local player while two or more communities exist.</summary>
        public static int UnscopedReads { get { return unscopedReads; } }
        /// <summary>
        /// Round MP6 (core fix): unscoped reads on a host with a local player (the host's community assumed) while two or more
        /// communities exist and remote clients are connected (review C-m6). Their traces are logged with ScConfig.Verbose only:
        /// most are the host's own UI, for which the host's community is right.
        /// </summary>
        public static int UnscopedHostReads { get { return unscopedHostReads; } }
        private static int unscopedReads, unscopedHostReads;
        private static int diagSecond, diagBudget;
        private static readonly HashSet<string> diagSeen = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Round MP6 (core fix): NoteUnscoped's listen-host half (Unscoped, review C-m6).</summary>
        private static void NoteHostUnscoped()
        {
            try
            {
                ConnectionManager cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
                if (cm == null || cm.Clients == null || cm.Clients.Count == 0) return;      // nobody else: the host's community is the only reader
                unscopedHostReads++;
                if (ScConfig.Verbose) NoteTrace(true);
            }
            catch (Exception) { }   // a diagnostic never costs the read
        }

        /// <summary>
        /// Round MP6 (core): the diagnostic for an unscoped read - logged once per distinct stack trace, at most a few traces
        /// taken a second and 200 logged in all, so a hot unscoped read costs a counter, not a stack walk each time.
        /// </summary>
        private static void NoteUnscoped()
        {
            try
            {
                unscopedReads++;
                NoteTrace(false);
            }
            catch (Exception) { }   // a diagnostic never costs the read
        }

        /// <summary>
        /// Round MP6 (core fix): the trace half of NoteUnscoped, shared with NoteHostUnscoped (host: the listen host's line). The
        /// message is built only when it is logged.
        /// </summary>
        private static void NoteTrace(bool host)
        {
            lock (diagSeen)
            {
                if (diagSeen.Count >= 200) return;      // round MP6 (core fix): no stack walk once the cap is reached (review C-n1)
            }
            int now = Environment.TickCount;
            if (now - diagSecond >= 1000 || now < diagSecond) { diagSecond = now; diagBudget = 4; }
            if (diagBudget <= 0) return;
            diagBudget--;
            string trace = new System.Diagnostics.StackTrace(4, false).ToString();
            lock (diagSeen)
            {
                if (diagSeen.Count >= 200 || !diagSeen.Add(trace)) return;
            }
            ScLog.Warn("[MP6 scope] community state read with no scope " + (host ? "on a listen host " : "") + "while " + ScCommunities.Count
                       + " communities exist" + (host ? " and clients are connected - the host's community assumed" : " - community 0 assumed")
                       + ". From:\n" + trace);
        }

        /// <summary>Groups of survivors holed up in POIs (ScEnclaves), in seeding order.</summary>
        public static readonly List<Enclave> Enclaves = new List<Enclave>();
        /// <summary>
        /// The enclaves' people (ScEnclavePeople, E2), in the order they were made. Never in Members: relink, consumption,
        /// morale and the roster walk that dictionary.
        /// </summary>
        public static readonly List<EncPerson> EncPeople = new List<EncPerson>();
        /// <summary>Infested POIs (ScInfestations), in the order they began.</summary>
        public static readonly List<Infestation> Infestations = new List<Infestation>();
        /// <summary>Bands on the road (ScBands, E13a), in the order they set out.</summary>
        public static readonly List<Band> Bands = new List<Band>();
        /// <summary>The bands' people (ScBands, E13a), in the order they were made. Never in Members, as EncPeople.</summary>
        public static readonly List<BandPerson> BandPeople = new List<BandPerson>();
        /// <summary>
        /// Set by the PersistentPlayerList.ReadXML postfix before Load: the save has no main.ttw yet, so this is its first
        /// start (World.LoadWorld creates the file later in the same startup). ScEnclaves seeds a new save from it.
        /// </summary>
        public static bool SaveIsNew;
        /// <summary>Entity ids whose record is gone but whose NPC still carries the mod's cvars/buffs; stripped when they next load.</summary>
        public static readonly HashSet<int> Cleanup = new HashSet<int>();
        /// <summary>Entity ids of survivors who died on a supply run while unloaded: their hidden entity is removed when it next loads.</summary>
        public static readonly HashSet<int> Doomed = new HashSet<int>();

        private static string saveDir;
        private static volatile bool shuttingDown;
        private static bool loaded;
        private static bool degraded;
        /// <summary>Round MP1: a client holds the server's snapshot (ApplyMirror).</summary>
        private static bool mirrored;
        /// <summary>Round MP1: the server's registry changed since the clients' last snapshot (SaveAtomic sets it; ScMirror takes it).</summary>
        private static volatile bool dirty;

        public static bool IsShuttingDown { get { return shuttingDown; } }
        /// <summary>
        /// The authoritative registry is here: the server (single player, a listen host, a dedicated server). Never on a client,
        /// whose copy is Mirrored - code that writes keeps asking this.
        /// </summary>
        public static bool Loaded { get { return loaded; } }
        /// <summary>
        /// Round MP1: a client holds a read-only copy of the server's registry in these same containers (NetPackageScState,
        /// ScMirror), so the community window, the Home Status HUD, the Facilities tab and the talk card show it. Never true where
        /// Loaded is. Nothing here is ever saved: SaveAtomic returns on a client.
        /// </summary>
        public static bool Mirrored { get { return mirrored; } }
        /// <summary>Round MP1: the containers hold community data to show - the real registry or a client's copy. For read gates only.</summary>
        public static bool Ready { get { return loaded || mirrored; } }
        /// <summary>True when the last load had to skip lines or fall back to the .bak.</summary>
        public static bool Degraded { get { return degraded; } }
        public static string SaveDir { get { return saveDir; } }

        public static void SetDir(string dir)
        {
            if (!string.IsNullOrEmpty(dir)) saveDir = dir;
        }

        // ---- queries ----

        /// <summary>
        /// The member with this id, in any community (round MP6 (core): entity ids are world-unique, so this global index never
        /// reads the scope; her record's CommunityId says whose she is). CountStatus, FollowerCount and Members stay per scope.
        /// </summary>
        public static Member GetMember(int entityId)
        {
            CommunityState[] all = ScCommunities.Array;
            for (int i = 0; i < all.Length; i++)
            {
                Member m;
                if (all[i].Members.TryGetValue(entityId, out m)) return m;
            }
            return null;
        }

        /// <summary>
        /// The prospect with this NPC id, in any community (round MP6 (core), as GetMember). Round MP6 (part relations): each
        /// community courts a survivor with its own favour under the NPC's id (the user's decision 2), so two communities can hold
        /// one key - this answers the community in scope's when it has one, else the lowest community's (a "does anyone court
        /// them" test reads the same). A favour's own flow (the talk rows, the favour ops) reads OwnProspect: never another's.
        /// </summary>
        public static Prospect GetProspect(int entityId)
        {
            CommunityState[] all = ScCommunities.Array;
            Prospect p;
            // round MP6 (part relations): the preferred community's first. Round MP6 (test fix t1): read quietly - with no scope open
            // (a body's PostInit, its loadout and looks) this is an "anyone's" lookup, not an unscoped read of one community
            CommunityState pref = all.Length > 1 ? PreferredQuiet() : null;
            if (pref != null && pref.Prospects.TryGetValue(entityId, out p)) return p;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Prospects.TryGetValue(entityId, out p)) return p;
            }
            return null;
        }

        /// <summary>
        /// Round MP6 (part relations): the community in scope's own prospect for this NPC, or null - another community's favour for
        /// the same survivor is never answered (the favour flow: rolling one, the talk rows, the favour ops). With one community:
        /// GetProspect.
        /// </summary>
        public static Prospect OwnProspect(int entityId)
        {
            Prospect p;
            return Current.Prospects.TryGetValue(entityId, out p) ? p : null;
        }

        /// <summary>Round MP6 (core): the community whose roster holds this member id, or -1 (ScCommunities.CommunityOfMember).</summary>
        public static int CommunityOfMember(int entityId)
        {
            return ScCommunities.CommunityOfMember(entityId);
        }

        // ---- round MP6 (core fix): a record found through a global lookup is changed in its own community (review C-M2) ----
        // GetMember / GetProspect answer across communities; removing or re-keying through ScRegistry.Members / .Prospects would
        // change the community in scope instead - a miss there leaves the record behind, a put moves it (the container stamps
        // CommunityId) and the save then writes it twice. Change the record's own container (these helpers), or scope to it
        // (using (ScRegistry.Use(m))).

        /// <summary>Round MP6 (core fix): the community that holds this record (its CommunityId), or null when none does.</summary>
        public static CommunityState StateOf(IScCommunityRecord record)
        {
            return record != null ? ScCommunities.ById(record.CommunityId) : null;
        }

        /// <summary>Round MP6 (core fix): the roster that holds this member - her own community's (the scope's if none holds her).</summary>
        public static ScRecordTable<Member> RosterOf(Member m)
        {
            CommunityState cs = StateOf(m);
            return cs != null ? cs.Members : Members;
        }

        /// <summary>Round MP6 (core fix): the prospects table that holds this prospect - its own community's (the scope's if none does).</summary>
        public static ScRecordTable<Prospect> ProspectsOf(Prospect p)
        {
            CommunityState cs = StateOf(p);
            return cs != null ? cs.Prospects : Prospects;
        }

        /// <summary>Round MP6 (core fix): removes the member with this id from whichever community holds her. True when one did.</summary>
        public static bool RemoveMember(int entityId)
        {
            CommunityState cs = ScCommunities.ById(ScCommunities.CommunityOfMember(entityId));
            return cs != null && cs.Members.Remove(entityId);
        }

        /// <summary>
        /// Round MP6 (core fix): removes this NPC's prospect from every community that courts them (the NPC joined someone or
        /// died: no community's favour for them stays). Returns how many were removed. With one community: Prospects.Remove.
        /// </summary>
        public static int RemoveProspectEverywhere(int entityId)
        {
            int n = 0;
            CommunityState[] all = ScCommunities.Array;
            for (int i = 0; i < all.Length; i++) if (all[i].Prospects.Remove(entityId)) n++;
            return n;
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
                saveBlocked = false;                   // round MP6 (core fix)
                ClearContainers();                     // round MP6 (core): community 0 alone, a fresh world record
                if (string.IsNullOrEmpty(saveDir)) { ScLog.Warn("registry load: no save directory"); return; }
                string real = Path.Combine(saveDir, FileName);
                string bak = real + ".bak";
                string legacy = Path.Combine(saveDir, LegacyFileName);
                string legacyBak = legacy + ".bak";
                bool ok = false;
                // round MP6 (core fix): whether a file could not be read at all (in use by another program, no access) rather than
                // read and found bad - the new file's (or its backup's), and the old file's (or its backup's) (review C-m1)
                bool newIo = false, oldIo = false;
                // round MP6 (core): the new file (or its backup) when this save has one; else the old one-community file is migrated
                // (its community becomes community 0, owner kept; world keys go to the world record; every id counter keeps its
                // value). The old file is only ever read: SaveAtomic writes the new one.
                if (File.Exists(real) || File.Exists(bak))
                {
                    if (File.Exists(real)) ok = TryLoadFile(real, ref newIo);
                    if (!ok && File.Exists(bak))
                    {
                        ScLog.Warn("registry: primary file unreadable, loading the backup");
                        degraded = true;
                        ClearContainers();
                        ok = TryLoadFile(bak, ref newIo);
                    }
                    bool readNew = ok;                 // round MP6 (core fix): the new file or its backup was read
                    // round MP6 (core fix): the new file and its backup both unreadable (a power loss right after the first save, whose
                    // File.Move left no backup) - the old file is migrated again rather than starting empty (review C-m1)
                    if (!ok && (File.Exists(legacy) || File.Exists(legacyBak)))
                    {
                        ScLog.Err("registry: " + FileName + " and its backup are unreadable - migrating " + LegacyFileName + " again (the state from before MP6: what changed since is lost)");
                        degraded = true;
                        ClearContainers();
                        ok = MigrateLegacy(legacy, legacyBak, ref oldIo);
                    }
                    // round MP6 (core fix): an older build that played the old file since this one wrote the new file (review C-n6)
                    else if (ok) WarnLegacyNewer(real, legacy);
                    // round MP6 (core fix): neither the new file nor its backup read, and one could not even be opened - saving would
                    // replace it (and the backup) with what this session builds from nothing or from the older migrated state; held
                    // off until the next load
                    if (!readNew && newIo) saveBlocked = true;
                    // round MP6 (core re-check): the fallback migration could not open the old file either (held by another program) -
                    // saving now would write an empty community over the intact old state's only route back
                    if (!ok && oldIo) saveBlocked = true;
                }
                else if (File.Exists(legacy) || File.Exists(legacyBak))
                {
                    ok = MigrateLegacy(legacy, legacyBak, ref oldIo);
                    // round MP6 (core fix): the old file is there but could not be opened (a sharing violation is enough) - no new file
                    // in this session, so the next load tries the migration again (review C-m1). Read but unreadable as a registry: the
                    // degraded start of before MP6 (the old file is left as it is either way)
                    if (!ok && oldIo) saveBlocked = true;
                }
                else ScLog.Out("registry: no " + FileName + " yet (first run in this save)");
                if (saveBlocked)
                    ScLog.Err("registry: " + (newIo ? FileName : LegacyFileName) + " exists but could not be read (in use by another program?) - "
                              + "SurvivorCommunity will NOT save in this session, so that file is not replaced. Close what holds it and load the save again.");
                loaded = true;
                foreach (CommunityState cs in ScCommunities.All)
                    foreach (var m in cs.Members.Values) m.MissingDays = Math.Max(m.MissingDays, 0);
                WarnPreMigration();
                // round MP6 (core): community 0's scope, as before MP6 (no diagnostic). Round MP6 (part G): both take every
                // community's records now (ScRunners.OnRegistryLoaded every runner, ScOutposts.OnRegistryChanged the id check and the
                // footprint index of every outpost)
                using (Use(ScPlayers.SharedCommunity))
                {
                    ScRunners.OnRegistryLoaded();          // a runner left in the world by a crash resumes from its snapshot
                    ScOutposts.OnRegistryChanged();
                }
                CommunityState c0 = ScCommunities.Zero;
                int members = 0, prospects = 0, outposts = 0, requests = 0;
                foreach (CommunityState cs in ScCommunities.All)
                {
                    members += cs.Members.Count; prospects += cs.Prospects.Count; outposts += cs.Outposts.Count; requests += cs.EncRequests.Count;
                }
                ScLog.Out("registry loaded: " + members + " member(s), " + prospects + " prospect(s), " + outposts + " outpost(s), "
                          + Enclaves.Count + " enclave(s) with " + EncPeople.Count + " person record(s) and " + requests + " open request(s), "
                          + Infestations.Count + " infestation(s), " + Bands.Count + " band(s) on the road"
                          + (SaveIsNew ? " (a new save)" : "") + ", morale "
                          + c0.Community.Morale + (c0.Community.HasHome ? ", home " + c0.Community.Home : ", no home yet")
                          + (ScCommunities.Count > 1 ? " (community 0; " + ScCommunities.Count + " communities, " + WorldState.Membership.Count + " player(s) outside community 0)" : "")
                          + (degraded ? " (DEGRADED)" : ""));
                ScMembership.OnRegistryLoaded();        // round MP7 (core): the community mode in force (and a config that differs, once)
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
                foreach (var m in ScCommunities.AllMembers())        // round MP6 (core): every community's
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

        /// <summary>
        /// One file into the containers. Round MP6 (core fix): ioFailed is set when the file could not be read at all (the read
        /// threw: in use, no access), as against read and found unreadable (review C-m1).
        /// </summary>
        private static bool TryLoadFile(string path, ref bool ioFailed)
        {
            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                ioFailed = true;
                ScLog.Err("registry: cannot read " + path + ": " + e.Message);
                return false;
            }
            try
            {
                // round MP1: the parse is ParseText, shared with a client's copy of the server's registry (ApplyMirror)
                return ParseText(text, path);
            }
            catch (Exception e)
            {
                ScLog.Err("registry: cannot read " + path + ": " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Round MP6 (core fix): the migration of the old one-community file (or its backup), Load's (review C-m1: also when the new
        /// file and its backup are both unreadable). ioFailed as TryLoadFile.
        /// </summary>
        private static bool MigrateLegacy(string legacy, string legacyBak, ref bool ioFailed)
        {
            ScLog.Out("registry: migrating " + LegacyFileName + " to " + FileName + " - its community becomes community 0 (the old file is left as it is)");
            bool ok = false;
            if (File.Exists(legacy)) ok = TryLoadFile(legacy, ref ioFailed);
            if (!ok && File.Exists(legacyBak))
            {
                ScLog.Warn("registry: " + LegacyFileName + " unreadable, migrating its backup");
                degraded = true;
                ClearContainers();
                ok = TryLoadFile(legacyBak, ref ioFailed);
            }
            return ok;
        }

        /// <summary>
        /// Round MP6 (core fix): one warning when the old file was written after the new one - an older SC build played the old
        /// file since; this build keeps the new file and ignores those changes (review C-n6).
        /// </summary>
        private static void WarnLegacyNewer(string real, string legacy)
        {
            try
            {
                if (!File.Exists(real) || !File.Exists(legacy)) return;
                DateTime newer = File.GetLastWriteTimeUtc(legacy), ours = File.GetLastWriteTimeUtc(real);
                if (newer > ours)
                    ScLog.Warn("registry: " + LegacyFileName + " was written after " + FileName + " (" + newer.ToString("u", CultureInfo.InvariantCulture)
                               + " against " + ours.ToString("u", CultureInfo.InvariantCulture) + ") - an older SurvivorCommunity build played this save since; "
                               + "this build loads " + FileName + " and ignores those changes");
            }
            catch (Exception e) { ScLog.Once("registry-legacy-newer", e); }
        }

        /// <summary>
        /// Round MP6 (core fix): this session must not save - the file it would replace (the new one, or the old one to migrate)
        /// exists but could not be read at load (review C-m1). SaveAtomic then only marks the clients' copy dirty.
        /// </summary>
        public static bool SaveBlocked { get { return saveBlocked; } }
        private static bool saveBlocked;

        /// <summary>
        /// Round MP1: the registry's text (BuildText's form) into the containers, which the caller has emptied: the save file
        /// (TryLoadFile) and a client's copy of the server's registry (ApplyMirror). Returns false when the text is unreadable
        /// as a whole; skipped lines mark the load degraded. Only the containers and the records' own state are written.
        /// </summary>
        public static bool ParseText(string text, string source = "the server's snapshot")
        {
            return ParseText(text, source, ScPlayers.SharedCommunity);
        }

        /// <summary>
        /// Round MP6 (core fix): ParseText with the community that a [community] section or a record with no cid= belongs to - 0
        /// for a save (every old file's one community), a client's mirrored community for its copy (ApplyMirror, review C-m2).
        /// </summary>
        private static bool ParseText(string text, string source, int defaultCid)
        {
            if (defaultCid < 0) defaultCid = ScPlayers.SharedCommunity;
            try
            {
                // round fx1 (the review): the registry loads before GameStartDone parses the config, and the Shooting XP
                // migration below reads ShootingXpMult - parse it now (as ScInfestations.Rebuild already does in this load)
                ScConfig.EnsureParsed();
                // round MP1: from text, not the file (File.ReadAllLines split it before; every line is trimmed below, '\r' too)
                string[] lines = (text ?? "").Split('\n');
                string path = source;
                if (lines.Length == 0 || !lines[0].StartsWith("SCOM")) { ScLog.Err("registry: bad header in " + path); return false; }
                string section = "";
                Member m = null;
                Prospect p = null;
                Contact ct = null;
                Outpost op = null;
                Enclave en = null;
                EncPerson ep = null;
                Infestation inf = null;
                EnclaveRequest rq = null;
                Band bd = null;
                BandPerson bp = null;
                int bad = 0;
                // round MP6 (core): a [community] section's lines wait for its end, so its cid= picks the community whichever line it
                // is on (none: community 0 - every old file's one community); sawWorld: the text has a [world] section (else it is an
                // old file or a client's snapshot: the world's day guard is its community's)
                List<KeyValuePair<string, string>> comm = null;
                // round MP6 (core fix): none: defaultCid - community 0 for a save, the mirrored community for a client's copy (C-m2)
                int commCid = defaultCid;
                bool sawWorld = false;
                loadShootScale = 1;   // round fx1
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    if (line[0] == '[')
                    {
                        Commit(ref m, ref p); CommitContact(ref ct); CommitOutpost(ref op); CommitEnclave(ref en); CommitPerson(ref ep); CommitInfestation(ref inf); CommitRequest(ref rq);
                        CommitBand(ref bd); CommitBandPerson(ref bp);
                        bad += CommitCommunity(ref comm, commCid);      // round MP6 (core)
                        loadShootScale = 1;   // round fx1: a block without shootxp= was written before Shooting's thresholds grew
                        section = line.Trim('[', ']').ToLowerInvariant();
                        if (section == "community") { comm = new List<KeyValuePair<string, string>>(); commCid = defaultCid; }
                        else if (section == "world") sawWorld = true;
                        // round MP6 (core fix): a community record with no cid= is defaultCid's (0 for a save, as before)
                        if (section == "member") m = new Member { CommunityId = defaultCid };
                        else if (section == "prospect") p = new Prospect { CommunityId = defaultCid };
                        else if (section == "contact") ct = new Contact { CommunityId = defaultCid };
                        else if (section == "outpost") op = new Outpost { CommunityId = defaultCid };
                        else if (section == "enclave") en = new Enclave();
                        else if (section == "encperson") ep = new EncPerson();
                        else if (section == "infestation") inf = new Infestation();
                        else if (section == "encrequest") rq = new EnclaveRequest { CommunityId = defaultCid };
                        else if (section == "band") bd = new Band();
                        else if (section == "bandperson") bp = new BandPerson();
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) { bad++; continue; }
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    try
                    {
                        // round MP6 (core): [community] waits for its end (CommitCommunity); [world] and [membership] are new
                        if (section == "community" && comm != null) { if (key == "cid") commCid = I(val); else comm.Add(new KeyValuePair<string, string>(key, val)); }
                        else if (section == "world") ParseWorld(key, val);
                        else if (section == "membership") ParseMembership(key, val);
                        else if (section == "member" && m != null) ParseMember(m, key, val);
                        else if (section == "prospect" && p != null) ParseProspect(p, key, val);
                        else if (section == "contact" && ct != null) ParseContact(ct, key, val);
                        else if (section == "outpost" && op != null) ParseOutpost(op, key, val);
                        else if (section == "enclave" && en != null) ParseEnclave(en, key, val);
                        else if (section == "encperson" && ep != null) ParsePerson(ep, key, val);
                        else if (section == "infestation" && inf != null) ParseInfestation(inf, key, val);
                        else if (section == "encrequest" && rq != null) ParseRequest(rq, key, val);
                        else if (section == "band" && bd != null) ParseBand(bd, key, val);
                        else if (section == "bandperson" && bp != null) ParseBandPerson(bp, key, val);
                        else bad++;
                    }
                    catch (Exception)
                    {
                        bad++;
                    }
                }
                Commit(ref m, ref p); CommitContact(ref ct); CommitOutpost(ref op); CommitEnclave(ref en); CommitPerson(ref ep); CommitInfestation(ref inf); CommitRequest(ref rq);
                CommitBand(ref bd); CommitBandPerson(ref bp);
                bad += CommitCommunity(ref comm, commCid);      // round MP6 (core)
                if (bad > 0) { degraded = true; ScLog.Warn("registry: " + bad + " unreadable line(s) skipped in " + path); }
                DropStrayPeople();
                DropStrayRequests();
                DropStrayBandPeople();
                AfterParse(sawWorld, defaultCid);              // round MP6 (core)
                return true;
            }
            catch (Exception e)
            {
                ScLog.Err("registry: cannot read " + source + ": " + e.Message);
                return false;
            }
        }

        // ---- round MP1: a client's copy of the server's registry ----

        /// <summary>
        /// Round MP1, client: the server's snapshot (BuildText(true), from NetPackageScState) replaces whatever copy was here. It
        /// fills only the containers: none of the load's hooks run (ScRunners, ScOutposts, the enclaves' indexes - all server
        /// state), Loaded stays false and nothing is ever saved. False (and an empty copy) on unreadable text, and always where the
        /// registry is the real one.
        /// </summary>
        public static bool ApplyMirror(string text)
        {
            if (loaded) return false;
            ClearContainers();
            // round MP6 (core fix): the copy is the mirrored community's - its sections and records with no cid= go there, so a
            // client's Current (and the id its actions send) is that community, not community 0 (review C-m2)
            // round MP6 (part N): the community is there before the first line and the parse runs in its scope, so a relation value a
            // world record's line sets (an enclave's standing=, known= ...) lands where the copy's readers look - the record's field
            // today, the mirrored community's row once relations are per community. ScMirror.Receive set MirroredCommunity first
            bool ok;
            if (MirroredCommunity < 0) MirroredCommunity = ScPlayers.SharedCommunity;
            ScCommunities.Ensure(MirroredCommunity);
            using (Use(MirroredCommunity)) ok = ParseText(text, "the server's snapshot", MirroredCommunity);
            degraded = false;                 // the copy says nothing about this machine's (absent) save
            Cleanup.Clear();                  // server bookkeeping (BuildText(true) leaves it out): never acted on here
            Doomed.Clear();
            if (!ok) ClearContainers();
            mirrored = ok;
            return ok;
        }

        /// <summary>Round MP1, client: forget the copy (the world is closing). Never touches the real registry.</summary>
        public static void ClearMirror()
        {
            if (loaded) return;
            mirrored = false;
            MirroredCommunity = ScPlayers.SharedCommunity;     // round MP6 (core): the copy's community goes with it
            ClearContainers();
        }

        /// <summary>
        /// Every container emptied. Round MP6 (core): community 0 alone (fresh), a fresh world record, the world lists empty.
        /// </summary>
        private static void ClearContainers()
        {
            ScCommunities.Reset();
            WorldState = new ScWorldState();
            Enclaves.Clear(); EncPeople.Clear(); Infestations.Clear(); Bands.Clear(); BandPeople.Clear();
            Cleanup.Clear();
            Doomed.Clear();
            ScLookKeep.Clear();               // round lk1: the kept looks
        }

        /// <summary>
        /// Round MP1, server: whether the registry was saved since the last call (SaveAtomic sets the flag after every mutation that
        /// matters; ScMirror's push takes it), clearing the flag.
        /// </summary>
        public static bool TakeDirty()
        {
            bool d = dirty;
            dirty = false;
            return d;
        }

        private static void CommitOutpost(ref Outpost op)
        {
            if (op != null)
            {
                if (op.Id > 0 && !string.IsNullOrEmpty(op.Row)) LoadTarget(op.CommunityId).Outposts.Add(op); else degraded = true;   // round MP6 (core): its community's
            }
            op = null;
        }

        private static void ParseOutpost(Outpost o, string key, string val)
        {
            switch (key)
            {
                case "cid": o.CommunityId = I(val); break;              // round MP6 (core): its community (none: 0)
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
                    for (int i = 0; i < ScNames.ResourceCount; i++) o.Weight[i] = w[i];
                    break;
                }
                case "tier": o.Tier = I(val); break;
                case "day": o.Day = I(val); break;
                // round ie1: "carry" (the fractional bundles carried to the next day) is no longer read - every yield is whole now.
                // "last" was fractions before it: an old float is read rounded down (the next rollover writes it anew)
                case "last": WholeFloors(val, o.LastYield); break;
                case "pending":
                {
                    string[] f = val.Split(',');
                    for (int i = 0; i < ScNames.ResourceCount && i < f.Length; i++) o.Pending[i] = Math.Max(0, I(f[i]));
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
                    for (int i = 0; i < ScNames.ResourceCount && i < f.Length; i++) o.Stash[i] = Math.Max(0, I(f[i]));
                    break;
                }
                case "produced": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) o.Produced = WholeFloor(f); break; }   // round ie1: an old float, rounded down
                case "levelday": o.LevelDay = I(val); break;
                case "visit": o.LastVisitDay = I(val); break;
                // E10: the hostile enclave behind the open raider demand or raid, and how many of them died raiding it so far
                case "raidenc": { int id = I(val); o.RaidEnclave = id > 0 ? id : -1; break; }
                case "raidlost": o.RaidLost = Math.Max(0, I(val)); break;
            }
        }

        private static void CommitEnclave(ref Enclave en)
        {
            if (en != null)
            {
                if (en.Id > 0 && en.Size.x > 0 && en.Size.z > 0 && !string.IsNullOrEmpty(en.Prefab)) Enclaves.Add(en); else degraded = true;
            }
            en = null;
        }

        private static void ParseEnclave(Enclave e, string key, string val)
        {
            switch (key)
            {
                case "id": e.Id = I(val); break;
                case "name": e.Name = val; break;
                case "poi": { Vector3i v; if (TryVec(val, out v)) e.Poi = v; break; }
                case "size": { Vector3i v; if (TryVec(val, out v)) e.Size = v; break; }
                case "prefab": e.Prefab = val; break;
                case "poiname": e.PoiName = val; break;
                case "biome": e.Biome = val; break;
                case "row": e.Row = val; break;
                case "tier": e.Tier = I(val); break;
                case "hostile": e.Hostile = val == "1"; break;
                // round MP6 (part relations): the section's relation keys are community 0's row (another community's: rel=)
                case "standing": e.Rels.Zero.Standing = Clamp(I(val), -100, 100); break;
                case "known": e.Rels.Zero.Known = val == "1"; break;
                case "knownday": e.Rels.Zero.KnownDay = I(val); break;
                case "seeded":
                {
                    // day:mode
                    int colon = val.IndexOf(':');
                    e.SeededDay = I(colon > 0 ? val.Substring(0, colon) : val);
                    if (colon > 0) e.Mode = val.Substring(colon + 1);
                    break;
                }
                // E2: the group's kind, the day it turned hostile (-1 = never), whether its people were made
                case "kind": e.Kind = val.Trim().ToLowerInvariant(); break;
                case "turned": { int d = I(val); e.Rels.Zero.TurnedHostile = d >= 0; e.Rels.Zero.HostileDay = d; break; }
                case "people": e.PeopleRolled = val == "1"; break;
                // E3: the leader's trade stock (packed, read back by ScEnclaveDeals.DecodeStock), the last recruit's day
                case "stock": e.Stock = val.Trim(); break;
                case "recruited": e.LastRecruitDay = I(val); break;
                // E4: the first day the daily roll may give them a new request
                case "nextreq": e.Rels.Zero.NextRequestDay = I(val); break;
                // E5: the kind of a request done while the player was away, thanked for at the next talk
                case "thanks": e.Rels.Zero.Thanks = val.Trim().ToLowerInvariant(); break;
                // E6: the group's benefit (an unknown one is set again at OnGameStart), the next ally reveal's day
                case "benefit": e.Benefit = val.Trim().ToLowerInvariant(); break;
                case "allyreveal": e.Rels.Zero.AllyRevealDay = Math.Max(0, I(val)); break;
                // E7: the next gift's day, the next ally barter's day (request clock)
                case "gift": e.Rels.Zero.GiftDay = Math.Max(0, I(val)); break;
                case "barter": e.Rels.Zero.BarterDay = Math.Max(0, I(val)); break;
                // E9: the next radio call's day (request clock); the call that is on: kind:at:pid:until (an unknown kind is dropped)
                case "callday": e.Rels.Zero.CallDay = Math.Max(0, I(val)); break;
                case "call":
                {
                    string[] f = val.Split(':');
                    string kind = f[0].Trim().ToLowerInvariant();
                    ulong at = 0UL, until = 0UL;
                    if (f.Length < 4 || (kind != ScEncCalls.Fighter && kind != ScEncCalls.Drop)
                        || !ulong.TryParse(f[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out at)
                        || !ulong.TryParse(f[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out until)) break;
                    EncRel r0 = e.Rels.Zero;      // round MP6 (part relations): community 0's row
                    r0.CallKind = kind;
                    r0.CallAt = at;
                    r0.CallPid = I(f[2]);
                    r0.CallUntil = kind == ScEncCalls.Fighter ? until : 0UL;
                    break;
                }
                // E10: the day they may raid again (request clock); people a held raid cost them before they were made
                case "raidday": e.RaidDay = Math.Max(0, I(val)); break;
                case "raidlost": e.RaidLost = Math.Max(0, I(val)); break;
                // E11: their last move of their own accord (day:x,y,z:dead:name - the request-clock day, the building they left,
                // their dead then; an unreleased build wrote day:x,y,z:name, read with the dead not known), the day of their last
                // death and the day an infestation has been next door since (request clock)
                case "moved":
                {
                    string[] f = val.Split(new[] { ':' }, 4);
                    Vector3i v;
                    int dead = -1;
                    if (f.Length < 2 || !TryVec(f[1], out v)) break;
                    e.MovedDay = Math.Max(0, I(f[0]));
                    e.MovedFrom = v;
                    bool counted = f.Length > 3 && int.TryParse(f[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out dead) && dead >= 0;
                    e.MovedDead = counted ? dead : -1;
                    e.MovedFromName = counted ? f[3] : f.Length > 2 ? string.Join(":", f, 2, f.Length - 2) : "";
                    break;
                }
                case "lossday": e.LossDay = Math.Max(0, I(val)); break;
                case "nestday": e.NestDay = Math.Max(0, I(val)); break;
                // round MP6 (part relations): another community's relation to them (the keys above are community 0's)
                case "rel": ScRelations.ParseEnclaveRow(e, val); break;
                case "relbase": ScRelations.ParseEnclaveBase(e, val); break;     // round en1 (review): EN1-M1 - a leaver's copy's base
                // round en1 (review): EN1-n3 - a client's snapshot says an enclave this load did not find (the server's Lost; never in a save)
                case "lost": e.Lost = val.Trim() == "1"; break;
            }
        }

        private static void CommitPerson(ref EncPerson ep)
        {
            if (ep != null)
            {
                if (ep.Id > 0 && ep.EnclaveId > 0 && !string.IsNullOrEmpty(ep.Who.Class)) EncPeople.Add(ep); else degraded = true;
            }
            ep = null;
        }

        /// <summary>One [encperson] line: the person's own keys, else one of the member keys the person's Member is written with.</summary>
        private static void ParsePerson(EncPerson ep, string key, string val)
        {
            switch (key)
            {
                case "pid": ep.Id = I(val); break;
                case "enclave": ep.EnclaveId = I(val); break;
                case "role": ep.Role = val.Trim().ToLowerInvariant() == EncPerson.Leader ? EncPerson.Leader : EncPerson.MemberRole; break;
                case "state": ep.State = val.Trim().ToLowerInvariant(); break;
                case "died": ep.DiedDay = I(val); break;
                case "spot": { Vector3i v; if (TryVec(val, out v)) { ep.Home = v; ep.HasHome = true; } break; }
                case "yaw": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) ep.Yaw = f; break; }
                case "health": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) ep.Health = Math.Max(0f, Math.Min(1f, f)); break; }
                case "ammostart": ep.AmmoStart = ScAmmo.Decode(val, out ep.AmmoStartMags); break;
                case "enlisted":
                {
                    // E3: player:until:body (OnRegistryChanged makes them home and clears that body's hire)
                    string[] f = val.Split(':');
                    ulong u;
                    if (f.Length >= 1) ep.EnlistedBy = I(f[0]);
                    if (f.Length >= 2 && ulong.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out u)) ep.EnlistedUntil = u;
                    if (f.Length >= 3) ep.EnlistedBody = I(f[2]);
                    break;
                }
                case "enlistedid": ep.EnlistedById = val; break;   // round MP7 (part F): the enlister's platform id (a load sends them home anyway)
                default: ParseMember(ep.Who, key, val); break;
            }
        }

        /// <summary>A person whose enclave is not in the file (an edit, a crash between saves) is dropped: a degraded load.</summary>
        private static void DropStrayPeople()
        {
            if (EncPeople.Count == 0) return;
            var ids = new HashSet<int>();
            foreach (Enclave e in Enclaves) ids.Add(e.Id);
            int n = EncPeople.RemoveAll(ep => !ids.Contains(ep.EnclaveId));
            if (n <= 0) return;
            degraded = true;
            ScLog.Warn("registry: " + n + " enclave person record(s) whose enclave is missing dropped");
        }

        private static void CommitRequest(ref EnclaveRequest rq)
        {
            if (rq != null)
            {
                if (rq.Id > 0 && rq.EnclaveId > 0 && !string.IsNullOrEmpty(rq.Kind)) LoadTarget(rq.CommunityId).EncRequests.Add(rq); else degraded = true;   // round MP6 (core)
            }
            rq = null;
        }

        /// <summary>One [encrequest] line (E4). E5's keys are read here already, so a save of its kinds loads its fields whole.</summary>
        private static void ParseRequest(EnclaveRequest r, string key, string val)
        {
            switch (key)
            {
                case "cid": r.CommunityId = I(val); break;              // round MP6 (core): its community (none: 0)
                case "id": r.Id = I(val); break;
                case "enclave": r.EnclaveId = I(val); break;
                case "kind": r.Kind = val.Trim().ToLowerInvariant(); break;
                case "state": r.State = val.Trim().ToLowerInvariant(); break;
                case "offered": r.OfferDay = I(val); break;
                case "lapse": r.LapseDay = I(val); break;
                case "accepted": r.AcceptDay = I(val); break;
                case "due": r.DueDay = I(val); break;
                case "res":
                {
                    // resource:units
                    string[] f = val.Split(':');
                    Resource res;
                    if (f.Length >= 1 && ScNames.TryResource(f[0], out res)) r.Res = res;
                    if (f.Length >= 2) r.Units = Clamp(I(f[1]), 0, 3);
                    break;
                }
                case "quest": r.QuestCode = I(val); break;
                case "acceptedby": r.AcceptedBy = val.Trim(); break;       // round MP4: whose journal holds its quest (absent: the host's)
                case "target": { Vector3i v; if (TryVec(val, out v)) { r.Target = v; r.HasTarget = true; } break; }
                case "infest": r.InfestationId = I(val); break;
                case "person": r.PersonId = I(val); break;
                case "deadline": { ulong u; if (ulong.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out u)) r.Deadline = u; break; }
                // E5: where the request stands, the building's name, where a missing survivor waits or lies, the escort, the siege
                case "stage": r.Stage = Clamp(I(val), 0, 9); break;
                case "targetname": r.TargetName = val; break;
                case "spot": { Vector3i v; if (TryVec(val, out v)) { r.Spot = v; r.HasSpot = true; } break; }
                case "spotyaw": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) r.SpotYaw = f; break; }
                case "escort":
                {
                    // player:body (a load ends the escort: OnRegistryChanged clears that body's hire)
                    string[] f = val.Split(':');
                    if (f.Length >= 1) r.EscortBy = I(f[0]);
                    if (f.Length >= 2) r.EscortBody = I(f[1]);
                    break;
                }
                case "siege":
                {
                    // size:spawned:killed:started
                    string[] f = val.Split(':');
                    ulong u;
                    if (f.Length >= 1) r.SiegeSize = Math.Max(0, I(f[0]));
                    if (f.Length >= 2) r.SiegeSpawned = Math.Max(0, I(f[1]));
                    if (f.Length >= 3) r.SiegeKilled = Math.Max(0, I(f[2]));
                    if (f.Length >= 4 && ulong.TryParse(f[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out u)) r.SiegeStarted = u;
                    break;
                }
            }
        }

        /// <summary>A request whose enclave is not in the file is dropped: a degraded load (its journal quest closes at the first tick).</summary>
        private static void DropStrayRequests()
        {
            var ids = new HashSet<int>();
            foreach (Enclave e in Enclaves) ids.Add(e.Id);
            int n = 0;
            foreach (CommunityState cs in ScCommunities.All)          // round MP6 (core): every community's requests
                if (cs.EncRequests.Count > 0) n += cs.EncRequests.RemoveAll(r => !ids.Contains(r.EnclaveId));
            if (n <= 0) return;
            degraded = true;
            ScLog.Warn("registry: " + n + " enclave request record(s) whose enclave is missing dropped");
        }

        private static void CommitBand(ref Band bd)
        {
            if (bd != null)
            {
                if (bd.Id > 0 && bd.Size > 0 && (bd.Kind == Band.Drifters || bd.Kind == Band.Raiders)) Bands.Add(bd); else degraded = true;
            }
            bd = null;
        }

        /// <summary>One [band] line (E13a, ScBands): the trip, the walk (at, depart, via, toat), meeting, trust, trouble.</summary>
        private static void ParseBand(Band b, string key, string val)
        {
            switch (key)
            {
                case "id": b.Id = I(val); break;
                case "kind": b.Kind = val.Trim().ToLowerInvariant(); break;
                case "name": b.Name = val; break;
                case "size": b.Size = Clamp(I(val), 0, 16); break;
                case "made": b.PeopleMade = val == "1"; break;
                case "from": { Vector3i v; if (TryVec(val, out v)) b.From = v; break; }
                case "fromname": b.FromName = val; break;
                case "to": { Vector3i v; if (TryVec(val, out v)) b.To = v; break; }
                case "toname": b.ToName = val; break;
                case "toenc": b.ToEnclave = Math.Max(0, I(val)); break;
                case "toat": { Vector3i v; if (TryVec(val, out v)) b.ToAt = v; break; }
                case "at": { Vector3i v; if (TryVec(val, out v)) b.At = v; break; }
                case "depart": { ulong u; if (ulong.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out u)) b.Depart = u; break; }
                case "via": { Vector3i v; if (TryVec(val, out v)) { b.Via = v; b.HasVia = true; } break; }
                case "day": b.StartDay = Math.Max(0, I(val)); break;
                // round MP6 (part relations): known=, knownday=, trust= are community 0's row (another community's: rel=)
                case "known": b.Rels.Zero.Known = val == "1"; break;
                case "knownday": b.Rels.Zero.KnownDay = I(val); break;
                case "seed": b.Seed = I(val); break;
                case "trust": b.Rels.Zero.Trust = Clamp(I(val), -1, 1); break;
                case "trouble": b.Trouble = Clamp(I(val), 0, 3); break;
                case "arrived": b.Arrived = val == "1"; break;
                case "rel": ScRelations.ParseBandRow(b, val); break;
                case "relbase": ScRelations.ParseBandBase(b, val); break;        // round en1 (review): EN1-M1
            }
        }

        private static void CommitBandPerson(ref BandPerson bp)
        {
            if (bp != null)
            {
                if (bp.Id > 0 && bp.BandId > 0 && !string.IsNullOrEmpty(bp.Who.Class)) BandPeople.Add(bp); else degraded = true;
            }
            bp = null;
        }

        /// <summary>One [bandperson] line (E13a): the person's own keys, else one of the member keys the person's Member is written with.</summary>
        private static void ParseBandPerson(BandPerson bp, string key, string val)
        {
            switch (key)
            {
                case "pid": bp.Id = I(val); break;
                case "band": bp.BandId = I(val); break;
                case "role": bp.Role = val.Trim().ToLowerInvariant() == BandPerson.Leader ? BandPerson.Leader : BandPerson.MemberRole; break;
                case "state": bp.State = val.Trim().ToLowerInvariant() == BandPerson.StateDead ? BandPerson.StateDead : BandPerson.StateAlive; break;
                case "died": bp.DiedDay = I(val); break;
                case "health": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) bp.Health = Math.Max(0f, Math.Min(1f, f)); break; }
                default: ParseMember(bp.Who, key, val); break;
            }
        }

        /// <summary>A band person whose band is not in the file is dropped: a degraded load.</summary>
        private static void DropStrayBandPeople()
        {
            if (BandPeople.Count == 0) return;
            var ids = new HashSet<int>();
            foreach (Band b in Bands) ids.Add(b.Id);
            int n = BandPeople.RemoveAll(bp => !ids.Contains(bp.BandId));
            if (n <= 0) return;
            degraded = true;
            ScLog.Warn("registry: " + n + " band person record(s) whose band is missing dropped");
        }

        private static void CommitInfestation(ref Infestation inf)
        {
            if (inf != null)
            {
                if (inf.Id > 0 && inf.Size.x > 0 && inf.Size.z > 0 && !string.IsNullOrEmpty(inf.Prefab)) Infestations.Add(inf); else degraded = true;
            }
            inf = null;
        }

        private static void ParseInfestation(Infestation inf, string key, string val)
        {
            switch (key)
            {
                case "id": inf.Id = I(val); break;
                case "poi": { Vector3i v; if (TryVec(val, out v)) inf.Poi = v; break; }
                case "size": { Vector3i v; if (TryVec(val, out v)) inf.Size = v; break; }
                case "prefab": inf.Prefab = val; break;
                case "poiname": inf.PoiName = val; break;
                case "tier": inf.Tier = I(val); break;
                case "start": inf.StartDay = I(val); break;
                case "how": inf.How = val; break;
                case "source": inf.Source = I(val); break;
                // round MP6 (part relations): known=, knownday= are community 0's row (another community's: rel=)
                case "known": inf.Rels.Zero.Known = val == "1"; break;
                case "knownday": inf.Rels.Zero.KnownDay = I(val); break;
                case "rel": ScRelations.ParseInfestationRow(inf, val); break;
                case "special":
                {
                    // none | owed | pending:x,y,z (the room bringing it) | met:class; a pending one without its room is owed
                    int colon = val.IndexOf(':');
                    string kind = (colon > 0 ? val.Substring(0, colon) : val).Trim().ToLowerInvariant();
                    string arg = colon > 0 ? val.Substring(colon + 1).Trim() : "";
                    Vector3i v;
                    if (kind == "owed") inf.Special = SpecialState.Owed;
                    else if (kind == "pending" && TryVec(arg, out v)) { inf.Special = SpecialState.Pending; inf.SpecialHost = v; }
                    else if (kind == "pending") inf.Special = SpecialState.Owed;
                    else if (kind == "met") { inf.Special = SpecialState.Met; inf.SpecialClass = arg; }
                    else inf.Special = SpecialState.None;
                    break;
                }
                case "spread": inf.LastSpreadDay = I(val); break;
                case "rearm":
                    // x,y,z;x,y,z - the rooms it re-armed that have not rolled again yet
                    foreach (string part in val.Split(';'))
                    {
                        Vector3i v;
                        if (TryVec(part, out v) && !inf.Rearmed.Contains(v)) inf.Rearmed.Add(v);
                    }
                    break;
            }
        }

        /// <summary>Round ie1: whole units from a list an older build wrote as fractions ("1.2,0,0.35"), each rounded down.</summary>
        private static void WholeFloors(string val, int[] into)
        {
            string[] f = val.Split(',');
            for (int i = 0; i < into.Length && i < f.Length; i++)
            {
                float v;
                if (float.TryParse(f[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) into[i] = WholeFloor(v);
            }
        }

        /// <summary>Round ie1: an old float rounded down, never below 0.</summary>
        private static int WholeFloor(float v)
        {
            if (float.IsNaN(v) || v <= 0f) return 0;
            return v >= int.MaxValue ? int.MaxValue : (int)Math.Floor(v);
        }

        private static void CommitContact(ref Contact ct)
        {
            if (ct != null && ct.Id > 0 && !string.IsNullOrEmpty(ct.Class)) LoadTarget(ct.CommunityId).Contacts.Add(ct);   // round MP6 (core)
            ct = null;
        }

        private static void ParseContact(Contact ct, string key, string val)
        {
            switch (key)
            {
                case "cid": ct.CommunityId = I(val); break;             // round MP6 (core): its community (none: 0)
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
                // E12: a member written as a resident with arrive= is still on the way (ScEncJoin)
                if (m.ArriveAt > 0UL && m.Status == MemberStatus.Resident) m.Status = MemberStatus.Arriving;
                // round op1: written as a resident too - one on the road to or from an outpost (transit= gave them a leg), and one
                // posted there whose body is the snapshot (posted= and postsnap=)
                else if (m.Status == MemberStatus.Resident && m.RunLeg != RunLeg.None && m.OutpostId >= 0)
                {
                    m.Status = MemberStatus.Transit;
                    m.RunEndDay = GameUtils.WorldTimeToDays(m.LegEnd);
                }
                else if (m.Status == MemberStatus.Resident && m.OutpostId >= 0 && !string.IsNullOrEmpty(m.Snap))
                {
                    m.Status = MemberStatus.Garrison;
                    m.Offline = true;
                }
                else if (m.Status == MemberStatus.Resident)
                {
                    // a resident has none of these (a half-read line: a resident at home, ScMissing's Call back if bodiless)
                    m.OutpostId = -1;
                    m.RunLeg = RunLeg.None;
                    m.Snap = null;
                    m.Offline = false;
                }
                if (m.Id <= 0) degraded = true;
                else
                {
                    CommunityState target = LoadTarget(m.CommunityId);                       // round MP6 (core): her community's
                    // round MP6 (core fix): a member id already in another community (entity ids are world-unique: a hand edit, or a
                    // file written while a scoped removal missed her) - the first one read is kept, this copy dropped (review C-M2).
                    // A second record under one id in the same community replaces the first, as before MP6
                    int other = ScCommunities.CommunityOfMember(m.Id);
                    if (other >= 0 && other != target.Id)
                    {
                        degraded = true;
                        ScLog.Warn("registry: member " + m.Name + " (id " + m.Id + ") is in community " + other + " and again in community " + target.Id
                                   + " - the copy in community " + target.Id + " is dropped");
                    }
                    else target.Members[m.Id] = m;
                }
                m = null;
            }
            if (p != null)
            {
                if (p.Id > 0) LoadTarget(p.CommunityId).Prospects[p.Id] = p; else degraded = true;   // round MP6 (core)
                p = null;
            }
        }

        /// <summary>
        /// Round MP6 (core): the community a loaded record goes to - its cid= (none: community 0), made when the text has no
        /// [community] for it yet (an edit; the community is then empty but its records stay theirs). A negative id is community 0.
        /// </summary>
        private static CommunityState LoadTarget(int cid)
        {
            if (cid < 0)
            {
                degraded = true;
                ScLog.Warn("registry: a record with community id " + cid + " - put in community 0");
                cid = ScPlayers.SharedCommunity;
            }
            return ScCommunities.Ensure(cid);
        }

        /// <summary>
        /// Round MP6 (core): one [community] section's lines, kept until its end, into the community its cid= names (none:
        /// community 0 - every old file's one community; several old sections all go into it, as they always merged). Returns the
        /// number of unreadable lines.
        /// </summary>
        private static int CommitCommunity(ref List<KeyValuePair<string, string>> lines, int cid)
        {
            if (lines == null) return 0;
            List<KeyValuePair<string, string>> l = lines;
            lines = null;
            Community c = LoadTarget(cid).Community;
            int bad = 0;
            foreach (KeyValuePair<string, string> kv in l)
            {
                try { ParseCommunity(c, kv.Key, kv.Value); }
                catch (Exception) { bad++; }
            }
            return bad;
        }

        /// <summary>
        /// Round MP6 (core): the [community] keys that are the world's (the id counters, the seeding, the request clock, remains,
        /// refills, the entity bookkeeping, streamer names): an old file's [community] held them, and a client's snapshot (the old
        /// layout) still does. They are routed to the world record (ParseWorld) by name - no other [community] key shares one.
        /// </summary>
        private static bool IsWorldKey(string key)
        {
            switch (key)
            {
                case "contactid": case "outpostid": case "enclaveid": case "enclaveseed": case "infestid": case "encpersonid":
                case "bandid": case "bandpersonid": case "encrequestid": case "encreqday": case "encremains": case "encrefill":
                case "cleanup": case "doomed": case "streamname":
                case ScLookKeep.Key:        // round lk1: keptlook= (a legacy-layout text's [community] carries it too)
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Round MP6 (core): one [world] line - the world record's keys (as ParseCommunity read them before MP6), plus lastday=
        /// (the world daily's day guard) and communityid= (the community id counter).
        /// </summary>
        private static void ParseWorld(string key, string val)
        {
            ScWorldState ws = WorldState;
            switch (key)
            {
                case "lastday": ws.LastDay = I(val); break;
                case "communityid": ws.NextCommunityId = Math.Max(0, I(val)); break;
                // round MP7 (core): the save's community mode (fixed when its first community record started), and a retired record's id
                case "mode":
                {
                    ScCommunityMode mode;
                    if (!ScMembership.TryParseMode(val, out mode)) throw new FormatException("mode: " + val);
                    ws.Mode = mode;
                    ws.ModeLocked = true;
                    ws.ModeWritten = true;
                    break;
                }
                case "retired": { int rid = I(val); if (rid > 0) ws.Retired.Add(rid); break; }
                case "noautohome": ws.ZeroNoAutoHome = val.Trim() == "1"; break;      // round db1 (review): m5
                case "contactid": ws.NextContactId = I(val); break;
                case "outpostid": ws.NextOutpostId = I(val); break;
                // enclaves (E1): the id counter, the seeding stamp ver:day:mode:seed:count
                case "enclaveid": ws.NextEnclaveId = I(val); break;
                case "enclaveseed":
                {
                    string[] f = val.Split(':');
                    if (f.Length >= 5 && I(f[0]) >= 1)
                    {
                        ws.EnclaveSeeded = true;
                        ws.EnclaveSeedDay = I(f[1]);
                        ws.EnclaveSeedMode = f[2];
                        ws.EnclaveSeed = I(f[3]);
                        ws.EnclaveSeedCount = Math.Max(0, I(f[4]));
                    }
                    break;
                }
                // infestations (I1): the id counter
                case "infestid": ws.NextInfestationId = I(val); break;
                // enclave people (E2): the person id counter
                case "encpersonid": ws.NextEncPersonId = I(val); break;
                // bands on the road (E13a): the band and band person id counters
                case "bandid": ws.NextBandId = Math.Max(0, I(val)); break;
                case "bandpersonid": ws.NextBandPersonId = Math.Max(0, I(val)); break;
                // enclave requests (E4): the id counter, the request clock's last day
                case "encrequestid": ws.NextEncRequestId = I(val); break;
                case "encreqday": ws.EncRequestDay = I(val); break;
                // E5: remains still to take out of the world
                case "encremains":
                    ws.EncRemains.Clear();
                    foreach (string part in val.Split(';')) { Vector3i v; if (TryVec(part, out v) && !ws.EncRemains.Contains(v)) ws.EncRemains.Add(v); }
                    break;
                // E6: the refills owed (day:x,y,z:name;..)
                case "encrefill":
                    ws.EncRefills.Clear();
                    foreach (string part in val.Split(';'))
                    {
                        string[] f = part.Split(new[] { ':' }, 3);
                        Vector3i v;
                        if (f.Length >= 2 && TryVec(f[1], out v)) ws.EncRefills.Add(new EncRefill { Day = Math.Max(0, I(f[0])), Poi = v, Name = f.Length > 2 ? f[2] : "" });
                    }
                    break;
                case "cleanup":
                    foreach (string part in val.Split(',')) { int id = I(part); if (id > 0) Cleanup.Add(id); }
                    break;
                case "doomed":
                    foreach (string part in val.Split(',')) { int id = I(part); if (id > 0) Doomed.Add(id); }
                    break;
                // round 30: a streamer name given out in this save (ScStreamerNames), one line each, in the order given
                case "streamname":
                    if (val.Length > 0) ws.StreamerNamesUsed.Add(val);
                    break;
                // round lk1: the look of a body the world saves with no record of SC's (ScLookKeep), one line each
                case ScLookKeep.Key:
                    ScLookKeep.Parse(val);
                    break;
            }
        }

        /// <summary>Round MP6 (core): one [membership] line, player=platformid:cid (the id is split off at the last ':').</summary>
        private static void ParseMembership(string key, string val)
        {
            if (ScJoins.Parse(key, val)) return;       // round MP7 (part AP): joined= (how a player joined) and optout= (Leave sticks)
            if (key != "player") return;
            int colon = val.LastIndexOf(':');
            if (colon <= 0) throw new FormatException("membership: " + val);
            string id = val.Substring(0, colon).Trim();
            int cid;
            if (id.Length == 0 || !int.TryParse(val.Substring(colon + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out cid) || cid < 0)
                throw new FormatException("membership: " + val);
            // round MP7 (core): a line for community 0 is kept - PerPlayer/PerParty list its players (nobody is in it by default there);
            // a Shared save never writes one (community 0 is everyone not listed), so an old save reads as before
            WorldState.Membership[id] = cid;
        }

        /// <summary>
        /// Round MP6 (core), after a parse: an old file (or a client's snapshot) had no [world], so the world daily's day guard is
        /// its community's; the community id counter is past every community there is; a membership naming a community the text
        /// does not have is dropped (that player is in community 0 again).
        /// </summary>
        private static void AfterParse(bool sawWorld, int defaultCid)
        {
            ScWorldState ws = WorldState;
            // round MP6 (core fix): "its community" is the text's default one (community 0, but a client's mirrored community)
            if (!sawWorld) ws.LastDay = (ScCommunities.ById(defaultCid) ?? ScCommunities.Zero).Community.LastDay;
            foreach (CommunityState cs in ScCommunities.All)
                if (cs.Id > ws.NextCommunityId) ws.NextCommunityId = cs.Id;
            // round MP7 (core): retired ids stay taken; and a text with no mode= whose community is going (a home, an owner, members,
            // several communities, players listed) is a save from before MP7 - Shared for good (decision 2), written nowhere (its text
            // stays as it was) until an admin switches it once (ScMembership.SwitchMode). One with nothing going yet: the config's
            // mode stays live until its first community record starts (ScMembership.LockMode)
            foreach (int rid in ws.Retired)
                if (rid > ws.NextCommunityId) ws.NextCommunityId = rid;
            if (!ws.ModeLocked && AnyCommunityStarted())
            {
                ws.ModeLocked = true;
                ws.Mode = ScCommunityMode.Shared;
                ws.ModeWritten = false;
            }
            if (ws.Membership.Count == 0) return;
            var gone = new List<string>();
            foreach (KeyValuePair<string, int> kv in ws.Membership)
                if (ScCommunities.ById(kv.Value) == null) gone.Add(kv.Key);
            if (gone.Count == 0) return;
            foreach (string id in gone) ws.Membership.Remove(id);
            degraded = true;
            ScLog.Warn("registry: " + gone.Count + " player(s) recorded in a community the file does not have - back in community 0 (Shared; in PerPlayer/PerParty in none: their own relations at their next spawn)");
        }

        /// <summary>
        /// Round MP7 (core), AfterParse: the text's community is going - several communities, players listed, or a community with a
        /// home, an owner or members. A save from before MP7 that is so stays Shared (decision 2). Round MP7 (core review): m5 - also a
        /// community that holds anything with no home (favors, requests, contacts, stock, a saved relation: ScMembership.HoldsNothing).
        /// </summary>
        private static bool AnyCommunityStarted()
        {
            if (ScCommunities.Count > 1 || WorldState.Membership.Count > 0) return true;
            foreach (CommunityState cs in ScCommunities.All)
            {
                Community c = cs.Community;
                if (!string.IsNullOrEmpty(c.OwnerId) || !string.IsNullOrEmpty(c.Owner) || !ScMembership.HoldsNothing(cs)) return true;
            }
            return false;
        }

        private static void ParseCommunity(Community c, string key, string val)
        {
            // round MP6 (core): the target is the section's community (CommitCommunity); the world's keys go to the world record
            if (IsWorldKey(key)) { ParseWorld(key, val); return; }
            switch (key)
            {
                case "name": c.Name = val; break;                                 // round MP6 (core): the community's name
                case "personal": { CommunityState pcs = ScCommunities.ById(c.Id); if (pcs != null) pcs.Personal = val == "1"; break; }   // round MP7 (core): a personal record
                case "home": { Vector3i v; if (TryVec(val, out v)) { c.Home = v; c.HasHome = true; } break; }
                case "owner": c.Owner = val; break;
                case "ownerid": c.OwnerId = val; break;                          // round MP0
                case "morale": c.Morale = Clamp(I(val), 0, 100); break;
                case "lastday": c.LastDay = I(val); break;
                case "lowmorale": c.LowMoraleDays = I(val); break;
                case "threat": c.ThreatMemberId = I(val); break;
                case "hungry": c.Hungry = val == "1"; break;
                case "deathpenalty": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c.DeathPenalty = f; break; }
                // "foodcarry" is no longer read (round ie1: every daily need is whole, nothing carried); nor is "ammocarry" (the old
                // daily ammo upkeep's fraction): there is no upkeep (decision D3)
                case "ammoopen": c.AmmoOpenPoints = Math.Max(0, I(val)); break;
                case "ammoopened": c.AmmoOpened = Math.Max(0, I(val)); break;
                case "ammoshort": c.AmmoShort = val == "1"; break;
                case "medsrestocked": c.MedsRestocked = Math.Max(0, I(val)); break;   // round md1: today's restocks (the ledger)
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
                case "radios":
                    c.Radios.Clear();
                    foreach (string part in val.Split(';')) { Vector3i v; if (part.Length > 0 && TryVec(part, out v) && !c.Radios.Contains(v)) c.Radios.Add(v); }
                    break;
                case "radioday": c.RadioOnlineDay = I(val); break;
                // "upkeepdebt" / "hauldebt" are no longer read (round ie1: no debt; an old save's is forgiven, the flags below stay)
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
                case "noiseev": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c.NoiseEvents = Math.Max(0f, f); break; }
                case "attention": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c.Attention = Math.Max(0f, Math.Min(100f, f)); break; }
                case "noisestamp": { ulong u; if (ulong.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out u)) c.NoiseStamp = u; break; }
                case "seentime": { ulong u; if (ulong.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out u)) c.SeenTime = u; break; }   // round MP6 (fix H-M2)
                case "baseattack": ParseBaseAttack(c, val); break;
                case "attacknext": { ulong u; if (ulong.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out u)) c.AttackNotBefore = u; break; }
                case "defended": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c.DefenceTriumph = Math.Max(0f, Math.Min(10f, f)); break; }
                case "watchposts":
                    c.WatchPosts.Clear();
                    foreach (string part in val.Split(';')) { Vector3i v; if (part.Length > 0 && TryVec(part, out v) && !c.WatchPosts.Contains(v)) c.WatchPosts.Add(v); }
                    break;
                case "attackwarn": c.AttackWarned = Clamp(I(val), 0, 2); break;
                case "attackaway":
                    {
                        // since:end:base:heard (the away part of the open attack, D4)
                        string[] f = val.Split(':');
                        ulong since, end;
                        if (f.Length >= 4 && ulong.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out since)
                            && ulong.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out end))
                        {
                            c.AttackAwaySince = since;
                            c.AttackAwayEnd = end;
                            c.AttackAwayBase = Math.Max(0, I(f[2]));
                            c.AttackHeard = Clamp(I(f[3]), 0, 3);
                        }
                        break;
                    }
                case "awayused": c.AttackAwayUsed = I(val) != 0; break;
                case "defloss": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c.DefenceLoss = Math.Max(0f, Math.Min(30f, f)); break; }
                case "homedef": c.HomeDefences = Math.Max(0, I(val)); break;
                case "scandef":
                    // cx:cz:n;... (chunks with any trap or turret; the rest of scanmap's chunks have none)
                    c.ScanDefences.Clear();
                    foreach (string part in val.Split(';'))
                    {
                        string[] f = part.Split(':');
                        if (f.Length != 3) continue;
                        int n = I(f[2]);
                        if (n > 0) c.ScanDefences[WorldChunkCache.MakeChunkKey(I(f[0]), I(f[1]))] = n;
                    }
                    break;
                case "bmhome": c.BloodMoonHomeDay = Math.Max(0, I(val)); break;
                case "awaynews":
                    c.AwayNews.Clear();
                    foreach (string part in val.Split(';')) if (part.Length > 0 && c.AwayNews.Count < 4) c.AwayNews.Add(part);
                    break;
                // "harvestcarry" is no longer read (round ie1: the garden's harvest is whole)
                // round wl: water, the facilities, the round of drinks (keys an older build ignores)
                case "thirsty": c.Thirsty = val == "1"; break;
                // "watercarry" is no longer read (round ie1)
                case "homewater": c.HomeWater = Math.Max(0, I(val)); break;
                case "scanwater":
                    // cx:cz:n;... (zone chunks with any well or dew collector)
                    c.ScanWater.Clear();
                    foreach (string part in val.Split(';'))
                    {
                        string[] f = part.Split(':');
                        if (f.Length != 3) continue;
                        int n = I(f[2]);
                        if (n > 0) c.ScanWater[WorldChunkCache.MakeChunkKey(I(f[0]), I(f[1]))] = n;
                    }
                    break;
                case "dewlevels":
                    // x,y,z:level;...
                    c.DewLevels.Clear();
                    foreach (string part in val.Split(';'))
                    {
                        int colon = part.LastIndexOf(':');
                        Vector3i v;
                        if (colon <= 0 || !TryVec(part.Substring(0, colon), out v)) continue;
                        int lv = I(part.Substring(colon + 1));
                        if (lv > 1) c.DewLevels[v] = lv;
                    }
                    break;
                case "drinks": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c.DrinksLeft = Math.Max(0f, f); break; }
                case "drinksday": c.DrinksDay = I(val); break;
                // round fd1: the feast and rationing
                case "feast": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c.FeastLeft = Math.Max(0f, f); break; }
                case "feastday": c.FeastDay = I(val); break;
                case "feastmorale": c.FeastMorale = I(val); break;
                case "feaststamina": c.FeastStamina = I(val); break;
                case "ration": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f)) c.RationLeft = Math.Max(0f, f); break; }
                case "rationday": c.RationDay = I(val); break;
                // round m1f: the Facilities tab's sites, Lights out, the last attack, the Guard Towers (keys an older build ignores)
                case "scansites": case "lightsout": case "lastattack": case "towers": ScFacilityTab.ParseKey(c, key, val); break;
                case "grudge": ScRelations.ParseGrudge(c, val); break;           // round MP6 (part relations): a player its people hold a grudge against
                // enclaves (E1): the day of the next radio reveal (round MP6 (core): the id counter and the seeding are ParseWorld's)
                case "enclaveradio": c.EnclaveRadioDay = I(val); break;
                // P2: the world's day of the last call for help and how many came that day
                case "encoffers":
                {
                    string[] dn = val.Split(':');
                    if (dn.Length == 2) { c.EncOfferDay = I(dn[0]); c.EncOffersToday = Math.Max(0, I(dn[1])); }
                    break;
                }
                // E5: the world time today's siege call is due (0: none) (round MP6 (core): the remains are ParseWorld's)
                case "encsiegeat": { ulong u; if (ulong.TryParse(val.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out u)) c.EncSiegeAt = u; break; }
                // E6: the Enclave lost morale part (fades a point a day) (round MP6 (core): the refills owed are ParseWorld's)
                case "encloss": { float f; if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f) && !float.IsNaN(f)) c.EnclaveLoss = Math.Max(0f, Math.Min(ScEncAllies.MaxLoss, f)); break; }
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
            }
        }

        /// <summary>
        /// Round fx1: the scale the person block being read wrote its Shooting XP in (shootxp=, written before skills=; 1 for a
        /// block from before the round). Saved XP is rescaled to today's ShootingXpMult, so a survivor's progress toward the
        /// next star stays where it was when the thresholds change.
        /// </summary>
        private static int loadShootScale = 1;

        private static void ParseMember(Member m, string key, string val)
        {
            switch (key)
            {
                case "cid": m.CommunityId = I(val); break;              // round MP6 (core): her community (none: 0)
                case "id": m.Id = I(val); break;
                case "class": m.Class = val; break;
                case "name": m.Name = val; break;
                case "leader": m.LeaderId = val; break;                          // round MP0
                case "born": { ulong b; if (ulong.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out b)) m.Born = b; break; }
                case "status": m.Status = (MemberStatus)Enum.Parse(typeof(MemberStatus), val, true); break;
                case "post": { Vector3i v; if (TryVec(val, out v)) m.Post = v; break; }
                case "join": m.JoinDay = I(val); break;
                case "mood": m.Mood = Clamp(I(val), 0, 100); break;
                case "injured": m.Injured = val == "1"; break;
                case "meds": { ulong u; if (ulong.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out u)) m.MedsAt = u; break; }   // round md1 (ScMedic.Restock)
                case "bond": m.Bond = Clamp(I(val), -100, 100); break;   // round U4 (ScBond); the cap applies where it is used
                case "bondkills":   // the Fable review of U4 (finding 2): day:count of her kill Bond (BondKillPerDay)
                    {
                        string[] dk = val.Split(':');
                        if (dk.Length == 2) { m.BondKillDay = I(dk[0]); m.BondKills = Math.Max(0, I(dk[1])); }
                        break;
                    }
                case "ranged": m.Ranged = val == "1"; break;
                case "missing": m.MissingDays = I(val); break;
                case "pendingunhide": m.PendingUnhide = val == "1"; break;
                case "shootxp": loadShootScale = Clamp(I(val), 1, 100); break;   // round fx1
                case "skills":
                    foreach (string part in val.Split(','))
                    {
                        string[] kv = part.Split(':');
                        Skill s;
                        if (kv.Length == 3 && ScNames.TrySkill(kv[0], out s))
                        {
                            m.Skill(s).Level = Clamp(I(kv[1]), 1, Member.MaxStars);
                            int xp = Math.Max(0, I(kv[2]));
                            int now = ScSkills.XpScale(s), was = s == Skill.Shooting ? loadShootScale : 1;
                            if (now != was) xp = (int)Math.Min(int.MaxValue, Math.Round((double)xp * now / was));   // round fx1
                            m.Skill(s).Xp = xp;
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
#if SC_V34
                case "handed": ScHanded.Read(m, val); break;                           // round n12: one exact handed item per line
                case "wardrobe": ScWardrobe.ReadWorn(m, val); break;                   // round n12 review: pieces the Wardrobe picked
                case "ammopick":                                                         // round U1: the ammo type chosen for her gun
                {
                    int bar = val.IndexOf('|');
                    if (bar > 0 && bar < val.Length - 1) { m.AmmoGun = val.Substring(0, bar); m.AmmoPick = val.Substring(bar + 1); }
                    break;
                }
                case "wq": ScLoadout.ReadQualities(m, val); break;                     // round wq1: the quality her weapons really have
#endif
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
                case "runhurt": m.RunHurt = val == "1"; break;                          // round r2 (ScRunners.TurnHomeHurt)
                case "snap": m.Snap = val; break;
                case "transit": ParseTransit(m, val); break;                           // round op1: on the road to or from an outpost
                case "posted": m.OutpostId = I(val); break;                            // round op1: posted, the body is the snapshot
                case "postsnap": m.Snap = val; break;                                   // round op1: that snapshot, or a traveler's
                case "bodyat": { Vector3i v; if (TryVec(val, out v)) m.BodyAt = v; break; }   // round op1 review: where their body last stood
                case "watch":
                {
                    // post x,y,z : start : prepaid points : home post x,y,z (base defence D3)
                    string[] f = val.Split(':');
                    Vector3i post, home;
                    ulong start;
                    if (f.Length == 4 && TryVec(f[0], out post) && post != Vector3i.zero
                        && ulong.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out start) && TryVec(f[3], out home))
                    {
                        m.WatchPost = post;
                        m.WatchStart = start;
                        m.WatchPrepaid = Math.Max(0, I(f[2]));
                        m.WatchHome = home;
                    }
                    break;
                }
                case "watchrest": { ulong u; if (ulong.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out u)) m.WatchRestUntil = u; break; }
#if SC_V34
                case "craftroles":
                {
                    // C1: mask:Specialty (ScCraft.SyncRoles)
                    string[] f = val.Split(':');
                    Specialty rs;
                    if (f.Length == 2 && ScNames.TrySpecialty(f[1], out rs)) { m.CraftRoles = Clamp(I(f[0]), 0, 15); m.CraftRolesFor = rs; }
                    break;
                }
                case "craftxp":
                {
                    float cx;
                    if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out cx)) m.CraftXpCarry = Math.Max(0f, Math.Min(1000f, cx));
                    break;
                }
                case "runradio": m.RunRadio = val == "1"; break;                       // R2 (ScHandheld)
                case "runtalk": m.RunTalk = Math.Max(0, I(val)); break;                 // R2 (ScRadioTalk)
                case "runjob": ScMining.DecodeJob(m, val); break;                       // round 33c (M33): a mining run
                case "haul":
                    // round 33c (M33): a mining run's haul, carried or waiting to be put away; while OnRun ON A MINING RUN its
                    // total is the carry (runjob= is written before haul=). Round 33 review M1: a member whose last haul still
                    // waits can be out on a supply run - that run's carry= stands, or the haul's item count became stockpile units.
                    m.RunHaul = ScMining.Encode(ScMining.Decode(val));
                    if (m.Status == MemberStatus.OnRun && ScMining.IsDig(m)) m.RunCarry = ScMining.Total(m.RunHaul);
                    break;
#endif
                case "arrive":
                {
                    // E12: at : enclave : x,y,z : name (split on the first three colons; the name may hold more); Commit makes them Arriving
                    string[] f = val.Split(new[] { ':' }, 4);
                    ulong at;
                    Vector3i from;
                    if (f.Length < 3 || !ulong.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out at) || !TryVec(f[2], out from)) break;
                    m.ArriveAt = Math.Max(1UL, at);
                    m.ArriveEnclave = I(f[1]);
                    m.ArriveFrom = from;
                    m.ArriveName = f.Length > 3 ? f[3] : "";
                    break;
                }
            }
        }

        /// <summary>
        /// Round op1: transit=leg:outpost:from x,y,z:to x,y,z:start:end:offline:name (split on the first seven colons; the name
        /// may hold more). Only a whole, sane line sets anything: Commit turns a resident with a leg and an outpost into Transit.
        /// </summary>
        private static void ParseTransit(Member m, string val)
        {
            string[] f = val.Split(new[] { ':' }, 8);
            RunLeg leg;
            Vector3i from, to;
            ulong a, b;
            int id;
            if (f.Length < 7 || !Enum.TryParse(f[0], true, out leg) || (leg != RunLeg.Outbound && leg != RunLeg.Inbound && leg != RunLeg.Back)
                || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || id < 0
                || !TryVec(f[2], out from) || !TryVec(f[3], out to)
                || !ulong.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out a)
                || !ulong.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out b)) return;
            m.RunLeg = leg;
            m.OutpostId = id;
            m.LegFrom = from;
            m.LegTo = to;
            m.LegStart = a;
            m.LegEnd = b;
            m.Offline = f[6] == "1";
            m.RunDestName = f.Length > 7 ? f[7] : "";
        }

        private static void ParseProspect(Prospect p, string key, string val)
        {
            switch (key)
            {
                case "cid": p.CommunityId = I(val); break;              // round MP6 (core): their community (none: 0)
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
                case "started": p.Started = val == "1"; break;
                case "done": p.Done = val == "1"; break;
                case "look": { Look l = p.Look ?? new Look(); if (l.Decode(val)) p.Look = l; break; }
                case "gear": { if (p.Look == null) p.Look = new Look(); p.Look.DecodeGear(val); break; }
                case "loadout": p.Loadout = Loadout.Decode(val); break;
                case "encp": p.EncPersonId = Math.Max(0, I(val)); break;     // E3: an enclave person's favour
            }
        }

        // ---- save ----

        // round db1 (review): m3 - a save hold. While held (HoldSaves), SaveAtomic marks the registry dirty (the clients' copy still
        // follows) and owes one save instead of writing the whole file; the last ReleaseSaves writes it once. ScMembership.Disband
        // holds it, so a disband is one write, not one per member, outpost, request and new record (always in a try/finally).
        private static int saveHold;
        private static bool saveOwed;

        /// <summary>Round db1 (review): m3 - SaveAtomic owes its write until the matching ReleaseSaves (nested holds count). Main thread.</summary>
        internal static void HoldSaves() { saveHold++; }

        /// <summary>Round db1 (review): m3 - the hold ends; the last one writes the save it owes (once).</summary>
        internal static void ReleaseSaves()
        {
            if (saveHold <= 0) { saveHold = 0; return; }
            if (--saveHold > 0 || !saveOwed) return;
            saveOwed = false;
            SaveAtomic();
        }

        /// <summary>Atomic write. Host only. Never throws. Round MP6 (core): survivor_communities.txt - never the old file.</summary>
        public static void SaveAtomic()
        {
            try
            {
                if (shuttingDown) return;
                var cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
                if (cm == null || !cm.IsServer) return;
                dirty = true;          // round MP1: every mutation that matters saves - the clients' copy follows (ScMirror)
                // round db1 (review): m3 - held: owed, written once when the hold ends
                if (saveHold > 0 && ThreadManager.IsMainThread()) { saveOwed = true; return; }
                if (string.IsNullOrEmpty(saveDir)) { ScLog.Once("save-nodir", "registry save skipped: no save directory yet"); return; }
                // round MP6 (core fix): the file this save would replace could not be read at load - not replaced (review C-m1)
                if (saveBlocked) { ScLog.Once("save-blocked", "registry save skipped for this session: the save file could not be read at load (see the error at load)"); return; }
                if (!ThreadManager.IsMainThread()) { OnMain(SaveAtomic); return; }

                // round MP1: the text is BuildText's, shared with the clients' snapshot
                string text = BuildText(false);
                string real = Path.Combine(saveDir, FileName);
                string tmp = real + ".tmp";
                string bak = real + ".bak";
                Directory.CreateDirectory(saveDir);
                File.WriteAllText(tmp, text);
                if (File.Exists(real)) File.Replace(tmp, real, bak);
                else File.Move(tmp, real);
                if (ScConfig.Verbose)
                {
                    int members = 0, prospects = 0;
                    foreach (CommunityState cs in ScCommunities.All) { members += cs.Members.Count; prospects += cs.Prospects.Count; }
                    ScLog.Verbose("registry saved: " + members + " member(s), " + prospects + " prospect(s)"
                                  + (ScCommunities.Count > 1 ? " in " + ScCommunities.Count + " communities" : ""));
                }
            }
            catch (Exception e)
            {
                ScLog.Err("registry save failed: " + e.Message);
                Log.Exception(e);
            }
        }

        /// <summary>
        /// The registry as text (read back by ParseText). Main thread. Round MP6 (core): the save (forClients false) is the SCOM 2
        /// layout - [world], [membership], one [community] per community, cid= on each community's records (BuildSaveText); a
        /// client's snapshot (forClients true, NetPackageScState) is one community's text in the old SCOM 1 layout - community 0's
        /// here, the client's own through BuildText(true, cid) (round MP6 (part N): BuildClientText; with one community exactly as
        /// before MP6, BuildLegacyText). Round MP1: forClients leaves out what a client must not get or does not need - platform
        /// ids (ownerid=, leader=), the server's cleanup bookkeeping and streamer names, the members' entity snapshots (snap= and
        /// postsnap= become "-", which keeps their meaning: the member is held offline), handed items, worn pieces, ammo and
        /// magazines, the enclaves' trade stock, and the enclaves' and bands' people. Throws on a bad record (SaveAtomic logs it).
        /// </summary>
        public static string BuildText(bool forClients)
        {
            // round MP6 (part N): a client's snapshot without a community named is community 0's (BuildText(true, cid) names one)
            return BuildText(forClients, ScPlayers.SharedCommunity);
        }

        /// <summary>
        /// Round MP6 (part N): BuildText for one community - forClients: that community's snapshot for its own clients
        /// (BuildClientText); otherwise the save file's text (every community, cid ignored).
        /// </summary>
        public static string BuildText(bool forClients, int cid)
        {
            return forClients ? BuildClientText(cid) : BuildSaveText();
        }

        /// <summary>
        /// Round MP6 (part N): community cid's snapshot for its clients (NetPackageScState), in the old one-community layout
        /// (SCOM 1, BuildLegacyText): its [community], members, contacts, outposts, requests and prospects, and the world's
        /// enclaves, infestations and bands. It is built inside cid's scope (ScRegistry.Use), so every relation value a world record
        /// writes - an enclave's standing=, known=, turned= and its dated stamps, an infestation's and a band's known= and trust= -
        /// is read through the record as the community in scope sees it: the record's own field while the relations live there,
        /// that community's row once they are per community. With one community this is exactly BuildLegacyText(community 0) - the
        /// bytes BuildText(true) sent before MP6. With several, an enclave this community has not found (Known false in its scope)
        /// goes as its footprint only (WriteEnclaveFootprint): what the client's claim gate and its refusal tips read, and nothing
        /// else - no relation, kind, request clock or stamp. A missing community (it went since the client was grouped) sends
        /// community 0's. Throws on a bad record, as BuildText.
        /// </summary>
        internal static string BuildClientText(int cid)
        {
            CommunityState cs = ScCommunities.OrNone(cid);          // round MP7 (core): PerPlayer/PerParty - the empty record, never community 0's
            using (Use(cs.Id))
            {
                if (ScCommunities.Count < 2) return BuildLegacyText(cs, true);      // one community: byte for byte as before MP6
                // several: BuildLegacyText's text and WriteRecords' order (forClients), with an enclave not found here as its footprint
                var sb = new StringBuilder();
                sb.AppendLine(LegacyHeader);
                sb.AppendLine("# SurvivorCommunity registry - the server's snapshot for a client");
                sb.AppendLine("[community]");
                WriteCommunityKeys(sb, cs.Community, true, true);
                foreach (var m in cs.Members.Values)
                {
                    sb.AppendLine("[member]");
                    WriteMemberFields(sb, m, true);
                }
                foreach (var ct in cs.Contacts) WriteContact(sb, ct, -1);
                foreach (var o in cs.Outposts) WriteOutpost(sb, o, -1);
                foreach (var en in Enclaves)
                {
                    if (en.Known) WriteEnclave(sb, en, true);
                    else WriteEnclaveFootprint(sb, en);
                }
                foreach (var rq in cs.EncRequests) WriteRequest(sb, rq, true, -1);
                foreach (var inf in Infestations) WriteInfestation(sb, inf, true);     // round MP6 (merge): part R's forClients - the scope's row
                foreach (var bd in Bands) WriteBand(sb, bd, true);
                foreach (var p in cs.Prospects.Values) WriteProspect(sb, p, -1);
                return sb.ToString();
            }
        }

        /// <summary>
        /// Round MP6 (part N): an enclave a community has not found, in that community's snapshot (two or more communities): what a
        /// client's claim gate needs (Patches_Outposts: the footprint, poi= and size=, and prefab=, without which the copy drops the
        /// record) and its refusal tips name (name=, poiname=: the player stands at the building then) - and known=0. No standing,
        /// kind, row, tier, request clock or stamp: the client learns those when the community finds them.
        /// </summary>
        private static void WriteEnclaveFootprint(StringBuilder sb, Enclave en)
        {
            sb.AppendLine("[enclave]");
            sb.AppendLine("id=" + en.Id);
            sb.AppendLine("name=" + Clean(en.Name));
            sb.AppendLine("poi=" + Vec(en.Poi));
            sb.AppendLine("size=" + Vec(en.Size));
            sb.AppendLine("prefab=" + Clean(en.Prefab));
            sb.AppendLine("poiname=" + Clean(en.PoiName));
            sb.AppendLine("known=0");
        }

        /// <summary>
        /// Round MP6 (core): one community's text in the old one-community layout (SCOM 1, the world keys inside [community], no
        /// cid=) - byte for byte what BuildText wrote before MP6 for that community and the world. A client's snapshot is
        /// community 0's (forClients); forClients false gives the old save file's text (a check that the migration kept
        /// everything: it reads back as the same registry).
        /// </summary>
        internal static string BuildLegacyText(CommunityState cs, bool forClients)
        {
            var sb = new StringBuilder();
            sb.AppendLine(LegacyHeader);
            sb.AppendLine(forClients ? "# SurvivorCommunity registry - the server's snapshot for a client" : "# SurvivorCommunity registry - edit only with the game closed");
            sb.AppendLine("[community]");
            WriteCommunityKeys(sb, cs.Community, forClients, true);
            WriteRecords(sb, new[] { cs }, forClients, false);
            return sb.ToString();
        }

        /// <summary>Round MP6 (core): the save file's text (SCOM 2): [world], [membership], every [community], then the records.</summary>
        private static string BuildSaveText()
        {
            var sb = new StringBuilder();
            sb.AppendLine(Header);
            sb.AppendLine("# SurvivorCommunity registry - edit only with the game closed");
            ScWorldState ws = WorldState;
            sb.AppendLine("[world]");
            sb.AppendLine("lastday=" + ws.LastDay.ToString(CultureInfo.InvariantCulture));
            if (ws.NextCommunityId > 0) sb.AppendLine("communityid=" + ws.NextCommunityId.ToString(CultureInfo.InvariantCulture));
            // round MP7 (core): the save's community mode once this build fixed it (a save from before MP7 is Shared and writes nothing),
            // and one line per retired record (its id stays taken)
            if (ws.ModeLocked && ws.ModeWritten) sb.AppendLine("mode=" + ScMembership.ModeName(ws.Mode));
            foreach (int rid in ws.Retired) sb.AppendLine("retired=" + rid.ToString(CultureInfo.InvariantCulture));
            if (ws.ZeroNoAutoHome) sb.AppendLine("noautohome=1");      // round db1 (review): m5 - written only while set (an old save's text stays)
            sb.AppendLine("contactid=" + ws.NextContactId);
            sb.AppendLine("outpostid=" + ws.NextOutpostId);
            WriteEnclaveIds(sb, ws);
            WriteWorldIds(sb, ws);
            WriteRemains(sb, ws);
            WriteRefills(sb, ws);
            WriteBookkeeping(sb, ws, false);
            // round MP7 (review AP/F): n1 - the section also when only part AP's lines have something to say (an opt-out of a player
            // whose empty record was pruned); a save with neither writes no section, as before
            var joins = new StringBuilder();
            ScJoins.Write(joins, ws);                  // round MP7 (part AP): joined= and optout= lines (none in a save without them)
            if (ws.Membership.Count > 0 || joins.Length > 0)
            {
                sb.AppendLine("[membership]");
                var ids = new List<string>(ws.Membership.Keys);
                ids.Sort(StringComparer.Ordinal);
                foreach (string id in ids) sb.AppendLine("player=" + Clean(id) + ":" + ws.Membership[id].ToString(CultureInfo.InvariantCulture));
                sb.Append(joins);
            }
            IReadOnlyList<CommunityState> all = ScCommunities.All;
            foreach (CommunityState cs in all)
            {
                sb.AppendLine("[community]");
                sb.AppendLine("cid=" + cs.Id.ToString(CultureInfo.InvariantCulture));
                if (!string.IsNullOrEmpty(cs.Community.Name)) sb.AppendLine("name=" + Clean(cs.Community.Name));
                if (cs.Personal) sb.AppendLine("personal=1");          // round MP7 (core): a player's own relations (never in a Shared save)
                WriteCommunityKeys(sb, cs.Community, false, false);
            }
            WriteRecords(sb, all, false, true);
            return sb.ToString();
        }

        /// <summary>
        /// Round MP6 (core): a [community] section's keys, as BuildText wrote them before MP6 (the section header is the caller's).
        /// legacy: the old layout - the world's keys (contactid=, outpostid=, enclaveid= ... streamname=) in their old places;
        /// otherwise they are [world]'s and left out here.
        /// </summary>
        private static void WriteCommunityKeys(StringBuilder sb, Community c, bool forClients, bool legacy)
        {
            if (c.HasHome) sb.AppendLine("home=" + Vec(c.Home));
            sb.AppendLine("owner=" + Clean(c.Owner));
            if (!forClients && !string.IsNullOrEmpty(c.OwnerId)) sb.AppendLine("ownerid=" + Clean(c.OwnerId));   // round MP0; MP1: never to a client
            sb.AppendLine("morale=" + c.Morale);
            sb.AppendLine("lastday=" + c.LastDay);
            sb.AppendLine("lowmorale=" + c.LowMoraleDays);
            sb.AppendLine("threat=" + c.ThreatMemberId);
            sb.AppendLine("hungry=" + (c.Hungry ? 1 : 0));
            sb.AppendLine("deathpenalty=" + c.DeathPenalty.ToString("0.###", CultureInfo.InvariantCulture));
            sb.AppendLine("ammoopen=" + c.AmmoOpenPoints);
            sb.AppendLine("ammoopened=" + c.AmmoOpened);
            sb.AppendLine("ammoshort=" + (c.AmmoShort ? 1 : 0));
            sb.AppendLine("medsrestocked=" + c.MedsRestocked);
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
            if (legacy) sb.AppendLine("contactid=" + c.NextContactId);        // round MP6 (core): the world's (in [world] since MP6)
            if (c.Radios.Count > 0)
            {
                var rd = new StringBuilder();
                foreach (Vector3i v in c.Radios) { if (rd.Length > 0) rd.Append(';'); rd.Append(Vec(v)); }
                sb.AppendLine("radios=" + rd);
            }
            sb.AppendLine("radioday=" + c.RadioOnlineDay);
            if (legacy) sb.AppendLine("outpostid=" + c.NextOutpostId);        // round MP6 (core): the world's
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
            if (c.NoiseEvents > 0f) sb.AppendLine("noiseev=" + c.NoiseEvents.ToString("0.###", CultureInfo.InvariantCulture));
            if (c.Attention > 0f) sb.AppendLine("attention=" + c.Attention.ToString("0.###", CultureInfo.InvariantCulture));
            if (c.NoiseStamp > 0UL) sb.AppendLine("noisestamp=" + c.NoiseStamp.ToString(CultureInfo.InvariantCulture));
            // round MP6 (fix H-M2): never with one community (its save stays as before), never to a client
            if (!forClients && c.SeenTime > 0UL && ScCommunities.Count > 1) sb.AppendLine("seentime=" + c.SeenTime.ToString(CultureInfo.InvariantCulture));
            if (c.AttackState != 0)
                sb.AppendLine("baseattack=" + c.AttackState + ":" + c.AttackArrive.ToString(CultureInfo.InvariantCulture) + ":" + c.AttackStarted.ToString(CultureInfo.InvariantCulture)
                              + ":" + c.AttackBearing + ":" + c.AttackSize + ":" + c.AttackSpawned + ":" + c.AttackKilled + ":" + c.AttackAmmoRounds + ":" + c.AttackAmmoPoints);
            if (c.AttackNotBefore > 0UL) sb.AppendLine("attacknext=" + c.AttackNotBefore.ToString(CultureInfo.InvariantCulture));
            if (c.DefenceTriumph > 0f) sb.AppendLine("defended=" + c.DefenceTriumph.ToString("0.##", CultureInfo.InvariantCulture));
            if (c.WatchPosts.Count > 0)
            {
                var wp = new StringBuilder();
                foreach (Vector3i v in c.WatchPosts) { if (wp.Length > 0) wp.Append(';'); wp.Append(Vec(v)); }
                sb.AppendLine("watchposts=" + wp);
            }
            if (c.AttackWarned > 0) sb.AppendLine("attackwarn=" + c.AttackWarned);
            if (c.AttackState != 0 && (c.AttackAwayEnd > 0UL || c.AttackHeard > 0))
                sb.AppendLine("attackaway=" + c.AttackAwaySince.ToString(CultureInfo.InvariantCulture) + ":" + c.AttackAwayEnd.ToString(CultureInfo.InvariantCulture)
                              + ":" + c.AttackAwayBase + ":" + c.AttackHeard);
            if (c.AttackAwayUsed) sb.AppendLine("awayused=1");
            if (c.DefenceLoss > 0f) sb.AppendLine("defloss=" + c.DefenceLoss.ToString("0.##", CultureInfo.InvariantCulture));
            if (c.HomeDefences > 0) sb.AppendLine("homedef=" + c.HomeDefences);
            if (c.ScanDefences.Count > 0)
            {
                var sd = new StringBuilder();
                foreach (KeyValuePair<long, int> kv in c.ScanDefences)
                {
                    if (kv.Value <= 0) continue;
                    if (sd.Length > 0) sd.Append(';');
                    sd.Append(WorldChunkCache.extractX(kv.Key).ToString(CultureInfo.InvariantCulture)).Append(':')
                      .Append(WorldChunkCache.extractZ(kv.Key).ToString(CultureInfo.InvariantCulture)).Append(':').Append(kv.Value);
                }
                if (sd.Length > 0) sb.AppendLine("scandef=" + sd);
            }
            if (c.BloodMoonHomeDay > 0) sb.AppendLine("bmhome=" + c.BloodMoonHomeDay);
            if (c.AwayNews.Count > 0)
            {
                var an = new StringBuilder();
                foreach (string n in c.AwayNews) { if (an.Length > 0) an.Append(';'); an.Append(Clean(n)); }
                sb.AppendLine("awaynews=" + an);
            }
            if (c.Thirsty) sb.AppendLine("thirsty=1");
            if (c.HomeWater > 0) sb.AppendLine("homewater=" + c.HomeWater);
            if (c.ScanWater.Count > 0)
            {
                var sw = new StringBuilder();
                foreach (KeyValuePair<long, int> kv in c.ScanWater)
                {
                    if (kv.Value <= 0) continue;
                    if (sw.Length > 0) sw.Append(';');
                    sw.Append(WorldChunkCache.extractX(kv.Key).ToString(CultureInfo.InvariantCulture)).Append(':')
                      .Append(WorldChunkCache.extractZ(kv.Key).ToString(CultureInfo.InvariantCulture)).Append(':').Append(kv.Value);
                }
                if (sw.Length > 0) sb.AppendLine("scanwater=" + sw);
            }
            if (c.DewLevels.Count > 0)
            {
                var dl = new StringBuilder();
                foreach (KeyValuePair<Vector3i, int> kv in c.DewLevels)
                {
                    if (kv.Value <= 1) continue;
                    if (dl.Length > 0) dl.Append(';');
                    dl.Append(Vec(kv.Key)).Append(':').Append(kv.Value);
                }
                if (dl.Length > 0) sb.AppendLine("dewlevels=" + dl);
            }
            if (c.DrinksLeft > 0f) sb.AppendLine("drinks=" + c.DrinksLeft.ToString("0.#", CultureInfo.InvariantCulture));
            if (c.DrinksDay >= 0) sb.AppendLine("drinksday=" + c.DrinksDay);
            if (c.FeastLeft > 0f) sb.AppendLine("feast=" + c.FeastLeft.ToString("0.#", CultureInfo.InvariantCulture));   // round fd1
            if (c.FeastDay >= 0) sb.AppendLine("feastday=" + c.FeastDay);
            if (c.FeastMorale != 0) sb.AppendLine("feastmorale=" + c.FeastMorale);
            if (c.FeastStamina != 0) sb.AppendLine("feaststamina=" + c.FeastStamina);
            if (c.RationLeft > 0f) sb.AppendLine("ration=" + c.RationLeft.ToString("0.#", CultureInfo.InvariantCulture));
            if (c.RationDay >= 0) sb.AppendLine("rationday=" + c.RationDay);
            ScFacilityTab.WriteKeys(c, sb);       // round m1f: scansites=, lightsout=, lastattack=, towers=
            ScRelations.WriteGrudges(sb, c, forClients);    // round MP6 (part relations): grudge= (never to a client)
            if (legacy) WriteEnclaveIds(sb, WorldState);                   // round MP6 (core): enclaveid=, enclaveseed= (the world's)
            sb.AppendLine("enclaveradio=" + c.EnclaveRadioDay);
            if (legacy) WriteWorldIds(sb, WorldState);                     // round MP6 (core): infestid= ... encreqday= (the world's)
            if (c.EncOffersToday > 0) sb.AppendLine("encoffers=" + c.EncOfferDay.ToString(CultureInfo.InvariantCulture) + ":" + c.EncOffersToday.ToString(CultureInfo.InvariantCulture));   // P2
            if (c.EncSiegeAt > 0) sb.AppendLine("encsiegeat=" + c.EncSiegeAt.ToString(CultureInfo.InvariantCulture));
            if (legacy) WriteRemains(sb, WorldState);                      // round MP6 (core): encremains= (the world's)
            if (c.EnclaveLoss > 0f) sb.AppendLine("encloss=" + c.EnclaveLoss.ToString("0.##", CultureInfo.InvariantCulture));
            if (legacy) WriteRefills(sb, WorldState);                      // round MP6 (core): encrefill= (the world's)
            var st = new StringBuilder();
            for (int i = 0; i < ScNames.ResourceCount; i++) { if (i > 0) st.Append(','); st.Append(ScNames.ResourceKeys[i]).Append(':').Append(c.Stock[i]); }
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
            if (legacy) WriteBookkeeping(sb, WorldState, forClients);      // round MP6 (core): cleanup=, doomed=, streamname= (the world's)
        }

        /// <summary>
        /// Round MP6 (core): every record section, in the order BuildText wrote them before MP6: the given communities' members,
        /// contacts and outposts, the world's enclaves and their people, the communities' requests, the world's infestations,
        /// bands and band people, the communities' prospects. withCid: a cid= line opens each community record (the SCOM 2 save).
        /// </summary>
        private static void WriteRecords(StringBuilder sb, IReadOnlyList<CommunityState> communities, bool forClients, bool withCid)
        {
            foreach (CommunityState cs in communities)
                foreach (var m in cs.Members.Values)
                {
                    sb.AppendLine("[member]");
                    if (withCid) sb.AppendLine("cid=" + cs.Id.ToString(CultureInfo.InvariantCulture));     // round MP6 (core): her community
                    WriteMemberFields(sb, m, forClients);
                }
            foreach (CommunityState cs in communities)
                foreach (var ct in cs.Contacts) WriteContact(sb, ct, withCid ? cs.Id : -1);
            foreach (CommunityState cs in communities)
                foreach (var o in cs.Outposts) WriteOutpost(sb, o, withCid ? cs.Id : -1);
            foreach (var en in Enclaves) WriteEnclave(sb, en, forClients);
            // round MP1: a client's snapshot has no enclave or band people (bulky, and nothing a client shows reads them yet)
            if (!forClients) foreach (var ep in EncPeople) WriteEncPerson(sb, ep, forClients);
            foreach (CommunityState cs in communities)
                foreach (var rq in cs.EncRequests) WriteRequest(sb, rq, forClients, withCid ? cs.Id : -1);
            foreach (var inf in Infestations) WriteInfestation(sb, inf, forClients);       // round MP6 (part relations): forClients
            // E13a: bands on the road and their people (forward-only sections)
            foreach (var bd in Bands) WriteBand(sb, bd, forClients);                       // round MP6 (part relations): forClients
            if (!forClients) foreach (var bp in BandPeople) WriteBandPerson(sb, bp, forClients);     // round MP1: as the enclave people
            foreach (CommunityState cs in communities)
                foreach (var p in cs.Prospects.Values) WriteProspect(sb, p, withCid ? cs.Id : -1);
        }

        /// <summary>Round MP6 (core): enclaveid= and enclaveseed= ([world]; an old layout's [community]), as before MP6.</summary>
        private static void WriteEnclaveIds(StringBuilder sb, ScWorldState ws)
        {
            sb.AppendLine("enclaveid=" + ws.NextEnclaveId);
            if (ws.EnclaveSeeded)
                sb.AppendLine("enclaveseed=1:" + ws.EnclaveSeedDay + ":" + Clean(ws.EnclaveSeedMode).Replace(":", "") + ":"
                              + ws.EnclaveSeed.ToString(CultureInfo.InvariantCulture) + ":" + ws.EnclaveSeedCount);
        }

        /// <summary>Round MP6 (core): infestid=, encpersonid=, bandid=, bandpersonid=, encrequestid= and encreqday=, as before MP6.</summary>
        private static void WriteWorldIds(StringBuilder sb, ScWorldState ws)
        {
            sb.AppendLine("infestid=" + ws.NextInfestationId);
            sb.AppendLine("encpersonid=" + ws.NextEncPersonId);
            if (ws.NextBandId > 0) sb.AppendLine("bandid=" + ws.NextBandId.ToString(CultureInfo.InvariantCulture));
            if (ws.NextBandPersonId > 0) sb.AppendLine("bandpersonid=" + ws.NextBandPersonId.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("encrequestid=" + ws.NextEncRequestId);
            if (ws.EncRequestDay > 0) sb.AppendLine("encreqday=" + ws.EncRequestDay.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Round MP6 (core): encremains=, as before MP6.</summary>
        private static void WriteRemains(StringBuilder sb, ScWorldState ws)
        {
            if (ws.EncRemains.Count > 0)
            {
                var rm = new StringBuilder();
                foreach (Vector3i v in ws.EncRemains) { if (rm.Length > 0) rm.Append(';'); rm.Append(Vec(v)); }
                sb.AppendLine("encremains=" + rm);
            }
        }

        /// <summary>Round MP6 (core): encrefill=, as before MP6.</summary>
        private static void WriteRefills(StringBuilder sb, ScWorldState ws)
        {
            if (ws.EncRefills.Count > 0)
            {
                var rf = new StringBuilder();
                foreach (EncRefill r in ws.EncRefills)
                {
                    if (rf.Length > 0) rf.Append(';');
                    rf.Append(r.Day.ToString(CultureInfo.InvariantCulture)).Append(':').Append(Vec(r.Poi)).Append(':').Append(Clean(r.Name));
                }
                sb.AppendLine("encrefill=" + rf);
            }
        }

        /// <summary>Round MP6 (core): cleanup=, doomed= and streamname= (never to a client), as before MP6.</summary>
        private static void WriteBookkeeping(StringBuilder sb, ScWorldState ws, bool forClients)
        {
            if (!forClients && Cleanup.Count > 0)       // round MP1: server bookkeeping (entity ids), never a client's
            {
                var cl = new StringBuilder();
                foreach (int id in Cleanup) { if (cl.Length > 0) cl.Append(','); cl.Append(id); }
                sb.AppendLine("cleanup=" + cl);
            }
            if (!forClients && Doomed.Count > 0)        // round MP1: the same
            {
                var dm = new StringBuilder();
                foreach (int id in Doomed) { if (dm.Length > 0) dm.Append(','); dm.Append(id); }
                sb.AppendLine("doomed=" + dm);
            }
            // round 30: the streamer names given out in this save, one line each (ScStreamerNames cleans a name of every
            // separator first, so Clean() is only a guard here)
            if (!forClients)                            // round MP1: the server's list, not shown on a client
                foreach (string n in ws.StreamerNamesUsed)
                {
                    string sn = Clean(n);
                    if (sn.Length > 0) sb.AppendLine("streamname=" + sn);
                }
            // round lk1: the looks of the bodies the world saves with no record of SC's (entity ids: server bookkeeping, never a client's)
            if (!forClients) ScLookKeep.Write(sb);
        }

        /// <summary>Round MP6 (core): one [contact] section, as BuildText wrote it inline before MP6 (cid &lt; 0: no cid= line, the old layout).</summary>
        private static void WriteContact(StringBuilder sb, Contact ct, int cid)
        {
            sb.AppendLine("[contact]");
            if (cid >= 0) sb.AppendLine("cid=" + cid.ToString(CultureInfo.InvariantCulture));     // round MP6 (core): its community
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

        /// <summary>Round MP6 (core): one [outpost] section, as BuildText wrote it inline before MP6 (cid &lt; 0: no cid= line).</summary>
        private static void WriteOutpost(StringBuilder sb, Outpost o, int cid)
        {
            sb.AppendLine("[outpost]");
            if (cid >= 0) sb.AppendLine("cid=" + cid.ToString(CultureInfo.InvariantCulture));     // round MP6 (core): its community
            sb.AppendLine("id=" + o.Id);
            sb.AppendLine("flag=" + Vec(o.Flag));
            sb.AppendLine("poi=" + Vec(o.Poi));
            sb.AppendLine("size=" + Vec(o.Size));
            sb.AppendLine("prefab=" + Clean(o.Prefab));
            sb.AppendLine("name=" + Clean(o.Name));
            sb.AppendLine("row=" + Clean(o.Row));
            var wt = new StringBuilder();
            for (int i = 0; i < ScNames.ResourceCount; i++)
            {
                if (o.Weight[i] <= 0f) continue;
                if (wt.Length > 0) wt.Append(',');
                wt.Append(ScNames.ResourceKeys[i]).Append(':').Append(o.Weight[i].ToString("0.###", CultureInfo.InvariantCulture));
            }
            if (wt.Length > 0) sb.AppendLine("weight=" + wt);
            sb.AppendLine("tier=" + o.Tier);
            sb.AppendLine("day=" + o.Day);
            sb.AppendLine("pending=" + string.Join(",", Array.ConvertAll(o.Pending, x => x.ToString(CultureInfo.InvariantCulture))));
            sb.AppendLine("last=" + string.Join(",", Array.ConvertAll(o.LastYield, x => x.ToString(CultureInfo.InvariantCulture))));   // round ie1: whole units (an older build reads them as floats)
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
            sb.AppendLine("produced=" + o.Produced.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("levelday=" + o.LevelDay);
            sb.AppendLine("visit=" + o.LastVisitDay);
            if (o.RaidEnclave > 0) sb.AppendLine("raidenc=" + o.RaidEnclave.ToString(CultureInfo.InvariantCulture));
            if (o.RaidEnclave > 0 && o.RaidLost > 0) sb.AppendLine("raidlost=" + o.RaidLost.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Round MP6 (core): one [enclave] section, as BuildText wrote it inline before MP6 (a world record: no cid).</summary>
        private static void WriteEnclave(StringBuilder sb, Enclave en, bool forClients)
        {
            // round MP6 (part relations): the relation keys are one community's row - community 0's in the save (each other
            // community's follows as a rel= line), the community in scope's in a client's snapshot (BuildText(true) run inside
            // ScRegistry.Use(cid) projects that community's standing, known flags, clocks and hostility into the keys it reads).
            // round MP6 (merge): BuildClientText (part N) builds each community's snapshot inside ScRegistry.Use(cid) - WriteInfestation and WriteBand do the same
            EncRel rr = forClients ? (en.Rels.Peek(ScRelations.Cid) ?? ScRelations.NoEnc) : en.Rels.Zero;
            sb.AppendLine("[enclave]");
            sb.AppendLine("id=" + en.Id);
            sb.AppendLine("name=" + Clean(en.Name));
            sb.AppendLine("poi=" + Vec(en.Poi));
            sb.AppendLine("size=" + Vec(en.Size));
            sb.AppendLine("prefab=" + Clean(en.Prefab));
            sb.AppendLine("poiname=" + Clean(en.PoiName));
            sb.AppendLine("biome=" + Clean(en.Biome));
            sb.AppendLine("row=" + Clean(en.Row));
            sb.AppendLine("tier=" + en.Tier);
            sb.AppendLine("hostile=" + (en.Hostile ? 1 : 0));
            sb.AppendLine("standing=" + rr.Standing.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("known=" + (rr.Known ? 1 : 0));
            sb.AppendLine("knownday=" + rr.KnownDay.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("seeded=" + en.SeededDay + ":" + Clean(en.Mode));
            if (en.Kind.Length > 0) sb.AppendLine("kind=" + Clean(en.Kind));
            sb.AppendLine("turned=" + (rr.TurnedHostile ? Math.Max(0, rr.HostileDay) : -1).ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("people=" + (en.PeopleRolled ? 1 : 0));
            if (!forClients && en.Stock.Length > 0) sb.AppendLine("stock=" + en.Stock);   // base64url: never Clean()ed (it has no '=' or ';'); MP1: bulky, server-side trading
            if (en.LastRecruitDay >= 0) sb.AppendLine("recruited=" + en.LastRecruitDay.ToString(CultureInfo.InvariantCulture));
            if (rr.NextRequestDay > 0) sb.AppendLine("nextreq=" + rr.NextRequestDay.ToString(CultureInfo.InvariantCulture));
            if (rr.Thanks.Length > 0) sb.AppendLine("thanks=" + Clean(rr.Thanks));
            if (en.Benefit.Length > 0) sb.AppendLine("benefit=" + Clean(en.Benefit));
            if (rr.AllyRevealDay > 0) sb.AppendLine("allyreveal=" + rr.AllyRevealDay.ToString(CultureInfo.InvariantCulture));
            if (rr.GiftDay > 0) sb.AppendLine("gift=" + rr.GiftDay.ToString(CultureInfo.InvariantCulture));
            if (rr.BarterDay > 0) sb.AppendLine("barter=" + rr.BarterDay.ToString(CultureInfo.InvariantCulture));
            if (rr.CallDay > 0) sb.AppendLine("callday=" + rr.CallDay.ToString(CultureInfo.InvariantCulture));
            if (rr.CallKind.Length > 0)
                sb.AppendLine("call=" + Clean(rr.CallKind) + ":" + rr.CallAt.ToString(CultureInfo.InvariantCulture) + ":" + rr.CallPid.ToString(CultureInfo.InvariantCulture)
                              + ":" + rr.CallUntil.ToString(CultureInfo.InvariantCulture));
            if (en.RaidDay > 0) sb.AppendLine("raidday=" + en.RaidDay.ToString(CultureInfo.InvariantCulture));
            if (en.RaidLost > 0) sb.AppendLine("raidlost=" + en.RaidLost.ToString(CultureInfo.InvariantCulture));
            if (en.MovedDay >= 0)
                sb.AppendLine("moved=" + en.MovedDay.ToString(CultureInfo.InvariantCulture) + ":" + Vec(en.MovedFrom) + ":"
                              + (en.MovedDead >= 0 ? en.MovedDead.ToString(CultureInfo.InvariantCulture) + ":" : "") + Clean(en.MovedFromName));
            if (en.LossDay >= 0) sb.AppendLine("lossday=" + en.LossDay.ToString(CultureInfo.InvariantCulture));
            if (en.NestDay >= 0) sb.AppendLine("nestday=" + en.NestDay.ToString(CultureInfo.InvariantCulture));
            if (!forClients) ScRelations.WriteEnclaveRows(sb, en);        // round MP6 (part relations): rel= for communities 1+
            if (!forClients) ScRelations.WriteEnclaveBases(sb, en);       // round en1 (review): EN1-M1 - relbase= (only while a copy has one)
            if (forClients && en.Lost) sb.AppendLine("lost=1");          // round en1 (review): EN1-n3 - the client's ENCLAVES tab skips it
        }

        /// <summary>Round MP6 (core): one [encperson] section, as BuildText wrote it inline before MP6 (a world record: no cid).</summary>
        private static void WriteEncPerson(StringBuilder sb, EncPerson ep, bool forClients)
        {
            sb.AppendLine("[encperson]");
            sb.AppendLine("pid=" + ep.Id);
            sb.AppendLine("enclave=" + ep.EnclaveId);
            sb.AppendLine("role=" + Clean(ep.Role));
            // E10: one out raiding is home in the file (bodies never outlive a load; an older build knows no "raiding")
            sb.AppendLine("state=" + Clean(ep.Raiding ? EncPerson.StateHome : ep.State));
            sb.AppendLine("died=" + ep.DiedDay.ToString(CultureInfo.InvariantCulture));
            if (ep.HasHome)
            {
                sb.AppendLine("spot=" + Vec(ep.Home));
                sb.AppendLine("yaw=" + ep.Yaw.ToString("0.#", CultureInfo.InvariantCulture));
            }
            sb.AppendLine("health=" + ep.Health.ToString("0.###", CultureInfo.InvariantCulture));
            if (ep.AmmoStart != null) sb.AppendLine("ammostart=" + Clean(ScAmmo.Encode(ep.AmmoStart, ep.AmmoStartMags)));
            if (ep.Enlisted)
                sb.AppendLine("enlisted=" + ep.EnlistedBy.ToString(CultureInfo.InvariantCulture) + ":" + ep.EnlistedUntil.ToString(CultureInfo.InvariantCulture)
                              + ":" + ep.EnlistedBody.ToString(CultureInfo.InvariantCulture));
            // round MP7 (part F): the enlister's platform id - only while enlisted, never to a client (as leader= and acceptedby=)
            if (ep.Enlisted && !forClients && !string.IsNullOrEmpty(ep.EnlistedById)) sb.AppendLine("enlistedid=" + Clean(ep.EnlistedById));
            WriteMemberFields(sb, ep.Who, forClients);
        }

        /// <summary>Round MP6 (core): one [encrequest] section, as BuildText wrote it inline before MP6 (cid &lt; 0: no cid= line).</summary>
        private static void WriteRequest(StringBuilder sb, EnclaveRequest rq, bool forClients, int cid)
        {
            sb.AppendLine("[encrequest]");
            if (cid >= 0) sb.AppendLine("cid=" + cid.ToString(CultureInfo.InvariantCulture));     // round MP6 (core): its community
            sb.AppendLine("id=" + rq.Id);
            sb.AppendLine("enclave=" + rq.EnclaveId);
            sb.AppendLine("kind=" + Clean(rq.Kind));
            sb.AppendLine("state=" + Clean(rq.State));
            sb.AppendLine("offered=" + rq.OfferDay.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("lapse=" + rq.LapseDay.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("accepted=" + rq.AcceptDay.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("due=" + rq.DueDay.ToString(CultureInfo.InvariantCulture));
            if (rq.Units > 0) sb.AppendLine("res=" + ScNames.Key(rq.Res) + ":" + rq.Units);
            if (rq.QuestCode != 0) sb.AppendLine("quest=" + rq.QuestCode.ToString(CultureInfo.InvariantCulture));
            // round MP4: the accepting player's platform id (ScQuestNet) - never to a client, as ownerid= and leader=
            if (!forClients && !string.IsNullOrEmpty(rq.AcceptedBy)) sb.AppendLine("acceptedby=" + Clean(rq.AcceptedBy));
            if (rq.HasTarget) sb.AppendLine("target=" + Vec(rq.Target));
            if (rq.InfestationId >= 0) sb.AppendLine("infest=" + rq.InfestationId.ToString(CultureInfo.InvariantCulture));
            if (rq.PersonId >= 0) sb.AppendLine("person=" + rq.PersonId.ToString(CultureInfo.InvariantCulture));
            if (rq.Deadline > 0) sb.AppendLine("deadline=" + rq.Deadline.ToString(CultureInfo.InvariantCulture));
            // E5
            if (rq.Stage != 0) sb.AppendLine("stage=" + rq.Stage.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(rq.TargetName)) sb.AppendLine("targetname=" + Clean(rq.TargetName));
            if (rq.HasSpot)
            {
                sb.AppendLine("spot=" + Vec(rq.Spot));
                sb.AppendLine("spotyaw=" + rq.SpotYaw.ToString("0.#", CultureInfo.InvariantCulture));
            }
            if (rq.EscortBy >= 0 || rq.EscortBody >= 0)
                sb.AppendLine("escort=" + rq.EscortBy.ToString(CultureInfo.InvariantCulture) + ":" + rq.EscortBody.ToString(CultureInfo.InvariantCulture));
            if (rq.SiegeSize > 0 || rq.SiegeStarted > 0)
                sb.AppendLine("siege=" + rq.SiegeSize.ToString(CultureInfo.InvariantCulture) + ":" + rq.SiegeSpawned.ToString(CultureInfo.InvariantCulture) + ":"
                              + rq.SiegeKilled.ToString(CultureInfo.InvariantCulture) + ":" + rq.SiegeStarted.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Round MP6 (core): one [infestation] section, as BuildText wrote it inline before MP6 (a world record: no cid).</summary>
        private static void WriteInfestation(StringBuilder sb, Infestation inf, bool forClients)
        {
            // round MP6 (part relations): community 0's row in the save (others: rel= below), the scope's in a client's snapshot
            InfRel ir = forClients ? (inf.Rels.Peek(ScRelations.Cid) ?? ScRelations.NoInf) : inf.Rels.Zero;
            sb.AppendLine("[infestation]");
            sb.AppendLine("id=" + inf.Id);
            sb.AppendLine("poi=" + Vec(inf.Poi));
            sb.AppendLine("size=" + Vec(inf.Size));
            sb.AppendLine("prefab=" + Clean(inf.Prefab));
            sb.AppendLine("poiname=" + Clean(inf.PoiName));
            sb.AppendLine("tier=" + inf.Tier);
            sb.AppendLine("start=" + inf.StartDay.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("how=" + Clean(inf.How));
            sb.AppendLine("source=" + inf.Source);
            sb.AppendLine("known=" + (ir.Known ? 1 : 0));
            sb.AppendLine("knownday=" + ir.KnownDay.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("special=" + (inf.Special == SpecialState.Owed ? "owed" : inf.Special == SpecialState.Pending ? "pending:" + Vec(inf.SpecialHost)
                                        : inf.Special == SpecialState.Met ? "met:" + Clean(inf.SpecialClass) : "none"));
            sb.AppendLine("spread=" + inf.LastSpreadDay.ToString(CultureInfo.InvariantCulture));
            if (inf.Rearmed.Count > 0)
            {
                var ra = new StringBuilder();
                foreach (Vector3i v in inf.Rearmed) { if (ra.Length > 0) ra.Append(';'); ra.Append(Vec(v)); }
                sb.AppendLine("rearm=" + ra);
            }
            if (!forClients) ScRelations.WriteInfestationRows(sb, inf);       // round MP6 (part relations): rel= for communities 1+
        }

        /// <summary>Round MP6 (core): one [band] section, as BuildText wrote it inline before MP6 (a world record: no cid).</summary>
        private static void WriteBand(StringBuilder sb, Band bd, bool forClients)
        {
            // round MP6 (part relations): community 0's row in the save (others: rel= below), the scope's in a client's snapshot
            BandRel br = forClients ? (bd.Rels.Peek(ScRelations.Cid) ?? ScRelations.NoBand) : bd.Rels.Zero;
            sb.AppendLine("[band]");
            sb.AppendLine("id=" + bd.Id.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("kind=" + Clean(bd.Kind));
            if (bd.Name.Length > 0) sb.AppendLine("name=" + Clean(bd.Name));
            sb.AppendLine("size=" + bd.Size.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("made=" + (bd.PeopleMade ? 1 : 0));
            sb.AppendLine("from=" + Vec(bd.From));
            sb.AppendLine("fromname=" + Clean(bd.FromName));
            sb.AppendLine("to=" + Vec(bd.To));
            sb.AppendLine("toname=" + Clean(bd.ToName));
            if (bd.ToEnclave > 0) sb.AppendLine("toenc=" + bd.ToEnclave.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("toat=" + Vec(bd.ToAt));
            sb.AppendLine("at=" + Vec(bd.At));
            sb.AppendLine("depart=" + bd.Depart.ToString(CultureInfo.InvariantCulture));
            if (bd.HasVia) sb.AppendLine("via=" + Vec(bd.Via));
            sb.AppendLine("day=" + bd.StartDay.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("known=" + (br.Known ? 1 : 0));
            if (br.KnownDay >= 0) sb.AppendLine("knownday=" + br.KnownDay.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("seed=" + bd.Seed.ToString(CultureInfo.InvariantCulture));
            if (br.Trust != 0) sb.AppendLine("trust=" + br.Trust.ToString(CultureInfo.InvariantCulture));
            // a fight that is on (2) never outlives a load: its zombies are not saved
            if (bd.Trouble != 0) sb.AppendLine("trouble=" + (bd.Trouble == 2 ? 3 : bd.Trouble).ToString(CultureInfo.InvariantCulture));
            if (bd.Arrived) sb.AppendLine("arrived=1");
            if (!forClients) ScRelations.WriteBandRows(sb, bd);               // round MP6 (part relations): rel= for communities 1+
            if (!forClients) ScRelations.WriteBandBases(sb, bd);              // round en1 (review): EN1-M1 - relbase= (only while set)
        }

        /// <summary>Round MP6 (core): one [bandperson] section, as BuildText wrote it inline before MP6 (a world record: no cid).</summary>
        private static void WriteBandPerson(StringBuilder sb, BandPerson bp, bool forClients)
        {
            sb.AppendLine("[bandperson]");
            sb.AppendLine("pid=" + bp.Id.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("band=" + bp.BandId.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("role=" + Clean(bp.Role));
            sb.AppendLine("state=" + Clean(bp.State));
            sb.AppendLine("died=" + bp.DiedDay.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("health=" + bp.Health.ToString("0.###", CultureInfo.InvariantCulture));
            WriteMemberFields(sb, bp.Who, forClients);
        }

        /// <summary>Round MP6 (core): one [prospect] section, as BuildText wrote it inline before MP6 (cid &lt; 0: no cid= line).</summary>
        private static void WriteProspect(StringBuilder sb, Prospect p, int cid)
        {
            sb.AppendLine("[prospect]");
            if (cid >= 0) sb.AppendLine("cid=" + cid.ToString(CultureInfo.InvariantCulture));     // round MP6 (core): its community
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
            if (p.Started) sb.AppendLine("started=1");
            sb.AppendLine("done=" + (p.Done ? 1 : 0));
            if (p.Look != null)
            {
                sb.AppendLine("look=" + Clean(p.Look.Encode()));
                sb.AppendLine("gear=" + Clean(p.Look.EncodeGear()));
            }
            if (p.Loadout != null) sb.AppendLine("loadout=" + Clean(p.Loadout.Encode()));
            if (p.EncPersonId > 0) sb.AppendLine("encp=" + p.EncPersonId.ToString(CultureInfo.InvariantCulture));
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
                    mirrored = false;          // round MP1: a client's copy goes with the world (the containers are cleared below)
                    dirty = false;
                    SaveIsNew = false;
                    MirroredCommunity = ScPlayers.SharedCommunity;     // round MP6 (core)
                    ClearContainers();         // round MP6 (core): community 0 alone, a fresh world record, the world lists empty
                    saveDir = null;
                    ScOutposts.OnRegistryChanged();
                    ScRunners.Reset();
                });
            }
        }

        // ---- helpers ----

        /// <summary>
        /// A Member's key=value lines - a [member] section's whole body, and an [encperson]'s person (E2) or a [bandperson]'s
        /// (E13a) after its own keys - so the sections share one field list, read back by ParseMember. Round MP1: forClients (a
        /// client's snapshot, BuildText) leaves out the leader's platform id, handed items, worn pieces, ammo and the ammo pick,
        /// and writes "-" for snap= and postsnap= (SnapStub).
        /// </summary>
        private static void WriteMemberFields(StringBuilder sb, Member m, bool forClients)
        {
            sb.AppendLine("id=" + m.Id);
            sb.AppendLine("class=" + Clean(m.Class));
            sb.AppendLine("name=" + Clean(m.Name));
            if (!forClients && !string.IsNullOrEmpty(m.LeaderId)) sb.AppendLine("leader=" + Clean(m.LeaderId));   // round MP0; MP1: never to a client
            sb.AppendLine("born=" + m.Born.ToString(CultureInfo.InvariantCulture));
            // E12: one on the way is written a resident (an older build knows no Arriving: a resident with no body is missing
            // there, and "Call back" brings them) with arrive= below, which makes them Arriving again here.
            // Round op1: so is one on the road to or from an outpost (Transit; with transit= below), and a posted member whose body
            // is the snapshot (posted= below) - an older build would count a posted member with no body missing and drop them
            // after MissingDaysToPurge days, with no Call back for posted members. A parked traveler's hidden body is simply home there.
            bool heldPost = m.Status == MemberStatus.Garrison && !string.IsNullOrEmpty(m.Snap);
            bool asResident = m.Status == MemberStatus.Arriving || m.Status == MemberStatus.Transit || heldPost;
            sb.AppendLine("status=" + (asResident ? MemberStatus.Resident : m.Status));
            sb.AppendLine("post=" + Vec(m.Post));
            sb.AppendLine("join=" + m.JoinDay);
            sb.AppendLine("mood=" + m.Mood);
            sb.AppendLine("injured=" + (m.Injured ? 1 : 0));
            if (m.MedsAt > 0UL) sb.AppendLine("meds=" + m.MedsAt.ToString(CultureInfo.InvariantCulture));   // round md1: her last medical restock (a key older builds ignore)
            if (m.Bond != 0) sb.AppendLine("bond=" + m.Bond.ToString(CultureInfo.InvariantCulture));       // round U4: her Bond (a key older builds ignore; absent = 0)
            if (m.BondKills > 0) sb.AppendLine("bondkills=" + m.BondKillDay.ToString(CultureInfo.InvariantCulture) + ":" + m.BondKills.ToString(CultureInfo.InvariantCulture));   // finding 2
            sb.AppendLine("ranged=" + (m.Ranged ? 1 : 0));
            sb.AppendLine("missing=" + m.MissingDays);
            // round fx1: the scale Shooting's XP is in (before skills=, which reads it). A key older builds ignore: they read
            // the larger XP against their own thresholds, so the next kill can give several Shooting stars at once, and if
            // they save, the key is gone and this build scales that XP up again. Going back to an older build is unsupported.
            sb.AppendLine("shootxp=" + ScSkills.XpScale(Skill.Shooting).ToString(CultureInfo.InvariantCulture));
            var sk = new StringBuilder();
            for (int i = 0; i < Member.SkillCount; i++) { if (i > 0) sk.Append(','); sk.Append(ScNames.SkillKeys[i]).Append(':').Append(m.Skills[i].Level).Append(':').Append(m.Skills[i].Xp); }
            sb.AppendLine("skills=" + sk);
            sb.AppendLine("spec=" + m.Specialty + ":" + m.SpecLevel + ":" + m.SpecXp);
            if (m.Traits.Count > 0) sb.AppendLine("traits=" + string.Join(",", m.Traits.ToArray()));
            if (m.Status == MemberStatus.OnRun) sb.AppendLine("run=" + ScNames.Key(m.RunKind) + ":" + m.RunEndDay);
            if (m.Status == MemberStatus.Garrison && !heldPost) sb.AppendLine("outpost=" + m.OutpostId);
            if (heldPost) sb.AppendLine("posted=" + m.OutpostId);                       // round op1 (a key older builds ignore)
            if (m.Status == MemberStatus.Transit && m.RunLeg != RunLeg.None)
            {
                // round op1 (keys older builds ignore; never leg= and the rest, which an older build reads for any status)
                sb.AppendLine("transit=" + m.RunLeg + ":" + m.OutpostId.ToString(CultureInfo.InvariantCulture) + ":" + Vec(m.LegFrom) + ":" + Vec(m.LegTo) + ":"
                              + m.LegStart.ToString(CultureInfo.InvariantCulture) + ":" + m.LegEnd.ToString(CultureInfo.InvariantCulture) + ":"
                              + (m.Offline ? 1 : 0) + ":" + Clean(m.RunDestName));
            }
            if ((heldPost || m.Status == MemberStatus.Transit) && !string.IsNullOrEmpty(m.Snap))
                sb.AppendLine("postsnap=" + (forClients ? SnapStub : m.Snap));         // round op1: base64url (gzip'd: "2:"), never Clean()ed (as snap=)
            if (m.BodyAt != Vector3i.zero) sb.AppendLine("bodyat=" + Vec(m.BodyAt));    // round op1 review (a key older builds ignore)
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
#if SC_V34
                // round 33c (M33): a mining run's carry is its haul (haul= below) - 0 here, so an older build that reads it as a
                // materials run adds nothing to the stockpile
                sb.AppendLine("carry=" + (ScMining.IsDig(m) ? 0 : m.RunCarry));
#else
                sb.AppendLine("carry=" + m.RunCarry);
#endif
                sb.AppendLine("rollpending=" + (m.RollPending ? 1 : 0));
                if (m.RunHurt) sb.AppendLine("runhurt=1");                              // round r2: turned home hurt (a key older builds ignore)
                if (!string.IsNullOrEmpty(m.Snap)) sb.AppendLine("snap=" + (forClients ? SnapStub : m.Snap));   // base64url: never Clean()ed (it has no '=' or ';')
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
#if SC_V34
            if (!forClients)     // round MP1: items, worn pieces and the ammo pick stay on the server (bulky; the member panel's slots are host-only)
            {
                // round n12: the exact items she was handed, one line each (base64url bytes and an item name: Clean leaves them be)
                foreach (string line in ScHanded.Lines(m)) sb.AppendLine("handed=" + Clean(line));
                string worn = ScWardrobe.EncodeWorn(m);                                     // n12 review: the Wardrobe's picks she wears
                if (worn != null) sb.AppendLine("wardrobe=" + Clean(worn));
                if (!string.IsNullOrEmpty(m.AmmoGun) && !string.IsNullOrEmpty(m.AmmoPick))   // round U1: the ammo type chosen for her gun
                    sb.AppendLine("ammopick=" + Clean(m.AmmoGun) + "|" + Clean(m.AmmoPick));
                string wq = ScLoadout.EncodeQualities(m);                                   // round wq1: her weapons' real qualities
                if (wq != null) sb.AppendLine("wq=" + Clean(wq));
            }
#endif
            if (!forClients && m.Ammo != null) sb.AppendLine("ammo=" + Clean(ScAmmo.Encode(m.Ammo, m.Mags)));   // magazines %-escaped inside (M11); MP1: not to a client
            if (m.WatchPost != Vector3i.zero)
                sb.AppendLine("watch=" + Vec(m.WatchPost) + ":" + m.WatchStart.ToString(CultureInfo.InvariantCulture) + ":" + m.WatchPrepaid + ":" + Vec(m.WatchHome));
            if (m.WatchRestUntil > 0UL) sb.AppendLine("watchrest=" + m.WatchRestUntil.ToString(CultureInfo.InvariantCulture));
#if SC_V34
            if (m.CraftRoles >= 0) sb.AppendLine("craftroles=" + m.CraftRoles + ":" + m.CraftRolesFor);              // C1 (ScCraft)
            if (m.CraftXpCarry > 0f) sb.AppendLine("craftxp=" + m.CraftXpCarry.ToString("0.###", CultureInfo.InvariantCulture));
            if (m.Status == MemberStatus.OnRun && m.RunRadio) sb.AppendLine("runradio=1");                         // R2 (ScHandheld)
            if (m.Status == MemberStatus.OnRun && m.RunTalk != 0) sb.AppendLine("runtalk=" + m.RunTalk);            // R2 (ScRadioTalk)
            if (m.Status == MemberStatus.OnRun && ScMining.IsDig(m)) sb.AppendLine("runjob=" + Clean(ScMining.EncodeJob(m)));   // round 33c (M33)
            if (!string.IsNullOrEmpty(m.RunHaul)) sb.AppendLine("haul=" + Clean(m.RunHaul));                          // round 33c: after carry=
#endif
            if (m.Status == MemberStatus.Arriving)
                sb.AppendLine("arrive=" + Math.Max(1UL, m.ArriveAt).ToString(CultureInfo.InvariantCulture) + ":" + m.ArriveEnclave.ToString(CultureInfo.InvariantCulture) + ":"
                              + Vec(m.ArriveFrom) + ":" + Clean(m.ArriveName));
        }

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

        /// <summary>baseattack=state:arrive:started:bearing:size:spawned:killed:rounds:points (ScBaseAttack). A short or bad line is no attack.</summary>
        private static void ParseBaseAttack(Community c, string val)
        {
            string[] f = val.Split(':');
            ulong arrive, started;
            if (f.Length < 9 || !ulong.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out arrive)
                || !ulong.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out started)) return;
            int state = I(f[0]);
            int size = Clamp(I(f[4]), 0, 200);
            if (state < 1 || state > 3 || size <= 0) return;     // 3: fought without the player (D4); older builds drop it
            c.AttackState = state;
            c.AttackArrive = arrive;
            c.AttackStarted = started;
            c.AttackBearing = Clamp(I(f[3]), 0, 359);
            c.AttackSize = size;
            c.AttackSpawned = Clamp(I(f[5]), 0, c.AttackSize);
            c.AttackKilled = Clamp(I(f[6]), 0, c.AttackSize);
            c.AttackAmmoRounds = Math.Max(0, I(f[7]));
            c.AttackAmmoPoints = Math.Max(0, I(f[8]));
        }
    }
}
