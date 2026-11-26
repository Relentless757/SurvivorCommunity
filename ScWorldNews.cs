using System;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round MP6 (part N): news of a world event - an enclave gone, an infestation cleared - for every community it concerns.
    /// A world step runs once, in the first online community's scope (ScTicker), so a TellCommunityKey there reaches that
    /// community alone, and its ledger line lands in that community's ledger. Each wraps the event's tells and ledger line: they
    /// run once per community, inside its scope, so each community reads its own relation to the thing (it knew of it: the
    /// record's relation fields as that community sees them) and tells its own players. With one community the block runs once,
    /// in the scope as it is: exactly as before MP6.
    /// Main thread only (the registry's scopes are).
    /// </summary>
    public static class ScWorldNews
    {
        /// <summary>
        /// The block once per community, lowest id first, each inside that community's scope (ScCommunities.ForEach); with one
        /// community, once as it is. One community's fault is logged and the others still hear.
        /// </summary>
        public static void Each(Action act)
        {
            if (act == null) return;
            if (ScCommunities.Count < 2) { act(); return; }
            ScCommunities.ForEach(cs =>
            {
                try { act(); }
                catch (Exception e) { ScLog.Once("world-news-" + cs.Id, e); }
            });
        }

        /// <summary>
        /// Whether the community in scope hears of a world event that, before MP6, the one community always heard: with one
        /// community always true (nothing changes); with several, only when it has a relation to the thing (related: it knew of
        /// it, read in its scope).
        /// </summary>
        public static bool Concerns(bool related)
        {
            return ScCommunities.Count < 2 || related;
        }
    }
}
