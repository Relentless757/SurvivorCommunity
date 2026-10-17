using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// E5, clear an infestation (E5_SPEC.md; Enclaves_Plan.md 2.10 kind 5; research missions.md 3.6): an enclave's leader asks
    /// the player to clear an I1 infestation within EnclaveInfestRange m of their footprint - the nearest one no other request
    /// names, or, with none there, one started for the request through I1's own infest (ScInfestations.StartNear: I1's
    /// eligibility without the daily roll's distance and tier rules, a POI it refuses stays refused, the cap holds). The
    /// target is fixed when the offer is made (a spread never moves it: Name). The leader telling the player of it makes it
    /// known (MakeKnown: I1's marker).
    ///
    /// Done when I1 ends that infestation as cleared (its own path: ScInfestations.End -> OnEnded) - wherever the player is:
    /// EnclaveGainInfestation, the favour event, and the leader's thanks owed for the next talk. Cleared while still an offer:
    /// the offer stays, its Stage marks it, and "I already took care of it." (or accepting it) completes it on the spot; left
    /// unanswered, it is withdrawn at its lapse with no loss. An infestation ended any other way (the console), or gone from
    /// the registry, withdraws the request. The journal quest is display only, its marker on the infested building; never a
    /// ClearSleepers objective (it would re-arm the rooms). Host only, main thread.
    /// </summary>
    public static class ScEncInfest
    {
        /// <summary>Stage: the target was cleared while the request was still an offer.</summary>
        public const int StageCleared = 1;

        private static string Loc(string key) { return Localization.Get(key); }

        /// <summary>Why this enclave cannot be given an infestation request now (null when it can): one near them, or room to start one.</summary>
        public static string CanOffer(Enclave e)
        {
            if (e == null) return "no enclave";
            if (!ScConfig.Infestations) return "Infestations is off";
            if (Nearest(e) != null) return null;
            string why = ScInfestations.CanStartNear(e);
            return why != null ? "no infestation within " + ScConfig.EnclaveInfestRange + " m, and none can start there (" + why + ")" : null;
        }

        /// <summary>The nearest infestation within EnclaveInfestRange m of the footprint (its centre) that no open request names, or null.</summary>
        private static Infestation Nearest(Enclave e)
        {
            var named = new HashSet<int>();
            foreach (EnclaveRequest r in ScRegistry.EncRequests) if (r.Kind == EnclaveRequest.KindInfestation) named.Add(r.InfestationId);
            Infestation best = null;
            float bestD = float.MaxValue, range = Math.Max(0, ScConfig.EnclaveInfestRange);
            foreach (Infestation inf in ScInfestations.All)
            {
                if (named.Contains(inf.Id)) continue;
                Vector3 c = inf.Centre;
                float d = e.DistanceXZ(c.x, c.z);
                if (d <= range && d < bestD) { bestD = d; best = inf; }
            }
            return best;
        }

        /// <summary>
        /// The offer's target (ScEncRequests.Offer): the nearest infestation within range, else one started for the request
        /// (ScInfestations.StartNear: how "request"). Null (and why) when there is neither.
        /// </summary>
        public static Infestation PickTarget(Enclave e, EntityPlayerLocal player, out string no)
        {
            no = null;
            if (!ScConfig.Infestations) { no = "Infestations is off"; return null; }
            Infestation inf = Nearest(e);
            if (inf != null) return inf;
            World w = ScCommunity.World;
            inf = ScInfestations.StartNear(e, player, w != null ? w.GetGameRandom() : null, out no);
            if (inf != null) ScLog.Out("enclave " + e.Id + " '" + e.Name + "': infestation #" + inf.Id + " at " + inf.PoiName + " started near them for their request");
            return inf;
        }

        /// <summary>The request names its target for good: the infestation, its building and the building's name.</summary>
        public static void Name(EnclaveRequest r, Infestation inf)
        {
            r.InfestationId = inf.Id;
            r.HasTarget = true;
            r.Target = inf.Poi;
            r.TargetName = inf.PoiName ?? "";
            r.Stage = 0;
        }

        /// <summary>"infestation: #7 at the Crack-a-Book (live)" - the log's and the console's form.</summary>
        public static string Describe(EnclaveRequest r)
        {
            Infestation inf = ScInfestations.ById(r.InfestationId);
            return "infestation: #" + r.InfestationId + " at " + r.TargetName + " " + r.Target + " ("
                   + (IsCleared(r) ? "cleared" : inf == null ? "gone" : inf.Known ? "live, known" : "live, not known yet") + ")";
        }

        /// <summary>The leader told the player of it (the offer's words, the acceptance): known, I1's marker on it.</summary>
        public static void MakeKnown(EnclaveRequest r, EntityPlayer player)
        {
            Infestation inf = r != null ? ScInfestations.ById(r.InfestationId) : null;
            if (inf == null || inf.Known) return;
            if (ScInfestations.SetKnown(inf, true, player) > 0) ScLog.Out("infestation #" + inf.Id + " at " + inf.PoiName + " is known now (enclave request #" + r.Id + ")");
        }

        /// <summary>Its target was cleared through I1's own path while the request was still an offer.</summary>
        public static bool IsCleared(EnclaveRequest r)
        {
            return r != null && r.Kind == EnclaveRequest.KindInfestation && r.Stage == StageCleared;
        }

        /// <summary>
        /// The day's pass (ScEncRequests.Daily, before its lapses): a request whose infestation is gone without passing OnEnded
        /// (dropped at a load: its building gone) or with Infestations off is withdrawn; one cleared while still an offer and
        /// left unanswered past its day is withdrawn with no loss (the work is done); one cleared and accepted all the same (a
        /// hand-edited save) is done. True when it closed.
        /// </summary>
        public static bool Squared(EnclaveRequest r, int day)
        {
            if (IsCleared(r))
            {
                if (r.IsAccepted)
                {
                    ScEncRequests.Close(r, ScEncRequests.Done, "its infestation was cleared already", ScEncRequests.GainFor(r), r.TargetName, ScEncRequests.EventFor(r));
                    return true;
                }
                if (day <= r.LapseDay) return false;
                ScEncRequests.Close(r, ScEncRequests.Withdrawn, "its infestation was cleared before they had an answer");
                return true;
            }
            if (!ScConfig.Infestations) { ScEncRequests.Close(r, ScEncRequests.Withdrawn, "Infestations is off"); return true; }
            if (ScInfestations.ById(r.InfestationId) != null) return false;
            ScEncRequests.Close(r, ScEncRequests.Withdrawn, "its infestation is gone");
            return true;
        }

        /// <summary>
        /// I1 ended an infestation (ScInfestations.End): a request naming it completes when it was cleared in play and the request
        /// was accepted (EnclaveGainInfestation, the favour event, a tell, their thanks owed for the next talk); one still an offer
        /// marks it cleared ("I already took care of it."); any other end (the console) withdraws it.
        /// </summary>
        public static void OnEnded(Infestation inf, string how)
        {
            try
            {
                if (inf == null) return;
                foreach (EnclaveRequest r in ScRegistry.EncRequests.ToArray())
                {
                    if (r.Kind != EnclaveRequest.KindInfestation || r.InfestationId != inf.Id || !ScRegistry.EncRequests.Contains(r)) continue;
                    Enclave e = ScEnclaves.ById(r.EnclaveId);
                    if (how != "cleared")
                    {
                        ScEncRequests.Close(r, ScEncRequests.Withdrawn, "its infestation ended (" + how + ")");
                        continue;
                    }
                    if (r.IsOffered)
                    {
                        r.Stage = StageCleared;
                        ScRegistry.SaveAtomic();
                        ScLog.Out("enclave request #" + r.Id + ": its infestation at " + inf.PoiName + " was cleared before the player took it on - \"I already took care of it.\" completes it");
                        continue;
                    }
                    EntityPlayerLocal pl = ScCommunity.LocalPlayer;
                    ScEncRequests.Close(r, ScEncRequests.Done, "its infestation was cleared", ScEncRequests.GainFor(r), r.TargetName, ScEncRequests.EventFor(r));
                    if (e != null)
                    {
                        e.Thanks = EnclaveRequest.KindInfestation;
                        if (pl != null) ScCommunity.Tell(pl, string.Format(Loc("scEncReqClearedTell"), e.Name, r.TargetName), "");
                        ScRegistry.SaveAtomic();
                    }
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("encinfest-ended", ex);
            }
        }

        /// <summary>"300 m north-east of here": the infested building from the enclave (the offer's words).</summary>
        public static string WhereText(EnclaveRequest r, Enclave e)
        {
            if (r == null || e == null) return "";
            Infestation inf = ScInfestations.ById(r.InfestationId);
            Vector3 to = inf != null ? inf.Centre : new Vector3(r.Target.x, r.Target.y, r.Target.z);
            return ScEnclaves.Where(e.Centre, to);
        }

        /// <summary>Where the request's quest points (ScEncRequests.RequestPlace): the infested building, its footprint, its name.</summary>
        internal static void Place(EnclaveRequest r, ref Vector3i at, ref Vector3i size, ref string name)
        {
            if (r == null || !r.HasTarget) return;
            Infestation inf = ScInfestations.ById(r.InfestationId);
            at = r.Target;
            if (inf != null) size = inf.Size;
            else
            {
                PrefabInstance pi = ScSecurity.PoiByKey(r.Target);
                size = pi != null ? pi.boundingBoxSize : new Vector3i(1, 1, 1);
            }
            name = r.TargetName;
        }
    }
}
