using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Base defence D3: Watch posts and guard shifts (BaseDefence_Plan.md in the game root; its section 6 decides). Host only.
    ///   Posts     the scWatchPost block (BlockScWatchPost) inside the home zone, at most WatchMaxPosts, remembered in
    ///             [community] watchposts=: placing it registers it, removing it forgets it, and every ScanSeconds the zone's
    ///             loaded chunks are checked against the list (Chunk.IndexedBlocks["scwatchpost"]) both ways.
    ///   Shift     "Assign guard" (the post's radial opens the community window to pick one; sc watch assign) puts a resident
    ///             on watch for WatchShiftHours. WatchShiftAmmo bundles leave the stockpile at once (a shift cannot start with
    ///             fewer) and prepay the guard's resupplies (ScAmmo spends Member.WatchPrepaid before the stockpile; what is left
    ///             at the end is spent). The guard stays a Resident: Post becomes the cell above the block, the old post is kept
    ///             to go back to ([member] watch=). After a full shift the post is empty and the guard rests WatchRestHours
    ///             ([member] watchrest=); relieved early, no rest. A broken, picked-up or blocked post, a status other than
    ///             Resident, a lost home or WatchPosts off ends the watch and sends the guard back to the old post.
    ///   On watch  the AI gates keep them on the post (utilityai.xml's ScNotOnWatch; ScIntruder; ScFarFromPost never widened
    ///             by a live attack); they face the post's way (ScCommunity.ApplyPost, from the block's rotation), never relax
    ///             (ScRelax), keep the gun up close (ScArms) and see WatchSightMult times farther (sightRangeBase, put back
    ///             when the watch ends). No watchdog, load check, Repost or Unstick (ScTicker.Relink calls TickGuard instead):
    ///             a guard not at the post WatchPlaceSeconds after setting off is placed on it while the player is not looking.
    ///   Effects   every manned post cuts attention gain by WatchAttentionCut, at most WatchAttentionCutMax in all (ScNoise).
    ///             While the player is at the base a guard warns of a horde (decision Q6, both times): at the call
    ///             ("gathering", a marker on that side) and when a guard first sees an attacker ("spotted", the marker moves
    ///             onto the horde until the fight ends). D4's defence rating will read Manned().
    /// </summary>
    public static class ScWatch
    {
        public const string NavClass = "sc_base_attack";
        /// <summary>The block's IndexName (blocks.xml): every chunk indexes its Watch posts under it.</summary>
        public const string IndexName = "scwatchpost";
        private const ulong TicksPerHour = 1000UL;
        private const float ScanSeconds = 15f;
        /// <summary>A spot this close to the camera counts as seen whatever the angle; one farther than ViewRange does not.</summary>
        private const float ViewNear = 2f, ViewRange = 150f;
        /// <summary>Within about 60 degrees of the view's centre (a wide screen's edge at the default field of view).</summary>
        private const float ViewDot = 0.5f;

        /// <summary>A guard's session state, keyed by member id.</summary>
        private sealed class Guard
        {
            /// <summary>Real time the guard was first seen away from the post (-1: at it, or not looked at yet).</summary>
            public float AwaySince = -1f;
            public bool Arrived;
            public int Placed;
            public float NextWaitLog;
        }

        private static readonly Dictionary<int, Guard> guards = new Dictionary<int, Guard>();
        /// <summary>Entity ids whose sightRangeBase this raised (put back when their watch ends).</summary>
        private static readonly HashSet<int> boosted = new HashSet<int>();
        private static readonly List<Member> scratch = new List<Member>();
        private static float nextTick, nextScan, nextRefusalTip;
        private static NavObject marker;
        private static bool saveDue;

        public static void Reset()
        {
            guards.Clear();
            boosted.Clear();
            scratch.Clear();
            nextTick = nextScan = nextRefusalTip = 0f;
            marker = null;          // the world's NavObjectManager goes with the world
            saveDue = false;
        }

        /// <summary>The registry was (re)loaded: the session's guard states start again, the marker is rebuilt from attackwarn=.</summary>
        public static void OnRegistryChanged()
        {
            guards.Clear();
            RemoveMarker();
        }

        private static ulong ShiftTicks { get { return (ulong)(Math.Max(0.25f, ScConfig.WatchShiftHours) * TicksPerHour); } }
        private static ulong RestTicks { get { return (ulong)(Math.Max(0f, ScConfig.WatchRestHours) * TicksPerHour); } }
        private static ulong Now { get { World w = ScCommunity.World; return w != null ? w.worldTime : 0UL; } }

        // ------------------------------------------------------------------ queries (cheap: the AI asks them) ----

        public static List<Vector3i> Posts { get { return ScRegistry.Community.WatchPosts; } }

        public static bool IsPost(Vector3i pos) { return ScRegistry.Community.WatchPosts.Contains(pos); }

        /// <summary>The member is on watch (Member.WatchPost set).</summary>
        public static bool OnWatch(Member m) { return m != null && m.WatchPost != Vector3i.zero; }

        /// <summary>The entity is a member on watch (UAI's ScNotOnWatch, ScRelax, ScArms).</summary>
        public static bool Guarding(int entityId)
        {
            Member m = ScRegistry.GetMember(entityId);
            return m != null && m.WatchPost != Vector3i.zero;
        }

        /// <summary>The member on watch at this post, or null.</summary>
        public static Member GuardOf(Vector3i post)
        {
            if (post == Vector3i.zero) return null;     // every member NOT on watch has WatchPost zero (the review's L1)
            foreach (Member m in ScRegistry.Members.Values) if (m.WatchPost == post) return m;
            return null;
        }

        /// <summary>
        /// Manned posts: members on watch (a shift running, whether or not the guard has reached the post). The attention
        /// cut reads it, and so will D4's defence rating (+10 a manned post), which this milestone does not build.
        /// </summary>
        public static int Manned()
        {
            int n = 0;
            foreach (Member m in ScRegistry.Members.Values) if (m.WatchPost != Vector3i.zero) n++;
            return n;
        }

        /// <summary>The share of attention gain left (ScNoise): 1 - WatchAttentionCut per manned post, never below 1 - WatchAttentionCutMax.</summary>
        public static float AttentionFactor()
        {
            if (!ScConfig.WatchPosts) return 1f;
            int n = Manned();
            if (n <= 0) return 1f;
            return 1f - Mathf.Clamp01(Math.Min(ScConfig.WatchAttentionCutMax, ScConfig.WatchAttentionCut * n));
        }

        /// <summary>Points the guard's shift prepaid that are left (0 for anyone not on watch) - ScAmmo spends these first.</summary>
        public static int Prepaid(Member m)
        {
            return OnWatch(m) ? Math.Max(0, m.WatchPrepaid) : 0;
        }

        /// <summary>Spends up to 'points' of the shift's prepaid ammo; returns what it covered.</summary>
        public static int SpendPrepaid(Member m, int points)
        {
            if (!OnWatch(m) || points <= 0) return 0;
            int used = Math.Min(points, Math.Max(0, m.WatchPrepaid));
            m.WatchPrepaid -= used;
            return used;
        }

        // ------------------------------------------------------------------ naming ----

        /// <summary>1-based place in the post list, 0 when the block is no registered post.</summary>
        public static int Number(Vector3i post) { return Posts.IndexOf(post) + 1; }

        /// <summary>The side of the home block a spot is on: "north", "south-west" ... ("centre" on the home column).</summary>
        public static string SideOf(Vector3 p)
        {
            Vector3i home = ScRegistry.Community.Home;
            float dx = p.x - (home.x + 0.5f), dz = p.z - (home.z + 0.5f);
            if (dx * dx + dz * dz < 4f) return "centre";
            return ScBaseAttack.Side(Mathf.RoundToInt(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg));
        }

        /// <summary>The compass word for a direction: "north" for +z, "east" for +x.</summary>
        public static string Compass(Vector3 dir)
        {
            return ScBaseAttack.Side(Mathf.RoundToInt(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg));
        }

        /// <summary>"Watch post 2 (north-east)".</summary>
        public static string Label(Vector3i post)
        {
            int n = Number(post);
            return string.Format(Localization.Get("scWatchPostLabel"), n > 0 ? n.ToString(CultureInfo.InvariantCulture) : "?",
                                 SideOf(new Vector3(post.x + 0.5f, post.y, post.z + 0.5f)));
        }

        /// <summary>Whole game hours left of a guard's shift (rounded up).</summary>
        public static int HoursLeft(Member m)
        {
            if (!OnWatch(m)) return 0;
            ulong end = m.WatchStart + ShiftTicks, now = Now;
            return now >= end ? 0 : (int)((end - now + TicksPerHour - 1) / TicksPerHour);
        }

        private static int RestHoursLeft(Member m)
        {
            ulong now = Now;
            return m == null || m.WatchRestUntil <= now ? 0 : (int)((m.WatchRestUntil - now + TicksPerHour - 1) / TicksPerHour);
        }

        /// <summary>
        /// A watch message: every text takes the same four arguments, {0} the member's name, {1} and {2} numbers (hours,
        /// bundles, the post cap), {3} the post's label - each uses the ones it needs.
        /// </summary>
        public static string ErrorText(string key, Member m, Vector3i post)
        {
            if (string.IsNullOrEmpty(key)) key = "scInternal";
            string name = m != null ? m.Name : "";
            object a = "", b = "";
            switch (key)
            {
                case "scWatchResting": a = RestHoursLeft(m); break;
                case "scWatchNoAmmo": a = ScConfig.WatchShiftAmmo; b = ScRegistry.Community.Get(Resource.Ammo); break;
                case "scWatchTooMany": a = ScConfig.WatchMaxPosts; break;
                case "scWatchManned": { Member g = GuardOf(post); if (g != null) name = g.Name; break; }
            }
            try
            {
                return string.Format(Localization.Get(key), name, a, b, post != Vector3i.zero ? Label(post) : "");
            }
            catch (FormatException)
            {
                return Localization.Get(key);
            }
        }

        public static string StatusText(Member m, bool brief)
        {
            if (brief || !OnWatch(m)) return Localization.Get("scStOnWatch");
            return string.Format(Localization.Get("scStOnWatchLong"), Label(m.WatchPost), HoursLeft(m));
        }

        /// <summary>The block's E prompt: the post, its guard and the hours left, or why it is not a working post.</summary>
        public static string ActivationText(Vector3i pos)
        {
            try
            {
                if (!ScCommunity.IsHost || !ScRegistry.Loaded) return Localization.Get("scWatchPost");
                if (!IsPost(pos))
                {
                    string why = ScConfig.WatchPosts ? PostRefusal(pos) : "scWatchOff";
                    // a post that counts but was never registered (placed before the home was set, stamped without
                    // OnBlockAdded): registered the moment the player looks at it, not at the next scan (the review's L5)
                    if (why == null) NotePost(pos, false);
                    if (!IsPost(pos))
                        return string.Format(Localization.Get("scWatchActNotPost"), ErrorText(why ?? "scWatchErrNotPost", null, Vector3i.zero));
                }
                Member g = GuardOf(pos);
                if (g == null) return string.Format(Localization.Get("scWatchActEmpty"), Label(pos));
                return string.Format(Localization.Get("scWatchActManned"), Label(pos), g.Name, HoursLeft(g));
            }
            catch (Exception e)
            {
                ScLog.Once("watch-acttext", e);
                return Localization.Get("scWatchPost");
            }
        }

        // ------------------------------------------------------------------ the posts ----

        /// <summary>
        /// Why a Watch post may not go here (a localization key for ErrorText), or null. Null on a client (it has no
        /// registry) and with WatchPosts off (the block is then just a cube).
        /// </summary>
        public static string PlaceRefusal(Vector3i pos)
        {
            if (!ScCommunity.IsHost || !ScRegistry.Loaded || !ScConfig.WatchPosts) return null;
            return PostRefusal(pos);
        }

        /// <summary>Why the block at pos is no working post: no home, outside the zone, or the cap reached (null: it can be one).</summary>
        private static string PostRefusal(Vector3i pos)
        {
            Community c = ScRegistry.Community;
            if (!c.HasHome) return "scWatchNoHome";
            if (!ScZone.Contains(pos)) return "scWatchOutside";
            if (!c.WatchPosts.Contains(pos) && c.WatchPosts.Count >= Math.Max(0, ScConfig.WatchMaxPosts)) return "scWatchTooMany";
            return null;
        }

        /// <summary>A refused placement (BlockScWatchPost.CanPlaceBlockAt on the press): the reason, at most once a second.</summary>
        public static void TellRefusal(EntityPlayerLocal player, string key)
        {
            if (player == null || Time.unscaledTime < nextRefusalTip) return;
            nextRefusalTip = Time.unscaledTime + 1f;
            ScCommunity.Tell(player, ErrorText(key, null, Vector3i.zero), "ui_denied");
        }

        /// <summary>BlockScWatchPost.OnBlockAdded (host, main thread): a Watch post placed - registered when it counts.</summary>
        public static void NotePost(Vector3i pos, bool byPlayer)
        {
            try
            {
                if (!ScCommunity.IsHost || !ScRegistry.Loaded || !ScConfig.WatchPosts) return;
                if (IsPost(pos)) return;
                EntityPlayerLocal player = ScCommunity.LocalPlayer;
                bool near = byPlayer && player != null && (player.position - new Vector3(pos.x + 0.5f, pos.y, pos.z + 0.5f)).sqrMagnitude < 400f;
                string why = PostRefusal(pos);
                if (why != null)
                {
                    ScLog.Out("watch post at " + pos + " not registered: " + ErrorText(why, null, Vector3i.zero));
                    if (near) ScCommunity.Tell(player, ErrorText(why, null, Vector3i.zero), "ui_denied");
                    return;
                }
                Posts.Add(pos);
                ScLog.Out(Label(pos) + " at " + pos + " registered (" + Posts.Count + " of " + ScConfig.WatchMaxPosts + ")");
                if (near) ScCommunity.Tell(player, ErrorText("scWatchReady", null, pos), "");
                ScRegistry.SaveAtomic();
            }
            catch (Exception e)
            {
                ScLog.Once("watch-note", e);
            }
        }

        /// <summary>BlockScWatchPost.OnBlockRemoved, and the scan: a post that is gone is forgotten, its guard goes back to their post.</summary>
        public static void ForgetPost(Vector3i pos, string why)
        {
            try
            {
                if (!ScCommunity.IsHost || !ScRegistry.Loaded) return;
                string label = Label(pos);
                Member g = GuardOf(pos);
                if (g != null) End(g, null, "scWatchPostGone", false, "the post " + why, ScCommunity.LocalPlayer);
                if (Posts.Remove(pos) || g != null)
                {
                    ScLog.Out(label + " at " + pos + " forgotten (" + why + ")");
                    ScRegistry.SaveAtomic();
                    saveDue = false;                    // saved now, not again on the next tick (the review's L3)
                }
            }
            catch (Exception e)
            {
                ScLog.Once("watch-forget", e);
            }
        }

        /// <summary>
        /// Every ScanSeconds (and sc watch scan): remembered posts outside the zone, or loaded and no Watch post any more,
        /// are forgotten; Watch posts in the zone's loaded chunks that are not remembered (placed before the home was set,
        /// or stamped by something that never ran OnBlockAdded here) are added up to the cap.
        /// </summary>
        public static void Scan(World w)
        {
            if (w == null || !ScRegistry.Loaded) return;
            Community c = ScRegistry.Community;
            for (int i = c.WatchPosts.Count - 1; i >= 0; i--)
            {
                Vector3i pos = c.WatchPosts[i];
                string why = null;
                if (!c.HasHome || !ScZone.Contains(pos)) why = "outside the home zone";
                else if (ScZone.IsLoaded(pos))
                {
                    BlockValue bv = w.GetBlock(pos);
                    if (bv.isair || !(bv.Block is BlockScWatchPost)) why = "no Watch post there any more";
                }
                if (why != null) ForgetPost(pos, why);
            }
            if (!c.HasHome || !ScConfig.WatchPosts) return;
            int xMin, xMax, zMin, zMax;
            ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
            bool added = false;
            for (int cx = World.toChunkXZ(xMin); cx <= World.toChunkXZ(xMax); cx++)
            {
                for (int cz = World.toChunkXZ(zMin); cz <= World.toChunkXZ(zMax); cz++)
                {
                    Chunk chunk = w.GetChunkSync(cx, cz) as Chunk;
                    if (chunk == null || chunk.IndexedBlocks == null) continue;
                    List<Vector3i> found;
                    if (!chunk.IndexedBlocks.TryGetValue(IndexName, out found) || found == null) continue;
                    for (int i = 0; i < found.Count; i++)
                    {
                        Vector3i pos = chunk.ToWorldPos(found[i]);
                        if (c.WatchPosts.Contains(pos) || PostRefusal(pos) != null) continue;
                        if (!(w.GetBlock(pos).Block is BlockScWatchPost)) continue;
                        c.WatchPosts.Add(pos);
                        added = true;
                        ScLog.Out(Label(pos) + " at " + pos + " found in the zone - registered (" + c.WatchPosts.Count + " of " + ScConfig.WatchMaxPosts + ")");
                    }
                }
            }
            if (added) ScRegistry.SaveAtomic();
        }

        /// <summary>The way the post faces: the block's rotation (TowardsPlacer: the way the player looked when placing it).</summary>
        public static Vector3 Facing(BlockValue bv)
        {
            int rot = bv.rotation;
            Vector3 f = (rot >= 0 && rot < 32 ? BlockShapeNew.GetRotationStatic(rot) : Quaternion.identity) * Vector3.forward;
            f.y = 0f;
            return f.sqrMagnitude < 0.01f ? Vector3.forward : f.normalized;
        }

        /// <summary>
        /// Where a guard on watch looks (ScCommunity.ApplyPost): ten metres out the way the post faces, a little below eye
        /// height - over the parapet at the approach. False for anyone not on watch, or while the post's chunk is not loaded.
        /// </summary>
        public static bool LookAt(Member m, Vector3 post, out Vector3 look)
        {
            look = Vector3.zero;
            if (!OnWatch(m) || !ScZone.IsLoaded(m.WatchPost)) return false;
            World w = ScCommunity.World;
            if (w == null) return false;
            BlockValue bv = w.GetBlock(m.WatchPost);
            if (!(bv.Block is BlockScWatchPost)) return false;
            look = post + Facing(bv) * 10f + new Vector3(0f, 1.2f, 0f);
            return true;
        }

        private static bool Room(World w, Vector3i post)
        {
            return ScCommunity.Standable(w, post.x, post.y + 1, post.z);
        }

        // ------------------------------------------------------------------ assigning and relieving ----

        /// <summary>Can this member start a shift at this post? error: a localization key for ErrorText.</summary>
        public static bool CanAssign(Vector3i post, Member m, out string error)
        {
            error = null;
            if (!ScCommunity.IsHost) { error = "scHostOnly"; return false; }
            if (!ScConfig.WatchPosts) { error = "scWatchOff"; return false; }
            if (!IsPost(post)) { error = "scWatchErrNotPost"; return false; }
            if (GuardOf(post) != null) { error = "scWatchManned"; return false; }
            if (m == null) { error = "scInternal"; return false; }
            if (OnWatch(m)) { error = "scWatchElsewhere"; return false; }
            if (m.Status != MemberStatus.Resident || ScMissing.IsMissing(m)) { error = "scWatchNotHome"; return false; }
            if (m.Injured) { error = "scWatchInjured"; return false; }
            if (m.Mood < 25) { error = "scRefuseMood"; return false; }
            if (m.WatchRestUntil > Now) { error = "scWatchResting"; return false; }
            if (ScRegistry.Community.Get(Resource.Ammo) < Math.Max(0, ScConfig.WatchShiftAmmo)) { error = "scWatchNoAmmo"; return false; }
            World w = ScCommunity.World;
            if (w != null && ScZone.IsLoaded(post) && !Room(w, post)) { error = "scWatchErrBlocked"; return false; }
            return true;
        }

        /// <summary>A shift starts: the ammo leaves the stockpile, the post becomes the guard's, the old post is kept.</summary>
        public static bool Assign(Vector3i post, Member m, EntityPlayerLocal player, out string error)
        {
            if (!CanAssign(post, m, out error)) return false;
            try
            {
                Community c = ScRegistry.Community;
                int bundles = Math.Max(0, ScConfig.WatchShiftAmmo);
                c.Add(Resource.Ammo, -bundles);
                c.AmmoOpened += bundles;                    // the day's ledger: "ammo used"
                m.WatchPost = post;
                m.WatchStart = Math.Max(1UL, Now);
                m.WatchPrepaid = bundles * Math.Max(1, ScConfig.PointsPerBundle[(int)Resource.Ammo]);
                m.WatchHome = m.Post;
                m.Post = post + Vector3i.up;
                m.PostChecked = true;
                m.BadPosts.Remove(m.Post);
                m.StillSeconds = m.AwaySeconds = 0;
                m.UnstickTimes.Clear();
                guards[m.Id] = new Guard { AwaySince = Time.unscaledTime };
                EntityAlive npc = ScCommunity.FindNpc(m.Id);
                if (npc != null && !npc.IsDead())
                {
                    ScCommunity.DropPath(npc);
                    ScCommunity.ApplyPost(npc, ScCommunity.PostCenter(m));
                    Boost(npc);
                }
                ScRegistry.SaveAtomic();
                if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get("scWatchAssigned"), m.Name, ScConfig.WatchShiftHours, bundles, Label(post)), "");
                ScLog.Out(m.Name + " is on watch at " + Label(post) + " " + post + " for " + ScConfig.WatchShiftHours + " h (" + bundles + " ammo bundle(s) from the stockpile, "
                          + c.Get(Resource.Ammo) + " left; " + m.WatchPrepaid + " point(s) prepaid); back to " + m.WatchHome + " after");
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("watch-assign", e);
                error = "scInternal";
                return false;
            }
        }

        /// <summary>The radial's "Relieve guard" and sc watch relieve: the shift ends early - no rest, nothing refunded.</summary>
        public static bool Relieve(Member m, EntityPlayerLocal player, string why)
        {
            if (!ScCommunity.IsHost || !OnWatch(m)) return false;
            End(m, null, "scWatchRelieved", false, why, player);
            ScRegistry.SaveAtomic();
            saveDue = false;
            return true;
        }

        /// <summary>sc watch end: the shift ends now as if its hours had run (the rest applies).</summary>
        public static bool EndShiftNow(Member m, EntityPlayerLocal player)
        {
            if (!ScCommunity.IsHost || !OnWatch(m)) return false;
            End(m, null, "scWatchShiftOver", true, "ended from the console", player);
            ScRegistry.SaveAtomic();
            saveDue = false;
            return true;
        }

        /// <summary>
        /// The watch ends: the post is empty, the guard's sight is put back and they go back to the post they had (Station
        /// checks it and picks a new one when it is gone); after a full shift ("full") they rest. tellKey: the tooltip, shown
        /// only while the player is at the base. The caller saves.
        /// </summary>
        private static void End(Member m, EntityAlive npc, string tellKey, bool full, string why, EntityPlayerLocal player)
        {
            Vector3i post = m.WatchPost;
            string label = Label(post);
            Vector3i home = m.WatchHome;
            int left = m.WatchPrepaid;
            m.WatchPost = Vector3i.zero;
            m.WatchHome = Vector3i.zero;
            m.WatchStart = 0UL;
            m.WatchPrepaid = 0;
            if (full) m.WatchRestUntil = Now + RestTicks;
            guards.Remove(m.Id);
            World w = ScCommunity.World;
            // the old post when it is still in the zone; else a fresh one now (an unloaded body keeps none rather than (0,0,0):
            // a Guard order with no post would walk it to the world's origin), else it stays where it was
            Vector3i back = home != Vector3i.zero && ScZone.Contains(home) ? home : Vector3i.zero;
            if (back == Vector3i.zero && w != null && ScZone.HasHome)
            {
                int tier;
                back = ScCommunity.ChoosePost(w.GetGameRandom(), m.BadPosts, out tier);
            }
            if (back != Vector3i.zero) m.Post = back;
            m.PostChecked = false;                       // checked again once its chunk is loaded (CheckPostOnce)
            m.StillSeconds = m.AwaySeconds = 0;
            if (npc == null) npc = ScCommunity.FindNpc(m.Id);
            string sight = "body not loaded";
            if (npc != null && !npc.IsDead())
            {
                Unboost(npc);
                sight = "sight " + npc.sightRangeBase.ToString("0", CultureInfo.InvariantCulture) + " m";
                if (m.Status == MemberStatus.Resident)
                {
                    ScCommunity.Station(m, npc);            // the Guard order and the guard position for the post they go back to
                    ScCommunity.DropPath(npc);              // not on up to the Watch post
                }
            }
            ScLog.Out(m.Name + "'s watch at " + label + " " + post + " ended (" + why + ")" + (full ? "; resting " + ScConfig.WatchRestHours + " h" : "")
                      + "; " + left + " prepaid point(s) unspent; back to the post " + m.Post + "; " + sight);
            if (tellKey != null && player != null && ScBaseAttack.PlayerHome(player))
                ScCommunity.Tell(player, string.Format(Localization.Get(tellKey), m.Name, ScConfig.WatchRestHours, "", label), "");
            saveDue = true;
        }

        /// <summary>The post's "Assign guard": the community window opens to pick one, or the reason shows.</summary>
        public static void OpenAssign(Vector3i post, EntityPlayerLocal player)
        {
            if (player == null) return;
            string err = null;
            if (!ScCommunity.IsHost) err = "scHostOnly";
            else if (!ScConfig.WatchPosts) err = "scWatchOff";
            else if (!IsPost(post)) err = PostRefusal(post) ?? "scWatchErrNotPost";
            else if (GuardOf(post) != null) err = "scWatchManned";
            else if (ScRegistry.Community.Get(Resource.Ammo) < Math.Max(0, ScConfig.WatchShiftAmmo)) err = "scWatchNoAmmo";
            if (err != null)
            {
                ScCommunity.Tell(player, ErrorText(err, null, post), "ui_denied");
                return;
            }
            XUiC_ScCommunity.PendingWatch = post;
            ScCommunity.OpenWindow(player);
        }

        /// <summary>The post's "Relieve guard".</summary>
        public static void RelieveAt(Vector3i post, EntityPlayerLocal player)
        {
            Member g = ScCommunity.IsHost ? GuardOf(post) : null;
            if (g == null)
            {
                if (player != null) ScCommunity.Tell(player, ErrorText(ScCommunity.IsHost ? "scWatchNoGuard" : "scHostOnly", null, post), "ui_denied");
                return;
            }
            Relieve(g, player, "relieved at the post");
        }

        // ------------------------------------------------------------------ the guard ----

        private static Guard StateOf(int id)
        {
            Guard g;
            if (!guards.TryGetValue(id, out g)) guards[id] = g = new Guard();
            return g;
        }

        private static float ClassSight(EntityAlive npc)
        {
            EntityClass ec;
            return EntityClass.list != null && EntityClass.list.TryGetValue(npc.entityClass, out ec) && ec != null ? ec.SightRange : 0f;
        }

        /// <summary>Sight x WatchSightMult (sightRangeBase is the class's SightRange again after every load: kept up each second).</summary>
        private static void Boost(EntityAlive npc)
        {
            float sight = ClassSight(npc);
            if (sight <= 0f) return;
            float want = sight * Math.Max(1f, ScConfig.WatchSightMult);
            if (Mathf.Abs(npc.sightRangeBase - want) > 0.01f) npc.sightRangeBase = want;
            boosted.Add(npc.entityId);
        }

        /// <summary>The class's own sight again, for a body this raised (ScCommunity.CleanupStale calls it too).</summary>
        public static void Unboost(EntityAlive npc)
        {
            if (npc == null || !boosted.Remove(npc.entityId)) return;
            float sight = ClassSight(npc);
            if (sight > 0f) npc.sightRangeBase = sight;
        }

        /// <summary>
        /// ScTicker.Relink, every second, for a loaded resident on watch - in place of CheckPostOnce, the post self-heal and
        /// the watchdog: the post must still be a Watch post with room on it (else the watch ends), the guard position and
        /// the facing are kept, the sight raised, and a guard away from the post for WatchPlaceSeconds is placed on it
        /// while the player is not looking.
        /// </summary>
        public static void TickGuard(Member m, EntityAlive npc, EntityPlayerLocal player)
        {
            try
            {
                World w = ScCommunity.World;
                if (w == null || !OnWatch(m) || npc == null || npc.IsDead()) return;
                Guard g = StateOf(m.Id);
                Boost(npc);
                bool loaded = ScZone.IsLoaded(m.WatchPost);
                Vector3i cell = m.WatchPost + Vector3i.up;
                if (loaded)
                {
                    BlockValue bv = w.GetBlock(m.WatchPost);
                    if (bv.isair || !(bv.Block is BlockScWatchPost)) { ForgetPost(m.WatchPost, "is gone"); return; }
                    if (!Room(w, m.WatchPost))
                    {
                        End(m, npc, "scWatchBlockedEnd", false, "no room to stand on the post", player);
                        return;
                    }
                }
                if (m.Post != cell) m.Post = cell;           // nothing else may move a guard's post
                Vector3 post = ScCommunity.PostCenter(m);
                IEntityOrderReceiverSDX orders = ScNpc.Orders(npc);
                Vector3 look;
                if (orders != null && (orders.GuardPosition != post || (LookAt(m, post, out look) && (orders.GuardLookPosition - look).sqrMagnitude > 0.01f)))
                    ScCommunity.ApplyPost(npc, post);
                float now = Time.unscaledTime;
                // the pause menu stops the world but not this ticker, and AwaySince is real time, which runs on through a
                // pause: a pause starts the 30 s count again (the review's L4, the re-check's N1)
                if (GameManager.Instance != null && GameManager.Instance.IsPaused())
                {
                    if (g.AwaySince >= 0f) g.AwaySince = -1f;
                    return;
                }
                if (UAI.UAIConsiderationScAtPost.AtPost(npc, m, 0.8f, 1.2f))
                {
                    g.AwaySince = -1f;
                    if (!g.Arrived)
                    {
                        g.Arrived = true;
                        ScLog.Out(m.Name + " reached " + Label(m.WatchPost) + " " + m.WatchPost + (g.Placed > 0 ? " (placed " + g.Placed + "x)" : ""));
                    }
                    return;
                }
                if (g.AwaySince < 0f) { g.AwaySince = now; return; }
                if (now - g.AwaySince < ScConfig.WatchPlaceSeconds || !loaded) return;
                if (InView(player, npc.getHeadPosition()) || InView(player, post + new Vector3(0f, 1f, 0f)))
                {
                    if (now >= g.NextWaitLog)
                    {
                        g.NextWaitLog = now + 30f;
                        ScLog.Verbose(m.Name + " is not at " + Label(m.WatchPost) + " after " + (int)(now - g.AwaySince) + " s - placed once the player looks away");
                    }
                    return;
                }
                Place(m, npc, g, "not there " + (int)(now - g.AwaySince) + " s after setting off, the player not looking");
            }
            catch (Exception e)
            {
                ScLog.Once("watch-guard", e);
            }
        }

        private static void Place(Member m, EntityAlive npc, Guard g, string why)
        {
            Vector3 post = ScCommunity.PostCenter(m);
            ScCommunity.DropPath(npc);
            npc.SetPosition(post, true);
            m.LastPos = post;
            ScCommunity.ApplyPost(npc, post);
            g.AwaySince = -1f;
            g.Placed++;
            ScLog.Out(m.Name + " placed on " + Label(m.WatchPost) + " " + m.WatchPost + " (" + why + ")");
        }

        /// <summary>sc watch place: the guard goes onto the post now, looked at or not.</summary>
        public static bool PlaceNow(Member m)
        {
            EntityAlive npc = OnWatch(m) ? ScCommunity.FindNpc(m.Id) : null;
            if (npc == null || npc.IsDead() || !ScZone.IsLoaded(m.WatchPost)) return false;
            Place(m, npc, StateOf(m.Id), "the console");
            return true;
        }

        /// <summary>The player can see this spot: near the camera, or within about 60 degrees of where it points and in range.</summary>
        private static bool InView(EntityPlayerLocal player, Vector3 p)
        {
            if (player == null) return false;
            Transform cam = player.cameraTransform;
            Vector3 from = cam != null ? cam.position : player.getHeadPosition();
            Vector3 fwd = cam != null ? cam.forward : player.GetLookVector();
            Vector3 d = p - from;
            float dist = d.magnitude;
            if (dist <= ViewNear) return true;
            if (dist > ViewRange) return false;
            return Vector3.Dot(fwd, d / dist) > ViewDot;
        }

        // ------------------------------------------------------------------ the ticker ----

        /// <summary>
        /// ScTicker, every second (after ScBaseAttack): shifts that ran out, guards no longer at home, posts gone; the
        /// zone scan; the call-outs and the marker.
        /// </summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            try
            {
                if (w == null || player == null || !ScRegistry.Loaded) return;
                float now = Time.unscaledTime;
                if (now < nextTick) return;
                nextTick = now + 1f;
                if (GameManager.Instance != null && GameManager.Instance.IsPaused()) return;
                Community c = ScRegistry.Community;
                ulong t = w.worldTime;
                scratch.Clear();
                foreach (Member m in ScRegistry.Members.Values) if (m.WatchPost != Vector3i.zero || m.WatchRestUntil > 0UL) scratch.Add(m);
                for (int i = 0; i < scratch.Count; i++)
                {
                    Member m = scratch[i];
                    if (m.WatchRestUntil > 0UL)
                    {
                        if (m.WatchRestUntil > t + RestTicks)
                        {
                            // the clock went back (a time command, an older save): the rest runs its full length from now
                            m.WatchRestUntil = t + RestTicks;
                            saveDue = true;
                            ScLog.Out(m.Name + "'s rest after a watch starts again: the clock went back");
                        }
                        if (t >= m.WatchRestUntil) { m.WatchRestUntil = 0UL; saveDue = true; }
                    }
                    if (m.WatchPost == Vector3i.zero) continue;
                    if (m.WatchStart > t)
                    {
                        // the clock went back: what was served is unknown now, so the shift runs its full length from now
                        m.WatchStart = t;
                        saveDue = true;
                        ScLog.Out(m.Name + "'s watch shift starts again: the clock went back");
                    }
                    string why = null, key = null;
                    bool full = false;
                    if (!ScConfig.WatchPosts) why = "WatchPosts is off";
                    else if (m.Status != MemberStatus.Resident) why = "no longer living at home (" + m.Status + ")";
                    else if (!c.HasHome || !c.WatchPosts.Contains(m.WatchPost)) { why = "the post is no Watch post any more"; key = "scWatchPostGone"; }
                    else if (t >= m.WatchStart + ShiftTicks) { why = "the shift is over"; key = "scWatchShiftOver"; full = true; }
                    if (why != null) End(m, null, key, full, why, player);
                }
                scratch.Clear();
                if (now >= nextScan) { nextScan = now + ScanSeconds; Scan(w); }
                TickCallouts(w, player, c);
                if (saveDue) { saveDue = false; ScRegistry.SaveAtomic(); }
            }
            catch (Exception e)
            {
                ScLog.Once("watch-tick", e);
            }
        }

        // ------------------------------------------------------------------ call-outs (decision Q6) ----

        /// <summary>
        /// While the player is at the base (ScBaseAttack.PlayerHome), a guard on watch with a body in the world tells of the
        /// open attack: once while it is called ("gathering", the first second both hold - at the call itself, or later if
        /// the guard or the player was not there yet), and once when a guard first sees a live attacker ("spotted"). The
        /// marker stays on the gathering side, then on the live attackers' centre, while the player is within
        /// BaseAttackLeaveRange of the zone; it goes when the attack ends.
        /// </summary>
        private static void TickCallouts(World w, EntityPlayerLocal player, Community c)
        {
            if (!ScBaseAttack.Open) { RemoveMarker(); return; }
            if (ScConfig.WatchPosts && ScBaseAttack.PlayerHome(player))
            {
                if (c.AttackWarned == 0 && c.AttackState == ScBaseAttack.StateCalled)
                {
                    foreach (Member m in ScRegistry.Members.Values)
                    {
                        if (!OnWatch(m) || GuardBody(m) == null) continue;
                        c.AttackWarned = 1;
                        saveDue = true;
                        ScCommunity.Tell(player, string.Format(Localization.Get("scWatchGathering"), m.Name, ScBaseAttack.Side(c.AttackBearing)), "ui_denied");
                        ScLog.Out(m.Name + " on watch calls out the horde gathering to the " + ScBaseAttack.Side(c.AttackBearing));
                        break;
                    }
                }
                if (c.AttackWarned < 2 && ScBaseAttack.Live)
                {
                    foreach (Member m in ScRegistry.Members.Values)
                    {
                        if (!OnWatch(m)) continue;
                        EntityAlive npc = GuardBody(m);
                        EntityAlive z = npc != null ? ScBaseAttack.SeenBy(w, npc) : null;
                        if (z == null) continue;
                        c.AttackWarned = 2;
                        saveDue = true;
                        string side = SideOf(z.position);
                        ScCommunity.Tell(player, string.Format(Localization.Get("scWatchSpotted"), m.Name, side), "ui_denied");
                        ScLog.Out(m.Name + " on watch spotted the horde to the " + side + " (" + z.EntityName + " #" + z.entityId + ", "
                                  + (int)Vector3.Distance(npc.position, z.position) + " m out)");
                        break;
                    }
                }
            }
            if (c.AttackWarned > 0 && ScBaseAttack.OutsideZone(player.position) <= ScConfig.BaseAttackLeaveRange)
            {
                Vector3 at;
                if (!(c.AttackWarned >= 2 && ScBaseAttack.Live && ScBaseAttack.Centroid(w, out at))) at = ScBaseAttack.ApproachPoint();
                ShowMarker(at + new Vector3(0f, 2f, 0f), player);
            }
            else RemoveMarker();
        }

        private static EntityAlive GuardBody(Member m)
        {
            EntityAlive npc = ScCommunity.FindNpc(m.Id);
            return npc != null && !npc.IsDead() ? npc : null;
        }

        /// <summary>The marker is still the one this registered (the manager pools and reuses removed ones).</summary>
        private static bool MarkerLive(NavObjectManager nav)
        {
            return marker != null && nav != null && marker.IsActive && nav.NavObjectList.Contains(marker)
                   && marker.NavObjectClass != null && marker.NavObjectClass.NavObjectClassName == NavClass;
        }

        private static void ShowMarker(Vector3 at, EntityPlayer owner)
        {
            try
            {
                NavObjectManager nav = NavObjectManager.HasInstance ? NavObjectManager.Instance : null;
                if (nav == null || owner == null || NavObjectClass.GetNavObjectClass(NavClass) == null) return;
                if (!MarkerLive(nav))
                {
                    marker = nav.RegisterNavObject(NavClass, at, "", false, -1, owner);
                    if (marker != null) marker.name = Localization.Get("scWatchMarker");
                    return;
                }
                if ((marker.TrackedPosition - at).sqrMagnitude > 0.25f) marker.TrackedPosition = at;
            }
            catch (Exception e)
            {
                ScLog.Once("watch-marker", e);
            }
        }

        private static void RemoveMarker()
        {
            if (marker == null) return;
            try
            {
                NavObjectManager nav = NavObjectManager.HasInstance ? NavObjectManager.Instance : null;
                if (nav != null) nav.UnRegisterNavObjectByClass(NavClass);
            }
            catch (Exception e)
            {
                ScLog.Once("watch-unmarker", e);
            }
            marker = null;
        }

        // ------------------------------------------------------------------ console ----

        /// <summary>A post by its number in the list (1-based) or by "x,y,z"; Vector3i.zero when neither.</summary>
        public static Vector3i ParsePost(string s)
        {
            if (string.IsNullOrEmpty(s)) return Vector3i.zero;
            int n;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n >= 1 && n <= Posts.Count ? Posts[n - 1] : Vector3i.zero;
            string[] f = s.Split(',');
            int x, y, z;
            if (f.Length == 3 && int.TryParse(f[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
                && int.TryParse(f[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out y)
                && int.TryParse(f[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out z)) return new Vector3i(x, y, z);
            return Vector3i.zero;
        }

        /// <summary>sc watch list: the posts and their guards, who is resting, the attention cut and the call-out state.</summary>
        public static string ListText(World w)
        {
            var sb = new StringBuilder();
            Community c = ScRegistry.Community;
            sb.Append("watch posts ").Append(ScConfig.WatchPosts ? "on" : "OFF (WatchPosts=false)").Append(": ").Append(Posts.Count).Append(" of ").Append(ScConfig.WatchMaxPosts)
              .Append("; ").Append(Manned()).Append(" manned, attention gain x").Append(AttentionFactor().ToString("0.##", CultureInfo.InvariantCulture))
              .Append("; stockpile ").Append(c.Get(Resource.Ammo)).Append(" ammo bundle(s), a shift takes ").Append(ScConfig.WatchShiftAmmo);
            for (int i = 0; i < Posts.Count; i++)
            {
                Vector3i p = Posts[i];
                sb.Append('\n').Append(Label(p)).Append(" at ").Append(p);
                if (w != null && ScZone.IsLoaded(p))
                {
                    BlockValue bv = w.GetBlock(p);
                    sb.Append(bv.Block is BlockScWatchPost ? ", faces " + Compass(Facing(bv)) + " (rotation " + bv.rotation + ")" : ", NOT A WATCH POST (" + (bv.Block != null ? bv.Block.GetBlockName() : "?") + ")")
                      .Append(Room(w, p) ? "" : ", NO ROOM TO STAND");
                }
                else sb.Append(", chunk not loaded");
                Member g = GuardOf(p);
                if (g == null) { sb.Append("; no guard"); continue; }
                Guard st;
                guards.TryGetValue(g.Id, out st);
                EntityAlive npc = ScCommunity.FindNpc(g.Id);
                sb.Append("; ").Append(g.Name).Append(" (id ").Append(g.Id).Append(") on watch, ").Append(HoursLeft(g)).Append(" h left, ")
                  .Append(g.WatchPrepaid).Append(" prepaid point(s) left, back to ").Append(g.WatchHome)
                  .Append(npc == null ? ", body not loaded" : ", " + (UAI.UAIConsiderationScAtPost.AtPost(npc, g, 0.8f, 1.2f) ? "at the post" : "NOT at the post ("
                          + Vector3.Distance(npc.position, ScCommunity.PostCenter(g)).ToString("0.0", CultureInfo.InvariantCulture) + " m)")
                          + ", sight " + npc.sightRangeBase.ToString("0", CultureInfo.InvariantCulture) + " m")
                  .Append(st != null && st.Placed > 0 ? ", placed " + st.Placed + "x" : "");
            }
            foreach (Member m in ScRegistry.Members.Values)
                if (m.WatchRestUntil > Now) sb.Append('\n').Append(m.Name).Append(" rests ").Append(RestHoursLeft(m)).Append(" more h after a watch");
            sb.Append("\ncall-outs: ").Append(!ScBaseAttack.Open ? "no attack open" : c.AttackWarned == 0 ? "none yet" : c.AttackWarned == 1 ? "gathering told" : "spotted told")
              .Append(", marker ").Append(marker != null ? "shown" : "none");
            return sb.ToString();
        }
    }
}
