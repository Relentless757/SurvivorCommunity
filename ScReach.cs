#if SC_V34
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round n08, measurement only: does 0-XNPCCore's "melee unreachable" mark actually affect SC's people?
    ///
    /// 0-XNPCCore marks a melee target unreachable after CombatMeleeUnreachableSeconds of trying without closing the
    /// distance, and then ignores it for CombatMeleeUnreachableForgetSeconds (20 s by default). While a mark is live:
    ///  - its UAIConsiderationSdcsReachable scores 0 for that target, so MeleeAttack, MeleeApproach, HoldEngaged and
    ///    HireSeek all score 0;
    ///  - its close-range tick turns the decision into Exit, which BYPASSES the minimum melee hold and puts the gun
    ///    back in her hand, and then refuses to re-enter melee for the rest of the window.
    /// The early clear only runs on its MELEE path, so once a gun is in her hand a zombie standing at 1 m stays marked
    /// for the full 20 s - and the exit runs at 5 Hz while the clear runs at 1 Hz, so the exit wins the race.
    ///
    /// Round n07 already removed the OTHER cause of "she runs around and never attacks" (SC's overwhelmed rule held her
    /// on the gun inside 4 m, where 0-XNPCCore's own BackupKeepDistance at weight 8 outranks its shot at 6 and holds no
    /// shot of its own). So the question this answers is whether the mark still matters at all. If it turns out to be
    /// rare in play, the right answer is to do nothing further rather than patch 0-XNPCCore by reflection.
    ///
    /// This file only WATCHES. It never clears a mark and never changes a decision.
    /// </summary>
    public static class ScReach
    {
        private static MethodInfo mIsMarked;
        private static bool bound, failed;

        /// <summary>One body's mark history, for "sc xnpc".</summary>
        private sealed class Seen
        {
            public int Target;          // the target currently marked, 0 = none
            public float Since;         // when this mark was first seen
            public float Last;          // when a mark was last seen at all
            public int Windows;         // how many separate marks this body has had
            public float Total;         // seconds spent marked, summed
        }
        private static readonly Dictionary<int, Seen> seen = new Dictionary<int, Seen>();

        /// <summary>Totals for the bare "sc xnpc" report.</summary>
        public static int Windows, Bodies;
        public static float Longest;

        public static void Reset() { seen.Clear(); Windows = Bodies = 0; Longest = 0f; bound = failed = false; mIsMarked = null; }

        public static void Forget(int entityId) { seen.Remove(entityId); }

        private static void Bind()
        {
            if (bound) return;
            bound = true;
            try
            {
                Type t = typeof(AvatarSdcsNpcController).Assembly.GetType("SdcsMeleeUnreachable", false);
                mIsMarked = t != null
                    ? t.GetMethod("IsMarked", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                                  null, new[] { typeof(int), typeof(int), typeof(float) }, null)
                    : null;
            }
            catch (Exception ex) { ScLog.Once("reach-bind", ex); mIsMarked = null; }
            if (mIsMarked == null)
            {
                failed = true;
                ScLog.Warn("reach: 0-XNPCCore's SdcsMeleeUnreachable.IsMarked(int, int, float) was not found - the "
                           + "melee-unreachable readout in 'sc xnpc' is off. Harmless; it is a diagnostic only. "
                           + "Check the 0-XNPCCore version if you wanted it.");
            }
        }

        /// <summary>
        /// The foe to ask about: her attack target, else her revenge target. Written out rather than with ?? on purpose -
        /// UnityEngine.Object overloads ==, so ?? does not see a destroyed entity as null, and a dead target would be
        /// reported as reachable or not when it is neither.
        /// </summary>
        private static EntityAlive Foe(EntityAlive e)
        {
            if (e == null) return null;
            EntityAlive t = e.GetAttackTarget();
            if (t != null && !t.IsDead()) return t;
            t = e.GetRevengeTarget();
            return t != null && !t.IsDead() ? t : null;
        }

        /// <summary>Has 0-XNPCCore marked this target unreachable for this body right now? False when it cannot be asked.</summary>
        public static bool Marked(EntityAlive self, EntityAlive target)
        {
            return self != null && target != null && Marked(self.entityId, target.entityId);
        }

        /// <summary>The same by id - so a mark can be followed after she has turned to another target, or it has died.</summary>
        private static bool Marked(int selfId, int targetId)
        {
            Bind();
            if (mIsMarked == null || targetId == 0) return false;
            try
            {
                object r = mIsMarked.Invoke(null, new object[] { selfId, targetId, Time.time });
                return r is bool && (bool)r;
            }
            catch (Exception ex) { ScLog.Once("reach-ismarked", ex); return false; }
        }

        /// <summary>
        /// 1 Hz from ScTicker: watches each loaded member's marks (see Note) and records when one opens and closes, so
        /// "sc xnpc" can answer "does this happen, and how often" without turning on 0-XNPCCore's VerboseLogging - that
        /// flag gates 573 log sites and would bury the answer.
        /// </summary>
        public static void Tick(World w, EntityPlayer player)
        {
            if (w == null || !ScCommunity.IsHost || !ScRegistry.Loaded) return;
            Bind();
            if (mIsMarked == null) return;
            try
            {
                foreach (Member m in ScRegistry.Members.Values)
                {
                    EntityAlive npc = ScCommunity.FindNpc(m.Id);
                    if (npc == null || npc.IsDead()) continue;
                    Note(npc, Foe(npc));
                }
            }
            catch (Exception ex) { ScLog.Once("reach-tick", ex); }
        }

        /// <summary>
        /// Follows ONE mark per body. While a mark is open it is asked about by the marked target's id, not her current
        /// target: once a target is marked she usually turns to another one, and the mark lives on - still blocking melee
        /// on that zombie - for the rest of its window. Asking only about her current target ended the window the moment
        /// she looked away and undercounted both its length and how long she was kept off melee.
        /// A second mark that opens while one is being followed is picked up when the first one ends (its start is then
        /// late by up to the first one's remainder). Sampled once a second, so a window shorter than that can be missed.
        /// </summary>
        private static void Note(EntityAlive npc, EntityAlive foe)
        {
            float now = Time.time;
            Seen s;
            if (!seen.TryGetValue(npc.entityId, out s)) seen[npc.entityId] = s = new Seen();

            if (s.Target != 0)
            {
                if (Marked(npc.entityId, s.Target)) { s.Last = now; return; }
                float len = now - s.Since;                     // the mark cleared
                s.Total += len;
                if (len > Longest) Longest = len;
                ScLog.Out("reach: 0-XNPCCore's melee-unreachable mark on " + ScCommunity.DisplayName(npc) + "'s target #"
                              + s.Target + " cleared after about " + len.ToString("0") + " s");
                s.Target = 0;
            }
            if (foe == null || !Marked(npc, foe)) return;
            if (s.Windows == 0) Bodies++;
            s.Target = foe.entityId;
            s.Since = now;
            s.Last = now;
            s.Windows++;
            Windows++;
            float dist = (foe.position - npc.position).magnitude;
            ScLog.Out("reach: 0-XNPCCore marked " + ScCommunity.DisplayName(npc) + "'s target " + foe.EntityName + "#"
                          + foe.entityId + " unreachable at " + dist.ToString("0.0") + " m (holding "
                          + HeldName(npc) + ") - she cannot enter melee on it until the mark clears");
        }

        private static string HeldName(EntityAlive e)
        {
            ItemValue iv = e != null && e.inventory != null ? e.inventory.holdingItemItemValue : null;
            ItemClass ic = iv != null && !iv.IsEmpty() ? iv.ItemClass : null;
            return ic != null ? ic.GetItemName() : "nothing";
        }

        /// <summary>The per-body line for "sc xnpc &lt;name&gt;".</summary>
        public static string Describe(EntityAlive e)
        {
            Bind();
            if (failed) return "unreachable marks: cannot be read on this 0-XNPCCore";
            if (e == null) return "unreachable marks: -";
            Seen s;
            seen.TryGetValue(e.entityId, out s);
            EntityAlive t = Foe(e);
            string nowPart = t == null ? "no target"
                           : Marked(e, t) ? "TARGET MARKED UNREACHABLE (" + t.EntityName + "#" + t.entityId + ") - she cannot enter melee on it"
                           : "target not marked (" + t.EntityName + "#" + t.entityId + ")";
            if (s != null && s.Target != 0 && (t == null || s.Target != t.entityId))
                nowPart += "; an earlier target #" + s.Target + " is still marked (" + (Time.time - s.Since).ToString("0") + " s so far)";
            if (s == null || s.Windows == 0) return "unreachable marks: " + nowPart + "; none seen so far this session";
            return "unreachable marks: " + nowPart + "; " + s.Windows + " so far, " + s.Total.ToString("0.0")
                   + " s marked in total, last " + (Time.time - s.Last).ToString("0") + " s ago";
        }

        /// <summary>The totals line for a bare "sc xnpc".</summary>
        public static string Report()
        {
            Bind();
            if (failed) return "cannot be read on this 0-XNPCCore (IsMarked not found)";
            if (Windows == 0) return "none seen this session (loaded members, sampled once a second)";
            return Windows + " mark(s) seen across " + Bodies + " member(s), longest finished one " + Longest.ToString("0") + " s"
                   + " (loaded members, sampled once a second)";
        }
    }
}
#endif
