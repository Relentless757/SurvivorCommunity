using System;
using System.Globalization;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Activities (round wl): what the community window's Activities tab spends luxuries on. The first, and so far only,
    /// one is "Prepare a round of drinks": DrinksLuxury luxuries and DrinksFood food buy +DrinksMorale morale for
    /// DrinksMinutes REAL minutes (Community.DrinksLeft, counted down here by the ticker while the game runs unpaused,
    /// saved with the registry), once per game day (DrinksDay). Meant for the days after a death. Host only.
    /// </summary>
    public static class ScActivities
    {
        public const string Drinks = "drinks";

        /// <summary>The activities in tab order: id, name and description keys, icon.</summary>
        public static readonly string[][] All =
        {
            new[] { Drinks, "scActDrinks", "scActDrinksDesc", "ui_game_symbol_beer" },
        };

        private static float lastTick = -1f;
        private static float sinceSave;

        public static void Reset()
        {
            lastTick = -1f;
            sinceSave = 0f;
        }

        public static bool DrinksActive { get { return ScRegistry.Loaded && ScRegistry.Community.DrinksLeft > 0f; } }

        /// <summary>The morale the round adds while it lasts (ScMorale's Activity part).</summary>
        public static int MoraleBonus() { return DrinksActive ? ScConfig.DrinksMorale : 0; }

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
                message = string.Format(Localization.Get("scActDrinksPoured"), ScConfig.DrinksMorale, FormatMinutes(ScConfig.DrinksMinutes));
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
        /// Ticker (every second): the round's countdown in real seconds, paused with the game; saved once a minute so a
        /// quit costs at most a minute of it, and at its end (with the player told).
        /// </summary>
        public static void Tick(EntityPlayerLocal player)
        {
            float now = Time.unscaledTime;
            float dt = lastTick < 0f ? 0f : now - lastTick;
            lastTick = now;
            Community c = ScRegistry.Community;
            if (c.DrinksLeft <= 0f) return;
            if (GameManager.Instance == null || GameManager.Instance.IsPaused()) return;
            dt = Mathf.Clamp(dt, 0f, 5f);                // a load or a hitch never eats the round
            c.DrinksLeft -= dt;
            sinceSave += dt;
            if (c.DrinksLeft <= 0f)
            {
                c.DrinksLeft = 0f;
                sinceSave = 0f;
                ScRegistry.SaveAtomic();
                ScMorale.Recompute();
                if (player != null) ScCommunity.Tell(player, Localization.Get("scActDrinksOver"), "");
                ScLog.Out("activity: the round of drinks has worn off (morale " + c.Morale + ")");
                return;
            }
            if (sinceSave >= 60f) { sinceSave = 0f; ScRegistry.SaveAtomic(); }
        }
    }
}
