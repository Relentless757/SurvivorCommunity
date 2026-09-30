using System;
using System.Text;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Supply runs (State of Decay style): a stationed, healthy, willing survivor is sent out for one of the
    /// five resources. The entity is hidden in place through SCore's SendOnMission (scale 0, invisible,
    /// ignored by AI) and parked with a Stay order - SendOnMission does NOT stop the entity ticking - and
    /// the run itself is bookkeeping in the registry, so it resolves at the day rollover whether or not the
    /// base is loaded. Outcome: yield (RunYieldBase x Wits x specialty x traits x morale) into the stockpile,
    /// with a risk (RunRiskBase minus combat stars) of coming back injured or not at all. A survivor who dies
    /// on a run is removed like any death; their hidden entity is removed when it is loaded (Doomed list).
    /// </summary>
    public static class ScRuns
    {
        public static int Runners()
        {
            return ScRegistry.CountStatus(MemberStatus.OnRun);
        }

        /// <summary>Can this member be sent right now? (error = localization key)</summary>
        public static bool CanSend(Member m, EntityAliveSDX npc, out string error)
        {
            error = null;
            if (!ScCommunity.IsHost) { error = "scHostOnly"; return false; }
            if (m == null) { error = "scInternal"; return false; }
            if (m.Status == MemberStatus.OnRun) { error = "scOnRun"; return false; }
            if (m.Status != MemberStatus.Resident) { error = "scRunNotHome"; return false; }
            if (npc == null) { error = "scNotLoaded"; return false; }
            if (m.Injured) { error = "scRunInjured"; return false; }
            if (m.Mood < 25) { error = "scRefuseMood"; return false; }
            if (Runners() >= ScConfig.MaxRunners) { error = "scRunTooMany"; return false; }
            return true;
        }

        public static bool Send(Member m, EntityAliveSDX npc, Resource kind, EntityPlayerLocal player, out string error)
        {
            if (!CanSend(m, npc, out error)) return false;
            try
            {
                m.Status = MemberStatus.OnRun;
                m.RunKind = kind;
                m.RunEndDay = ScCommunity.Today + Math.Max(1, ScConfig.RunDays);
                Park(npc);
                npc.SendOnMission(true);
                m.RunDistress = false;
                m.RunSafe = false;
                m.EventSpawned = false;
                World rw = ScCommunity.World;
                bool distress = ScRadio.RollDistress(m, player, rw != null ? rw.GetGameRandom() : null);
                ScRegistry.SaveAtomic();
                if (player != null && !distress) ScCommunity.Tell(player, string.Format(Localization.Get("scRunSent"), m.Name, Localization.Get(ScNames.LocKey(kind)), m.RunEndDay), "");
                ScLog.Out(m.Name + " left on a supply run for " + kind + ", back on day " + m.RunEndDay);
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("run-send", e);
                error = "scInternal";
                return false;
            }
        }

        /// <summary>A hidden entity still runs its AI: a Stay order keeps it from roaming or fighting unseen.</summary>
        public static void Park(EntityAliveSDX npc)
        {
            try
            {
                if (npc == null) return;
                if (npc.getNavigator() != null) npc.getNavigator().clearPath();
                if (npc.moveHelper != null) npc.moveHelper.Stop();
                if (EntityUtilities.GetCurrentOrder(npc.entityId) != EntityUtilities.Orders.Stay) EntityUtilities.SetCurrentOrder(npc.entityId, EntityUtilities.Orders.Stay);
            }
            catch (Exception e)
            {
                ScLog.Once("run-park", e);
            }
        }

        /// <summary>Day rollover: every run whose day has come resolves; the rest keep going.</summary>
        public static void Resolve(int day, EntityPlayerLocal player, StringBuilder summary)
        {
            var done = new System.Collections.Generic.List<Member>();
            foreach (var m in ScRegistry.Members.Values)
                if (m.Status == MemberStatus.OnRun && day >= m.RunEndDay) done.Add(m);
            if (done.Count == 0) return;
            World w = ScCommunity.World;
            GameRandom rnd = w != null ? w.GetGameRandom() : null;
            Community c = ScRegistry.Community;
            for (int i = 0; i < done.Count; i++)
            {
                Member m = done[i];
                try
                {
                    Resource kind = m.RunKind;
                    float roll = rnd != null ? rnd.RandomFloat : UnityEngine.Random.value;
                    float roll2 = rnd != null ? rnd.RandomFloat : UnityEngine.Random.value;
                    float risk = Mathf.Clamp(ScConfig.RunRiskBase - ScConfig.RunRiskPerCombatStar * (m.Stars(Skill.Fighting) + m.Stars(Skill.Shooting) - 2)
                                             - ScOutpostPerks.EscortRisk(), 0.02f, 0.5f);   // an ammo outpost's armed escort
                    int yield = Yield(m, kind);
                    bool bad = roll < risk;
                    bool dead = bad && roll2 < ScConfig.RunDeathShare;
                    if (m.RunDistress)
                    {
                        // a radio distress run: the player's answer decides it, not the dice
                        if (m.RunSafe) { bad = false; dead = false; yield = Mathf.RoundToInt(yield * ScConfig.RescueBonus); }
                        else { bad = true; dead = roll2 < ScConfig.RunDeathShare; }
                        ScRadio.EndDistress(w, player, m);
                    }

                    if (dead)
                    {
                        summary.Append(' ').Append(string.Format(Localization.Get("scDailyRunDied"), m.Name));
                        ScLog.Out(m.Name + " did not come back from the supply run (risk " + risk.ToString("0.00") + ")");
                        Die(m, player);
                        continue;
                    }
                    if (bad) { m.Injured = true; yield = Math.Max(0, yield / 2); }
                    c.Add(kind, yield);
                    ScSkills.AddXp(m, Skill.Wits, ScConfig.RunWitsXp);
                    if (SpecialtyFor(kind) == m.Specialty) ScSkills.AddSpecialtyXp(m, 3f);
                    m.Status = MemberStatus.Resident;
                    m.RunEndDay = -1;
                    // the entity: un-hide and re-station now when loaded; the relink does it when it next loads
                    EntityAliveSDX npc = ScCommunity.FindNpc(m.Id);
                    if (npc != null)
                    {
                        npc.SendOnMission(false);
                        ScCommunity.Station(m, npc);
                    }
                    summary.Append(' ').Append(string.Format(Localization.Get(bad ? "scDailyRunHurt" : "scDailyRunBack"), m.Name, yield, Localization.Get(ScNames.LocKey(kind))));
                    if (player != null) ScCommunity.Tell(player, string.Format(Localization.Get(bad ? "scRunHurt" : "scRunBack"), m.Name, yield, Localization.Get(ScNames.LocKey(kind))), bad ? "ui_denied" : "");
                    ScLog.Out(m.Name + " is back from the supply run: " + yield + " " + kind + (bad ? " (injured)" : "") + " (risk " + risk.ToString("0.00") + ")");
                }
                catch (Exception e)
                {
                    ScLog.Once("run-resolve", e);
                    m.Status = MemberStatus.Resident;
                    m.RunEndDay = -1;
                }
            }
        }

        public static int Yield(Member m, Resource kind)
        {
            float y = ScConfig.RunYieldBase;
            y *= 1f + ScConfig.RunYieldPerWitsStar * (m.Stars(Skill.Wits) - 1);
            if (SpecialtyFor(kind) == m.Specialty) y *= 1f + ScConfig.RunSpecialistPerStar * Math.Max(0, m.SpecLevel - 1);
            y *= ScTraits.RunYieldMult(m);
            if (ScOutpostPerks.PerkActive(Resource.Fuel)) y *= ScConfig.OutpostMotorPoolMult;   // a fuel outpost's motor pool
            if (ScRegistry.Community.Morale > ScConfig.HighMoraleThreshold) y *= 1.25f;
            return Math.Max(1, Mathf.RoundToInt(y));
        }

        /// <summary>Which specialty knows where to find a resource: Mechanics for materials and fuel, Chemistry for medicine, Cooking for food.</summary>
        public static Specialty SpecialtyFor(Resource kind)
        {
            switch (kind)
            {
                case Resource.Materials:
                case Resource.Fuel: return Specialty.Mechanics;
                case Resource.Meds: return Specialty.Chemistry;
                case Resource.Food: return Specialty.Cooking;
                default: return Specialty.None;
            }
        }

        private static void Die(Member m, EntityPlayerLocal player)
        {
            EntityAliveSDX npc = ScCommunity.FindNpc(m.Id);
            ScCommunity.RecordDeath(m, player, "scRunDied");
            if (npc != null)
            {
                RemoveEntity(npc);
            }
            else
            {
                ScRegistry.Doomed.Add(m.Id);        // the hidden entity is removed when its chunk next loads
            }
        }

        /// <summary>Removes a hidden runner's entity for good (no corpse, no chunk record).</summary>
        public static void RemoveEntity(EntityAliveSDX npc)
        {
            try
            {
                World w = ScCommunity.World;
                if (w == null || npc == null) return;
                npc.Buffs.RemoveCustomVar("Persist");
                w.RemoveEntity(npc.entityId, EnumRemoveEntityReason.Killed);
            }
            catch (Exception e)
            {
                ScLog.Once("run-remove", e);
            }
        }

        public static string StatusLine(Member m)
        {
            if (m == null || m.Status != MemberStatus.OnRun) return "";
            return string.Format(Localization.Get("scWinOnRun"), Localization.Get(ScNames.LocKey(m.RunKind)), m.RunEndDay);
        }
    }
}
