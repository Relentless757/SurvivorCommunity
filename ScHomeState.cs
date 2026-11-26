using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    // round MP6 (part H), 2026-11-26: the home's machinery, one of each per community. Before MP6 each home system (the home
    // attack, the blood-moon siege, noise, the watch, the home scan, shelter, beds, the home hold, intruders, the doors' owner,
    // activities, the ticker steps' throttles ...) kept its session state in plain statics: one home's worth. With several
    // communities each needs its own, so those statics live in a side table keyed by community id (ScHomeState<T>), and the
    // old static names are properties that read the table's row for ScRegistry.Current - the code that uses them is unchanged.
    //
    // Why a side table and not fields on CommunityState: a client replaces its CommunityState objects with every snapshot
    // (ScRegistry.ApplyMirror), and several of these (the countdowns' clocks, the zone's rectangle cache) are read on clients
    // too; a side table outlives those swaps, and each system's own Reset clears its table as it cleared its statics.
    //
    // With one community every row is community 0's, held in a plain field (no lookup, no lock): nothing changes.

    /// <summary>
    /// Round MP6 (part H): one T per community, made on first use - a home system's session state, which used to be one set of
    /// statics. Current is the row of the community in scope (ScRegistry.CurrentId), For(cid) any community's. Community 0's row
    /// is a plain field; the others sit in a dictionary under a lock, so a stray read from another thread cannot corrupt it.
    /// </summary>
    public sealed class ScHomeState<T> where T : class, new()
    {
        private T zero = new T();
        private readonly Dictionary<int, T> rest = new Dictionary<int, T>();

        /// <summary>The row of the community in scope (ScRegistry.Current).</summary>
        public T Current { get { return For(ScRegistry.CurrentId); } }

        /// <summary>The row of community cid, made empty when it has none yet.</summary>
        public T For(int cid)
        {
            if (cid == ScPlayers.SharedCommunity) return zero;
            lock (rest)
            {
                T s;
                if (!rest.TryGetValue(cid, out s))
                {
                    s = new T();
                    rest[cid] = s;
                }
                return s;
            }
        }

        /// <summary>The row of community cid if one was made (community 0's always is).</summary>
        public bool TryGet(int cid, out T s)
        {
            if (cid == ScPlayers.SharedCommunity) { s = zero; return true; }
            lock (rest) return rest.TryGetValue(cid, out s);
        }

        /// <summary>Every row made so far, community 0's first, as a copy (act may make rows or reset the table).</summary>
        public List<KeyValuePair<int, T>> Rows()
        {
            var list = new List<KeyValuePair<int, T>>();
            list.Add(new KeyValuePair<int, T>(ScPlayers.SharedCommunity, zero));
            lock (rest) foreach (KeyValuePair<int, T> kv in rest) list.Add(kv);
            return list;
        }

        /// <summary>Runs act on every row made so far (community 0's first).</summary>
        public void ForEach(Action<int, T> act)
        {
            if (act == null) return;
            foreach (KeyValuePair<int, T> kv in Rows()) act(kv.Key, kv.Value);
        }

        /// <summary>Every community back to a fresh row (the system's per-world Reset).</summary>
        public void Reset()
        {
            zero = new T();
            lock (rest) rest.Clear();
        }
    }

    /// <summary>
    /// Round MP6 (part H): a ticker step's throttle ("the next real time it works"), one per community - before MP6 one static, so
    /// with two communities only the first community's pass of the second ever passed it (ScTicker's TickCommunity).
    /// </summary>
    public sealed class ScHomeTimer
    {
        public float Next;
    }

    /// <summary>Round MP6 (part H): helpers the home systems share.</summary>
    public static class ScHomeScope
    {
        /// <summary>
        /// A scope for the member this body is (her record, or the member an outpost or rescue stand-in stands in for), when two or
        /// more communities exist; with one, or for anyone else, nothing (the community in scope stays). Always in a using.
        /// </summary>
        public static ScScope Of(EntityAlive e)
        {
            if (e == null || ScCommunities.Count < 2) return default(ScScope);
            int cid = ScCommunities.CommunityOfMember(e.entityId);
            if (cid < 0 && e.Buffs != null && e.Buffs.HasCustomVar(ScRadio.StandInCvar))
                cid = ScCommunities.CommunityOfMember((int)e.Buffs.GetCustomVar(ScRadio.StandInCvar));
            return cid >= 0 ? ScRegistry.Use(cid) : default(ScScope);
        }

        /// <summary>A scope for community cid when two or more communities exist and cid is one (else nothing). Always in a using.</summary>
        public static ScScope Of(int cid)
        {
            return cid >= 0 && ScCommunities.Count > 1 ? ScRegistry.Use(cid) : default(ScScope);
        }
    }
}
