using System;
using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// Rounds U4, U5 and U6 (2026-11-16, research/npc306/FEATURES_306.md sections 3, 5, 6 and 7 - the user's decisions D7 to
    /// D10 and D12): the config keys of 0-XNPCCore 3.4.06's relationships, gifts, weapon perks and party cap as SC adapts them.
    /// Kept apart from ScConfig.cs's long lists, as ScConfigTeam. Config/blocks.xml, block ConfigSurvivorCommunity, after
    /// RegenNearRadius; each key is commented there. ResetNpc306 also clears ScBond's, ScPerks' and ScDrinks' per-world state
    /// (ScConfig.Reset runs at GameStarting).
    /// </summary>
    public static partial class ScConfig
    {
        // ------------------------------------------------------------------ U4: Bond (D7, D9) ----

        /// <summary>
        /// U4 (D7): 0-XNPCCore's relationship events feed each member's Bond, a term of her mood (ScBond). false: no Bond and no
        /// mood term; 0-XNPCCore's numbers stay SC's all the same (its writes skipped for SC's people, the Progress line from mood).
        /// </summary>
        public static bool MemberBond = true;
        /// <summary>Bond never goes past +- this (the user: 15).</summary>
        public static int BondCap = 15;
        /// <summary>Bond fades this much toward 0 at every day rollover (the user: 1).</summary>
        public static int BondDecayPerDay = 1;
        /// <summary>Bond per point of a gift (0-XNPCCore's gift points: log2 of its value, at most 6 a day per member). The user: 1:1.</summary>
        public static int BondGiftPerPoint = 1;
        /// <summary>Bond when she is revived, whoever revives her (the user: +3).</summary>
        public static int BondRevive = 3;
        /// <summary>Bond for a blood moon survived together (the user: +4).</summary>
        public static int BondBloodMoon = 4;
        /// <summary>Bond when she goes down (the user: -2).</summary>
        public static int BondDowned = -2;
        /// <summary>Bond when you kill an enemy that was on her (the user: +1).</summary>
        public static int BondKillOnHer = 1;
        /// <summary>
        /// The Fable review of U4 (finding 2): at most this many kills on her give Bond per in-game day (the gift cap's twin: a
        /// base horde would put every fighting resident at the cap, and daily kills outran the fade). 0: kills never give Bond.
        /// </summary>
        public static int BondKillPerDay = 3;
        /// <summary>The Progress page says "Trust: loyal" from this Bond up and "wary" from minus this down (and while she threatens to leave).</summary>
        public static int BondLoyalAt = 8;

        /// <summary>U4 (D9): coins handed to a member or a stand-in as a gift are refused ("I've no use for that."): members are never paid.</summary>
        public static bool MemberRefuseCoins = true;
        /// <summary>U4 (D9): ScMedic uses painkillers when she has no first aid kit or first aid bandage left (a last resort).</summary>
        public static bool MedicPainkillers = true;
        /// <summary>Health a painkiller gives one of SC's bodies (vanilla's painkillers heal 40 once).</summary>
        public static int PainkillerHeal = 40;
        /// <summary>
        /// U4 (D9): at home a follower is topped up to this many drinks from the Water stockpile (0-XNPCCore's hired survivors spend a
        /// drink to put out fire on themselves; residents douse for free). 0: never.
        /// </summary>
        public static int MemberDrinks = 2;
        /// <summary>The top-up happens when she carries fewer drinks than this.</summary>
        public static int MemberDrinksBelow = 1;
        /// <summary>The drink the top-up gives (one of 0-XNPCCore's douse drinks).</summary>
        public static string MemberDrinkItem = "drinkJarBoiledWater";
        /// <summary>In-game hours between two drink top-ups of one member (session only).</summary>
        public static float MemberDrinksCooldownHours = 12f;

        // ------------------------------------------------------------------ U5: weapon perks (D10) ----

        /// <summary>
        /// U5 (D10): a member's Shooting stars give 0-XNPCCore's ranged weapon-class perk ranks and her Fighting stars the melee
        /// ones, rank = clamp((stars - 1) / 2, 0, 3), within 0-XNPCCore's PerkRankCap and its PerkList (ScPerks).
        /// </summary>
        public static bool MemberWeaponPerks = true;

        // ------------------------------------------------------------------ U6: party cap (D12) ----

        /// <summary>
        /// U6 (D12): SC's own Follow (talk, voice, community window) honors 0-XNPCCore's PartyMaxMembers, which paid hires and SC's
        /// followers share (ScPartyCap). MaxFollowers stays SC's own lower cap.
        /// </summary>
        public static bool SharePartyCap = true;

        private static void ResetNpc306()
        {
            MemberBond = true;
            BondCap = 15;
            BondDecayPerDay = 1;
            BondGiftPerPoint = 1;
            BondRevive = 3;
            BondBloodMoon = 4;
            BondDowned = -2;
            BondKillOnHer = 1;
            BondKillPerDay = 3;
            BondLoyalAt = 8;
            MemberRefuseCoins = true;
            MedicPainkillers = true;
            PainkillerHeal = 40;
            MemberDrinks = 2;
            MemberDrinksBelow = 1;
            MemberDrinkItem = "drinkJarBoiledWater";
            MemberDrinksCooldownHours = 12f;
            MemberWeaponPerks = true;
            SharePartyCap = true;
            // the per-world state of the three rounds: a new world starts with none of it
            ScBond.Reset();
            ScPerks.Reset();
            ScDrinks.Reset();
            ScPartyCap.Reset();
            Patches_Relationship.Reset();
        }

        private static void ParseNpc306(DynamicProperties p)
        {
            ParseBoolKey(p, "MemberBond", ref MemberBond);
            ParseInt(p, "BondCap", ref BondCap, 0, 100);
            ParseInt(p, "BondDecayPerDay", ref BondDecayPerDay, 0, 100);
            ParseInt(p, "BondGiftPerPoint", ref BondGiftPerPoint, 0, 10);
            ParseInt(p, "BondRevive", ref BondRevive, -100, 100);
            ParseInt(p, "BondBloodMoon", ref BondBloodMoon, -100, 100);
            ParseInt(p, "BondDowned", ref BondDowned, -100, 100);
            ParseInt(p, "BondKillOnHer", ref BondKillOnHer, -100, 100);
            ParseInt(p, "BondKillPerDay", ref BondKillPerDay, 0, 100);
            ParseInt(p, "BondLoyalAt", ref BondLoyalAt, 1, 100);
            ParseBoolKey(p, "MemberRefuseCoins", ref MemberRefuseCoins);
            ParseBoolKey(p, "MedicPainkillers", ref MedicPainkillers);
            ParseInt(p, "PainkillerHeal", ref PainkillerHeal, 0, 1000);
            ParseInt(p, "MemberDrinks", ref MemberDrinks, 0, 10);
            ParseInt(p, "MemberDrinksBelow", ref MemberDrinksBelow, 0, 10);
            string v;
            if (p.Values.TryGetValue("MemberDrinkItem", out v) && !string.IsNullOrEmpty(v)) MemberDrinkItem = v.Trim();
            ParseFloat(p, "MemberDrinksCooldownHours", ref MemberDrinksCooldownHours, 0f, 720f);
            ParseBoolKey(p, "MemberWeaponPerks", ref MemberWeaponPerks);
            ParseBoolKey(p, "SharePartyCap", ref SharePartyCap);
        }

        /// <summary>The config line's U4-U6 part.</summary>
        internal static string Npc306Line()
        {
            return "bond=" + (MemberBond ? "on cap " + BondCap + " decay " + BondDecayPerDay + "/day, gift x" + BondGiftPerPoint + " revive " + Signed(BondRevive)
                                           + " bloodmoon " + Signed(BondBloodMoon) + " downed " + Signed(BondDowned) + " kill-on-her " + Signed(BondKillOnHer) + " (" + BondKillPerDay + "/day)"
                                           + ", loyal at " + BondLoyalAt : "off")
                   + " refuseCoins=" + MemberRefuseCoins
                   + " painkillers=" + (MedicPainkillers ? "last resort, heal " + PainkillerHeal : "off")
                   + " drinks=" + (MemberDrinks > 0 ? "followers to " + MemberDrinks + " below " + MemberDrinksBelow + " (" + MemberDrinkItem + ", every "
                                                      + MemberDrinksCooldownHours.ToString("0.##", CultureInfo.InvariantCulture) + " h)" : "off")
                   + " weaponPerks=" + MemberWeaponPerks
                   + " sharePartyCap=" + SharePartyCap;
        }

        private static string Signed(int v) { return (v >= 0 ? "+" : "") + v.ToString(CultureInfo.InvariantCulture); }
    }
}
