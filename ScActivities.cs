using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Activities (round wl): what the community spends luxuries on. The first, and so far only, one is "Prepare a round of
    /// drinks": DrinksLuxury luxuries and DrinksFood food buy +DrinksMorale morale for DrinksMinutes REAL minutes
    /// (Community.DrinksLeft, counted down here by the ticker while the game runs unpaused, saved with the registry), once
    /// per game day (DrinksDay). Meant for the days after a death. Host only.
    /// Round m1f: the Activities tab is gone - each activity is an action of the facility that hosts it (All's fifth column,
    /// a ScFacilityTab entry id), shown on the Facilities tab. The round of drinks is the campfire's for now (the Lounge's later).
    /// Round fd1 (the user 2026-11-21), two more on the campfire, the base's food crafting station:
    ///   "Prepare a feast" - with a cook at home (the best one's level) and a campfire in the base, FeastFood food buys
    ///   +FeastMorale morale and +FeastStamina max stamina (each cook level above the first adds FeastMoralePerLevel and
    ///   FeastStaminaPerLevel) for FeastMinutes real minutes, for the members and the player; once a game day (FeastDay).
    ///   "Enact rationing" - the rollover that closes the day eats RationShare of the food (ScEconomy.FoodEaten, rounded up),
    ///   for -RationMorale morale and -RationStamina max stamina for RationMinutes real minutes; once a game day (RationDay).
    /// The max stamina is a buff (buffSCFeast / buffSCRationing, StaminaMax from a cvar) the ticker keeps on the player and on
    /// every loaded member while it lasts, and takes off when it ends (a member loaded later gets it then; ScSkills.ClearCvars
    /// takes it off one who leaves). Only the local player: in multiplayer the other players are not given it.
    /// Round MP0: every community player online now (a server-side buff or cvar on a remote player reaches that client), so a
    /// dedicated server has a beneficiary and none is left holding it when it ends; single player: you, as before.
    /// </summary>
    public static class ScActivities
    {
        public const string Drinks = "drinks";
        public const string Feast = "feast", Ration = "ration";      // round fd1
        public const string FeastBuff = "buffSCFeast", RationBuff = "buffSCRationing";
        public const string FeastCvar = "sc_feaststam", RationCvar = "sc_rationstam";

        /// <summary>The activities: id, name and description keys, icon, and the Facilities tab entry that hosts it (round m1f).</summary>
        public static readonly string[][] All =
        {
            new[] { Drinks, "scActDrinks", "scActDrinksDesc", "ui_game_symbol_beer", ScFacilityTab.Campfire },
            new[] { Feast, "scActFeast", "scActFeastDesc", "ui_game_symbol_fork", ScFacilityTab.Campfire },          // round fd1
            new[] { Ration, "scActRation", "scActRationDesc", "ui_game_symbol_hunger", ScFacilityTab.Campfire },
        };

        private static float lastTick = -1f;
        private static float sinceSave;
        private static float nextBuffCheck;

        public static void Reset()
        {
            lastTick = -1f;
            sinceSave = 0f;
            nextBuffCheck = 0f;
        }

        // ---- round fd1: the feast and rationing ----

        // round MP1: reads (the Facilities tab, morale): Ready - a client shows the server's copy (ScMirror keeps the countdowns live)
        public static bool FeastActive { get { return ScRegistry.Ready && ScRegistry.Community.FeastLeft > 0f; } }
        public static bool RationActive { get { return ScRegistry.Ready && ScRegistry.Community.RationLeft > 0f; } }

        /// <summary>The feast's morale while it lasts (ScMorale's Feast part).</summary>
        public static int FeastMoraleNow() { return FeastActive ? ScRegistry.Community.FeastMorale : 0; }

        /// <summary>Rationing's morale while it lasts (ScMorale's Rationing part, negative).</summary>
        public static int RationMoraleNow() { return RationActive ? -ScConfig.RationMorale : 0; }

        /// <summary>What a feast held now gives: morale and max stamina for this cook level (each level above the first adds).</summary>
        public static int FeastMoraleFor(int level) { return ScConfig.FeastMorale + ScConfig.FeastMoralePerLevel * Math.Max(0, level - 1); }
        public static int FeastStaminaFor(int level) { return ScConfig.FeastStamina + ScConfig.FeastStaminaPerLevel * Math.Max(0, level - 1); }

        /// <summary>"12:34" of seconds.</summary>
        public static string Clock(float seconds)
        {
            int s = Mathf.CeilToInt(Math.Max(0f, seconds));
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public static string FeastTimeLeft() { return FeastActive ? Clock(ScRegistry.Community.FeastLeft) : ""; }

        /// <summary>
        /// Round fd2 (the user: a feast and rationing never at once): the community is on rations - enacted for today (tonight's
        /// food is cut) or its penalty still running from an earlier day. No feast while it is; no rationing during a feast.
        /// </summary>
        public static bool RationInForce
        {
            get { return ScRegistry.Ready && (ScRegistry.Community.RationDay == ScCommunity.Today || ScRegistry.Community.RationLeft > 0f); }   // round MP1: a read
        }
        public static string RationTimeLeft() { return RationActive ? Clock(ScRegistry.Community.RationLeft) : ""; }

        /// <summary>Why a feast cannot be held now, as a finished line; null = it can.</summary>
        public static string WhyNotFeast()
        {
            if (!ScCommunity.IsHost) return Localization.Get("scHostOnly");
            if (!ScRegistry.Loaded || !ScRegistry.Community.HasHome) return Localization.Get("scNeedHome");
            if (ScRegistry.Members.Count == 0) return Localization.Get("scActNoMembersFeast");
            Community c = ScRegistry.Community;
            if (FeastActive) return Localization.Get("scActFeastRunning");          // review: also yesterday's, past the rollover
            if (c.FeastDay == ScCommunity.Today) return Localization.Get("scActFeastToday");
            if (RationInForce) return Localization.Get("scActFeastRationing");     // round fd2: not while the community is on rations
            Member cook;
            if (ScSkills.BestSpecialist(Specialty.Cooking, out cook) <= 0 || cook == null) return Localization.Get("scActFeastNoCook");
            if (ScFacilityTab.CampfireCount() <= 0) return Localization.Get("scActFeastNoStation");
            if (c.Get(Resource.Food) < ScConfig.FeastFood) return string.Format(Localization.Get("scActFeastShort"), ScConfig.FeastFood, c.Get(Resource.Food));
            return null;
        }

        /// <summary>Holds the feast: the food leaves the stockpile, the morale and the max stamina start at once.</summary>
        public static bool PrepareFeast(EntityPlayerLocal player, out string message)
        {
            message = WhyNotFeast();
            if (message != null) return false;
            try
            {
                Community c = ScRegistry.Community;
                Member cook;
                int level = ScSkills.BestSpecialist(Specialty.Cooking, out cook);
                c.Add(Resource.Food, -ScConfig.FeastFood);
                c.FeastMorale = FeastMoraleFor(level);
                c.FeastStamina = FeastStaminaFor(level);
                c.FeastLeft = ScConfig.FeastMinutes * 60f;
                c.FeastDay = ScCommunity.Today;
                if (cook != null) ScSkills.AddSpecialtyXp(cook, 2f);      // the cook learns from it, as from a day's cooking
                sinceSave = 0f;
                ScRegistry.SaveAtomic();
                ScMorale.Recompute();
                nextBuffCheck = 0f;
                KeepBuffs(player);
                message = string.Format(Localization.Get("scActFeastServed"), cook != null ? cook.Name : "?", c.FeastMorale, c.FeastStamina,
                                        FormatMinutes(ScConfig.FeastMinutes));
                ScLog.Out("activity: a feast by " + (cook != null ? cook.Name : "?") + " (cooking " + level + ") for " + ScRegistry.Members.Count + " member(s) (-"
                          + ScConfig.FeastFood + " food), +" + c.FeastMorale + " morale and +" + c.FeastStamina + " max stamina for " + ScConfig.FeastMinutes
                          + " real minutes; morale now " + c.Morale);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("activity-feast", e);
                message = Localization.Get("scInternal");
                return false;
            }
        }

        /// <summary>Why rationing cannot be enacted now, as a finished line; null = it can.</summary>
        public static string WhyNotRation()
        {
            if (!ScCommunity.IsHost) return Localization.Get("scHostOnly");
            if (!ScRegistry.Loaded || !ScRegistry.Community.HasHome) return Localization.Get("scNeedHome");
            if (ScRegistry.Members.Count == 0) return Localization.Get("scActNoMembersRation");
            if (ScRegistry.Community.RationDay == ScCommunity.Today) return Localization.Get("scActRationToday");
            if (FeastActive) return Localization.Get("scActRationFeast");           // round fd2: not during a feast
            if (RationSavesNothing()) return Localization.Get("scActRationNothing");
            return null;
        }

        /// <summary>Round fd1 (review): tonight's food is so small that rationing it would spare nothing (1 stays 1).</summary>
        public static bool RationSavesNothing()
        {
            int raw, saved;
            Member cook;
            int eaten = ScEconomy.FoodEaten(out raw, out saved, out cook);
            int kept = (int)Math.Ceiling(eaten * (double)(decimal)ScConfig.RationShare - 1e-9);
            return eaten - Math.Max(0, Math.Min(eaten, kept)) <= 0;
        }

        /// <summary>Enacts rationing: tonight's rollover eats less, and the penalty starts at once.</summary>
        public static bool EnactRationing(EntityPlayerLocal player, out string message)
        {
            message = WhyNotRation();
            if (message != null) return false;
            try
            {
                Community c = ScRegistry.Community;
                c.RationDay = ScCommunity.Today;
                c.RationLeft = ScConfig.RationMinutes * 60f;
                sinceSave = 0f;
                ScRegistry.SaveAtomic();
                ScMorale.Recompute();
                nextBuffCheck = 0f;
                KeepBuffs(player);
                int raw, saved, cut;
                Member cook;
                int eaten = ScEconomy.FoodEaten(ScCommunity.Today + 1, out raw, out saved, out cook, out cut);
                message = string.Format(Localization.Get("scActRationEnacted"), eaten, eaten + cut, ScConfig.RationMorale, ScConfig.RationStamina,
                                        FormatMinutes(ScConfig.RationMinutes));
                ScLog.Out("activity: rationing for day " + c.RationDay + " (tonight's food " + (eaten + cut) + " -> " + eaten + "), -" + ScConfig.RationMorale
                          + " morale and -" + ScConfig.RationStamina + " max stamina for " + ScConfig.RationMinutes + " real minutes; morale now " + c.Morale);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("activity-ration", e);
                message = Localization.Get("scInternal");
                return false;
            }
        }

        /// <summary>
        /// The feast's and rationing's max stamina on the player and every loaded member while each lasts, and off when it
        /// does not (a member loaded later gets it then; a buff left on one from before is taken off).
        /// </summary>
        public static void KeepBuffs(EntityPlayer player)
        {
            if (!ScRegistry.Loaded) return;
            Community c = ScRegistry.Community;
            float feast = c.FeastLeft > 0f ? c.FeastStamina : 0f;
            float ration = c.RationLeft > 0f ? -ScConfig.RationStamina : 0f;
            // round MP0: on every community player online, no longer only the one passed in (the host's own on a host, else the
            // anchor): an anchor that changes on a dedicated server would keep the buff for good. EntityBuffs sends a server-side
            // AddBuff / RemoveBuff and SetCustomVar on a remote player to the clients
            List<EntityPlayer> all = ScPlayers.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null || !ScPlayers.InCommunity(all[i])) continue;
                Keep(all[i], FeastBuff, FeastCvar, feast);
                Keep(all[i], RationBuff, RationCvar, ration);
            }
            foreach (Member m in ScRegistry.Members.Values)
            {
                EntityAlive npc = ScNpc.Find(m.Id);              // never forces a body that is still being built
                if (npc == null || npc.IsDead()) continue;
                Keep(npc, FeastBuff, FeastCvar, feast);
                Keep(npc, RationBuff, RationCvar, ration);
            }
        }

        private static void Keep(EntityAlive e, string buff, string cvar, float value)
        {
            if (e == null || e.Buffs == null) return;
            if (value != 0f)
            {
                if (e.Buffs.GetCustomVar(cvar) != value) e.Buffs.SetCustomVar(cvar, value);
                if (!e.Buffs.HasBuff(buff) && BuffManager.GetBuff(buff) != null) e.Buffs.AddBuff(buff);
            }
            else
            {
                if (e.Buffs.HasBuff(buff)) e.Buffs.RemoveBuff(buff);
                // round MP0 (the review, N2): a remote player's copy is set to 0 - a removal never reaches their client
                if (e is EntityPlayer && !(e is EntityPlayerLocal)) { if (e.Buffs.GetCustomVar(cvar) != 0f) e.Buffs.SetCustomVar(cvar, 0f); }
                else if (e.Buffs.HasCustomVar(cvar)) e.Buffs.RemoveCustomVar(cvar);
            }
        }

        /// <summary>Takes both off one entity (a member who leaves: ScSkills.ClearCvars).</summary>
        public static void ClearBuffs(EntityAlive e)
        {
            Keep(e, FeastBuff, FeastCvar, 0f);
            Keep(e, RationBuff, RationCvar, 0f);
        }

        /// <summary>Console: start the feast (free, at the current cook's level) or end it, and allow another today.</summary>
        public static void ConsoleFeast(bool on)
        {
            Community c = ScRegistry.Community;
            Member cook;
            int level = Math.Max(1, ScSkills.BestSpecialist(Specialty.Cooking, out cook));
            if (on) { c.FeastMorale = FeastMoraleFor(level); c.FeastStamina = FeastStaminaFor(level); c.FeastLeft = ScConfig.FeastMinutes * 60f; c.FeastDay = ScCommunity.Today; }
            else { c.FeastLeft = 0f; c.FeastDay = -1; }
            sinceSave = 0f;
            ScRegistry.SaveAtomic();
            ScMorale.Recompute();
            nextBuffCheck = 0f;
        }

        /// <summary>Console: enact rationing for today (free) or lift it, and allow it again today.</summary>
        public static void ConsoleRation(bool on)
        {
            Community c = ScRegistry.Community;
            if (on) { c.RationDay = ScCommunity.Today; c.RationLeft = ScConfig.RationMinutes * 60f; }
            else { c.RationLeft = 0f; c.RationDay = -1; }
            sinceSave = 0f;
            ScRegistry.SaveAtomic();
            ScMorale.Recompute();
            nextBuffCheck = 0f;
        }

        public static bool DrinksActive { get { return ScRegistry.Ready && ScRegistry.Community.DrinksLeft > 0f; } }   // round MP1: a read (as FeastActive)

        /// <summary>The morale the round adds while it lasts (ScMorale's Activity part).</summary>
#if SC_V34
        public static int MoraleBonus() { return DrinksActive ? ScConfig.DrinksMorale + ScLifeSkills.DrinksBonus() : 0; }   // round m1t: Entertainment
#else
        public static int MoraleBonus() { return DrinksActive ? ScConfig.DrinksMorale : 0; }
#endif

        /// <summary>"12:34" of the round still to go.</summary>
        public static string TimeLeft()
        {
            if (!DrinksActive) return "";
            int s = Mathf.CeilToInt(ScRegistry.Community.DrinksLeft);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>Why the round cannot be poured now, as a finished line; null = it can.</summary>
        public static string WhyNotDrinks()
        {
            if (!ScCommunity.IsHost) return Localization.Get("scHostOnly");
            if (!ScRegistry.Loaded || !ScRegistry.Community.HasHome) return Localization.Get("scNeedHome");
            if (ScRegistry.Members.Count == 0) return Localization.Get("scActNoMembers");
            Community c = ScRegistry.Community;
            if (c.DrinksDay == ScCommunity.Today) return Localization.Get(DrinksActive ? "scActDrinksRunning" : "scActDrinksToday");
            if (c.Get(Resource.Luxury) < ScConfig.DrinksLuxury || c.Get(Resource.Food) < ScConfig.DrinksFood)
                return string.Format(Localization.Get("scActDrinksShort"), ScConfig.DrinksLuxury, ScConfig.DrinksFood, c.Get(Resource.Luxury), c.Get(Resource.Food));
            return null;
        }

        /// <summary>Pours the round: the cost leaves the stockpile and the morale starts at once.</summary>
        public static bool PourDrinks(EntityPlayerLocal player, out string message)
        {
            message = WhyNotDrinks();
            if (message != null) return false;
            try
            {
                Community c = ScRegistry.Community;
                c.Add(Resource.Luxury, -ScConfig.DrinksLuxury);
                c.Add(Resource.Food, -ScConfig.DrinksFood);
                c.DrinksLeft = ScConfig.DrinksMinutes * 60f;
                c.DrinksDay = ScCommunity.Today;
                sinceSave = 0f;
                ScRegistry.SaveAtomic();
                ScMorale.Recompute();
#if SC_V34
                // round m1t (T2): an entertainer at home lifts it more, and learns
                Member host;
                int extra = ScLifeSkills.DrinksBonus(out host);
                ScLifeSkills.OnDrinksPoured();
                message = string.Format(Localization.Get("scActDrinksPoured"), ScConfig.DrinksMorale + extra, FormatMinutes(ScConfig.DrinksMinutes))
                          + (host != null ? " " + string.Format(Localization.Get("scActDrinksHost"), host.Name, extra) : "");
#else
                message = string.Format(Localization.Get("scActDrinksPoured"), ScConfig.DrinksMorale, FormatMinutes(ScConfig.DrinksMinutes));
#endif
                ScLog.Out("activity: a round of drinks for " + ScRegistry.Members.Count + " member(s) (-" + ScConfig.DrinksLuxury + " luxury, -" + ScConfig.DrinksFood
                          + " food), +" + ScConfig.DrinksMorale + " morale for " + ScConfig.DrinksMinutes + " real minutes; morale now " + c.Morale);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("activity-drinks", e);
                message = Localization.Get("scInternal");
                return false;
            }
        }

        public static string FormatMinutes(float minutes)
        {
            return minutes.ToString(minutes == Mathf.Floor(minutes) ? "0" : "0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>Console: end the round now (and allow another today), or start one free of charge.</summary>
        public static void ConsoleSet(bool on)
        {
            Community c = ScRegistry.Community;
            if (on) { c.DrinksLeft = ScConfig.DrinksMinutes * 60f; c.DrinksDay = ScCommunity.Today; }
            else { c.DrinksLeft = 0f; c.DrinksDay = -1; }
            sinceSave = 0f;
            ScRegistry.SaveAtomic();
            ScMorale.Recompute();
        }

        /// <summary>
        /// Ticker (every second): the countdowns in real seconds - the round of drinks, and (round fd1) the feast and
        /// rationing - paused with the game; saved once a minute so a quit costs at most a minute of one, and at its end (with
        /// the player told). Every two seconds the feast's and rationing's buffs are kept on the player and the loaded members.
        /// </summary>
        public static void Tick(EntityPlayer player)     // round MP0: any player (the anchor)
        {
            float now = Time.unscaledTime;
            float dt = lastTick < 0f ? 0f : now - lastTick;
            lastTick = now;
            Community c = ScRegistry.Community;
            if (now >= nextBuffCheck)
            {
                nextBuffCheck = now + 2f;
                try { KeepBuffs(player); }
                catch (Exception e) { ScLog.Once("activity-buffs", e); }
            }
            if (c.DrinksLeft <= 0f && c.FeastLeft <= 0f && c.RationLeft <= 0f) return;
            if (GameManager.Instance == null || GameManager.Instance.IsPaused()) return;
            dt = Mathf.Clamp(dt, 0f, 5f);                // a load or a hitch never eats the time
            sinceSave += dt;
            bool ended = false;
            if (c.DrinksLeft > 0f)
            {
                c.DrinksLeft -= dt;
                if (c.DrinksLeft <= 0f)
                {
                    c.DrinksLeft = 0f;
                    ended = true;
                    ScPlayers.TellCommunity(Localization.Get("scActDrinksOver"), "");     // round MP0: the community's news
                    ScLog.Out("activity: the round of drinks has worn off (morale " + c.Morale + ")");
                }
            }
            if (c.FeastLeft > 0f)
            {
                c.FeastLeft -= dt;
                if (c.FeastLeft <= 0f)
                {
                    c.FeastLeft = 0f;
                    ended = true;
                    ScPlayers.TellCommunity(Localization.Get("scActFeastOver"), "");      // round MP0: the community's news
                    ScLog.Out("activity: the feast has worn off (morale " + c.Morale + ")");
                }
            }
            if (c.RationLeft > 0f)
            {
                c.RationLeft -= dt;
                if (c.RationLeft <= 0f)
                {
                    c.RationLeft = 0f;
                    ended = true;
                    ScPlayers.TellCommunity(Localization.Get("scActRationOver"), "");     // round MP0: the community's news
                    ScLog.Out("activity: rationing's penalty has worn off (morale " + c.Morale + ")");
                }
            }
            if (ended)
            {
                sinceSave = 0f;
                ScRegistry.SaveAtomic();
                ScMorale.Recompute();
                nextBuffCheck = 0f;                      // the buffs come off on the next tick
                return;
            }
            if (sinceSave >= 60f) { sinceSave = 0f; ScRegistry.SaveAtomic(); }
        }
    }
}
