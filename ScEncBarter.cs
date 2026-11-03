using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Ally barter, milestone E7 (research\ideas\DESIGN.md section 7; the user's Q6: 2 of yours for 1 of theirs, once a day per
    /// ally, their own resource). The radio window's Allies panel (XUiC_ScRadio, Tools/generate_radio_ui.py's allies()) picks
    /// one Allied enclave (ScEncAllies.IsAlly), nearest first; Give cycles through what the radio barters (ScBarter.Kinds: the
    /// five classic supplies and water) but what you get, Get is the ally's own resource by kind (growers food, medics
    /// medicine, militia ammo, builders building materials, a road crew fuel: ScEncAllies.ResourceOf(ForKind)) - a
    /// scavengers' ("general") group lets you pick any of the five classic ones. Trade: EnclaveBarterGive units out of the
    /// stockpile, EnclaveBarterGet in, once every EnclaveBarterDays days of the request clock per ally (Enclave.BarterDay,
    /// saved as [enclave] barter=N, shifted back by ScEnclaves.OnWorldDayBack through ScEncGifts.ShiftDays). A stockpile-only
    /// trade, modelled on the radio's own barter (ScBarter: 3 for 1 with anyone, once a day), which stays as it is. Needs a
    /// usable radio (ScRadio.Usable), as that barter does - the window opens only at a radio block. Host only, main thread.
    /// </summary>
    public static class ScEncBarter
    {
        private static string Loc(string key) { return Localization.Get(key); }

        private static string Word(Resource r) { return Loc(ScNames.LocKey(r)); }

        /// <summary>The resource the group trades: their kind's (growers food ...), or -1 for a scavengers' group (any classic one).</summary>
        public static int TheirResource(Enclave e)
        {
            return e == null ? -1 : ScEncAllies.ResourceOf(ScEncAllies.ForKind(e.Kind));
        }

        /// <summary>A scavengers' group: the player picks what they get.</summary>
        public static bool ChoosesGet(Enclave e) { return e != null && TheirResource(e) < 0; }

        /// <summary>The group trades this: their own resource, or (scavengers) any of the five classic ones.</summary>
        public static bool Gets(Enclave e, Resource get)
        {
            int own = TheirResource(e);
            return own >= 0 ? (int)get == own : (int)get < ScNames.ClassicCount;
        }

        /// <summary>What the panel's Get starts on: their own resource, or food.</summary>
        public static Resource DefaultGet(Enclave e)
        {
            int own = TheirResource(e);
            return own >= 0 ? (Resource)own : Resource.Food;
        }

        /// <summary>Get pressed: the next classic resource for a scavengers' group; their own resource for anyone else.</summary>
        public static Resource NextGet(Enclave e, Resource get)
        {
            if (!ChoosesGet(e)) return DefaultGet(e);
            return (Resource)(((int)get + 1) % ScNames.ClassicCount);
        }

        /// <summary>Give pressed: the next of ScBarter.Kinds, never what you get.</summary>
        public static Resource NextGive(Resource give, Resource get)
        {
            Resource next = (Resource)(((int)give + 1) % ScBarter.Kinds);
            if (next == get) next = (Resource)(((int)next + 1) % ScBarter.Kinds);
            return next;
        }

        /// <summary>give as it is when it can be given for get, else the next one that can.</summary>
        public static Resource FixGive(Resource give, Resource get)
        {
            return (int)give < ScBarter.Kinds && give != get ? give : NextGive(give, get);
        }

        /// <summary>Request-clock days until this ally trades again (0: today).</summary>
        public static int DaysLeft(Enclave e) { return e == null ? 0 : Math.Max(0, e.BarterDay - ScEncRequests.Now); }

        /// <summary>The allies (ScEncAllies.IsAlly), nearest the point first: the panel's pick.</summary>
        public static void Allies(Vector3 from, List<Enclave> into)
        {
            into.Clear();
            foreach (Enclave e in ScEnclaves.All) if (ScEncAllies.IsAlly(e)) into.Add(e);
            into.Sort((a, b) => a.DistanceXZ(from.x, from.z).CompareTo(b.DistanceXZ(from.x, from.z)));
        }

        /// <summary>Whether this trade can be made now, and why not: off, no ally, not an ally, no radio, not theirs, the pair, the day, the stockpile.</summary>
        public static bool CanTrade(Enclave e, Resource give, Resource get, out string why)
        {
            why = null;
            if (!ScCommunity.IsHost) { why = Loc("scHostOnly"); return false; }
            if (!ScConfig.Enclaves || !ScConfig.EnclaveBarter) { why = Loc("scEncBarterOff"); return false; }
            if (e == null) { why = Loc("scEncBarterNoAlly"); return false; }
            if (!ScEncAllies.IsAlly(e)) { why = Loc("scEncBarterNotAlly"); return false; }
            if (!ScRadio.Usable()) { why = Loc("scRadioNoPower"); return false; }
            if (!Gets(e, get)) { why = string.Format(Loc("scEncBarterNotTheirs"), e.Name, Word(get)); return false; }
            if ((int)give >= ScBarter.Kinds) { why = Loc("scBarterNoLuxury"); return false; }
            if (give == get) { why = Loc("scBarterSame"); return false; }
            int left = DaysLeft(e);
            if (left > 0) { why = string.Format(Loc("scEncBarterWait"), left); return false; }
            Community c = ScRegistry.Community;
            int n = Math.Max(1, ScConfig.EnclaveBarterGive);
            if (c.Get(give) < n) { why = string.Format(Loc("scBarterShort"), n, Word(give), c.Get(give)); return false; }
            return true;
        }

        /// <summary>
        /// The trade (checked again): EnclaveBarterGive of give out of the stockpile, EnclaveBarterGet of get in, the ally's next
        /// trade EnclaveBarterDays later on the request clock, a ledger line, the log, a save. message: the panel's words, or why not.
        /// </summary>
        public static bool Trade(Enclave e, Resource give, Resource get, EntityPlayerLocal player, out string message)
        {
            if (!CanTrade(e, give, get, out message)) return false;
            Community c = ScRegistry.Community;
            int n = Math.Max(1, ScConfig.EnclaveBarterGive), m = Math.Max(1, ScConfig.EnclaveBarterGet);
            c.Add(give, -n);
            c.Add(get, m);
            e.BarterDay = ScEncRequests.Now + Math.Max(0, ScConfig.EnclaveBarterDays);
            ScOutposts.NoteLedger(string.Format(Loc("scEncBarterLedger"), e.Name, n, Word(give), m, Word(get)));
            message = string.Format(Loc("scEncBarterDone"), n, Word(give), m, Word(get), e.Name);
            ScLog.Out("ally barter with enclave " + e.Id + " '" + e.Name + "': " + n + " " + ScNames.Key(give) + " for " + m + " " + ScNames.Key(get)
                      + "; the next from day " + e.BarterDay + " (request clock day " + ScEncRequests.Now + ")");
            ScRegistry.SaveAtomic();
            return true;
        }

        /// <summary>"ally barter: ..." for sc enclave info (ScEncGifts.InfoLine).</summary>
        public static string InfoText(Enclave e)
        {
            string s;
            if (!ScConfig.EnclaveBarter) s = "off (EnclaveBarter=false)";
            else if (e.IsHostile) s = "never (hostile)";
            else
            {
                int own = TheirResource(e), left = DaysLeft(e);
                s = Math.Max(1, ScConfig.EnclaveBarterGive) + " of yours for " + Math.Max(1, ScConfig.EnclaveBarterGet) + " "
                    + (own >= 0 ? ScNames.Key((Resource)own) : "food|meds|ammo|mats|fuel (scavengers: any)")
                    + (ScEncAllies.IsAlly(e) ? "" : ", only while Allied")
                    + (left > 0 ? ", the next trade from day " + e.BarterDay + " (" + left + " day(s))" : ", they trade today");
            }
            return "ally barter: " + s;
        }

        /// <summary>
        /// sc enclave barter &lt;id&gt; [&lt;give&gt; [get]]: the barter state; with give, one trade as from the radio window that ignores
        /// the once-a-day wait (as "sc barter" does) - an ally, a usable radio and the stockpile still needed. get defaults to
        /// their own resource (food for a scavengers' group).
        /// </summary>
        public static void Command(Enclave e, List<string> p, List<string> into)
        {
            Resource get = DefaultGet(e), give = FixGive(Resource.Food, get);
            if (p.Count <= 3)
            {
                string why;
                bool ok = CanTrade(e, give, get, out why);
                into.Add(e.Name + " (enclave " + e.Id + ", " + ScEnclaves.StandingWord(e) + "): " + InfoText(e) + " (request clock day " + ScEncRequests.Now + ")");
                into.Add("  " + Math.Max(1, ScConfig.EnclaveBarterGive) + " " + ScNames.Key(give) + " for " + Math.Max(1, ScConfig.EnclaveBarterGet) + " "
                         + ScNames.Key(get) + " now: " + (ok ? "can trade" : why) + "; the stockpile has " + ScRegistry.Community.Get(give) + " " + ScNames.Key(give));
                return;
            }
            if (!ScNames.TryResource(p[3], out give) || (p.Count > 4 && !ScNames.TryResource(p[4], out get)))
            {
                into.Add("sc enclave barter <id> [<food|meds|ammo|mats|fuel|water> [food|meds|ammo|mats|fuel]] (ignores the once-a-day wait)");
                return;
            }
            int keep = e.BarterDay;
            e.BarterDay = 0;
            string msg;
            bool done = Trade(e, give, get, ScCommunity.LocalPlayer, out msg);
            if (!done) e.BarterDay = keep;
            into.Add((done ? "" : "refused: ") + msg);
        }
    }
}
