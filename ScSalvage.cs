using System;
using System.Globalization;

namespace SurvivorCommunity
{
    /// <summary>
    /// Base defense BD1, Salvage (round m1f, 2026-11-17; BaseDefence_Plan.md's idea menu, ROADMAP.md section 3 item 5): after a
    /// held attack the corpse pile yields materials - 1 unit per SalvageKillsPerMaterial kills (rounded down; 0 turns it off).
    ///   held       ScBaseAttack.Held, the fight with the player: the attackers killed; a tooltip and a ledger line.
    ///   away       ScDefence.Settle with the result "held", the defenders alone or a siege: the whole horde - for an attack
    ///              the player left, the ones killed before too (NoteAwayTotal, from ScBaseAttack.SettleAway: the Fable review's
    ///              finding 7), for a siege the horde it was; a ledger line (the radio's news and the return's news are unchanged).
    /// Damaged, overrun and a fight with nobody killed yield nothing. Also the last attack's record for the Facilities tab's
    /// Defense entry (Record, saved as lastattack=): every ending sets it - held, the three results without the player, a
    /// siege's, and one that lost the trail.
    /// </summary>
    public static class ScSalvage
    {
        public const int KindNone = 0, KindHeld = 1, KindHeldAway = 2, KindDamaged = 3, KindOverrun = 4,
                         KindSiegeHeld = 5, KindSiegeDamaged = 6, KindSiegeOverrun = 7, KindLostTrail = 8;

        /// <summary>The whole horde of the attack ScBaseAttack.SettleAway is settling (0: none noted); Settled takes it once.</summary>
        private static int awayTotal;

        /// <summary>Per-world reset (ModApi): no noted total carries into the next world (the re-check's N4).</summary>
        public static void Reset()
        {
            awayTotal = 0;
        }

        /// <summary>ScBaseAttack.SettleAway, before it clears the attack: its full size, for the corpse pile and the record.</summary>
        public static void NoteAwayTotal(int total)
        {
            awayTotal = Math.Max(0, total);
        }

        /// <summary>Materials a pile of this many dead yields (0 with Salvage off).</summary>
        public static int UnitsFor(int kills)
        {
            int n = ScConfig.SalvageKillsPerMaterial;
            return n > 0 && kills > 0 ? kills / n : 0;
        }

        private static int Give(int kills, string why)
        {
            int units = UnitsFor(kills);
            if (units <= 0) return 0;
            ScRegistry.Community.Add(Resource.Materials, units);
            ScLog.Out("salvage: " + units + " material(s) from " + kills + " dead (" + why + ", 1 per " + ScConfig.SalvageKillsPerMaterial + "); stockpile "
                      + ScRegistry.Community.Get(Resource.Materials));
            return units;
        }

        /// <summary>ScBaseAttack.Held (the caller saves): the fight the player was at held - the salvage, its tooltip and ledger line, the record.</summary>
        public static void Held(EntityPlayerLocal player, int killed)
        {
            try
            {
                int units = Give(killed, "a held attack");
                Record(KindHeld, killed, units);
                if (units <= 0) return;
                string line = string.Format(Localization.Get("scSalvageHeld"), units, killed);
                ScOutposts.NoteLedger(line);
                if (player != null) ScCommunity.Tell(player, line, "");
            }
            catch (Exception e)
            {
                ScLog.Once("salvage-held", e);
            }
        }

        /// <summary>ScDefence.Settle (the caller saves): a fight without the player, or a siege - salvage when held, and the record.</summary>
        public static void Settled(bool siege, int result, int horde)
        {
            int total = siege || awayTotal <= 0 ? horde : Math.Max(horde, awayTotal);
            awayTotal = 0;
            try
            {
                int r = Math.Max(ScDefence.Held, Math.Min(ScDefence.Overrun, result));
                int kind = siege ? KindSiegeHeld + r : r == ScDefence.Held ? KindHeldAway : r == ScDefence.Damaged ? KindDamaged : KindOverrun;
                int units = r == ScDefence.Held ? Give(total, siege ? "a siege held" : "a fight held without the player") : 0;
                Record(kind, total, units);
                if (units > 0) ScOutposts.NoteLedger(string.Format(Localization.Get("scSalvageAway"), units, total));
            }
            catch (Exception e)
            {
                ScLog.Once("salvage-settled", e);
            }
        }

        /// <summary>The last attack's ending, for the Defense entry (lastattack=).</summary>
        public static void Record(int kind, int count, int salvage)
        {
            Community c = ScRegistry.Community;
            c.LastAttackDay = ScCommunity.Today;
            c.LastAttackKind = kind;
            c.LastAttackCount = Math.Max(0, count);
            c.LastAttackSalvage = Math.Max(0, salvage);
        }

        private static readonly string[] KindKeys =
        {
            "scUiFacLastNone", "scUiFacLastHeld", "scUiFacLastHeldAway", "scUiFacLastDamaged", "scUiFacLastOverrun",
            "scUiFacLastSiegeHeld", "scUiFacLastSiegeDamaged", "scUiFacLastSiegeOverrun", "scUiFacLastLostTrail",
        };

        /// <summary>"Day 12: held, 14 killed (+2 materials)", or "None yet".</summary>
        public static string LastText()
        {
            Community c = ScRegistry.Community;
            int k = c.LastAttackKind;
            if (k <= KindNone || k >= KindKeys.Length) return Localization.Get(KindKeys[0]);
            string s = string.Format(Localization.Get(KindKeys[k]), c.LastAttackDay.ToString(CultureInfo.InvariantCulture), c.LastAttackCount);
            if (c.LastAttackSalvage > 0) s += " " + string.Format(Localization.Get("scUiFacLastSalvage"), c.LastAttackSalvage);
            return s;
        }

        public static string LastColor()
        {
            switch (ScRegistry.Community.LastAttackKind)
            {
                case KindHeld: case KindHeldAway: case KindSiegeHeld: return ScFacilityTab.Good;
                case KindDamaged: case KindSiegeDamaged: return ScFacilityTab.Warn;
                case KindOverrun: case KindSiegeOverrun: return ScFacilityTab.Bad;
                default: return ScFacilityTab.Body;
            }
        }
    }
}
