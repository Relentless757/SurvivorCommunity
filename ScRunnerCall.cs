using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round 29b (M29; the user, 2026-11-11: "Call when attacked"): a member on a physical supply run who carries a handheld
    /// radio (Member.RunRadio) and is hurt by a raider or a zombie - on the road or at the site - radios for help, once a run:
    ///  - her Trouble line on the radio (ScRadioTalk.Say, RadioTalk.Trouble: their voice as a radio, the caption alone until it
    ///    is recorded; heard only while the player carries a handheld, as every radio line);
    ///  - a message saying where she is ("... under attack on the way to &lt;site&gt;, about N m north-east of home! ..."); and
    ///  - a map marker on her - vanilla's "quest" map object, the one the distress call's rescue quest shows (map, compass and
    ///    the on-screen icon with its distance) - that follows her while the fight lasts.
    /// She fights or flees by her own rules; nothing else changes (no distress event, no rescue quest, no zombies spawned). No
    /// call without her radio, with the player within ScRadioTalk.InPersonRange of her (they would just shout), when nothing
    /// can hear it - the reach part of ScHandheld.DistressAllowed: the player carries a handheld or a radio station at home has
    /// power -, during a distress call (she radioed already), or once Trouble was said this run (Member.RunTalk, saved: a
    /// reload does not bring a second call). RunnerCallsWhenAttacked (ScConfigRadio.cs) false: none.
    ///
    /// The call ends - its marker removed, a log line - when the fight is over (no hit for QuietSeconds and no live enemy within
    /// ScCommunity.FightRange of her), when she dies or leaves the community, when her run ends or her body leaves the world
    /// (out of the player's range: the fight cannot be followed there), and with the world. Session only: after a reload the
    /// marker is gone, and Trouble already said keeps a second call from coming.
    ///
    /// The hit is seen in Patches_Trust.Patch_DamageResponse (V4's ProcessDamageResponseLocal postfix: every landed hit, on
    /// the host); no new Harmony target. Host only (SC is single player), main thread.
    /// </summary>
    public static class ScRunnerCall
    {
        /// <summary>The map object class: vanilla's quest marker, what the distress call's rescue quest puts on the map.</summary>
        public const string MarkerClass = "quest";
        /// <summary>No hit for this many real seconds and no live enemy near her: the fight is over and the call ends.</summary>
        private const float QuietSeconds = 30f;
        /// <summary>A hit that brought no call (no radio, in person, nothing to hear it) keeps the next ones quiet this long.</summary>
        private const float SkipQuietSeconds = 10f;
        /// <summary>The marker moves with her once she is this far (meters) from where it stands.</summary>
        private const float MarkerMove = 1.5f;

        private sealed class Call
        {
            public Member M;
            public int BodyId;
            public string Attacker;
            public float Since, LastHit;
            public NavObject Marker;
            public string MarkerName;
        }

        private static readonly List<Call> calls = new List<Call>();
        /// <summary>Member id -> real time until which hits on them are not looked at again (after a hit that brought no call).</summary>
        private static readonly Dictionary<int, float> quietUntil = new Dictionary<int, float>();

        /// <summary>Per world (ScConfig.ResetRadio) and as the world shuts down: open calls and their markers go.</summary>
        public static void Reset()
        {
            for (int i = 0; i < calls.Count; i++) RemoveMarker(calls[i]);
            calls.Clear();
            quietUntil.Clear();
        }

        // ------------------------------------------------------------------ the hit ----

        /// <summary>
        /// Patches_Trust.Patch_DamageResponse, for every landed hit on a V4 body on the host (not a killing blow, not a bleed or
        /// other effect over time). Cheap for anyone who is not a runner: one registry lookup.
        /// </summary>
        public static void OnHit(EntityAlive body, DamageSource src)
        {
            try
            {
                if (body == null || src == null || body.IsDead()) return;
                Member m = ScRegistry.GetMember(body.entityId);
                if (m == null || !ScRunners.IsPhysical(m) || m.Offline) return;
                World w = ScCommunity.World;
                EntityAlive attacker = w != null ? w.GetEntity(src.getEntityId()) as EntityAlive : null;
                if (!IsFoe(body, attacker)) return;
                float now = Time.unscaledTime;
                Call open = Find(m);
                if (open != null) { open.LastHit = now; return; }       // the call is on: the fight goes on
                float until;
                if (quietUntil.TryGetValue(m.Id, out until) && now < until) return;
                string result;
                if (!Make(m, body, attacker, ScCommunity.LocalPlayer, false, out result)) quietUntil[m.Id] = now + SkipQuietSeconds;
            }
            catch (Exception e)
            {
                ScLog.Once("runner-call-hit", e);
            }
        }

        /// <summary>
        /// A raider or a zombie: SC's raiders, any other NPC on the framework that is her enemy (0-XNPCCore's bandits), and every
        /// vanilla EntityEnemy - zombies, zombie dogs and vultures, and the hostile animals (wolves, bears ...), which hurt as much.
        /// Never a player (their hits are the trust rules'), never one of her own.
        /// </summary>
        private static bool IsFoe(EntityAlive body, EntityAlive a)
        {
            if (a == null || a == body || a is EntityPlayer || a.IsDead()) return false;
            if (a is EntityEnemy) return true;
            if (!ScNpc.IsFramework(a)) return false;
            return ScXnpc.IsRaider(a) || EntityTargetingUtilities.IsEnemy(body, a);
        }

        private static Call Find(Member m)
        {
            for (int i = 0; i < calls.Count; i++) if (calls[i].M == m) return calls[i];
            return null;
        }

        // ------------------------------------------------------------------ the call ----

        /// <summary>
        /// The call itself, when its rules allow it (force: the console - whatever the rules; her line is said even if it was).
        /// result: what happened, for the console. True when the call was made.
        /// </summary>
        private static bool Make(Member m, EntityAlive body, EntityAlive attacker, EntityPlayerLocal player, bool force, out string result)
        {
            result = null;
            if (!force)
            {
                if (!ScConfig.RunnerCallsWhenAttacked) { ScHandheld.Count("attack call off"); result = "RunnerCallsWhenAttacked is off"; return false; }
                if (m.RunDistress) { ScHandheld.Count("attack call skipped: radioed for help already (distress)"); result = m.Name + " radioed for help already (a distress call)"; return false; }
                if (ScRadioTalk.Said(m, RadioTalk.Trouble)) { ScHandheld.Count("attack call skipped: called once this run"); result = m.Name + " called for help once this run already"; return false; }
                if (!m.RunRadio) { ScHandheld.Count("attack call skipped: runner has no radio"); result = m.Name + " carries no radio"; return false; }
                if (player == null || player.IsDead()) { result = "no live player"; return false; }
                if (ScRadioTalk.InPerson(m, player)) { ScHandheld.Count("attack call skipped: you are with them"); result = "you are within " + ScRadioTalk.InPersonRange.ToString("0", CultureInfo.InvariantCulture) + " m of " + m.Name; return false; }
                if (!ScRadio.Online() && !ScHandheld.Carries(player)) { ScHandheld.Count("attack call skipped: no station or handheld to hear it"); result = "nothing hears it: you carry no handheld and no radio station at home has power"; return false; }
            }
            if (player == null) { result = "no player"; return false; }
            Vector3 at = body.position;
            string said = ScRadioTalk.Say(m, RadioTalk.Trouble, player, force);
            string where = WhereWords(m);
            Vector3i home = ScZone.Home;
            float dx = at.x - home.x, dz = at.z - home.z;
            int dist = Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dz * dz));
            string dir = Localization.Get(DirKey(dx, dz));
            ScCommunity.Tell(player, string.Format(Localization.Get("scRunnerAttacked"), m.Name, where, dist, dir), "ui_denied");
            var c = new Call { M = m, BodyId = body.entityId, Attacker = Describe(attacker), Since = Time.unscaledTime, LastHit = Time.unscaledTime };
            c.MarkerName = string.Format(Localization.Get("scRunnerAttackedMarker"), m.Name);
            AddMarker(c, at, player);
            calls.Add(c);
            quietUntil.Remove(m.Id);
            ScHandheld.Count("attack call made");
            ScRegistry.SaveAtomic();                                    // RunTalk: Trouble said, a reload brings no second call
            ScLog.Out(m.Name + " radios for help: attacked by " + c.Attacker + " " + m.RunLeg + " (" + where + ") at " + World.worldToBlockPos(at) + ", " + dist + " m " + dir
                      + " of home - " + (c.Marker != null ? "marked on the map" : "NO MARKER (the map object did not register)") + "; the line: " + said);
            result = "called: " + said;
            return true;
        }

        /// <summary>Where she is, for the message: on the way to the site, at it, on the way home from it, near the base.</summary>
        private static string WhereWords(Member m)
        {
            switch (m.RunLeg)
            {
                case RunLeg.Outbound: return string.Format(Localization.Get("scRunnerAttackedOut"), m.RunDestName);
                case RunLeg.AtSite: return string.Format(Localization.Get("scRunnerAttackedSite"), m.RunDestName);
                case RunLeg.Inbound: return string.Format(Localization.Get("scRunnerAttackedHome"), m.RunDestName);
                default: return Localization.Get("scRunnerAttackedBack");
            }
        }

        private static string DirKey(float dx, float dz)
        {
            int sector = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, 360f) / 45f) % 8;   // 0 = north (+z), 2 = east (+x)
            switch (sector)
            {
                case 0: return "scDirN";
                case 1: return "scDirNE";
                case 2: return "scDirE";
                case 3: return "scDirSE";
                case 4: return "scDirS";
                case 5: return "scDirSW";
                case 6: return "scDirW";
                default: return "scDirNW";
            }
        }

        private static string Describe(EntityAlive a)
        {
            if (a == null) return "nobody (a console test)";
            string kind = ScXnpc.IsRaider(a) ? "raider " : "";
            EntityClass ec;
            string cls = EntityClass.list.TryGetValue(a.entityClass, out ec) && ec != null ? ec.entityClassName : "?";
            return kind + ScCommunity.DisplayName(a) + " (" + cls + ", " + a.entityId + ")";
        }

        // ------------------------------------------------------------------ the marker ----

        /// <summary>
        /// Vanilla's quest map object on her, as ScQuests.EnsureMapObject places the rescue quest's (owner-only, by position, the
        /// fields a recycled object keeps cleared), a quarter block above her feet: a fractional position, so it never aliases
        /// one of vanilla's integer quest positions (RegisterNavObject hands back an existing object at the same spot).
        /// </summary>
        private static void AddMarker(Call c, Vector3 at, EntityPlayer owner)
        {
            try
            {
                if (GameManager.IsDedicatedServer || owner == null) return;
                NavObjectManager nav = NavObjectManager.Instance;
                if (nav == null || NavObjectClass.GetNavObjectClass(MarkerClass) == null) return;
                Vector3 p = at + new Vector3(0f, 0.25f, 0f);
                NavObject obj = nav.RegisterNavObject(MarkerClass, p, "", false, -1, owner);
                if (obj == null) return;
                if (HeldByQuest(owner, obj))
                {
                    obj = nav.RegisterNavObject(MarkerClass, p + new Vector3(0.01f, 0f, 0f), "", false, -1, owner);
                    if (obj == null || HeldByQuest(owner, obj)) return;
                }
                obj.ExtraData = -1f;
                obj.hiddenOnMap = false;
                obj.HiddenDisplayName = null;
                obj.UseOverrideFontColor = false;
                obj.name = c.MarkerName;
                obj.IsActive = true;
                c.Marker = obj;
            }
            catch (Exception e)
            {
                ScLog.Once("runner-call-marker", e);
            }
        }

        /// <summary>The object RegisterNavObject handed back is a quest's own (the same class, owner and spot): never ours to move or remove.</summary>
        private static bool HeldByQuest(EntityPlayer owner, NavObject obj)
        {
            QuestJournal j = owner != null ? owner.QuestJournal : null;
            if (j == null || j.quests == null) return false;
            for (int i = 0; i < j.quests.Count; i++) if (j.quests[i] != null && j.quests[i].NavObject == obj) return true;
            return false;
        }

        /// <summary>Ours still: registered, ours by name (an object recycled from the pool gets its new owner's name).</summary>
        private static bool Owns(Call c)
        {
            if (c.Marker == null || !NavObjectManager.HasInstance) return false;
            NavObjectManager nav = NavObjectManager.Instance;
            return nav.NavObjectList.Contains(c.Marker) && c.Marker.name == c.MarkerName;
        }

        private static void RemoveMarker(Call c)
        {
            try
            {
                if (Owns(c)) NavObjectManager.Instance.UnRegisterNavObject(c.Marker);
            }
            catch (Exception e)
            {
                ScLog.Once("runner-call-unmarker", e);
            }
            c.Marker = null;
        }

        // ------------------------------------------------------------------ the tick ----

        /// <summary>ScTicker, once a second (host): each open call's marker follows her; a call whose fight is over ends.</summary>
        public static void Tick(World w, EntityPlayerLocal player)
        {
            if (calls.Count == 0 || w == null) return;
            try
            {
                float now = Time.unscaledTime;
                for (int i = calls.Count - 1; i >= 0; i--)
                {
                    Call c = calls[i];
                    Member m = c.M;
                    EntityAlive body = w.GetEntity(c.BodyId) as EntityAlive;
                    string end = null;
                    if (ScRegistry.GetMember(m.Id) != m) end = body == null || body.IsDead() ? m.Name + " died" : m.Name + " is no longer in the community";
                    else if (body != null && body.IsDead()) end = m.Name + " died";
                    else if (!ScRunners.IsPhysical(m)) end = m.Name + "'s run is over";
                    else if (m.Offline || body == null) end = m.Name + " is out of range now (the fight cannot be followed there)";
                    else if (now - c.LastHit >= QuietSeconds && ScCommunity.LiveFoe(body, ScCommunity.FightRange) == null) end = "the fight is over";
                    if (end != null)
                    {
                        RemoveMarker(c);
                        calls.RemoveAt(i);
                        ScHandheld.Count("attack call ended");
                        ScLog.Out(m.Name + "'s call for help is over: " + end + " (after " + (now - c.Since).ToString("0", CultureInfo.InvariantCulture) + " s) - the marker is off the map");
                        continue;
                    }
                    if (c.Marker == null) continue;
                    if (!Owns(c)) { c.Marker = null; continue; }               // removed by someone else (a world reset): not re-placed
                    Vector3 p = body.position + new Vector3(0f, 0.25f, 0f);
                    if ((c.Marker.TrackedPosition - p).sqrMagnitude >= MarkerMove * MarkerMove) c.Marker.TrackedPosition = p;
                }
            }
            catch (Exception e)
            {
                ScLog.Once("runner-call-tick", e);
            }
        }

        // ------------------------------------------------------------------ the console ----

        /// <summary>sc radio's line: the switch and the open calls.</summary>
        public static string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("calls when attacked ").Append(ScConfig.RunnerCallsWhenAttacked ? "on" : "OFF").Append(", ").Append(calls.Count).Append(" open");
            float now = Time.unscaledTime;
            foreach (Call c in calls)
            {
                sb.Append("; ").Append(c.M.Name).Append(" (attacked by ").Append(c.Attacker).Append(", ").Append((now - c.Since).ToString("0", CultureInfo.InvariantCulture))
                  .Append(" s ago, last hit ").Append((now - c.LastHit).ToString("0", CultureInfo.InvariantCulture)).Append(" s ago, marker ")
                  .Append(Owns(c) ? "at " + World.worldToBlockPos(c.Marker.TrackedPosition) : "none").Append(')');
            }
            return sb.ToString();
        }

        /// <summary>
        /// sc radio attacked &lt;member&gt; [force]: the call as if a zombie had just hurt her - every rule applied (force: whatever
        /// the rules, and her line said again). Null on a call, else why not.
        /// </summary>
        public static string Test(Member m, bool force)
        {
            if (m == null) return "nobody";
            if (!ScRunners.IsPhysical(m)) return m.Name + " is not on a physical supply run";
            if (m.Offline) return m.Name + " is out of range (offline): only a body in the world can be attacked";
            EntityAlive body = ScCommunity.FindNpc(m.Id);
            if (body == null || body.IsDead()) return m.Name + "'s body is not loaded";
            Call open = Find(m);
            if (open != null) { open.LastHit = Time.unscaledTime; return m.Name + "'s call is open already - its fight goes on"; }
            string result;
            bool made = Make(m, body, ScCommunity.LiveFoe(body, ScCommunity.FightRange), ScCommunity.LocalPlayer, force, out result);
            return made ? null : result;
        }
    }
}
