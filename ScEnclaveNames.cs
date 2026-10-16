using System;
using System.Collections.Generic;

namespace SurvivorCommunity
{
    /// <summary>
    /// Enclave names (Enclaves_Plan.md 2.2), a port of the prototype enclave_names.py: a weighted template filled from word
    /// lists, flavoured by the stance (survivors or raiders), the biome at the POI (place names), the POI's kind ("The
    /// Steeple Believers" at a church) and a leader or family ("Jody's Lot", "The Quill Family"). Place names are compounds
    /// (prefix + suffix), so the space is large (about 54,000 distinct names in 200,000 draws). The lists and template
    /// weights are the prototype's, word for word. English only: a name is generated once and kept in the registry.
    /// </summary>
    public static class ScEnclaveNames
    {
        private static readonly Dictionary<string, string[]> PlacePrefix = new Dictionary<string, string[]>
        {
            { "forest", new[] { "Pine", "Cedar", "Oak", "Birch", "Willow", "Elm", "Maple", "Moss", "Fern", "Aspen", "Hemlock", "Timber", "Green", "Fox", "Deer", "Bear", "Wolf", "Elk", "Hawk", "Crow" } },
            { "desert", new[] { "Dust", "Sand", "Dry", "Red", "Copper", "Sun", "Bone", "Mesa", "Cactus", "Sage", "Coyote", "Rattler", "Adobe", "Mirage", "Scorpion", "Buzzard", "Thirst", "Gold", "Amber", "Salt" } },
            { "snow", new[] { "Frost", "Snow", "White", "Ice", "Cold", "Winter", "Glacier", "Sleet", "Hoar", "Silver", "Pale", "North", "Drift", "Blizzard", "Avalanche", "Flint", "Crystal", "Polar", "Hail", "Chill" } },
            { "wasteland", new[] { "Rust", "Ash", "Scrap", "Iron", "Slag", "Grey", "Lead", "Tin", "Gravel", "Cinder", "Smog", "Crater", "Ruin", "Static", "Hollow", "Grim", "Wreck", "Chrome", "Bleak", "Soot" } },
            { "burnt", new[] { "Ember", "Cinder", "Char", "Smoke", "Burn", "Scorch", "Blaze", "Kindle", "Ash", "Soot", "Tinder", "Pyre", "Flame", "Coal", "Spark", "Black", "Brand", "Sear", "Torch", "Fire" } },
        };
        private static readonly string[] PlaceSuffix = { "wood", "field", "ridge", " Hollow", " Creek", "brook", " Ford", "ville", "ton", "dale", "haven", " Gulch",
            " Crossing", " Flats", " Bend", " Falls", " Rock", " Point", " Springs", " Gap", " Run", " Hill", " Valley",
            " Pass", "stead", " Junction", " Landing", " Grove", " Mill", " Station" };

        private static readonly string[] Group = { "Survivors", "Collective", "Wanderers", "Drifters", "Settlers", "Holdouts", "Remnant", "Folk", "Co-op",
            "Commune", "Neighbors", "Pilgrims", "Refugees", "Stragglers", "Scavengers", "Salvagers", "Homesteaders",
            "Band", "Circle", "Fellowship", "Union", "Guild", "Watch", "Caretakers", "Keepers", "Outcasts", "Misfits",
            "Strays", "Nomads", "Travelers", "Preppers", "Trappers", "Rangers", "Diehards", "Castaways", "Holdfast",
            "Hearth", "Lantern Bearers", "Night Shift", "Lucky Few" };
        private static readonly string[] Adj = { "Scattered", "Lost", "Last", "Hungry", "Tired", "Stubborn", "Quiet", "Lucky", "Unlucky", "Forgotten", "Hopeful",
            "Patient", "Restless", "Weary", "Ragged", "Humble", "Hidden", "Little", "Lonely", "Old", "Stranded", "Mended",
            "Honest", "Northern", "Southern", "Eastern", "Western", "Midnight", "Sunday", "Wandering", "Faithful", "Grateful",
            "Careful", "Barefoot", "Sleepless", "Wayward", "Unbroken", "Steadfast", "Gentle", "Lantern" };
        /// <summary>Neutral-but-shady groups (the user's own examples: The Looters, The Squatters).</summary>
        private static readonly string[] NeutralPlain = { "Squatters", "Looters", "Scavengers", "Drifters", "Hoarders", "Strays", "Holdouts", "Diggers", "Pickers", "Moochers" };
        /// <summary>The "plain" template's pool: NeutralPlain plus one two-word name.</summary>
        private static readonly string[] PlainPool = Concat(NeutralPlain, new[] { "Scattered Survivors" });

        private static readonly string[] HostileAdj = { "Black", "Red", "Rusted", "Iron", "Bloody", "Grim", "Savage", "Rabid", "Burning", "Feral", "Rotten",
            "Crooked", "Wicked", "Mad", "Bone", "Dead", "Howling", "Gutter", "Razor", "Scrap", "Toxic", "Cinder",
            "Hollow", "Broken", "Hungry", "Grinning", "Screaming", "Chained", "Branded", "Gutted" };
        private static readonly string[] HostileGroup = { "Looters", "Raiders", "Marauders", "Reapers", "Jackals", "Vipers", "Wolves", "Hyenas", "Vultures",
            "Butchers", "Crows", "Wreckers", "Bandits", "Outlaws", "Cutthroats", "Rats", "Dogs", "Hounds",
            "Kings", "Saints", "Brotherhood", "Syndicate", "Mob", "Pack", "Horde", "Skulls", "Teeth", "Knives",
            "Chainmen", "Scalpers" };

        /// <summary>POI kinds and the words / groups that pair naturally with them ("The Chapel Faithful").</summary>
        private static readonly Dictionary<string, string[][]> PoiPairs = new Dictionary<string, string[][]>
        {
            { "church", new[] { new[] { "Chapel", "Steeple", "Church", "Parish" }, new[] { "Faithful", "Congregation", "Flock", "Choir", "Believers" } } },
            { "farm", new[] { new[] { "Barn", "Silo", "Orchard", "Ranch", "Homestead" }, new[] { "Farmers", "Growers", "Homesteaders", "Hands", "Folk" } } },
            { "school", new[] { new[] { "Schoolhouse", "Campus", "Classroom", "Gym" }, new[] { "Teachers", "Class", "Students", "Staff", "Kids" } } },
            { "medical", new[] { new[] { "Clinic", "Ward", "Pharmacy", "Infirmary" }, new[] { "Medics", "Caretakers", "Staff", "Nurses", "Orderlies" } } },
            { "fire", new[] { new[] { "Firehouse", "Station Nine", "Engine House" }, new[] { "Brigade", "Crew", "Company", "Ladder Crew" } } },
            { "police", new[] { new[] { "Precinct", "Station House", "Sheriff's Office" }, new[] { "Watch", "Deputies", "Night Watch", "Posse" } } },
            { "factory", new[] { new[] { "Mill", "Foundry", "Plant", "Works", "Warehouse" }, new[] { "Workers", "Union", "Shift", "Crew", "Hands" } } },
            { "lodging", new[] { new[] { "Motel", "Inn", "Lodge", "Hotel" }, new[] { "Guests", "Regulars", "Long-Stayers", "Night Clerks" } } },
            { "food", new[] { new[] { "Diner", "Tavern", "Saloon", "Cafe", "Bar" }, new[] { "Regulars", "Last Call Club", "Cooks", "Lunch Crowd" } } },
            { "store", new[] { new[] { "Market", "Corner Store", "Mall", "Pawn Shop", "Hardware" }, new[] { "Shopkeepers", "Traders", "Clerks", "Hagglers" } } },
            { "military", new[] { new[] { "Bunker", "Outpost", "Depot", "Armory" }, new[] { "Squad", "Detachment", "Platoon", "Garrison", "Holdouts" } } },
            { "fuel", new[] { new[] { "Pump", "Gas Station", "Truck Stop", "Garage" }, new[] { "Pit Crew", "Grease Monkeys", "Truckers", "Mechanics" } } },
            { "residence", new[] { new[] { "Cul-de-sac", "Block", "Trailer Park", "Apartments" }, new[] { "Neighbors", "Tenants", "Block Club", "Residents" } } },
            { "cabin", new[] { new[] { "Cabin", "Lodge", "Camp", "Lookout" }, new[] { "Hermits", "Trappers", "Campers", "Rangers", "Hunters" } } },
        };
        /// <summary>Who the "at" template puts at the POI ("The Holdouts at the Diner").</summary>
        private static readonly string[] AtGroups = { "Folks", "Strays", "Holdouts", "Regulars", "Diehards", "Squatters", "Survivors" };

        private static readonly string[] First = { "Martha", "Dale", "Rosa", "Hank", "June", "Earl", "Dottie", "Wade", "Nell", "Gus", "Ida", "Boone", "Lou", "Ray",
            "Maggie", "Otis", "Pearl", "Cody", "Vera", "Jesse", "Tilly", "Amos", "Bea", "Clyde", "Hazel", "Duke", "Mabel",
            "Roy", "Esther", "Silas", "Wanda", "Rex", "Opal", "Jody", "Grady", "Lena", "Carl", "Sadie", "Floyd", "Iris" };
        private static readonly string[] Surname = { "Holloway", "Mercer", "Barlow", "Whitlock", "Crane", "Pruitt", "Dawes", "Kessler", "Harlan", "Sutter",
            "Grimes", "Tate", "Booker", "Calloway", "Ridley", "Voss", "McCrae", "Lund", "Ashby", "Fenwick", "Pike",
            "Rourke", "Hatch", "Coburn", "Wells", "Doyle", "Garrity", "Sims", "Ostrander", "Quill" };

        private static readonly string[] Family = { "Family", "Clan", "Kin", "Household", "Boys", "Sisters", "Cousins", "Folks" };
        private static readonly string[] Crew = { "Crew", "Kin", "People", "Folk", "Band", "Gang", "Lot", "Bunch", "Strays", "Circle" };
        private static readonly string[] HostileLeader = { "Gang", "Butchers", "Boys", "Dogs", "Crew", "Mob", "Wolves", "Knives" };

        private static readonly string[] HostileTemplates = { "adj", "place", "leader", "plain", "of" };
        private static readonly int[] HostileWeights = { 40, 20, 15, 15, 10 };
        private static readonly string[] Templates = { "adj", "place", "poi", "leader", "family", "plain", "of", "at" };

        /// <summary>The first tries keep word roots apart across the save too (see Unique); the rest only within a name.</summary>
        private const int StrictTries = 12;

        /// <summary>The name generator's kinds (PoiPairs keys); "" = no particular kind.</summary>
        public static IEnumerable<string> PoiKinds { get { return PoiPairs.Keys; } }

        public static readonly string[] BiomeKeys = { "forest", "desert", "snow", "wasteland", "burnt" };

        private static string[] Concat(string[] a, string[] b)
        {
            var r = new string[a.Length + b.Length];
            a.CopyTo(r, 0);
            b.CopyTo(r, a.Length);
            return r;
        }

        private static string Pick(GameRandom rng, string[] list) { return list[rng.RandomRange(list.Length)]; }

        /// <summary>Python's random.choices with integer weights: one index, a zero weight never drawn.</summary>
        private static int Weighted(GameRandom rng, int[] weights)
        {
            int total = 0;
            foreach (int w in weights) total += Math.Max(0, w);
            double r = rng.RandomDouble * total;
            int last = 0;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0) continue;
                last = i;
                if (r < weights[i]) return i;
                r -= weights[i];
            }
            return last;
        }

        /// <summary>3.2's biome name -> the prototype's place-word set (anything else reads as forest).</summary>
        public static string BiomeKey(string biome)
        {
            switch ((biome ?? "").ToLowerInvariant())
            {
                case "snow": return "snow";
                case "desert": return "desert";
                case "wasteland": return "wasteland";
                case "burnt_forest": return "burnt";
                default: return "forest";
            }
        }

        /// <summary>The outpost rows (ScPoiTypes) as the generator's POI kinds.</summary>
        private static readonly Dictionary<string, string> RowKinds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "grocery", "store" }, { "diner", "food" }, { "farm", "farm" }, { "clinic", "medical" }, { "military", "military" },
            { "gunstore", "store" }, { "police", "police" }, { "firestation", "fire" }, { "hardware", "store" }, { "industry", "factory" },
            { "junkyard", "factory" }, { "gasstation", "fuel" }, { "refinery", "fuel" }, { "waterworks", "factory" },
        };

        /// <summary>
        /// Name tokens (ScPoiTypes.Tokens of the prefab and localized names) -> kind, for a POI no outpost row names: first
        /// match wins, so the specific kinds come before the residence and cabin catch-alls.
        /// </summary>
        private static readonly string[][] TokenKinds =
        {
            new[] { "church", "church", "chapel", "cathedral", "steeple", "parish", "temple", "mission" },
            new[] { "school", "school", "schoolhouse", "campus", "college", "university", "academy", "elementary" },
            new[] { "medical", "hospital", "clinic", "pharmacy", "medical", "doctor", "dentist", "infirmary" },
            new[] { "fire", "fire", "firestation", "firehouse" },
            new[] { "police", "police", "sheriff", "jail", "prison", "precinct" },
            new[] { "military", "army", "military", "bunker", "fort", "barracks", "armory" },
            new[] { "lodging", "hotel", "motel", "inn", "resort", "hostel" },
            new[] { "food", "diner", "restaurant", "cafe", "bar", "pub", "saloon", "tavern", "bakery", "pizza", "burger" },
            new[] { "fuel", "gas", "garage", "truckstop" },
            new[] { "farm", "farm", "farmhouse", "barn", "orchard", "homestead", "silo" },
            new[] { "factory", "factory", "mill", "sawmill", "warehouse", "plant", "works", "foundry", "depot", "lumber" },
            new[] { "store", "store", "shop", "market", "mall", "pawn", "boutique", "books" },
            new[] { "cabin", "cabin", "lodge", "camp", "campsite", "campground", "lookout", "hunting", "ranger", "shack" },
            new[] { "residence", "house", "home", "residence", "apartment", "apartments", "trailer", "duplex", "condo", "mansion", "cottage", "bungalow", "townhouse" },
        };

        /// <summary>The generator's POI kind for a POI: its outpost row, else its name words; "" = none.</summary>
        public static string PoiKind(string row, string prefab, string display)
        {
            string k;
            if (!string.IsNullOrEmpty(row) && RowKinds.TryGetValue(row, out k)) return k;
            var toks = ScPoiTypes.Tokens(prefab);
            toks.AddRange(ScPoiTypes.Tokens(display));
            foreach (string[] t in TokenKinds)
                for (int i = 1; i < t.Length; i++)
                    if (toks.Contains(t[i])) return t[0];
            return "";
        }

        public static string Place(GameRandom rng, string biome)
        {
            string[] pre;
            if (!PlacePrefix.TryGetValue(biome ?? "", out pre)) pre = PlacePrefix["forest"];
            return Pick(rng, pre) + Pick(rng, PlaceSuffix);
        }

        /// <summary>One draw from the templates (the prototype's name()).</summary>
        public static string Name(GameRandom rng, bool hostile, string biome, string kind)
        {
            if (hostile)
            {
                switch (HostileTemplates[Weighted(rng, HostileWeights)])
                {
                    case "adj": return "The " + Pick(rng, HostileAdj) + " " + Pick(rng, HostileGroup);
                    case "place": return "The " + Place(rng, biome) + " " + Pick(rng, HostileGroup);
                    case "leader":
                        return rng.RandomDouble < 0.5 ? Pick(rng, First) + "'s " + Pick(rng, HostileLeader)
                                                      : "The " + Pick(rng, Surname) + " " + Pick(rng, HostileLeader);
                    case "plain": return "The " + Pick(rng, HostileGroup);
                    default: return "The " + Pick(rng, HostileGroup) + " of " + Place(rng, biome);
                }
            }
            string[][] pair = null;
            bool hasKind = !string.IsNullOrEmpty(kind) && PoiPairs.TryGetValue(kind, out pair);
            int[] weights = { 24, 18, hasKind ? 16 : 0, 10, 10, 7, 7, hasKind ? 8 : 0 };
            switch (Templates[Weighted(rng, weights)])
            {
                case "adj": return "The " + Pick(rng, Adj) + " " + Pick(rng, Group);
                case "place": return "The " + Place(rng, biome) + " " + Pick(rng, Group);
                case "poi": return "The " + Pick(rng, pair[0]) + " " + Pick(rng, pair[1]);
                case "leader": return Pick(rng, First) + "'s " + Pick(rng, Crew);
                case "family": return "The " + Pick(rng, Surname) + " " + Pick(rng, Family);
                case "plain": return "The " + Pick(rng, PlainPool);
                case "at": return "The " + Pick(rng, AtGroups) + " at the " + Pick(rng, pair[0]);
                default: return "The " + Pick(rng, Group) + " of " + Place(rng, biome);
            }
        }

        /// <summary>The words of a name that carry meaning, lower-case ("the", "of", "at" and the possessive dropped).</summary>
        private static List<string> Words(string n)
        {
            var w = new List<string>();
            foreach (string x in (n ?? "").Replace("'s", "").Split(' '))
            {
                string l = x.ToLowerInvariant();
                if (l.Length > 0 && l != "the" && l != "of" && l != "at") w.Add(l);
            }
            return w;
        }

        /// <summary>The prototype's clash(): two words of one name share a 4-letter root ("The Cinder Cinderfolk").</summary>
        public static bool Clash(string n)
        {
            List<string> w = Words(n);
            for (int i = 0; i < w.Count; i++)
                for (int j = i + 1; j < w.Count; j++)
                    if (w[i].Length >= 4 && w[j].Length >= 4 && string.CompareOrdinal(w[i], 0, w[j], 0, 4) == 0) return true;
            return false;
        }

        /// <summary>The 4-letter roots of a name's words of 4+ letters (what clash() compares).</summary>
        private static IEnumerable<string> Roots(string n)
        {
            foreach (string w in Words(n)) if (w.Length >= 4) yield return w.Substring(0, 4);
        }

        /// <summary>
        /// A name nobody in the save has (taken: case-insensitive, and the new name is added to it), as the prototype's
        /// unique(): up to 20 draws that are not taken and do not clash, then "... of {place}". The first StrictTries
        /// draws also refuse a name that shares a word root with a name already taken, so a save does not get "The
        /// Drifters" beside "The Drifters of Cedar Ford" (E1_SPEC: no two names sharing a word root); the later draws only
        /// keep the prototype's own rules, so a crowded save still gets a name quickly.
        /// </summary>
        public static string Unique(GameRandom rng, HashSet<string> taken, bool hostile, string biome, string kind)
        {
            var roots = new HashSet<string>(StringComparer.Ordinal);
            foreach (string t in taken) foreach (string r in Roots(t)) roots.Add(r);
            string n;
            for (int i = 0; i < 20; i++)
            {
                n = Name(rng, hostile, biome, kind);
                if (taken.Contains(n) || Clash(n)) continue;
                if (i < StrictTries && SharesRoot(n, roots)) continue;
                taken.Add(n);
                return n;
            }
            n = Name(rng, hostile, biome, kind) + " of " + Place(rng, biome);
            for (int i = 0; i < 10 && taken.Contains(n); i++) n = Name(rng, hostile, biome, kind) + " of " + Place(rng, biome);
            // never reached in practice (thousands of free names per template); a number keeps the rule absolute
            for (int k = 2; taken.Contains(n); k++) n = Name(rng, hostile, biome, kind) + " " + k;
            taken.Add(n);
            return n;
        }

        private static bool SharesRoot(string n, HashSet<string> roots)
        {
            foreach (string r in Roots(n)) if (roots.Contains(r)) return true;
            return false;
        }
    }
}
