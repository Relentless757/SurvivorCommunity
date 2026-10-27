using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Who gave a SurvivorCommunity quest, for other mods' quest screens (AshfallUI's Quests page "Quest Giver" card finds this
    /// class by its full name through reflection: neither mod references the other, so the signatures are the contract).
    ///
    /// The giver is looked up from the quest each call, never cached, so it follows every re-key:
    ///  - a recruit favour (quest_scSecurePoi / quest_scSupplies / quest_scRescue): the prospect under Quest.QuestGiverID, which
    ///    RekeyRescue and ScEnclaveDeals.RekeyFavour move with the survivor's body (an older POI quest is matched by its POI,
    ///    as the ticker does); a prospect who has since joined answers as the member;
    ///  - an enclave request (quest_scEnc*): its giver is the enclave, which has no QuestGiverID (its people's bodies are rebuilt
    ///    with new ids), so the request id the quest carries names the enclave, and its leader speaks for it.
    ///
    /// Both methods are cheap (dictionary and list lookups), never throw, and return null for any quest that is not SC's and
    /// wherever SC has no records (a multiplayer client: the registry lives on the host) or cannot render (a dedicated server).
    /// </summary>
    public static class ScQuestGiverApi
    {
        /// <summary>Portrait ids for enclave people with a look: their bodies' entity ids change at every spawn, so the
        /// picture is keyed on the person instead, in a range no entity id reaches.</summary>
        private const int EncPortraitBase = -2000000;

        /// <summary>The survivor who gave this quest, or null: { name, role, bio } (localized; the bio is one or two short
        /// sentences for a 270x62 box).</summary>
        public static string[] Describe(Quest quest)
        {
            try
            {
                if (quest == null || !ScQuests.IsOurs(quest)) return null;

                if (ScQuests.IsRequestQuest(quest))
                {
                    Enclave e;
                    EnclaveRequest r;
                    EncPerson leader = LeaderFor(quest, out e, out r);
                    if (e == null) return null;
                    string name = leader != null && !string.IsNullOrEmpty(leader.Who.Name) ? leader.Who.Name : e.Name;
                    return new[] { name, Fmt("scQgRoleLeader", e.Name), Clip(EnclaveBio(e, r)) };
                }

                Member member = ScRegistry.GetMember(quest.QuestGiverID);
                if (member != null)
                {
                    return new[] { member.Name, Localization.Get("scQgRoleMember"), Clip(MemberBio(member)) };
                }

                Prospect p = ScQuests.ProspectFor(quest);
                if (p == null) return null;
                return new[] { p.Name, Localization.Get("scQgRoleProspect"), Clip(ProspectBio(p)) };
            }
            catch (Exception ex)
            {
                ScLog.Once("questgiver-describe", ex);
                return null;
            }
        }

        /// <summary>The giver's portrait in the Community window's style (ScPortraits, 256 square), or null until it is ready.
        /// Never renders here: a missing picture is queued and a later call returns it. Never destroy what this returns.</summary>
        public static Texture Portrait(Quest quest)
        {
            try
            {
                if (quest == null || GameManager.IsDedicatedServer || !ScQuests.IsOurs(quest)) return null;
                Member subject = Subject(quest);
                if (subject == null) return null;
                // the window's own flag: an asking screen is a window with portraits (it only ever turns on)
                ScPortraits.Wanted = true;
                return ScPortraits.Get(subject);
            }
            catch (Exception ex)
            {
                ScLog.Once("questgiver-portrait", ex);
                return null;
            }
        }

        // ---------------------------------------------------------------- who

        /// <summary>The record ScPortraits shoots: a member's own, else a stand-in carrying the look (or, for a pack NPC with
        /// no look, the live body's id, which is all a live capture can use).</summary>
        private static Member Subject(Quest q)
        {
            if (ScQuests.IsRequestQuest(q))
            {
                Enclave e;
                EnclaveRequest r;
                EncPerson leader = LeaderFor(q, out e, out r);
                if (leader == null || leader.Who == null) return null;
                Member w = leader.Who;
                if (w.Look != null) return StandIn(EncPortraitBase - leader.Id, w.Name, w.Class, 0UL, w.Look);
                return leader.EntityId >= 0 ? StandIn(leader.EntityId, w.Name, w.Class, leader.EntityBorn, null) : null;
            }

            Member m = ScRegistry.GetMember(q.QuestGiverID);
            if (m != null) return m;
            Prospect p = ScQuests.ProspectFor(q);
            if (p == null || p.Id <= 0) return null;               // parked (no body) and no look: nothing to shoot
            ulong born = 0UL;
            if (p.Look == null)
            {
                EntityAlive npc = ScCommunity.FindNpc(p.Id);
                if (npc == null) return null;
                born = npc.WorldTimeBorn;
            }
            return StandIn(p.Id, p.Name, p.Class, born, p.Look);
        }

        /// <summary>One stand-in per id, updated in place: ScPortraits keys its cache on the id and re-shoots when the look
        /// changes, and a fresh object per call (twice a second) would only make garbage.</summary>
        private static readonly Dictionary<int, Member> standIns = new Dictionary<int, Member>();

        private static Member StandIn(int id, string name, string cls, ulong born, Look look)
        {
            Member m;
            if (!standIns.TryGetValue(id, out m))
            {
                if (standIns.Count > 64) standIns.Clear();
                m = new Member { Id = id };
                standIns[id] = m;
            }
            m.Name = name ?? "";
            m.Class = cls ?? "";
            m.Born = born;
            m.Look = look;
            return m;
        }

        private static EncPerson LeaderFor(Quest q, out Enclave e, out EnclaveRequest r)
        {
            e = null;
            r = ScEncRequests.ById(ScQuests.RequestIdOf(q));
            if (r == null) return null;
            e = ScEnclaves.ById(r.EnclaveId);
            return e != null ? ScEnclavePeople.LeaderOf(e) : null;
        }

        // ---------------------------------------------------------------- bios

        private static string ProspectBio(Prospect p)
        {
            switch (p.Favour)
            {
                case FavourKind.Poi:
                    return Fmt("scQgBioPoi", string.IsNullOrEmpty(p.PoiName) ? Localization.Get("scQgSomePlace") : p.PoiName);
                case FavourKind.Supplies:
                    return Fmt("scQgBioSupplies", p.ItemCount, Localization.Get(ScNames.LocKey(p.ItemCategory)));
                default:
                    return Localization.Get("scQgBioProspect");
            }
        }

        private static string MemberBio(Member m)
        {
            string spec = m.Specialty != Specialty.None
                ? Fmt("scQgBioSpec", Localization.Get(ScNames.LocKey(m.Specialty)), Math.Max(1, m.SpecLevel))
                : Localization.Get("scQgBioNoSpec");
            if (m.Traits.Count == 0) return spec;
            var names = new List<string>();
            for (int i = 0; i < m.Traits.Count && i < 3; i++) names.Add(ScTraits.Display(m.Traits[i]));
            return spec + " " + Fmt("scQgBioTraits", string.Join(", ", names.ToArray()));
        }

        private static string EnclaveBio(Enclave e, EnclaveRequest r)
        {
            string standing = Fmt("scQgBioStanding", e.Name, ScEnclaves.StandingWord(e));
            if (r == null) return standing;
            string need;
            switch (r.Kind)
            {
                case EnclaveRequest.KindMissing: need = Fmt("scQgBioMissing", ScEncSearch.PersonName(r)); break;
                case EnclaveRequest.KindInfestation: need = Localization.Get("scQgBioInfest"); break;
                case EnclaveRequest.KindSiege: need = Localization.Get("scQgBioSiege"); break;
                default: need = r.DueDay >= 0 ? Fmt("scQgBioDeliver", r.DueDay) : Localization.Get("scQgBioDeliverAny"); break;
            }
            return standing + " " + need;
        }

        private static string Fmt(string key, params object[] args)
        {
            string f = Localization.Get(key);
            try { return string.Format(f, args); }
            catch (FormatException) { return f; }
        }

        /// <summary>The card's box holds about 120 characters at 17pt.</summary>
        private static string Clip(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            const int Max = 120;
            if (s.Length <= Max) return s;
            int cut = s.LastIndexOf(' ', Max - 1);
            return s.Substring(0, cut > 60 ? cut : Max - 1).TrimEnd(',', ';', ' ') + "…";
        }
    }
}
