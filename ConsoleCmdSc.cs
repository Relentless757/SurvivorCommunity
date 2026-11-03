using System;
using System.Collections.Generic;
using UnityEngine;
using System.Text;

namespace SurvivorCommunity
{
    /// <summary>"sc" console command: status | members | day | morale N | give res N | relink | home | dump | save.</summary>
    public class ConsoleCmdSc : ConsoleCmdAbstract
    {
        public override string[] getCommands() { return new[] { "sc", "survivorcommunity" }; }
        public override string getDescription() { return "SurvivorCommunity: status, members, day, morale <n>, give <food|meds|ammo|mats|fuel|water|luxury> <n>, facility [list] | facility level <x y z> <1-5>, drinks [on|off], xp <id|name> <cardio|wits|fighting|shooting|spec> <n>, injure <id|name>, scan, run <id|name> <res> | run <id|name> <recall|home|skip|offline>, runners, distress <id|name>, spawn [hostile] [male|female] [T1|T2|T3] [weapon] [weapon|meleeonly] [ranged-lead|melee-lead] [Set] (sc weapons lists them; melee|bow|firearm pick one of that family), raiders [r], weapons, loadout <id|name> [weapon] [weapon|meleeonly] [ranged-lead|melee-lead] | loadout near [r] | loadout sim [n] [hostile] [T1|T2|T3] [any], skills sim [T1|T2|T3] [n], voice [play <line> [id|name] | stop | rescan | gate | sayline [line] [seconds] | context [id|name] | who [id|name] | greet [id|name] | chatter [id|name] | taunt <event> [id|name] | barks | line <words>], arms <id|name> [melee|ranged], look <id|name>, paint <id|name> [body <id> | face <id> | tattoo <shape>[/ink] | none | roll], gear <id|name> <Set|Outfit|none> | gear sim [n] [hostile] [T1|T2|T3] [any] [male|female], anim <id|name>, aim [id|name], portrait <id|name|all> | portrait frame [fov dist yaw eyelift aimlift], poi [near <r> | classify <prefab> | census], outpost [list | add [row] | remove <id> | yield | pending <id> <n> | reclassify <id> | state <id> <state> | post <id> <member> | recall <member> | upgrade <id> | level <id> <1-3> | mode <id> <normal|quiet|push> | security <id> | attack <id> [raid] | demand <id> | pay <id> | refuse <id> | repair <id>], enclave [list | info <id> | people <id> | spawn <id> | despawn <id> | standing <id> <points> | hostile <id> | kill <id> [<person id>|all] | trade <id> | release <id> | requests | request <id> [offer <resource|generator|gun|missing|infestation> [res] [size] [force] | accept | complete [points] | fail | lapse | clear | fate <alive|dead>] | siege <id> [now [force] | win | lose | clear] | refill [now] | gift <id> [give <res> [size] [force]] | barter <id> [<give> [get]] | call <id> [fighter|drop [force] [now] | cancel] | raid <id> [<outpost id> [demand|now]] | move <id> [sim|now] | reveal <id|all> | hide <id|all> | sim [late] | names [n] [hostile] | reseed confirm], infest [list | info <id> | add [<x> <z>] | clear <id|all> | spread <id> | roll | reveal <id|all> | hide <id|all>], sleepers [status | scan | repair], scout <res>, barter <give> <get>, radio, restore <member>, repost <member>, relink, home, dump, save, xnpc [id|name], trust <id|name> [forgive], recruit <id|name>, noise [add <n> | clear], attention <0-100>, attack [now] [size] | attack status | attack end | attack settle [held|damaged|overrun], defence [horde], siege [held|damaged|overrun], watch [list] | watch assign <post> <member> | watch relieve <post|member> | watch end <post|member> | watch rest <member> [clear] | watch place <post|member> | watch scan, tower [info] | tower check [rot] | tower stamp [rot]"; }
        public override string getHelp() { return getDescription(); }

        private static void Output(string s) { SingletonMonoBehaviour<SdtdConsole>.Instance.Output(s); }

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            try
            {
                string sub = _params.Count > 0 ? _params[0].ToLowerInvariant() : "status";
                switch (sub)
                {
                    case "status": Status(); break;
                    case "members": MembersCmd(); break;
                    case "day": Day(); break;
                    case "morale": Morale(_params); break;
                    case "give": Give(_params); break;
                    case "facility": case "facilities": FacilityCmd(_params); break;
                    case "drinks": DrinksCmd(_params); break;
                    case "xp": Xp(_params); break;
                    case "injure": Injure(_params); break;
                    case "scan": Scan(); break;
                    case "run": Run(_params); break;
                    case "runners": RunnersCmd(); break;
                    case "distress": Distress(_params); break;
                    case "spawn": Spawn(_params); break;
                    case "recruit": RecruitCmd(_params); break;
                    case "weapons":
                        // one catalogue since M10: survivors and raiders roll their pair from the same vanilla weapons
                        Output("[SC] weapons (XnpcWeapons, survivors and raiders): " + string.Join(", ", ScLoadout.Tokens().ToArray()));
                        Output("[SC] survivors: a firearm at " + (100f * ScConfig.SurvivorFirearmChance).ToString("0") + "% of ranged weapons; raiders: the ranged weapon at "
                               + (100f * ScConfig.RaiderRangedShareFor(0)).ToString("0") + "/" + (100f * ScConfig.RaiderRangedShareFor(1)).ToString("0") + "/"
                               + (100f * ScConfig.RaiderRangedShareFor(2)).ToString("0") + "% lead with it (T1/T2/T3, the rest melee-lead; all carry both), a firearm at "
                               + (100f * ScConfig.RaiderFirearmChance).ToString("0") + "%, " + ScConfig.RaiderSpawnAmmo + " rounds / " + ScConfig.RaiderSpawnArrows + " arrows, never resupplied");
                        Output("[SC] Locked & Loaded magazines: " + ScMags.State + (ScMags.Active ? " - a gun that takes magazines starts with its tier's (T1/T2/T3: "
                               + ScMags.TierMagazines(1) + "/" + ScMags.TierMagazines(2) + "/" + ScMags.TierMagazines(3) + ") instead of loose rounds" : ""));
                        break;
                    case "raiders": RaidersCmd(_params); break;
                    case "loadout": LoadoutCmd(_params); break;
                    case "skills": SkillsCmd(_params); break;
                    case "voice": VoiceCmd(_params); break;
                    case "arms": ArmsCmd(_params); break;
                    case "look": LookCmd(_params); break;
                    case "paint": PaintCmd(_params); break;
                    case "gear": Gear(_params); break;
                    case "anim": Anim(_params); break;
                    case "aim": AimCmd(_params); break;
                    case "portrait": PortraitCmd(_params); break;
                    case "poi": PoiCmd(_params); break;
                    case "outpost": case "outposts": OutpostCmd(_params); break;
                    case "enclave": case "enclaves": EnclaveCmd(_params); break;
                    case "infest": case "infestation": case "infestations": InfestCmd(_params); break;
                    case "sleepers": case "sleeper": SleepersCmd(_params); break;
                    case "radio": RadioCmd(); break;
                    case "restore":
                    {
                        // a resident whose body is gone (or just unloaded - the console does not wait for the check)
                        Member m = _params.Count > 1 ? FindMember(_params[1]) : null;
                        if (m == null) { Output("[SC] usage: sc restore <member id|name> - respawns a resident whose entity is gone, at their post"); break; }
                        string err;
                        bool ok = ScMissing.Restore(m, ScCommunity.LocalPlayer, true, out err);
                        Output("[SC] " + (ok ? m.Name + " is back at the post as entity " + m.Id : "refused: " + Localization.Get(err ?? "scInternal")));
                        break;
                    }
                    case "barter":
                    {
                        Resource give, get;
                        if (_params.Count < 3 || !ScNames.TryResource(_params[1], out give) || !ScNames.TryResource(_params[2], out get)) { Output("[SC] sc barter <give> <get> (ignores the once-a-day wait)"); break; }
                        ScRegistry.Community.RadioNextBarterDay = 0;
                        string msg;
                        bool ok = ScBarter.Trade(give, get, ScCommunity.LocalPlayer, out msg);
                        Output("[SC] " + (ok ? "" : "refused: ") + msg);
                        break;
                    }
                    case "scout":
                    {
                        Resource r;
                        if (_params.Count < 2 || !ScNames.TryResource(_params[1], out r)) { Output("[SC] sc scout <food|meds|ammo|mats|fuel> (ignores the once-a-day wait)"); break; }
                        ScRegistry.Community.RadioNextScoutDay = 0;
                        string msg;
                        bool ok = ScScouting.Scout(r, ScCommunity.LocalPlayer, out msg);
                        Output("[SC] " + (ok ? "" : "refused: ") + msg);
                        break;
                    }
                    case "noise": NoiseCmd(_params); break;
                    case "attention": AttentionCmd(_params); break;
                    case "attack": AttackCmd(_params); break;
                    case "defence":
                    case "defense": DefenceCmd(_params); break;
                    case "siege": SiegeCmd(_params); break;
                    case "watch": WatchCmd(_params); break;
                    case "tower": TowerCmd(_params); break;
                    case "relink": Relink(); break;
                    case "repost": RepostCmd(_params); break;
                    case "xnpc": XnpcCmd(_params); break;
                    case "trust": TrustCmd(_params); break;
                    case "home": Home(); break;
                    case "dump": Dump(); break;
                    case "save": ScRegistry.SaveAtomic(); Output("[SC] saved"); break;
                    default: Output("[SC] " + getDescription()); break;
                }
            }
            catch (Exception e)
            {
                Output("[SC] error: " + e.Message);
                Log.Exception(e);
            }
        }

        private static void Status()
        {
            Community c = ScRegistry.Community;
            Output("[SC] host=" + ScCommunity.IsHost + " loaded=" + ScRegistry.Loaded + " dir=" + (ScRegistry.SaveDir ?? "<none>")
                   + " degraded=" + ScRegistry.Degraded + " lcbu=" + ScZone.LcbuAvailable);
            Output("[SC] home=" + (c.HasHome ? c.Home.ToString() : "<none>") + " owner=" + c.Owner + " day=" + ScCommunity.Today
                   + " lastday=" + c.LastDay + " morale=" + c.Morale + " lowMoraleDays=" + c.LowMoraleDays + " hungry=" + c.Hungry + " thirsty=" + c.Thirsty
                   + " beds=" + c.Beds + (ScEncAllies.ExtraBeds() > 0 ? "+" + ScEncAllies.ExtraBeds() + "(allies)" : "") + " facilities=" + c.Facilities + " crops=" + c.Crops + " water/day=" + c.HomeWater
                   + (ScActivities.DrinksActive ? " drinks=" + ScActivities.TimeLeft() : ""));
            var sb = new StringBuilder("[SC] stock:");
            for (int i = 0; i < ScNames.ResourceCount; i++) sb.Append(' ').Append(ScNames.ResourceKeys[i]).Append('=').Append(c.Stock[i]);
            Output(sb.ToString());
            Output("[SC] members=" + ScRegistry.Members.Count + " (residents " + ScRegistry.CountStatus(MemberStatus.Resident)
                   + ", following " + ScRegistry.FollowerCount + ", on run " + ScRegistry.CountStatus(MemberStatus.OnRun) + ") prospects=" + ScRegistry.Prospects.Count);
            if (c.HasHome)
            {
                int xMin, xMax, zMin, zMax;
                ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
                Output("[SC] zone bounds x " + xMin + ".." + xMax + " z " + zMin + ".." + zMax + " loaded=" + ScZone.HomeLoaded);
            }
            for (int i = 0; i < c.Ledger.Count; i++) Output("[SC] ledger: " + c.Ledger[i]);
        }

        private static void MembersCmd()
        {
            World w = ScCommunity.World;
            foreach (var m in ScRegistry.Members.Values)
            {
                EntityAlive npc = ScCommunity.FindNpc(m.Id);
                string held = ScOrders.StateWord(m);
                string live = npc == null ? (ScRunners.IsPhysical(m) && m.Offline ? "virtual at " + World.worldToBlockPos(ScRunners.Where(m)) : "unloaded") : ("at " + npc.GetBlockPosition() + " " + ScOrders.Describe(npc)
                    + (held.Length > 0 ? " [" + held + "]" : "")
                    + " leader " + (EntityUtilities.GetLeaderOrOwner(m.Id) != null ? "yes" : "no") + " spawner " + npc.GetSpawnerSource()
                    + " pkgs [" + string.Join(",", npc.AIPackages.ToArray()) + "] sc_member=" + npc.Buffs.GetCustomVar("sc_member")
                    + " persist=" + npc.Buffs.HasCustomVar("Persist"));
                Output("[SC] " + m.Id + " " + m.Name + " (" + m.Class + ") " + m.Status + " post " + m.Post + " mood " + m.Mood
                       + (m.Injured ? " INJURED" : "") + (m.Ranged ? " ranged" : " melee") + " missing " + m.MissingDays
                       + (ScRunners.IsPhysical(m) ? " run " + m.RunLeg + (m.Offline ? " offline" : " online") : "") + " - " + live);
                Output("[SC]    skills " + string.Join(" ", new[] {
                    "cardio " + m.Skill(Skill.Cardio).Level + "(" + m.Skill(Skill.Cardio).Xp + "xp)", "wits " + m.Skill(Skill.Wits).Level + "(" + m.Skill(Skill.Wits).Xp + "xp)",
                    "fighting " + m.Skill(Skill.Fighting).Level + "(" + m.Skill(Skill.Fighting).Xp + "xp)", "shooting " + m.Skill(Skill.Shooting).Level + "(" + m.Skill(Skill.Shooting).Xp + "xp)" })
                       + " spec " + m.Specialty + " " + m.SpecLevel + "(" + m.SpecXp + "xp) traits [" + string.Join(",", m.Traits.ToArray()) + "]"
                       + (npc != null ? " | melee+" + npc.Buffs.GetCustomVar("sc_dmgmelee") + " ranged+" + npc.Buffs.GetCustomVar("sc_dmgranged") + " speed+" + npc.Buffs.GetCustomVar("sc_speed") + " hp+" + npc.Buffs.GetCustomVar("sc_hp") + " maxhp " + npc.Stats.Health.Max + " buffs " + (npc.Buffs.HasBuff(ScSkills.BuffName) ? "skills " : "") + (npc.Buffs.HasBuff(ScSkills.HungryBuff) ? "hungry " : "") + (npc.Buffs.HasBuff(ScSkills.ThirstyBuff) ? "thirsty " : "") + (npc.Buffs.HasBuff(ScSkills.InjuredBuff) ? "injured " : "") + (npc.Buffs.GetCustomVar("$Timid") >= 1f ? "timid" : "") : ""));
            }
            foreach (var p in ScRegistry.Prospects.Values)
            {
                Output("[SC] prospect " + p.Id + " " + p.Name + " (" + p.Class + ") favour " + p.Favour
                       + (p.Favour == FavourKind.Poi ? " " + p.PoiName + " at " + p.PoiPos : " " + p.ItemCount + " " + p.ItemCategory)
                       + (p.Started ? " taken on" : " not taken on") + (p.Done ? " DONE" : ""));
            }
            if (ScRegistry.Members.Count == 0 && ScRegistry.Prospects.Count == 0) Output("[SC] no members or prospects");
        }

        private static void Day()
        {
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (!ScCommunity.IsHost || p == null) { Output("[SC] host only"); return; }
            Community c = ScRegistry.Community;
            c.LastDay++;
            ScTicker.SimulatedDay = c.LastDay;     // the ticker's resync back to the world's day is then ours, not a fault
            ScTicker.SimulatedFromDay = ScCommunity.World != null ? GameUtils.WorldTimeToDays(ScCommunity.World.worldTime) : -1;
            ScDaily.Run(c.LastDay, p);
            Output("[SC] simulated day " + c.LastDay + " - morale " + c.Morale);
            Status();
        }

        private static void Morale(List<string> p)
        {
            int v;
            if (p.Count >= 2 && int.TryParse(p[1], out v))
            {
                ScMorale.Override = v < 0 ? -1 : Math.Min(100, v);
                Output("[SC] morale override " + (ScMorale.Override < 0 ? "cleared" : ScMorale.Override.ToString()) + " (session only; -1 clears)");
            }
            ScMorale.Recompute();
            foreach (string line in ScMorale.Breakdown().Split('\n')) Output("[SC] " + line);
            Community c = ScRegistry.Community;
            Output("[SC] lowMoraleDays=" + c.LowMoraleDays + " threat=" + c.ThreatMemberId + " deathPenalty=" + c.DeathPenalty
                   + " enclaveLoss=" + c.EnclaveLoss + " allies=" + ScEncAllies.AlliedCount() + " allyBeds=" + ScEncAllies.ExtraBeds()   // E6
                   + " nestsNearHome=" + ScInfestations.NearHome().Count);                                                            // I3
        }

        /// <summary>sc noise [add &lt;n&gt; | clear] - the base's noise and attention (base defence D1).</summary>
        private static void NoiseCmd(List<string> p)
        {
            Community c = ScRegistry.Community;
            string verb = p.Count >= 2 ? p[1].ToLowerInvariant() : "";
            float v;
            if (verb == "add" && p.Count >= 3 && float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
            {
                c.NoiseEvents = Math.Max(0f, c.NoiseEvents + v);
                ScRegistry.SaveAtomic();
                Output("[SC] event noise " + c.NoiseEvents.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " (it fades like any event)");
            }
            else if (verb == "clear")
            {
                c.NoiseEvents = 0f;
                ScRegistry.SaveAtomic();
                Output("[SC] event noise cleared");
            }
            else if (verb.Length > 0) { Output("[SC] noise [add <n> | clear]"); return; }
            if (!c.HasHome) Output("[SC] no community home yet - nothing is counted");
            foreach (string line in ScNoise.Breakdown().Split('\n')) Output("[SC] " + line);
            World w = ScCommunity.World;
            if (c.HasHome && w != null) foreach (string line in ScBaseAttack.StatusText(w, ScCommunity.LocalPlayer).Split('\n')) Output("[SC] " + line);
        }

        /// <summary>
        /// sc attack [now] [size] - calls a horde on the home base (base defence D2) whatever the attention, the gap after the
        /// last attack and the blood moon: it arrives the usual 1-2 game hours later, or with "now" at once, with the usual
        /// size unless one is given. Within BaseAttackJoinRange of the claim it is fought live; farther off the defenders
        /// fight it alone (D4; a console call forgets this absence's earlier fight). sc attack status - the open attack and
        /// what the next call waits for. sc attack end - despawns the attackers and closes the attack (nothing won). sc attack
        /// settle [held|damaged|overrun] - ends it now as a fight without the player: settled on the rating, or as given.
        /// </summary>
        private static void AttackCmd(List<string> p)
        {
            World w = ScCommunity.World;
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            if (w == null || player == null) { Output("[SC] no world / player"); return; }
            string verb = p.Count >= 2 ? p[1].ToLowerInvariant() : "";
            if (verb == "status")
            {
                foreach (string line in ScBaseAttack.StatusText(w, player).Split('\n')) Output("[SC] " + line);
                return;
            }
            if (verb == "end")
            {
                if (!ScBaseAttack.Open) { Output("[SC] no attack is open"); return; }
                ScBaseAttack.Abort(w, "the console");
                Output("[SC] the attack is over (nothing won; attention starts again from where it is, 0 since the call)");
                return;
            }
            if (verb == "settle")
            {
                int forced = p.Count >= 3 ? ScDefence.ParseResult(p[2]) : -1;
                if (p.Count >= 3 && forced < 0) { Output("[SC] attack settle [held|damaged|overrun]"); return; }
                if (!ScBaseAttack.SettleNow(w, player, forced)) { Output("[SC] no attack is open"); return; }
                Output("[SC] settled as a fight without the player (the log line 'base attack without the player' has the roll and every death chance)");
                return;
            }
            bool now = verb == "now";
            int size = 0;
            string sizeArg = now ? (p.Count >= 3 ? p[2] : null) : (verb.Length > 0 ? verb : null);
            if (sizeArg != null && (!int.TryParse(sizeArg, out size) || size < 1))
            {
                Output("[SC] attack [now] [size] | attack status | attack end | attack settle [held|damaged|overrun]");
                return;
            }
            string why = ScBaseAttack.WhyNoCall(w, player, true);
            if (why != null) { Output("[SC] no attack: " + why); return; }
            if (ScRegistry.Community.AttackAwayUsed)
            {
                ScRegistry.Community.AttackAwayUsed = false;     // a test call may be fought without the player again
                Output("[SC] this absence's earlier fight forgotten for the console call");
            }
            ScBaseAttack.Call(w, player, size, now, "the console");
            foreach (string line in ScBaseAttack.StatusText(w, player).Split('\n')) Output("[SC] " + line);
        }

        /// <summary>
        /// sc defence [horde] - the defence rating term by term against the open attack's horde (or 'horde', or the next
        /// one's usual size), the odds of a fight without the player and the highest death risk (base defence D4).
        /// </summary>
        private static void DefenceCmd(List<string> p)
        {
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            Community c = ScRegistry.Community;
            int horde;
            if (p.Count >= 2)
            {
                if (!int.TryParse(p[1], out horde) || horde < 1) { Output("[SC] defence [horde]"); return; }
            }
            else if (ScBaseAttack.Away) horde = Math.Max(1, c.AttackSize - c.AttackAwayBase);      // what the defenders' fight is rated against
            else horde = ScBaseAttack.Open ? Math.Max(1, c.AttackSize - c.AttackKilled) : ScBaseAttack.Size(player);
            foreach (string line in ScDefence.Breakdown(horde).Split('\n')) Output("[SC] " + line);
        }

        /// <summary>
        /// sc siege [held|damaged|overrun] - the blood-moon siege now, as if a blood moon had passed with the player away
        /// (base defence D4): settled on the rating less BaseSiegePenalty, or as given.
        /// </summary>
        private static void SiegeCmd(List<string> p)
        {
            World w = ScCommunity.World;
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            if (!ScCommunity.IsHost || w == null || player == null) { Output("[SC] host only, in a world"); return; }
            if (!ScRegistry.Community.HasHome) { Output("[SC] no community home"); return; }
            int forced = p.Count >= 2 ? ScDefence.ParseResult(p[1]) : -1;
            if (p.Count >= 2 && forced < 0) { Output("[SC] siege [held|damaged|overrun]"); return; }
            ScSiege.Besiege(w, player, forced, "the console");
            Output("[SC] siege settled (the log line 'base SIEGE' has the roll and every death chance)");
        }

        /// <summary>
        /// sc watch [list] - the Watch posts, their guards and hours left, who rests, the attention cut and the call-outs
        /// (base defence D3). assign &lt;post&gt; &lt;member&gt; - a shift, with every rule the radial has (ammo, rest, room).
        /// relieve &lt;post|member&gt; - ends it early (no rest). end &lt;post|member&gt; - ends it now as if its hours had run
        /// (the rest applies). rest &lt;member&gt; [clear] - the rest left, or cleared. place &lt;post|member&gt; - the guard
        /// onto the post now, looked at or not. scan - the zone's loaded chunks against the post list now. A post is its
        /// number in the list or x,y,z. Host only.
        /// </summary>
        private static void WatchCmd(List<string> p)
        {
            World w = ScCommunity.World;
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            if (!ScCommunity.IsHost || w == null) { Output("[SC] host only, in a world"); return; }
            string verb = p.Count >= 2 ? p[1].ToLowerInvariant() : "list";
            string usage = "watch [list] | watch assign <post> <member> | watch relieve <post|member> | watch end <post|member> | watch rest <member> [clear] | watch place <post|member> | watch scan (a post: its number or x,y,z)";
            switch (verb)
            {
                case "list":
                    break;
                case "scan":
                    ScWatch.Scan(w);
                    break;
                case "assign":
                {
                    Vector3i post = p.Count >= 3 ? ScWatch.ParsePost(p[2]) : Vector3i.zero;
                    Member m = p.Count >= 4 ? FindMember(p[3]) : null;
                    if (post == Vector3i.zero || m == null) { Output("[SC] " + usage); return; }
                    string err;
                    bool ok = ScWatch.Assign(post, m, player, out err);
                    Output("[SC] " + (ok ? m.Name + " is on watch at " + ScWatch.Label(post) : "refused: " + ScWatch.ErrorText(err, m, post)));
                    break;
                }
                case "relieve":
                case "end":
                case "place":
                {
                    Member m = p.Count >= 3 ? WatchMember(p[2]) : null;
                    if (m == null) { Output("[SC] nobody on watch there - " + usage); return; }
                    bool ok = verb == "relieve" ? ScWatch.Relieve(m, player, "the console")
                            : verb == "end" ? ScWatch.EndShiftNow(m, player)
                            : ScWatch.PlaceNow(m);
                    Output("[SC] " + m.Name + ": " + (ok ? verb + " done" : verb + " failed (not loaded, or the post's chunk is not)"));
                    break;
                }
                case "rest":
                {
                    Member m = p.Count >= 3 ? FindMember(p[2]) : null;
                    if (m == null) { Output("[SC] " + usage); return; }
                    if (p.Count >= 4 && p[3].Equals("clear", StringComparison.OrdinalIgnoreCase)) { m.WatchRestUntil = 0UL; ScRegistry.SaveAtomic(); Output("[SC] " + m.Name + " may take a watch at once"); return; }
                    Output("[SC] " + m.Name + (m.WatchRestUntil > w.worldTime ? " rests until " + ScBaseAttack.Clock(m.WatchRestUntil) : " is not resting"));
                    return;
                }
                default:
                    Output("[SC] " + usage);
                    return;
            }
            foreach (string line in ScWatch.ListText(w).Split('\n')) Output("[SC] " + line);
        }

        /// <summary>
        /// sc tower [info] - the Guard Tower prefab: size, anchor, Watch post, blocks. check [rot] - would it fit with the
        /// kit on the block you look at (as the kit would land there; rot 0-3, default: as placed from where you stand).
        /// stamp [rot] - the same, then built at once (no kit used). Host only for stamp.
        /// </summary>
        private static void TowerCmd(List<string> p)
        {
            string verb = p.Count >= 2 ? p[1].ToLowerInvariant() : "info";
            string usage = "tower [info] | tower check [rot] | tower stamp [rot] (rot 0-3; default: as the kit would face, placed from where you stand)";
            if (verb == "info")
            {
                foreach (string line in ScTower.InfoText().Split('\n')) Output("[SC] " + line);
                return;
            }
            if (verb != "check" && verb != "stamp") { Output("[SC] " + usage); return; }
            World w = ScCommunity.World;
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            if (w == null || player == null) { Output("[SC] in a world, as the local player"); return; }
            if (verb == "stamp" && !ScCommunity.IsHost) { Output("[SC] host only"); return; }
            WorldRayHitInfo hit = player.HitInfo;
            if (hit == null || !hit.bHitValid || !GameUtils.IsBlockOrTerrain(hit.tag)) { Output("[SC] look at the ground where the tower should stand"); return; }
            Vector3i kit = hit.lastBlockPos;
            int rot = ScTower.PlacerRotation(player.position, kit);
            if (p.Count >= 3 && (!int.TryParse(p[2], out rot) || rot < 0 || rot > 3)) { Output("[SC] " + usage); return; }
            var plan = new ScTower.Plan();
            string arg;
            string key = ScTower.Check(w, kit, rot, GameManager.Instance.persistentLocalPlayer, ScTower.Owner(player.entityId), plan, out arg);
            if (key != null) { Output("[SC] kit at " + ScTower.P(kit) + " rotation " + rot + ": refused - " + ScTower.Text(key, arg)); return; }
            Output("[SC] kit at " + ScTower.P(kit) + " rotation " + rot + " (tower turned " + plan.Turns * 90 + " deg): fits - " + ScTower.Counts(plan));
            if (verb != "stamp") return;
            ScTower.Stamp(w, plan);
            Output("[SC] built (" + plan.Changes.Count + " block change(s))");
        }

        /// <summary>The guard of a post (its number or x,y,z), or a member on watch by id or name.</summary>
        private static Member WatchMember(string key)
        {
            Vector3i post = ScWatch.ParsePost(key);
            Member g = post != Vector3i.zero ? ScWatch.GuardOf(post) : null;
            if (g != null) return g;
            Member m = FindMember(key);
            return ScWatch.OnWatch(m) ? m : null;
        }

        /// <summary>sc attention &lt;0-100&gt; - sets the dead's attention (at 100 a horde is called once the player is home).</summary>
        private static void AttentionCmd(List<string> p)
        {
            float v;
            if (p.Count < 2 || !float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v))
            {
                Output("[SC] attention <0-100>");
                return;
            }
            ScRegistry.Community.Attention = Math.Max(0f, Math.Min(100f, v));
            ScRegistry.SaveAtomic();
            Output("[SC] attention " + ScRegistry.Community.Attention.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%");
        }

        private static void Give(List<string> p)
        {
            Resource r; int n;
            if (p.Count < 3 || !ScNames.TryResource(p[1], out r) || !int.TryParse(p[2], out n)) { Output("[SC] give <food|meds|ammo|mats|fuel|water|luxury> <n> (negative takes)"); return; }
            ScRegistry.Community.Add(r, n);
            ScRegistry.SaveAtomic();
            Output("[SC] " + r + " = " + ScRegistry.Community.Get(r));
        }

        /// <summary>
        /// sc facility [list]: every well and dew collector in the loaded chunks of the home zone, with its level and water a
        /// day, and the scan's total; sc facility level x y z n: set one's level (a well becomes that model, free).
        /// </summary>
        private static void FacilityCmd(List<string> p)
        {
            World w = ScCommunity.World;
            Community c = ScRegistry.Community;
            if (w == null || !ScRegistry.Loaded) { Output("[SC] no world"); return; }
            ScFacilities.EnsureTable();
            string sub = p.Count > 1 ? p[1].ToLowerInvariant() : "list";
            if (sub == "level")
            {
                int x, y, z, lv;
                if (p.Count < 6 || !int.TryParse(p[2], out x) || !int.TryParse(p[3], out y) || !int.TryParse(p[4], out z) || !int.TryParse(p[5], out lv))
                { Output("[SC] usage: sc facility level <x> <y> <z> <level> (the parent block's position, as sc facility lists it)"); return; }
                var at = new Vector3i(x, y, z);
                BlockValue bv = w.GetBlock(at);
                if (!ScFacilities.IsFacility(bv.type) || bv.ischild) { Output("[SC] no well or dew collector parent block at " + at); return; }
                lv = Math.Max(1, Math.Min(lv, ScFacilities.MaxLevel(bv.type)));
                if (ScFacilities.WellLevel(bv.type) > 0)
                {
                    Block b = Block.GetBlockByName(ScConfig.WellBlocks[lv - 1], true);
                    if (b == null) { Output("[SC] " + ScConfig.WellBlocks[lv - 1] + " is not a block"); return; }
                    BlockValue nv = b.ToBlockValue();
                    nv.rotation = bv.rotation;
                    w.SetBlockRPC(at, nv);
                }
                else if (lv > 1) c.DewLevels[at] = lv;
                else c.DewLevels.Remove(at);
                ScRegistry.SaveAtomic();
                Output("[SC] " + ScFacilities.NameOf(bv) + " at " + at + " set to level " + lv + " (the scan picks it up within seconds; sc scan to count now)");
                return;
            }
            Output("[SC] facilities: " + ScFacilities.WellTypes + " well level(s), " + ScFacilities.DewTypes + " dew collector block(s); upgrades "
                   + string.Join("/", Array.ConvertAll(ScConfig.FacilityUpgradeCost, v => v.ToString())) + " materials; water a day "
                   + ScConfig.WellWaterPerLevel + " per well level, " + ScConfig.DewWaterPerLevel + " per dew level; the zone's last count " + c.HomeWater + "/day");
            if (!c.HasHome) { Output("[SC] no home"); return; }
            int xMin, xMax, zMin, zMax, shown = 0;
            ScZone.Bounds(out xMin, out xMax, out zMin, out zMax);
            for (int cx = World.toChunkXZ(xMin); cx <= World.toChunkXZ(xMax); cx++)
            {
                for (int cz = World.toChunkXZ(zMin); cz <= World.toChunkXZ(zMax); cz++)
                {
                    Chunk ch = w.GetChunkSync(cx, cz) as Chunk;
                    if (ch == null || ch.blockEntityStubs == null) continue;
                    foreach (BlockEntityData d in ch.blockEntityStubs.list)
                    {
                        if (d == null || d.blockValue.ischild || !ScFacilities.IsFacility(d.blockValue.type)) continue;
                        bool inZone = ScZone.Contains(d.pos);
                        Output("[SC]   " + ScFacilities.NameOf(d.blockValue) + " at " + d.pos.x + " " + d.pos.y + " " + d.pos.z + ": level "
                               + ScFacilities.LevelAt(d.pos, d.blockValue) + "/" + ScFacilities.MaxLevel(d.blockValue.type)
                               + (inZone ? ", " + ScFacilities.WaterAt(d.pos, d.blockValue) + " water a day" : " - outside the zone, nothing"));
                        shown++;
                    }
                }
            }
            if (shown == 0) Output("[SC]   none in the loaded chunks of the home zone");
            if (c.DewLevels.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in c.DewLevels) parts.Add(kv.Key + ":" + kv.Value);
                Output("[SC]   upgraded dew collectors on record: " + string.Join(", ", parts.ToArray()));
            }
        }

        /// <summary>sc drinks: the round's state; on = pour one free (and mark today), off = end it and allow another today.</summary>
        private static void DrinksCmd(List<string> p)
        {
            if (!ScRegistry.Loaded) { Output("[SC] no registry"); return; }
            string sub = p.Count > 1 ? p[1].ToLowerInvariant() : "";
            if (sub == "on" || sub == "off") ScActivities.ConsoleSet(sub == "on");
            Community c = ScRegistry.Community;
            string why = ScActivities.WhyNotDrinks();
            Output("[SC] drinks: " + (ScActivities.DrinksActive ? "running, " + ScActivities.TimeLeft() + " left (+" + ScConfig.DrinksMorale + " morale)" : "not running")
                   + ", last poured day " + c.DrinksDay + " (today " + ScCommunity.Today + "); cost " + ScConfig.DrinksLuxury + " luxury + " + ScConfig.DrinksFood
                   + " food (stock " + c.Get(Resource.Luxury) + " / " + c.Get(Resource.Food) + "); " + (why == null ? "can pour now" : "cannot: " + why)
                   + "; morale " + c.Morale);
        }

        /// <summary>
        /// sc spawn [hostile] [male|female] [T1|T2|T3] [weapon] [weapon|meleeonly] [ranged-lead|melee-lead] [Set] - an NPC on
        /// 0-XNPCCore's player body, in front of the player: one of the community's survivors, or with "hostile" (or
        /// "raider") one of SC's raiders (M10). The class is the one of its sex at the given band tier, else at the
        /// gamestage tier. Each weapon given fixes that kind of its pair (an item, its short name, or melee / bow / firearm
        /// for one of that family); the kind not given is rolled at the class's tier cap. A raider's lead is rolled
        /// (RaiderRangedShare) unless ranged-lead / melee-lead fixes it. Saved with the world (Persist, StaticSpawner).
        /// </summary>
        private static void Spawn(List<string> p)
        {
            World w = ScCommunity.World;
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            if (w == null || player == null) { Output("[SC] no world / player"); return; }
            bool hostile = false;
            for (int i = 1; i < p.Count; i++)
            {
                string a = p[i].ToLowerInvariant();
                if (a == "hostile" || a == "raider") hostile = true;
            }
            SpawnXnpc(p, w, player, hostile);
        }

        /// <summary>A raider's lead from a console word: Loadout.LeadRanged for "ranged-lead", LeadMelee for "melee-lead", else LeadNone.</summary>
        private static int LeadWord(string a)
        {
            a = a.ToLowerInvariant().Replace("_", "-");
            if (a == "ranged-lead" || a == "rangedlead") return Loadout.LeadRanged;
            if (a == "melee-lead" || a == "meleelead") return Loadout.LeadMelee;
            return Loadout.LeadNone;
        }

        /// <summary>
        /// The spawn itself (see Spawn): the class of the side, sex and tier, a rolled look (a raider's clothes from
        /// RaiderGearSets), and the fixed weapons handed to ScLoadout as Pending; ScLoadout rolls the rest and gives the
        /// gun its starting ammo.
        /// </summary>
        private static void SpawnXnpc(List<string> p, World w, EntityPlayerLocal player, bool hostile)
        {
            GameRandom rnd = w.GetGameRandom();
            string sex = null, set = null;
            int tier = -1;
            var fix = new Loadout();
            for (int i = 1; i < p.Count; i++)
            {
                string a = p[i].ToLowerInvariant();
                if (a == "male" || a == "female") { sex = a; continue; }
                if (a == "hostile" || a == "raider") continue;
                if (a.Length == 2 && a[0] == 't' && a[1] >= '1' && a[1] <= '3') { tier = a[1] - '1'; continue; }
                if (a == "meleeonly" || a == "melee-only")
                {
                    if (fix.Ranged != null) { Output("[SC] " + fix.Ranged + " is a ranged weapon - a melee-only NPC carries none"); return; }
                    fix.Ranged = Loadout.NoRanged;
                    continue;
                }
                int lead = LeadWord(a);
                if (lead != Loadout.LeadNone)
                {
                    if (!hostile) { Output("[SC] only a raider has a lead weapon (sc spawn hostile ... " + a + ")"); return; }
                    fix.Lead = lead;
                    continue;
                }
                string why;
                string tok = IsArmorSet(p[i]) && !ScLoadout.Knows(p[i]) ? null : ScLoadout.MatchToken(p[i], rnd, out why);
                if (tok == null) { set = p[i]; continue; }                // "Primitive" is a set, not the primitive bow
                bool ranged = ScLoadout.IsRangedToken(tok);
                if (ranged && fix.Ranged == Loadout.NoRanged) { Output("[SC] " + tok + " is a ranged weapon - a melee-only NPC carries none"); return; }
                if (fix.Of(ranged) != null) { Output("[SC] " + tok + " and " + fix.Of(ranged) + " are both " + (ranged ? "ranged" : "melee") + " - an NPC carries one of each"); return; }
                fix.Set(ranged, tok);
            }
            bool male = sex != null ? sex == "male" : rnd.RandomFloat < 0.5f;
            int band = tier >= 0 ? tier : ScLooks.MaxTierFor(player.gameStage);
            string cls = hostile ? ScXnpc.RaiderClassFor(male, band) : ScXnpc.ClassFor(male, band);
            if (cls == null)
            {
                Output(hostile ? "[SC] no raider class of that sex is loaded (XnpcRaiderClasses in Config/blocks.xml, Config/entityclasses.xml)"
                               : "[SC] no 0-XNPCCore survivor class of that sex is loaded (XnpcSurvivorClasses in Config/blocks.xml)");
                return;
            }
            Vector3 fwd = player.GetForwardVector(); fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            // a raider a little further out: it is hostile from its first frame
            Vector3 at = player.position + fwd * (hostile ? 6f : 3f);
            Vector3 pos = ScRadio.SpotNear(w, new Vector3i(Mathf.RoundToInt(at.x), Mathf.RoundToInt(at.y), Mathf.RoundToInt(at.z)), 3);
            int cap = ScLoadout.TierCap(cls);
            Look look = ScLooks.Roll(male, rnd, true, hostile, cap);   // clothes at the weapons' tier cap
            if (set != null && !ScLooks.WearSet(look, set, rnd, cap)) { Output("[SC] no weapon, armor set or outfit '" + set + "' for a " + (male ? "man" : "woman") + " (sc weapons lists the weapons, sc gear the sets and outfits)"); return; }
            Loadout pending = fix.Ranged != null || fix.Melee != null || fix.Lead != Loadout.LeadNone ? fix : null;
            EntityAlive npc = ScSpawn.Create(EntityClass.FromString(cls), pos, Vector3.zero, look, pending);
            if (npc == null) { Output("[SC] " + cls + " did not spawn as an SCore NPC"); return; }
            w.SpawnEntityInWorld(npc);
            npc.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
            npc.Buffs.SetCustomVar("Persist", 1f);
            Loadout pair = ScLoadout.Current(npc);
            Output("[SC] spawned " + (hostile ? "raider " : "") + cls + " (" + (pair != null ? pair.ToString() : "no pair") + ", tier " + ScLoadout.TierCap(cls) + ") id " + npc.entityId
                   + " '" + ScCommunity.DisplayName(npc) + "': " + ScLooks.Describe(look));
        }

        private static bool IsArmorSet(string name)
        {
            for (int slot = 0; slot < 4; slot++) if (ScLooks.SetItem(name, slot).Length > 0) return true;
            return ScLooks.FindOutfit(name) != null;
        }

        /// <summary>A loaded player-body survivor or raider by member id/name, entity id, or display-name substring (within 200 m).</summary>
        private static EntityAlive FindBodyNpc(string key)
        {
            int id;
            if (int.TryParse(key, out id) && ScCommunity.World != null) return ScCommunity.World.GetEntity(id) as EntityAlive;
            // SC's raiders are EntityAliveSDXV4 since M10 (no longer EntityNPCBandit), so FindSurvivor's scan of SCore's
            // EntityTrader-based classes finds them by name too
            return FindSurvivor(key);
        }

        /// <summary>
        /// sc voice - the dialog line and voice switches, the player in use and the voices found. sc voice play &lt;line&gt;
        /// [id|name] - a survivor (that one, or the nearest with a voice within 20 m) says a line now; the line is a clip's
        /// name (scFavourPoi, SdcsNPC.start). sc voice stop. sc voice rescan - read Resources/Voices again, so new or replaced
        /// clips play without a restart. V1: sc voice gate - whom the voice gate holds Voice Control on, the answer pass,
        /// and how many of Voice Control's lines it kept quiet this session (also the second line of plain sc voice); sc voice
        /// sayline [line] [seconds] - the nearest survivor (20 m) is given 0-XNPCCore's say-line (ammo.dry, 5 s by default)
        /// through Voice Control after that delay, to watch the gate with a talk open. E8: sc voice context [id|name] - the
        /// paragraph Voice Control's chat is given for one of an enclave's people (that one, or the nearest within 20 m).
        /// V2: sc voice who [id|name] - race, sex, where they came from, the voice chain (their voice, the other of their race
        /// and sex, the borrowed ones) and which greeting / chatter / taunt families and XNPCCore lines they can say (the
        /// nearest survivor within 20 m or raider within 40 m); sc voice greet [id|name] - the nearest survivor greets you now;
        /// sc voice chatter [id|name] - a resident at home says a chatter line now; sc voice taunt &lt;event&gt; [id|name] - the
        /// nearest raider (40 m) taunts now (spot, search, lost, hit, killed, idle, dying, retreat), pacing aside; sc voice
        /// barks - the V2 switches and this session's counters; sc voice line &lt;words&gt; - the key Voice Control's words map to
        /// and whether the nearest survivor has its clip.
        /// </summary>
        private static void VoiceCmd(List<string> p)
        {
            string a = p.Count > 1 ? p[1].ToLowerInvariant() : "";
            if (a == "") { Output("[SC] voice: " + ScVoice.Status()); Output("[SC] voice gate: " + ScVoice.GateStatus()); return; }
            if (VoiceV2Cmd(a, p)) return;
            if (a == "gate") { Output("[SC] voice gate: " + ScVoice.GateStatus()); return; }
            if (a == "sayline")
            {
                // V1 test aid: the nearest survivor is given one of 0-XNPCCore's say-lines through Voice Control after a delay
                string line = p.Count > 2 ? p[2] : "ammo.dry";
                float secs = 5f;
                if (p.Count > 3 && !float.TryParse(p[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out secs)) secs = 5f;
                secs = Mathf.Clamp(secs, 0f, 60f);
                EntityPlayerLocal lp = ScCommunity.LocalPlayer;
                EntityAlive near = lp != null ? ScVoice.Nearest(lp.position, 20f) : null;
                if (near == null) { Output("[SC] voice: no survivor with a voice within 20 m"); return; }
                Output("[SC] voice: " + ScVoice.SayLineLater(near, line, secs));
                return;
            }
            if (a == "context")
            {
                // E8: what Voice Control's chat is told about one of an enclave's people (that one, or the nearest within 20 m)
                EntityAlive npc = p.Count > 2 ? FindBodyNpc(p[2]) : NearestEnclaveBody(20f);
                if (npc == null) { Output("[SC] voice context: " + (p.Count > 2 ? "no NPC matches " + p[2] : "none of an enclave's people within 20 m")); return; }
                string what;
                string text = ScVoiceContext.For(npc, out what);
                string who = ScCommunity.DisplayName(npc) + " (" + npc.entityId + ")";
                if (text == null) { Output("[SC] voice context: " + who + " is not one of an enclave's people - Voice Control's chat gets nothing from SC"); return; }
                Output("[SC] voice context: " + who + ", " + what + ", " + text.Length + " characters" + (ScConfig.VoiceEnclaveContext ? "" : " (VoiceEnclaveContext is off: not sent)") + ":");
                Output(text);
                return;
            }
            if (a == "stop") { ScVoice.Stop(); Output("[SC] voice: stopped"); return; }
            if (a == "rescan") { Output("[SC] voice: " + ScVoice.Rescan()); return; }
            if (a == "play" && p.Count > 2)
            {
                EntityAlive npc = null;
                if (p.Count > 3) npc = FindBodyNpc(p[3]);
                else
                {
                    EntityPlayerLocal player = ScCommunity.LocalPlayer;
                    if (player != null) npc = ScVoice.Nearest(player.position, 20f);
                }
                if (npc == null) { Output("[SC] voice: no survivor with a voice " + (p.Count > 3 ? "matches " + p[3] : "within 20 m")); return; }
                Output("[SC] voice: " + ScVoice.PlayLine(npc, p[2]));
                return;
            }
            Output("[SC] voice [play <line> [id|name] | stop | rescan | gate | sayline [line] [seconds] | context [id|name] | who [id|name] | greet [id|name] | chatter [id|name] | taunt <event> [id|name] | barks | line <words>]");
        }

        /// <summary>V2's sc voice subcommands (who, greet, chatter, taunt, barks, line); false when a is none of them.</summary>
        private static bool VoiceV2Cmd(string a, List<string> p)
        {
            EntityPlayerLocal lp = ScCommunity.LocalPlayer;
            switch (a)
            {
                case "who":
                {
                    EntityAlive npc = p.Count > 2 ? FindBodyNpc(p[2]) : null;
                    if (npc == null && p.Count <= 2 && lp != null)
                    {
                        EntityAlive s = ScVoice.Nearest(lp.position, 20f), r = ScTaunts.NearestRaider(lp.position, 40f);
                        npc = s == null ? r : r == null ? s : (s.position - lp.position).sqrMagnitude <= (r.position - lp.position).sqrMagnitude ? s : r;
                    }
                    if (npc == null) { Output("[SC] voice who: " + (p.Count > 2 ? "no NPC matches " + p[2] : "no survivor within 20 m and no raider within 40 m")); return true; }
                    foreach (string line in ScVoice.Who(npc)) Output("[SC] voice who: " + line);
                    return true;
                }
                case "greet":
                {
                    EntityAlive npc = p.Count > 2 ? FindBodyNpc(p[2]) : lp != null ? ScVoice.Nearest(lp.position, 20f) : null;
                    if (npc == null) { Output("[SC] voice greet: " + (p.Count > 2 ? "no NPC matches " + p[2] : "no survivor with a voice within 20 m")); return true; }
                    Output("[SC] voice greet: " + ScGreetings.GreetNow(npc, lp));
                    return true;
                }
                case "chatter":
                {
                    EntityAlive npc = p.Count > 2 ? FindBodyNpc(p[2]) : null;
                    if (p.Count > 2 && npc == null) { Output("[SC] voice chatter: no NPC matches " + p[2]); return true; }
                    Output("[SC] voice chatter: " + ScGreetings.ChatterNow(npc));
                    return true;
                }
                case "taunt":
                {
                    string ev = p.Count > 2 ? p[2].ToLowerInvariant() : "";
                    if (Array.IndexOf(ScTaunts.Events, ev) < 0) { Output("[SC] voice taunt <" + string.Join("|", ScTaunts.Events) + "> [id|name]"); return true; }
                    EntityAlive raider = p.Count > 3 ? FindBodyNpc(p[3]) : lp != null ? ScTaunts.NearestRaider(lp.position, 40f) : null;
                    if (raider == null) { Output("[SC] voice taunt: " + (p.Count > 3 ? "no NPC matches " + p[3] : "no raider within 40 m (sc spawn hostile)")); return true; }
                    Output("[SC] voice taunt: " + ScTaunts.TauntNow(raider, ev));
                    return true;
                }
                case "barks":
                    Output("[SC] voice barks: VoiceByRace=" + ScConfig.VoiceByRace + " ScBarks=" + ScConfig.ScBarks + " VoiceBarkSubtitles=" + ScConfig.VoiceBarkSubtitles
                           + " VoiceBarkDebug=" + ScConfig.VoiceBarkDebug + "; " + ScGreetings.Status() + "; " + ScTaunts.Status());
                    Output("[SC] voice barks: said / matched this session: " + ScBarks.Counters());
                    Output("[SC] voice barks: " + ScClipCache.Describe());
                    return true;
                case "line":
                {
                    if (p.Count < 3) { Output("[SC] voice line <Voice Control's words>"); return true; }
                    string words = string.Join(" ", p.GetRange(2, p.Count - 2).ToArray());
                    EntityAlive npc = lp != null ? ScVoice.Nearest(lp.position, 20f) : null;
                    Output("[SC] voice line: " + ScBarks.Match(npc, words));
                    return true;
                }
            }
            return false;
        }

        /// <summary>E8: the nearest living body of an enclave's people within r metres of the local player, or null.</summary>
        private static EntityAlive NearestEnclaveBody(float r)
        {
            World w = ScCommunity.World;
            EntityPlayerLocal lp = ScCommunity.LocalPlayer;
            if (w == null || lp == null) return null;
            EntityAlive best = null;
            float bestSq = r * r;
            List<Entity> list = w.Entities.list;
            for (int i = 0; i < list.Count; i++)
            {
                EntityAlive e = list[i] as EntityAlive;
                if (e == null || e.IsDead() || ScEnclavePeople.PersonOf(e) == null) continue;
                float sq = (e.position - lp.position).sqrMagnitude;
                if (sq < bestSq) { best = e; bestSq = sq; }
            }
            return best;
        }

        /// <summary>sc skills sim [T1|T2|T3] [n] - n new survivors' starting skills per class tier (all three without a tier).</summary>
        private static void SkillsCmd(List<string> p)
        {
            if (p.Count < 2 || !p[1].Equals("sim", StringComparison.OrdinalIgnoreCase)) { Output("[SC] skills sim [T1|T2|T3] [n]"); return; }
            int n = 1000, tier = -1;
            for (int i = 2; i < p.Count; i++)
            {
                int v;
                string k = p[i].ToLowerInvariant();
                if (k.Length == 2 && k[0] == 't' && k[1] >= '1' && k[1] <= '3') tier = k[1] - '1';
                else if (int.TryParse(k, out v)) n = v;
            }
            n = Math.Max(1, Math.Min(100000, n));
            for (int t = 0; t < 3; t++) if (tier < 0 || tier == t) Output(ScSkills.SimulateStart(t, n));
        }

        /// <summary>
        /// sc loadout <id|name> [weapon] [weapon] [ranged-lead|melee-lead] - an NPC's two weapons, or give it new ones (one
        /// ranged, one melee; a single weapon replaces the one of its kind; a raider's lead may be changed, which puts that
        /// weapon in its hand). sc loadout near [r] - the pairs around the player, with the firearm share. sc loadout sim
        /// [n] [hostile] [T1|T2|T3] [any] - n rolls without making anyone (a class tier; any = ignore the gamestage cap).
        /// </summary>
        private static void LoadoutCmd(List<string> p)
        {
            if (p.Count < 2) { Output("[SC] loadout <id|name> [weapon] [weapon|meleeonly] [ranged-lead|melee-lead] | loadout near [r] | loadout sim [n] [hostile] [T1|T2|T3] [any]"); return; }
            string a = p[1].ToLowerInvariant();
            if (a == "sim")
            {
                int n = 1000, classTier = -1;
                bool hostile = false, anyStage = false;
                for (int i = 2; i < p.Count; i++)
                {
                    int v;
                    string k = p[i].ToLowerInvariant();
                    if (k == "hostile" || k == "raider") hostile = true;
                    else if (k == "any") anyStage = true;
                    else if (k.Length == 2 && k[0] == 't' && k[1] >= '1' && k[1] <= '3') classTier = k[1] - '1';
                    else if (int.TryParse(k, out v)) n = v;
                }
                Output(ScLoadout.Simulate(Math.Max(1, Math.Min(100000, n)), hostile, classTier, anyStage));
                return;
            }
            if (a == "near")
            {
                float r = 40f;
                if (p.Count >= 3) float.TryParse(p[2], out r);
                LoadoutNear(Mathf.Clamp(r, 2f, 400f));
                return;
            }
            EntityAlive e = FindBodyNpc(p[1]);
            if (e == null) { Output("[SC] no loaded NPC '" + p[1] + "'"); return; }
            if (p.Count >= 3)
            {
                // one catalogue for both sides since M10; only a raider has a lead
                bool raider = ScXnpc.IsRaiderClass(ScCommunity.ClassName(e));
                var change = new Loadout();
                for (int i = 2; i < p.Count; i++)
                {
                    int lead = LeadWord(p[i]);
                    if (lead != Loadout.LeadNone)
                    {
                        if (!raider) { Output("[SC] only a raider has a lead weapon"); return; }
                        change.Lead = lead;
                        continue;
                    }
                    string k = p[i].ToLowerInvariant();
                    if (k == "meleeonly" || k == "melee-only") { change.Ranged = Loadout.NoRanged; continue; }
                    string bad;
                    string tok = ScLoadout.MatchToken(p[i], null, out bad);
                    if (tok == null) { Output("[SC] " + bad); return; }
                    change.Set(ScLoadout.IsRangedToken(tok), tok);
                }
                string why;
                if (!ScLoadout.Set(e, change, out why)) { Output("[SC] " + why); return; }
                if (why != null) Output("[SC] pair set, but re-arming failed: " + why);
            }
            Output(ScLoadout.Describe(e));
        }

        private static void LoadoutNear(float r)
        {
            World w = ScCommunity.World;
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            if (w == null || player == null) { Output("[SC] no world / player"); return; }
            var list = new List<Entity>();
            w.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(player.position, new Vector3(2f * r, 2f * r, 2f * r)), list);
            int n = 0, fire = 0, none = 0;
            foreach (Entity en in list)
            {
                EntityAlive e = en as EntityAlive;
                if (e == null || e is EntityPlayer || e.IsDead() || (e.position - player.position).magnitude > r) continue;
                string cls = ScCommunity.ClassName(e);
                if (!ScLoadout.IsBodyClass(cls)) continue;
                Loadout l = ScLoadout.Current(e);
                n++;
                if (l == null) none++;
                else if (ScLoadout.IsFirearm(l.Ranged)) fire++;
                ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
                Output("[SC] " + e.entityId + " " + ScCommunity.DisplayName(e) + " (" + cls + ") " + (l != null ? l.ToString() : "no pair")
                       + " - holding " + (held != null && !held.IsEmpty() && held.ItemClass != null ? held.ItemClass.GetItemName() : "nothing"));
            }
            Output("[SC] " + n + " survivor(s)/raider(s) carrying a pair within " + r + " m, " + fire + " with a firearm ("
                   + (n - none > 0 ? (100.0 * fire / (n - none)).ToString("0.0") : "0") + "%; configured, of those with a ranged weapon: survivors "
                   + (100f * ScConfig.SurvivorFirearmChance).ToString("0.0") + "%, raiders " + (100f * ScConfig.RaiderFirearmChance).ToString("0.0") + "%)"
                   + (none > 0 ? ", " + none + " without a pair" : ""));
        }

        /// <summary>sc arms <id|name> [melee|ranged] - put one of the NPC's two weapons in its hand (debug; W4 automates it).</summary>
        private static void ArmsCmd(List<string> p)
        {
            if (p.Count < 2) { Output("[SC] arms <id|name> [melee|ranged]"); return; }
            EntityAlive e = FindBodyNpc(p[1]);
            if (e == null) { Output("[SC] no loaded NPC '" + p[1] + "'"); return; }
            if (p.Count >= 3)
            {
                string k = p[2].ToLowerInvariant();
                if (k != "melee" && k != "ranged") { Output("[SC] arms <id|name> [melee|ranged]"); return; }
                string why;
                if (!ScLoadout.Arm(e, k == "ranged", out why)) { Output("[SC] " + why); return; }
            }
            Output(ScLoadout.Describe(e));
        }

        /// <summary>A loaded NPC by member id/name, entity id, or display-name substring (any survivor within 200 m).</summary>
        /// <summary>
        /// Test helper: joins a survivor without the favour (the same ScCommunity.Join the dialog ends in, with every
        /// other check intact - a home, room on the roster, hireable, not someone else's hire).
        /// </summary>
        private static void RecruitCmd(List<string> p)
        {
            EntityAlive npc = p.Count > 1 ? FindSurvivor(p[1]) : null;
            EntityPlayerLocal lp = ScCommunity.LocalPlayer;
            if (npc == null || lp == null) { Output("[SC] usage: sc recruit <id|name> - joins a loaded survivor without the favour (testing)"); return; }
            string error;
            bool ok = ScCommunity.Join(npc, lp, out error);
            Output("[SC] " + (ok ? ScCommunity.DisplayName(npc) + " joined the community (entity " + npc.entityId + ")"
                                 : "refused: " + Localization.Get(error ?? "scInternal")));
        }

        private static EntityAlive FindSurvivor(string key)
        {
            Member m = FindMember(key);
            if (m != null) { EntityAlive e = ScCommunity.FindNpc(m.Id); if (e != null) return e; }
            World w = ScCommunity.World;
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            if (w == null || player == null) return null;
            int id;
            if (int.TryParse(key, out id)) return ScNpc.As(w.GetEntity(id));
            var list = new List<Entity>();
            // both framework classes derive from EntityTrader; the filter below keeps only them (vanilla traders are not survivors)
            w.GetEntitiesInBounds(typeof(EntityTrader), new Bounds(player.position, new Vector3(400f, 400f, 400f)), list);
            for (int i = 0; i < list.Count; i++)
            {
                EntityAlive s = ScNpc.As(list[i]);
                if (s != null && !s.IsDead() && ScCommunity.DisplayName(s).IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return s;
            }
            return null;
        }

        /// <summary>sc look <id|name> - a new face (same sex, same clothes) for a loaded survivor wearing the player's body.</summary>
        private static void LookCmd(List<string> p)
        {
            if (p.Count < 2) { Output("[SC] look <id|name>"); return; }
            EntityAlive npc = FindSurvivor(p[1]);
            if (npc == null) { Output("[SC] no loaded NPC '" + p[1] + "'"); return; }
            if (!ScLooks.IsSurvivor(npc)) { Output("[SC] " + ScCommunity.DisplayName(npc) + " does not wear the player's body"); return; }
            Look old = ScLooks.Get(npc);
            Look look = ScLooks.Roll(old != null ? old.Male : npc.IsMale, ScCommunity.World.GetGameRandom(), false,
                                     ScXnpc.IsRaiderClass(ScCommunity.ClassName(npc)));   // a raider's paints from the raider lists
            if (old != null) for (int i = 0; i < 4; i++) { look.Gear[i] = old.Gear[i]; look.Quality[i] = old.Quality[i]; }
            Member m = ScRegistry.GetMember(npc.entityId);
            if (m != null) m.Look = look;
            Prospect pr = ScRegistry.GetProspect(npc.entityId);
            if (pr != null) pr.Look = look;
            ScLooks.Apply(npc, look);
            ScRegistry.SaveAtomic();
            Output("[SC] " + ScCommunity.DisplayName(npc) + ": " + ScLooks.Describe(look));
        }

        /// <summary>
        /// sc paint <id|name> [body <id> | face <id> | tattoo <shape>[/ink] | none | roll] - a loaded survivor's Vanity
        /// Vault paints (ScVanity): with the name alone, theirs and what the lists offer; otherwise set (a body paint brings
        /// its set's face paint), clear or re-roll them (roll uses the chances, so often nothing) and rebuild the body.
        /// Names may have spaces; a tattoo's ink follows a '/': sc paint Ana tattoo Maori 03/Royal Lapis.
        /// </summary>
        private static void PaintCmd(List<string> p)
        {
            if (p.Count < 2) { Output("[SC] paint <id|name> [body <id> | face <id> | tattoo <shape>[/ink] | none | roll]"); return; }
            EntityAlive npc = FindSurvivor(p[1]);
            if (npc == null) { Output("[SC] no loaded NPC '" + p[1] + "'"); return; }
            Look look = ScLooks.IsSurvivor(npc) ? ScLooks.Get(npc) : null;
            if (look == null) { Output("[SC] " + ScCommunity.DisplayName(npc) + " does not wear the player's body"); return; }
            if (p.Count == 2)
            {
                Output("[SC] " + ScCommunity.DisplayName(npc) + ": " + ScLooks.DescribePaints(look));
                Output("[SC] " + ScVanity.Report(look.Male));
                return;
            }
            if (!ScVanity.Ready) { Output("[SC] Vanity Vault is not loaded - no paints"); return; }
            string arg = p.Count > 3 ? string.Join(" ", p.GetRange(3, p.Count - 3).ToArray()).Trim() : "";
            switch (p[2].ToLowerInvariant())
            {
                case "none":
                    look.BodyPaint = look.FacePaint = look.Tattoo = "";
                    break;
                case "roll":
                    ScVanity.Roll(look, ScCommunity.World.GetGameRandom(), ScXnpc.IsRaiderClass(ScCommunity.ClassName(npc)));
                    break;
                case "body":
                    string body = ScVanity.BodyKey(arg);
                    if (body == null) { Output("[SC] Vanity Vault has no body paint '" + arg + "'"); return; }
                    if (!ScVanity.BodyFits(body, look.Male)) { Output("[SC] " + body + " has no " + (look.Male ? "male" : "female") + " version"); return; }
                    look.BodyPaint = body;
                    look.FacePaint = ScVanity.FaceFor(body) ?? "";   // the set's face paint; none for a body outside the lists
                    if (look.FacePaint.Length == 0 && !ScVanity.ListedAlone(body)) Output("[SC] note: " + body + " goes on without a face paint");
                    break;
                case "face":
                    string face = ScVanity.FaceKey(arg);
                    if (face == null) { Output("[SC] Vanity Vault has no face paint '" + arg + "'"); return; }
                    if (!ScVanity.FaceFits(face, look.Male)) { Output("[SC] " + face + " has no " + (look.Male ? "male" : "female") + " version"); return; }
                    look.FacePaint = face;
                    break;
                case "tattoo":
                    int slash = arg.IndexOf('/');
                    string shape = slash >= 0 ? arg.Substring(0, slash).Trim() : arg;
                    List<string> inks = ScXnpc.IsRaiderClass(ScCommunity.ClassName(npc)) ? ScConfig.RaiderTattooInks : ScConfig.SurvivorTattooInks;
                    string ink = slash >= 0 ? arg.Substring(slash + 1).Trim() : (inks.Count > 0 ? inks[0] : "No Color");
                    string tattoo = ScVanity.TattooId(shape, ink, ScConfig.TattooMode);
                    if (tattoo == null) { Output("[SC] Vanity Vault has no tattoo '" + shape + "'"); return; }
                    look.Tattoo = tattoo;
                    break;
                default:
                    Output("[SC] paint <id|name> [body <id> | face <id> | tattoo <shape>[/ink] | none | roll]");
                    return;
            }
            // the record takes this look (right after a respawn the body's look is still a copy of it), as sc look does
            Member m = ScRegistry.GetMember(npc.entityId);
            if (m != null) m.Look = look;
            Prospect pr = ScRegistry.GetProspect(npc.entityId);
            if (pr != null) pr.Look = look;
            ScLooks.Rebuild(npc);
            ScRegistry.SaveAtomic();
            Output("[SC] " + ScCommunity.DisplayName(npc) + ": " + ScLooks.DescribePaints(look) + " (body rebuilt)");
        }

        /// <summary>
        /// sc gear <id|name> <Set|Outfit|none> - dress a loaded survivor in a whole vanilla armor set or mod outfit (or strip
        /// them). sc gear sim [n] [hostile] [T1|T2|T3] [any] [male|female] - n clothes rolls without making anyone.
        /// </summary>
        private static void Gear(List<string> p)
        {
            if (p.Count >= 2 && p[1].Equals("sim", StringComparison.OrdinalIgnoreCase))
            {
                int n = 1000, classTier = -1, sex = 0;
                bool hostile = false, anyStage = false;
                for (int i = 2; i < p.Count; i++)
                {
                    int v;
                    string k = p[i].ToLowerInvariant();
                    if (k == "hostile" || k == "raider") hostile = true;
                    else if (k == "any") anyStage = true;
                    else if (k == "male") sex = 1;
                    else if (k == "female") sex = 2;
                    else if (k.Length == 2 && k[0] == 't' && k[1] >= '1' && k[1] <= '3') classTier = k[1] - '1';
                    else if (int.TryParse(k, out v)) n = v;
                }
                // a class tier rolls as that class (ScLoadout.TierCap); without one, a T3 class spawned now, which the
                // gamestage brings down (Patch_ClassByGamestage) unless any
                int band = classTier >= 0 ? classTier : anyStage ? 2 : ScLoadout.GamestageTier();
                Output(ScLooks.SimulateGear(Math.Max(1, Math.Min(100000, n)), hostile, band, sex));
                return;
            }
            if (p.Count < 3)
            {
                Output("[SC] gear <id|name> <Set|Outfit|none> | gear sim [n] [hostile] [T1|T2|T3] [any] [male|female]   sets: " + string.Join(",", ScConfig.SurvivorGearSets.ToArray())
                       + "   outfits: " + string.Join(",", ScLooks.InstalledOutfits().ToArray()));
                return;
            }
            EntityAlive npc = FindSurvivor(p[1]);
            if (npc == null) { Output("[SC] no loaded NPC '" + p[1] + "'"); return; }
            Look look = ScLooks.Get(npc);
            if (look == null) { Output("[SC] " + ScCommunity.DisplayName(npc) + " does not wear the player's body"); return; }
            if (!ScLooks.WearSet(look, p[2], ScCommunity.World.GetGameRandom())) { Output("[SC] no armor set or installed outfit '" + p[2] + "' for a " + (look.Male ? "man" : "woman") + " (sc gear lists them)"); return; }
            ScLooks.SyncGear(npc, look);
            ScRegistry.SaveAtomic();
            Output("[SC] " + ScCommunity.DisplayName(npc) + " wears [" + look.EncodeGear() + "]");
        }

        /// <summary>sc aim [id|name]: the aim pose's hook counters and every NPC in a ranged fight, or one NPC in detail.
        /// An id also finds raiders, which are not framework NPCs (ScNpc).</summary>
        private static void AimCmd(List<string> p)
        {
            EntityAlive e = null;
            if (p.Count >= 2)
            {
                int id;
                if (int.TryParse(p[1], out id)) e = ScCommunity.World != null ? ScCommunity.World.GetEntity(id) as EntityAlive : null;
                else e = FindSurvivor(p[1]);
                if (e == null) { Output("[SC] no loaded NPC '" + p[1] + "'"); return; }
            }
            Output(ScAim.Describe(e));
        }

        /// <summary>sc anim <id|name> - what drives a survivor's animator (diagnostic).</summary>
        private static void Anim(List<string> p)
        {
            if (p.Count < 2) { Output("[SC] anim <id|name>"); return; }
            EntityAlive npc = FindSurvivor(p[1]);
            if (npc == null) { Output("[SC] no loaded NPC '" + p[1] + "'"); return; }
            var sb = new StringBuilder();
            EModelBase em = npc.emodel;
            EModelSDCS sdcs = em as EModelSDCS;
            sb.Append("[SC] ").Append(ScCommunity.DisplayName(npc)).Append(" id ").Append(npc.entityId).Append(" class ").Append(ScCommunity.ClassName(npc))
              .Append(" alive=").Append(npc.IsAlive()).Append(" hp=").Append(npc.Health).Append(" rootMotion=").Append(npc.RootMotion).Append(" remote=").Append(npc.isEntityRemote)
              .Append(" speedFwd=").Append(npc.speedForward.ToString("0.00")).Append(" isMale=").Append(npc.IsMale).Append('\n');
            sb.Append(" emodel=").Append(em != null ? em.GetType().Name : "null").Append(" visible=").Append(em != null && em.visible).Append(" fpv=").Append(em != null && em.IsFPV)
              .Append(" modelName=").Append(em != null ? (em.modelName ?? "-") : "-").Append(" playerModel=").Append(sdcs != null && sdcs.playerEntity != null)
              .Append(" rig=").Append(sdcs != null && sdcs.baseRig != null ? sdcs.baseRig.name + (sdcs.baseRig.activeInHierarchy ? " active" : " INACTIVE") : "null").Append('\n');
            AvatarController ac = em != null ? em.avatarController : null;
            LegacyAvatarController lac = ac as LegacyAvatarController;
            sb.Append(" controller=").Append(ac != null ? ac.GetType().Name : "null").Append(" enabled=").Append(ac != null && ac.enabled).Append(" visibleFlag=").Append(lac != null && lac.m_bVisible)
              .Append(" biped=").Append(lac != null && lac.bipedTransform != null ? lac.bipedTransform.name + (lac.bipedTransform.gameObject.activeInHierarchy ? " active" : " INACTIVE") : "null").Append('\n');
            Animator a = ac != null ? ac.anim : null;
            if (a == null) sb.Append(" animator=null");
            else
            {
                sb.Append(" animator on '").Append(a.gameObject.name).Append("' enabled=").Append(a.enabled).Append(" activeAndEnabled=").Append(a.isActiveAndEnabled).Append(" speed=").Append(a.speed)
                  .Append(" controller=").Append(a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "NULL")
                  .Append(" avatar=").Append(a.avatar != null ? a.avatar.name + (a.avatar.isValid ? " valid" : " INVALID") : "NULL")
                  .Append(" layers=").Append(a.layerCount).Append(" culling=").Append(a.cullingMode).Append('\n');
                if (sdcs != null && sdcs.baseRig != null)
                {
                    Animator ra = sdcs.baseRig.GetComponent<Animator>();
                    sb.Append(" rig root animator=").Append(ra == null ? "none" : (ra == a ? "same" : "DIFFERENT (" + ra.gameObject.name + ")")).Append('\n');
                }
                try
                {
                    if (a.isActiveAndEnabled && a.layerCount > 0)
                    {
                        sb.Append(" layer0 clips:");
                        foreach (AnimatorClipInfo c in a.GetCurrentAnimatorClipInfo(0)) sb.Append(' ').Append(c.clip != null ? c.clip.name : "?").Append('(').Append(c.weight.ToString("0.00")).Append(')');
                        sb.Append('\n');
                    }
                    int n = 0;
                    foreach (AnimatorControllerParameter prm in a.parameters)
                    {
                        if (n++ >= 40) { sb.Append(" ..."); break; }
                        sb.Append(' ').Append(prm.name).Append('=');
                        switch (prm.type)
                        {
                            case AnimatorControllerParameterType.Bool: sb.Append(a.GetBool(prm.nameHash)); break;
                            case AnimatorControllerParameterType.Int: sb.Append(a.GetInteger(prm.nameHash)); break;
                            case AnimatorControllerParameterType.Float: sb.Append(a.GetFloat(prm.nameHash).ToString("0.00")); break;
                            default: sb.Append('T'); break;
                        }
                    }
                }
                catch (Exception e) { sb.Append(" (param read failed: ").Append(e.Message).Append(')'); }
            }
            foreach (string line in sb.ToString().Split('\n')) Output(line);
        }

        private static Member FindMember(string key)
        {
            int id;
            if (int.TryParse(key, out id)) return ScRegistry.GetMember(id);
            foreach (var m in ScRegistry.Members.Values)
                if (m.Name != null && m.Name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return m;
            return null;
        }

        private static void Xp(List<string> p)
        {
            int n;
            if (p.Count < 4 || !int.TryParse(p[3], out n)) { Output("[SC] xp <id|name> <cardio|wits|fighting|shooting|spec> <n>"); return; }
            Member m = FindMember(p[1]);
            if (m == null) { Output("[SC] no member '" + p[1] + "'"); return; }
            Skill s;
            if (p[2].Equals("spec", StringComparison.OrdinalIgnoreCase)) ScSkills.AddSpecialtyXp(m, n);
            else if (ScNames.TrySkill(p[2], out s)) ScSkills.AddXp(m, s, n);
            else { Output("[SC] unknown skill '" + p[2] + "'"); return; }
            EntityAlive npc = ScCommunity.FindNpc(m.Id);      // never whatever holds an offline runner's id
            if (npc != null) ScSkills.StampCvars(m, npc);
            ScRegistry.SaveAtomic();
            Output("[SC] " + m.Name + ": cardio " + m.Stars(Skill.Cardio) + " wits " + m.Stars(Skill.Wits) + " fighting " + m.Stars(Skill.Fighting)
                   + " shooting " + m.Stars(Skill.Shooting) + " " + m.Specialty + " " + m.SpecLevel
                   + (npc != null ? " | cvars melee+" + npc.Buffs.GetCustomVar("sc_dmgmelee") + " ranged+" + npc.Buffs.GetCustomVar("sc_dmgranged") + " speed+" + npc.Buffs.GetCustomVar("sc_speed") + " hp+" + npc.Buffs.GetCustomVar("sc_hp") + " maxhp " + npc.Stats.Health.Max : " | not loaded"));
        }

        private static void Injure(List<string> p)
        {
            if (p.Count < 2) { Output("[SC] injure <id|name>"); return; }
            Member m = FindMember(p[1]);
            if (m == null) { Output("[SC] no member '" + p[1] + "'"); return; }
            m.Injured = !m.Injured;
            ScRegistry.SaveAtomic();
            Output("[SC] " + m.Name + " injured = " + m.Injured);
        }

        private static void RunnersCmd()
        {
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            int n = 0;
            foreach (var m in ScRegistry.Members.Values)
            {
                if (m.Status != MemberStatus.OnRun) continue;
                n++;
                if (ScRunners.IsPhysical(m)) Output("[SC] " + ScRunners.Describe(m, player));
                else Output("[SC] " + m.Name + " [" + m.Id + "] hidden (legacy) run for " + m.RunKind + ", resolves on day " + m.RunEndDay);
            }
            Output("[SC] " + n + " runner(s) out; physical runs " + (ScConfig.PhysicalRuns ? "on" : "OFF") + ", offline pace " + ScConfig.RunWalkMps + " m/s, "
                   + ScConfig.RunMinDist + "-" + ScConfig.RunMaxDist + " m, " + ScConfig.RunSiteHours + " h at the site");
        }

        /// <summary>sc run &lt;member&gt; recall | home | skip | offline - drive a physical run (tests).</summary>
        private static void RunControl(Member m, string sub)
        {
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            World w = ScCommunity.World;
            if (!ScRunners.IsPhysical(m) || w == null) { Output("[SC] " + m.Name + " is not on a physical run"); return; }
            EntityAlive npc = m.Offline ? null : ScNpc.As(w.GetEntity(m.Id));
            switch (sub)
            {
                case "recall":
                {
                    string err;
                    bool ok = ScRunners.Recall(m, player, out err);
                    Output("[SC] " + (ok ? m.Name + " turns for home carrying " + m.RunCarry : "refused: " + Localization.Get(err ?? "scInternal")));
                    break;
                }
                case "home":
                    if (npc != null && !ScRunners.GoOffline(w, m, npc, "console home", false)) { Output("[SC] could not take " + m.Name + " offline"); break; }
                    ScRunners.ForceHome(m, player);
                    Output("[SC] " + (ScRegistry.GetMember(m.Id) == m ? m.Name + " is at the gate (" + m.RunLeg + ")" : m.Name + " did not make it (the run's dice)"));
                    break;
                case "offline":
                    if (npc == null) { Output("[SC] " + m.Name + " is already offline"); break; }
                    Output("[SC] " + (ScRunners.GoOffline(w, m, npc, "console", false) ? m.Name + " went offline" : "could not capture " + m.Name));
                    break;
                case "skip":
                    if (npc != null && !ScRunners.GoOffline(w, m, npc, "console skip", false)) { Output("[SC] could not take " + m.Name + " offline"); break; }
                    if (m.RunLeg == RunLeg.Back) { Output("[SC] " + m.Name + " waits at the gate - they walk in once you are home"); break; }
                    m.LegEnd = w.worldTime;                   // the next tick moves them on to the next leg
                    ScRegistry.SaveAtomic();
                    Output("[SC] " + m.Name + ": " + m.RunLeg + " ends now");
                    break;
            }
        }

        private static void Run(List<string> p)
        {
            if (p.Count >= 3)
            {
                string rsub = p[2].ToLowerInvariant();
                if (rsub == "recall" || rsub == "home" || rsub == "skip" || rsub == "offline")
                {
                    Member rm = FindMember(p[1]);
                    if (rm == null) { Output("[SC] no member '" + p[1] + "'"); return; }
                    RunControl(rm, rsub);
                    return;
                }
            }
            Resource r;
            if (p.Count < 3 || !ScNames.TryResource(p[2], out r)) { Output("[SC] run <id|name> <food|water|meds|ammo|mats|fuel>"); return; }
            Member m = FindMember(p[1]);
            if (m == null) { Output("[SC] no member '" + p[1] + "'"); return; }
            string err;
            bool ok = ScRuns.Send(m, ScCommunity.FindNpc(m.Id), r, ScCommunity.LocalPlayer, out err);
            Output("[SC] " + (ok ? m.Name + " sent for " + r + (ScRunners.IsPhysical(m) ? " on foot to " + m.RunDestName + " " + m.RunDest : "") + ", back around day " + m.RunEndDay + " (expected " + ScRuns.Yield(m, r) + ")" : "refused: " + err));
        }

        /// <summary>sc portrait &lt;id|name|all&gt; re-shoots; sc portrait frame [fov dist yaw eyelift aimlift] shows or tunes the framing.</summary>
        private static void PortraitCmd(List<string> p)
        {
            if (p.Count >= 2 && p[1].Equals("frame", StringComparison.OrdinalIgnoreCase))
            {
                float[] v = { ScPortraits.Fov, ScPortraits.Distance, ScPortraits.Yaw, ScPortraits.EyeLift, ScPortraits.AimLift };
                for (int i = 0; i < v.Length && i + 2 < p.Count; i++)
                {
                    float f;
                    if (float.TryParse(p[i + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) v[i] = f;
                }
                ScPortraits.Fov = Mathf.Clamp(v[0], 5f, 90f);
                ScPortraits.Distance = Mathf.Clamp(v[1], 0.3f, 6f);
                ScPortraits.Yaw = Mathf.Clamp(v[2], -90f, 90f);
                ScPortraits.EyeLift = Mathf.Clamp(v[3], -1f, 1f);
                ScPortraits.AimLift = Mathf.Clamp(v[4], -1f, 1f);
                Output("[SC] portrait framing: fov " + ScPortraits.Fov + ", distance " + ScPortraits.Distance + ", yaw " + ScPortraits.Yaw
                       + ", eyelift " + ScPortraits.EyeLift + ", aimlift " + ScPortraits.AimLift + " (this session; 'sc portrait all' re-shoots)");
                return;
            }
            if (p.Count < 2) { Output("[SC] usage: sc portrait <id|name|all> | sc portrait frame [fov dist yaw eyelift aimlift]"); return; }
            if (p[1].Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                var ids = new List<int>(ScRegistry.Members.Keys);
                foreach (int id in ids) ScPortraits.Invalidate(id);
                foreach (int id in ids) { Member m = ScRegistry.GetMember(id); if (m != null) ScPortraits.Request(m); }
                Output("[SC] re-shooting " + ids.Count + " portrait(s)");
                return;
            }
            Member one = FindMember(p[1]);
            if (one == null) { Output("[SC] no member '" + p[1] + "'"); return; }
            ScPortraits.Invalidate(one.Id);
            ScPortraits.Request(one, true);
            Output("[SC] re-shooting " + one.Name + (one.Look != null ? " (from their look)" : " (live: they must be loaded, within 40 m and in view)"));
        }

        // ---------------------------------------------------------------- outposts: POI probes (milestone 1)

        /// <summary>sc poi | sc poi near [r] | sc poi classify &lt;prefab&gt; | sc poi census</summary>
        private static void PoiCmd(List<string> p)
        {
            ScConfig.EnsureParsed();
            string sub = p.Count > 1 ? p[1].ToLowerInvariant() : "";
            World w = ScCommunity.World;
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (sub == "classify")
            {
                if (p.Count < 3) { Output("[SC] usage: sc poi classify <prefab name>"); return; }
                PrefabInstance placed = FindPlaced(p[2]);
                if (placed != null) { DescribePoi(placed, w, pl); return; }
                PoiClass c = ScPoiTypes.ClassifyName(p[2]);
                Output("[SC] " + p[2] + " is not placed in this world - by name only (tier and sleepers unknown):");
                Output("[SC]   outpost type: " + c.Label + " (rule " + c.Rule + ": " + c.Why + ") - at T2 " + ScPoiTypes.YieldText(c));
                return;
            }
            if (sub == "census") { PoiCensus(); return; }
            if (w == null || pl == null) { Output("[SC] no world / player"); return; }
            if (sub == "near")
            {
                int r = 500;
                if (p.Count > 2) int.TryParse(p[2], out r);
                r = Mathf.Clamp(r, 20, 5000);
                PoiNear(w, pl, r);
                return;
            }
            PrefabInstance pi = ScSecurity.PoiAt(pl.GetBlockPosition());
            if (pi == null) { Output("[SC] you are not standing in a POI (sc poi near [radius] lists the ones around you)"); return; }
            DescribePoi(pi, w, pl);
        }

        private static PrefabInstance FindPlaced(string prefab)
        {
            DynamicPrefabDecorator dec = GameManager.Instance != null ? GameManager.Instance.GetDynamicPrefabDecorator() : null;
            if (dec == null || dec.allPrefabs == null) return null;
            foreach (PrefabInstance pi in dec.allPrefabs)
                if (pi != null && pi.prefab != null && string.Equals(pi.prefab.PrefabName, prefab, StringComparison.OrdinalIgnoreCase)) return pi;
            return null;
        }

        private static float DistanceToHome(PrefabInstance pi)
        {
            if (!ScZone.HasHome) return -1f;
            Vector3i a = pi.boundingBoxPosition, s = pi.boundingBoxSize, h = ScZone.Home;
            float cx = a.x + s.x * 0.5f, cz = a.z + s.z * 0.5f;
            return Mathf.Sqrt((cx - h.x) * (cx - h.x) + (cz - h.z) * (cz - h.z));
        }

        private static void DescribePoi(PrefabInstance pi, World w, EntityPlayerLocal pl)
        {
            Prefab pf = pi.prefab;
            PoiClass c = ScPoiTypes.Classify(pi);
            Output("[SC] " + c.Display + " (" + pf.PrefabName + ") tier " + pf.DifficultyTier + ", " + c.SleeperVolumes + " sleeper volume(s), "
                   + pi.boundingBoxSize.x + "x" + pi.boundingBoxSize.z + " at " + pi.boundingBoxPosition);
            Output("[SC]   tags: " + pf.Tags + " | theme: " + pf.ThemeTags + " | zoning: " + string.Join(",", pf.allowedZones.ToArray()));
            Output("[SC]   outpost type: " + c.Label + " (rule " + c.Rule + ": " + c.Why + ") - " + ScPoiTypes.YieldText(c));
            if (w != null)
            {
                string why;
                bool cleared = ScSecurity.IsCleared(w, pi, out why);
                float d = DistanceToHome(pi);
                Output("[SC]   cleared: " + (cleared ? "yes" : "no - " + why)
                       + " | home: " + (d < 0f ? "none" : Mathf.RoundToInt(d) + " m (outposts need " + ScConfig.OutpostMinDistance + ")")
                       + (ScSecurity.OverlapsHome(pi) ? " | touches the home zone" : ""));
            }
            Enclave en = ScEnclaves.Holding(pi);
            if (en != null)
                Output("[SC]   enclave #" + en.Id + ": " + en.Name + " (" + ScEnclaves.StandingWord(en) + (en.Known ? "" : ", not found yet")
                       + ") - no sleepers, claims, outposts or trader quests here (sc enclave info " + en.Id + ")");
            string infested = ScInfestations.PoiLine(pi);
            if (infested != null) Output("[SC]   " + infested);
        }

        private static void PoiNear(World w, EntityPlayerLocal pl, int r)
        {
            DynamicPrefabDecorator dec = GameManager.Instance.GetDynamicPrefabDecorator();
            if (dec == null) { Output("[SC] no prefab decorator"); return; }
            var list = new List<PrefabInstance>();
            Vector3 at = pl.position;
            dec.GetPrefabsAtXZ((int)at.x - r, (int)at.x + r, (int)at.z - r, (int)at.z + r, list);
            var rows = new List<KeyValuePair<float, string>>();
            var seen = new HashSet<int>();
            foreach (PrefabInstance pi in list)
            {
                if (!ScSecurity.IsRealPoi(pi) || !seen.Add(pi.id)) continue;
                Vector3i a = pi.boundingBoxPosition, s = pi.boundingBoxSize;
                float cx = a.x + s.x * 0.5f, cz = a.z + s.z * 0.5f;
                float d = Mathf.Sqrt((cx - at.x) * (cx - at.x) + (cz - at.z) * (cz - at.z));
                if (d > r) continue;
                PoiClass c = ScPoiTypes.Classify(pi);
                if (c.Kind == "none" || c.Kind == "ineligible") continue;
                string why;
                bool cleared = ScSecurity.IsCleared(w, pi, out why);
                rows.Add(new KeyValuePair<float, string>(d, Mathf.RoundToInt(d) + " m  " + c.Display + " (" + pi.prefab.PrefabName + ", T" + pi.prefab.DifficultyTier + ")  "
                         + c.Label + "  " + ScPoiTypes.YieldText(c) + (cleared ? "  [cleared]" : "")));
            }
            rows.Sort((x, y) => x.Key.CompareTo(y.Key));
            Output("[SC] " + rows.Count + " outpost candidate(s) within " + r + " m" + (rows.Count > 25 ? " (nearest 25)" : "") + ":");
            for (int i = 0; i < rows.Count && i < 25; i++) Output("[SC]   " + rows[i].Value);
        }

        private static void PoiCensus()
        {
            DynamicPrefabDecorator dec = GameManager.Instance != null ? GameManager.Instance.GetDynamicPrefabDecorator() : null;
            if (dec == null || dec.allPrefabs == null) { Output("[SC] no world"); return; }
            var byRes = new Dictionary<string, Dictionary<string, int>>();
            int real = 0;
            foreach (PrefabInstance pi in dec.allPrefabs)
            {
                if (!ScSecurity.IsRealPoi(pi)) continue;
                real++;
                PoiClass c = ScPoiTypes.Classify(pi);
                string key = c.Kind == "row" && c.Row != null ? ScNames.ResourceKeys[(int)c.Row.Primary] : c.Kind;
                Dictionary<string, int> names;
                if (!byRes.TryGetValue(key, out names)) byRes[key] = names = new Dictionary<string, int>();
                string n = c.Display + (c.Kind == "row" ? "" : "");
                int k; names.TryGetValue(n, out k); names[n] = k + 1;
            }
            Output("[SC] outpost census of this world: " + real + " real POIs placed");
            foreach (string res in new[] { "food", "meds", "ammo", "mats", "fuel", "water", "scavenge", "none", "ineligible" })
            {
                Dictionary<string, int> names;
                byRes.TryGetValue(res, out names);
                int total = 0; if (names != null) foreach (var kv in names) total += kv.Value;
                string sample = "";
                if (names != null && res != "scavenge" && res != "ineligible" && res != "none")
                {
                    var parts = new List<string>();
                    foreach (var kv in names) parts.Add(kv.Key + (kv.Value > 1 ? " x" + kv.Value : ""));
                    parts.Sort(StringComparer.OrdinalIgnoreCase);
                    sample = "  " + string.Join(", ", parts.GetRange(0, Math.Min(12, parts.Count)).ToArray()) + (parts.Count > 12 ? ", ..." : "");
                }
                Output("[SC]   " + res.PadRight(10) + total.ToString().PadLeft(4) + sample);
                if (total == 0 && res != "none" && res != "ineligible" && res != "scavenge")
                    Output("[SC]     no " + res + " site in this world - a scavenge outpost will cover it");
            }
        }

        // ---------------------------------------------------------------- enclaves (E1)

        /// <summary>
        /// sc enclave [list | info &lt;id&gt; | people &lt;id&gt; | spawn &lt;id&gt; | despawn &lt;id&gt; | standing &lt;id&gt; &lt;points&gt; |
        /// hostile &lt;id&gt; | kill &lt;id&gt; [&lt;person id&gt;|all] | trade &lt;id&gt; | release &lt;id&gt; | requests | request &lt;id&gt;
        /// [offer &lt;kind&gt; [res] [size] [force] | accept | complete [points] | fail | lapse | clear] | reveal &lt;id|all&gt; |
        /// hide &lt;id|all&gt; | sim [late] | names [n] [hostile] | reseed confirm] (people to kill: E2; trade, release: E3;
        /// requests: E4; siege: E5; refill [now]: E6)
        /// </summary>
        private static void EnclaveCmd(List<string> p)
        {
            ScConfig.EnsureParsed();
            string sub = p.Count > 1 ? p[1].ToLowerInvariant() : "list";
            var lines = new List<string>();
            if (sub == "names")
            {
                // the generator only: works anywhere
                int n = 10;
                bool hostile = false;
                for (int i = 2; i < p.Count; i++)
                {
                    int v;
                    if (int.TryParse(p[i], out v)) n = Mathf.Clamp(v, 1, 200);
                    else if (p[i].Equals("hostile", StringComparison.OrdinalIgnoreCase)) hostile = true;
                }
                Output("[SC] " + n + " sample " + (hostile ? "raider" : "survivor") + " enclave name(s):");
                ScEnclaves.SampleNames(n, hostile, lines);
                foreach (string l in lines) Output("[SC] " + l);
                return;
            }
            if (!ScCommunity.IsHost) { Output("[SC] host only"); return; }
            switch (sub)
            {
                case "list": ScEnclaves.ListLines(lines); break;
                case "info":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    ScEnclaves.InfoLines(e, lines);
                    break;
                }
                case "reveal":
                case "hide":
                {
                    bool all = p.Count > 2 && p[2].Equals("all", StringComparison.OrdinalIgnoreCase);
                    Enclave e = all ? null : EnclaveArg(p);
                    if (!all && e == null) return;
                    int n = ScEnclaves.SetKnown(e, sub == "reveal", ScCommunity.LocalPlayer);
                    lines.Add(n + " enclave(s) " + (sub == "reveal" ? "revealed, with their markers" : "hidden again, markers off"));
                    break;
                }
                case "sim": ScEnclaves.Simulate(p.Count > 2 && p[2].Equals("late", StringComparison.OrdinalIgnoreCase), lines); break;
                // E2: the people
                case "people":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    ScEnclavePeople.PeopleLines(e, lines);
                    break;
                }
                case "spawn":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    lines.Add(ScEnclavePeople.SpawnNow(e));
                    break;
                }
                case "despawn":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    lines.Add(ScEnclavePeople.DespawnNow(e));
                    break;
                }
                case "kill":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    lines.Add(ScEnclavePeople.Kill(e, p.Count > 3 ? p[3] : null));
                    break;
                }
                // E3: the leader's trade (id, stock, restock, prices); the enlisted person sent home
                case "trade":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    ScEnclaveDeals.TradeLines(e, lines);
                    break;
                }
                case "release":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    lines.Add(ScEnclaveDeals.ReleaseNow(e));
                    break;
                }
                // E4: the requests - every open one; one enclave's made or moved (testing)
                case "requests": ScEncRequests.ListLines(lines); break;
                case "request":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    if (!ScConfig.Enclaves || !ScEnclaves.Ready) { Output("[SC] enclaves are off in the config, or the world is still starting"); return; }
                    ScEncRequests.Command(e, p, lines);
                    break;
                }
                // E5: the siege - its state, a call now (the pace ignored; the rules kept unless "force"), settled for testing
                case "siege":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    if (!ScConfig.Enclaves || !ScEnclaves.Ready) { Output("[SC] enclaves are off in the config, or the world is still starting"); return; }
                    ScEncSiege.Command(e, p, lines);
                    break;
                }
                case "standing":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    int points;
                    if (p.Count < 4 || !int.TryParse(p[3], out points)) { Output("[SC] sc enclave standing <id> <points> (" + ScConfig.EnclaveStandingMin + "..100)"); return; }
                    ScEnclaves.AddStanding(e, points - e.Standing, "the console");
                    lines.Add(e.Name + ": standing " + e.Standing + " (" + ScEnclaves.StandingWord(e) + ")");
                    break;
                }
                case "hostile":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    if (e.IsHostile) { lines.Add(e.Name + " is hostile already" + (e.Hostile ? " (raiders)" : " (since day " + e.HostileDay + ")")); break; }
                    ScEnclaves.TurnHostile(e, "the console");
                    lines.Add(e.Name + " is hostile for good now (only sc enclave reseed undoes it)");
                    break;
                }
                case "reseed":
                    if (p.Count < 3 || !p[2].Equals("confirm", StringComparison.OrdinalIgnoreCase))
                    {
                        Output("[SC] sc enclave reseed confirm - drops EVERY enclave and seeds again with the late-seed rules (testing only)");
                        return;
                    }
                    lines.Add(ScEnclaves.Reseed(ScCommunity.LocalPlayer));
                    break;
                // E6: the refills owed for lost enclaves; "now" moves them in at once (one anyway with none owed)
                case "refill": ScEncAllies.RefillCommand(p, lines); break;
                // E7: a gift's state, or one as if handed over; the ally barter's state, or one trade (the day's wait ignored)
                case "gift":
                case "gifts":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    ScEncGifts.Command(e, p, lines);
                    break;
                }
                case "barter":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    ScEncBarter.Command(e, p, lines);
                    break;
                }
                // E9: the radio calls' state and who would go; a call as from the radio window (force: the cooldown and the Allied
                // rule ignored; now: it comes at once); a pending call off
                case "call":
                case "calls":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    if (!ScEnclaves.Ready) { Output("[SC] the world is still starting"); return; }
                    ScEncCalls.Command(e, p, lines);
                    break;
                }
                // E10: a hostile group's raids - their state and each outpost's distance; one outpost's; a demand made now (the
                // day's roll and their rest ignored), or made and refused at once (the raid called)
                case "raid":
                case "raids":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    if (!ScEnclaves.Ready) { Output("[SC] the world is still starting"); return; }
                    ScEncRaids.Command(e, p, lines);
                    break;
                }
                // E11: a group's move - its reasons, what holds it, the chance; sim: where it would go today (nothing changes);
                // now: it moves at once (the reasons and the chance ignored, what holds it kept)
                case "move":
                case "moves":
                {
                    Enclave e = EnclaveArg(p);
                    if (e == null) return;
                    if (!ScEnclaves.Ready) { Output("[SC] the world is still starting"); return; }
                    ScEncMoves.Command(e, p, lines);
                    break;
                }
                default:
                    Output("[SC] sc enclave [list | info <id> | people <id> | spawn <id> | despawn <id> | standing <id> <points> | hostile <id> | kill <id> [<person id>|all] | trade <id> | release <id> | requests | request <id> [offer <resource|generator|gun|missing|infestation> [res] [size] [force] | accept | complete [points] | fail | lapse | clear | fate <alive|dead>] | siege <id> [now [force] | win | lose | clear] | refill [now] | gift <id> [give <res> [size] [force]] | barter <id> [<give> [get]] | call <id> [fighter|drop [force] [now] | cancel] | raid <id> [<outpost id> [demand|now]] | move <id> [sim|now] | reveal <id|all> | hide <id|all> | sim [late] | names [n] [hostile] | reseed confirm]");
                    return;
            }
            foreach (string l in lines) Output("[SC] " + l);
        }

        private static Enclave EnclaveArg(List<string> p)
        {
            int id;
            if (p.Count < 3 || !int.TryParse(p[2], out id)) { Output("[SC] give an enclave id (sc enclave list)"); return null; }
            Enclave e = ScEnclaves.ById(id);
            if (e == null) Output("[SC] no enclave " + id);
            return e;
        }

        // ---------------------------------------------------------------- infestations (I1)

        /// <summary>sc infest [list | info &lt;id&gt; | add [&lt;x&gt; &lt;z&gt;] | clear &lt;id|all&gt; | spread &lt;id&gt; | roll | reveal &lt;id|all&gt; | hide &lt;id|all&gt;]</summary>
        private static void InfestCmd(List<string> p)
        {
            ScConfig.EnsureParsed();
            if (!ScCommunity.IsHost) { Output("[SC] host only"); return; }
            string sub = p.Count > 1 ? p[1].ToLowerInvariant() : "list";
            var lines = new List<string>();
            switch (sub)
            {
                case "list": ScInfestations.ListLines(lines); break;
                case "info":
                {
                    Infestation inf = InfestArg(p);
                    if (inf == null) return;
                    ScInfestations.InfoLines(inf, lines);
                    break;
                }
                case "add":
                {
                    int x = 0, z = 0;
                    bool at = p.Count > 3 && int.TryParse(p[2], out x) && int.TryParse(p[3], out z);
                    if (p.Count > 2 && !at) { Output("[SC] sc infest add [<x> <z>] - the POI you stand in, or the one at x z"); return; }
                    lines.Add(ScInfestations.ConsoleAdd(at, x, z, ScCommunity.LocalPlayer));
                    break;
                }
                case "clear":
                {
                    bool all = p.Count > 2 && p[2].Equals("all", StringComparison.OrdinalIgnoreCase);
                    Infestation inf = all ? null : InfestArg(p);
                    if (!all && inf == null) return;
                    int n = ScInfestations.ConsoleClear(inf, ScCommunity.LocalPlayer);
                    lines.Add(n + " infestation(s) ended - records and markers only, the rooms are left as they are");
                    break;
                }
                case "spread":
                {
                    Infestation inf = InfestArg(p);
                    if (inf == null) return;
                    lines.Add(ScInfestations.ConsoleSpread(inf, ScCommunity.LocalPlayer));
                    break;
                }
                case "roll": lines.Add(ScInfestations.ConsoleRoll(ScCommunity.LocalPlayer)); break;
                case "reveal":
                case "hide":
                {
                    bool all = p.Count > 2 && p[2].Equals("all", StringComparison.OrdinalIgnoreCase);
                    Infestation inf = all ? null : InfestArg(p);
                    if (!all && inf == null) return;
                    int n = ScInfestations.SetKnown(inf, sub == "reveal", ScCommunity.LocalPlayer);
                    lines.Add(n + " infestation(s) " + (sub == "reveal" ? "revealed, with their markers" : "hidden again, markers off"));
                    break;
                }
                default:
                    Output("[SC] sc infest [list | info <id> | add [<x> <z>] | clear <id|all> | spread <id> | roll | reveal <id|all> | hide <id|all>]");
                    return;
            }
            foreach (string l in lines) Output("[SC] " + l);
        }

        private static Infestation InfestArg(List<string> p)
        {
            int id;
            if (p.Count < 3 || !int.TryParse(p[2], out id)) { Output("[SC] give an infestation id (sc infest list)"); return null; }
            Infestation inf = ScInfestations.ById(id);
            if (inf == null) Output("[SC] no infestation " + id);
            return inf;
        }

        // ---------------------------------------------------------------- the zombie-cap fix (I2)

        /// <summary>sc sleepers [status | scan | repair]</summary>
        private static void SleepersCmd(List<string> p)
        {
            ScConfig.EnsureParsed();
            if (!ScCommunity.IsHost) { Output("[SC] host only"); return; }
            string sub = p.Count > 1 ? p[1].ToLowerInvariant() : "status";
            var lines = new List<string>();
            switch (sub)
            {
                case "status": ScSleeperCap.StatusLines(lines); break;
                case "scan": ScSleeperCap.ScanLines(lines); break;
                case "repair": ScSleeperCap.RepairLines(lines); break;
                default:
                    Output("[SC] sc sleepers [status | scan | repair]");
                    return;
            }
            foreach (string l in lines) Output("[SC] " + l);
        }

        // ---------------------------------------------------------------- outposts (milestone 2)

        private static void OutpostCmd(List<string> p)
        {
            ScConfig.EnsureParsed();
            string sub = p.Count > 1 ? p[1].ToLowerInvariant() : "list";
            EntityPlayerLocal pl = ScCommunity.LocalPlayer;
            if (!ScCommunity.IsHost) { Output("[SC] host only"); return; }
            switch (sub)
            {
                case "list": OutpostList(); return;
                case "yield": OutpostYield(); return;
                case "add":
                {
                    if (pl == null) { Output("[SC] no player"); return; }
                    PrefabInstance pi = ScSecurity.PoiAt(pl.GetBlockPosition());
                    if (pi == null) { Output("[SC] stand inside the POI (sc poi near lists them)"); return; }
                    Vector3i claim;
                    if (!OwnClaimIn(pi, pl, out claim)) { Output("[SC] no land claim of yours inside " + ScSecurity.DisplayName(pi) + " - place one there first"); return; }
                    string msg;
                    Outpost o = ScOutposts.Establish(claim, pl, true, out msg);
                    if (o != null && p.Count > 2)
                    {
                        PoiRow row = ScPoiTypes.FindRow(p[2]);
                        if (row != null) ScOutposts.SetType(o, new PoiClass { Kind = "row", Row = row });
                        else if (p[2].Equals(ScOutposts.ScavengeRow, StringComparison.OrdinalIgnoreCase)) ScOutposts.SetType(o, null);
                        else Output("[SC] no row '" + p[2] + "' - kept the classified type");
                        ScOutposts.RefreshMarkers(pl);
                        ScRegistry.SaveAtomic();
                    }
                    Output("[SC] " + (o != null ? "outpost " + o.Id + ": " + OutpostLine(o) : "refused: " + msg));
                    return;
                }
                case "remove":
                case "disband":
                {
                    Outpost o = OutpostArg(p);
                    if (o == null) return;
                    ScOutposts.Disband(o, pl);
                    Output("[SC] outpost " + o.Id + " at " + o.Name + " disbanded");
                    return;
                }
                case "pending":
                {
                    Outpost o = OutpostArg(p);
                    int n;
                    if (o == null) return;
                    if (p.Count < 4 || !int.TryParse(p[3], out n)) { Output("[SC] usage: sc outpost pending <id> <n>"); return; }
                    o.Pending[(int)ScOutposts.PrimaryToday(o)] = Math.Max(0, n);
                    ScRegistry.SaveAtomic();
                    Output("[SC] " + OutpostLine(o));
                    return;
                }
                case "reclassify":
                {
                    Outpost o = OutpostArg(p);
                    if (o == null) return;
                    PrefabInstance pi = ScSecurity.PoiByKey(o.Poi);
                    if (pi == null) { Output("[SC] the POI at " + o.Poi + " is not in the world any more"); return; }
                    string before = o.Row;
                    ScOutposts.SetType(o, ScPoiTypes.Classify(pi));
                    o.Tier = Math.Max(0, (int)pi.prefab.DifficultyTier);
                    ScOutposts.RefreshMarkers(pl);
                    ScRegistry.SaveAtomic();
                    Output("[SC] outpost " + o.Id + " reclassified: " + before + " -> " + o.Row);
                    return;
                }
                case "state":
                {
                    Outpost o = OutpostArg(p);
                    if (o == null) return;
                    OutpostState st;
                    try { st = (OutpostState)Enum.Parse(typeof(OutpostState), p.Count > 3 ? p[3] : "", true); }
                    catch (Exception) { Output("[SC] states: Active, Idle, Contested, Lost"); return; }
                    o.State = st;
                    if (st == OutpostState.Lost) o.LostDay = ScCommunity.Today;
                    ScOutposts.Rebuild();
                    ScOutposts.RefreshMarkers(pl);
                    ScRegistry.SaveAtomic();
                    Output("[SC] " + OutpostLine(o));
                    return;
                }
                case "post":
                {
                    Outpost o = OutpostArg(p);
                    if (o == null) return;
                    Member m = p.Count > 3 ? FindMember(p[3]) : null;
                    if (m == null) { Output("[SC] usage: sc outpost post <id> <member id|name>"); return; }
                    string err;
                    bool ok = ScGarrison.Post(m, ScCommunity.FindNpc(m.Id), o, pl, out err);
                    Output("[SC] " + (ok ? m.Name + " posted at " + o.Name : "refused: " + Localization.Get(err ?? "scInternal")));
                    return;
                }
                case "recall":
                {
                    Member m = p.Count > 2 ? FindMember(p[2]) : null;
                    if (m == null || m.Status != MemberStatus.Garrison) { Output("[SC] usage: sc outpost recall <member id|name> (a posted member)"); return; }
                    ScGarrison.Recall(m, pl, "scOpRecalled");
                    ScRegistry.SaveAtomic();
                    Output("[SC] " + m.Name + " recalled");
                    return;
                }
                case "upgrade":
                {
                    Outpost o = OutpostArg(p);
                    if (o == null) return;
                    string msg;
                    bool ok = ScOutpostPerks.Upgrade(o, pl, out msg);
                    Output("[SC] " + (ok ? "" : "refused: ") + msg);
                    return;
                }
                case "attack":
                {
                    Outpost o = OutpostArg(p);
                    if (o == null) return;
                    ScOutpostRisk.StartAttack(o, p.Count > 3 && p[3].StartsWith("raid", StringComparison.OrdinalIgnoreCase), pl, true);   // heard: the console is the radio
                    Output("[SC] attack started on " + o.Name + " - security " + ScOutpostRisk.SecurityText(o));
                    return;
                }
                case "demand":
                {
                    Outpost o = OutpostArg(p);
                    if (o == null) return;
                    o.DemandUntil = ScCommunity.Today; o.DemandAmmo = ScConfig.OutpostDemandAmmo; o.DemandFood = ScConfig.OutpostDemandFood;
                    o.RaidEnclave = -1;      // E10: a nameless demand (sc enclave raid <id> <outpost id> demand makes an enclave's)
                    ScRegistry.SaveAtomic();
                    Output("[SC] raiders demand " + o.DemandAmmo + " ammo + " + o.DemandFood + " food for " + o.Name + " (answer on the radio, or sc outpost pay|refuse " + o.Id + ")");
                    return;
                }
                case "pay":
                case "refuse":
                case "repair":
                {
                    Outpost o = OutpostArg(p);
                    if (o == null) return;
                    string msg;
                    bool ok = sub == "pay" ? ScOutpostRisk.PayDemand(o, pl, out msg) : sub == "refuse" ? ScOutpostRisk.RefuseDemand(o, pl, out msg) : ScOutpostRisk.Repair(o, pl, out msg);
                    Output("[SC] " + (ok ? "" : "refused: ") + msg);
                    return;
                }
                case "security":
                {
                    Outpost o = OutpostArg(p);
                    if (o == null) return;
                    Output("[SC] " + o.Name + ": security " + ScOutpostRisk.SecurityText(o) + (ScConfig.OutpostAttacks ? "" : " (OutpostAttacks is off: no attacks roll)"));
                    return;
                }
                case "mode":
                {
                    Outpost o = OutpostArg(p);
                    if (o == null) return;
                    string want = p.Count > 3 ? p[3].ToLowerInvariant() : "";
                    int mode = want == "quiet" ? 1 : want == "push" ? 2 : want == "normal" ? 0 : -1;
                    if (mode < 0) { Output("[SC] usage: sc outpost mode <id> <normal|quiet|push>"); return; }
                    o.Mode = mode;
                    ScRegistry.SaveAtomic();
                    Output("[SC] " + OutpostLine(o));
                    return;
                }
                case "level":
                {
                    Outpost o = OutpostArg(p);
                    int n;
                    if (o == null) return;
                    if (p.Count < 4 || !int.TryParse(p[3], out n)) { Output("[SC] usage: sc outpost level <id> <1-3> (debug, free)"); return; }
                    o.Level = Mathf.Clamp(n, 1, 3);
                    o.LevelDay = ScCommunity.Today;
                    ScOutposts.RefreshMarkers(pl);
                    ScRegistry.SaveAtomic();
                    Output("[SC] " + OutpostLine(o));
                    return;
                }
                default:
                    Output("[SC] sc outpost list | add [row|scavenge] | remove <id> | yield | pending <id> <n> | reclassify <id> | state <id> <state> | post <id> <member> | recall <member> | upgrade <id> | level <id> <1-3>");
                    return;
            }
        }

        private static Outpost OutpostArg(List<string> p)
        {
            int id;
            if (p.Count < 3 || !int.TryParse(p[2], out id)) { Output("[SC] give an outpost id (sc outpost list)"); return null; }
            Outpost o = ScOutposts.ById(id);
            if (o == null) Output("[SC] no outpost " + id);
            return o;
        }

        private static bool OwnClaimIn(PrefabInstance pi, EntityPlayerLocal pl, out Vector3i claim)
        {
            claim = Vector3i.zero;
            var pp = GameManager.Instance.persistentPlayers;
            PersistentPlayerData ppd = pp != null ? pp.GetPlayerDataFromEntityID(pl.entityId) : null;
            if (ppd == null || ppd.LPBlocks == null) return false;
            foreach (Vector3i lp in ppd.LPBlocks)
            {
                if (!ScSecurity.FootprintContains(pi, lp)) continue;
                if (ScZone.HasHome && ScZone.Home == lp) continue;
                claim = lp;
                return true;
            }
            return false;
        }

        private static string OutpostLine(Outpost o)
        {
            var staff = ScGarrison.Staff(o);
            var names = new List<string>();
            foreach (Member sm in staff) names.Add(sm.Name);
            return o.Name + " (" + o.Prefab + ", T" + o.Tier + ", L" + o.Level + ", " + ScOutposts.TypeLabel(o) + ", " + ScOutpostPerks.ModeLabel(o) + ") " + ScOutposts.StateLabel(o)
                   + (ScOutposts.RelayOnline(o) ? " [relay]" : "")
                   + " built[" + ScOutpostPerks.InvestText(o) + "]" + (o.StashTotal > 0 ? " stash " + o.StashTotal : "")
                   + (names.Count > 0 ? " [staff: " + string.Join(", ", names.ToArray()) + "]" : "")
                   + " - " + ScOutposts.YieldText(ScOutposts.Estimate(o))
                   + (o.PendingTotal > 0 ? ", " + o.PendingTotal + " waiting" : "")
                   + " | flag " + o.Flag + ", " + Mathf.RoundToInt(ScOutposts.DistanceToHome(o.Poi, o.Size)) + " m from home, since day " + o.Day
                   + (o.State == OutpostState.Lost ? ", lost day " + o.LostDay : "");
        }

        private static void OutpostList()
        {
            Community c = ScRegistry.Community;
            Output("[SC] " + ScOutposts.HeldCount() + " of " + ScOutposts.Slots() + " outpost slot(s) (max " + ScConfig.OutpostMax + "); radio "
                   + (ScOutposts.RadioOnlineAnywhere() ? "ONLINE" : "silent") + " (last reported day " + c.RadioOnlineDay + ", today " + ScCommunity.Today + ")"
                   + "; upkeep debt " + c.UpkeepDebt.ToString("0.##") + (c.OutpostRunDown ? " RUN-DOWN" : "")
                   + ", haulage debt " + c.HaulDebt.ToString("0.##") + (c.OutpostOnFoot ? " ON FOOT" : "")
                   + (c.OutpostLoss > 0f ? ", loss penalty " + c.OutpostLoss.ToString("0.#") : ""));
            foreach (Outpost o in ScOutposts.All) Output("[SC]   #" + o.Id + " " + OutpostLine(o));
        }

        private static void OutpostYield()
        {
            float[] total = new float[ScNames.ResourceCount];
            foreach (Outpost o in ScOutposts.All)
            {
                if (!o.Holding) continue;
                float[] raw = ScOutposts.RawYield(o), est = ScOutposts.Estimate(o);
                for (int i = 0; i < ScNames.ResourceCount; i++) total[i] += est[i];
                Output("[SC]   #" + o.Id + " " + o.Name + " [" + o.State + "] raw " + ScOutposts.YieldText(raw) + " -> tonight " + ScOutposts.YieldText(est)
                       + (o.Scavenge ? " (scavenge: shortest is " + ScOutposts.Shortest() + ")" : ""));
            }
            int working = 0;
            foreach (Outpost o in ScOutposts.All) if (o.State == OutpostState.Active || o.State == OutpostState.Contested) working++;
            Output("[SC] network tonight: " + ScOutposts.YieldText(total) + "; upkeep " + (ScConfig.OutpostUpkeep * working).ToString("0.##") + " mats"
                   + ", haulage about " + (ScConfig.OutpostHaulage * (total[0] + total[1] + total[2] + total[3] + total[4])).ToString("0.##") + " fuel"
                   + (ScOutposts.RadioOnlineAnywhere() ? "" : "; the radio is silent - deliveries will wait"));
        }

        private static void RadioCmd()
        {
            Community c = ScRegistry.Community;
            PowerManager pm = PowerManager.Instance;
            Output("[SC] radio: online(loaded)=" + ScRadio.Online() + " online(anywhere)=" + ScOutposts.RadioOnlineAnywhere()
                   + " last reported day " + c.RadioOnlineDay + " (today " + ScCommunity.Today + "), " + c.Radios.Count + " remembered, " + ScRadio.Radios.Count + " seen this session");
            var all = new HashSet<Vector3i>(c.Radios);
            foreach (Vector3i v in ScRadio.Radios) all.Add(v);
            foreach (Vector3i v in all)
            {
                PowerItem item = pm != null ? pm.GetPowerItemByWorldPos(v) : null;
                Output("[SC]   " + v + (c.Radios.Contains(v) ? " remembered" : " session-only") + (ScZone.Contains(v) ? ", in the home zone" : ", OUTSIDE the home zone")
                       + ", power item " + (item == null ? "none (not wired?)" : (item.IsPowered ? "POWERED" : "unpowered") + " (" + item.GetType().Name + ")")
                       + (ScZone.IsLoaded(v) ? ", loaded" : ", unloaded"));
            }
        }

        private static void Distress(List<string> p)
        {
            if (p.Count < 2) { Output("[SC] distress <id|name> - forces a radio distress call on a survivor who is out on a run"); return; }
            Member m = FindMember(p[1]);
            if (m == null) { Output("[SC] no member '" + p[1] + "'"); return; }
            if (m.Status != MemberStatus.OnRun) { Output("[SC] " + m.Name + " is not on a supply run - send them first"); return; }
            if (m.RunDistress) { Output("[SC] " + m.Name + " already called for help from " + m.RunPoiName); return; }
            World w = ScCommunity.World;
            if (ScRunners.IsPhysical(m) && m.RunLeg != RunLeg.AtSite) { Output("[SC] " + m.Name + " is on a physical run and calls for help only at the site (" + m.RunLeg + " now; sc run " + m.Name + " skip)"); return; }
            bool ok = ScRadio.ForceDistress(m, ScCommunity.LocalPlayer, w != null ? w.GetGameRandom() : null);
            if (ok && ScRunners.IsPhysical(m) && w != null) ScRunners.BeginLeg(m, RunLeg.AtSite, ScRunners.C(m.RunSite), ScRunners.C(m.RunSite), w.worldTime);   // held for RunDistressHours
            if (ok) ScRegistry.SaveAtomic();
            Output("[SC] " + (ok ? m.Name + " radios for help from " + m.RunPoiName + " " + m.RunPoi : "no POI in range or no radio online (radios seen: " + ScRadio.Radios.Count + ", online " + ScRadio.Online() + ")"));
        }

        private static void Scan()
        {
            bool ok = ScScan.Run(true);
            Community c = ScRegistry.Community;
            Output("[SC] scan " + (ok ? "done" : "skipped (no home / world)") + ": beds " + c.Beds + ", facilities " + c.Facilities + ", crops " + c.Crops);
            if (ok)
                Output("[SC]   " + ScScan.LastChunks + " zone chunk(s), " + ScScan.LastLoaded + " loaded now, " + ScScan.LastMissing + " never counted, "
                       + ScScan.LastMs.ToString("0.00") + " ms; block types: " + ScScan.BedTypes + " bed, " + ScScan.FacilityTypes + " facility, " + ScScan.CropTypes + " crop");
        }

        /// <summary>0-XNPCCore 3.2 checks: the billing guard's counters, and the body hooks that met one of its own NPCs.</summary>
        /// <summary>
        /// sc xnpc: the billing guard's counters, the body hooks that stepped aside for a player-body NPC that is not one of
        /// ours (0-XNPCCore's own classes: since M10 round 2 SC's own bodies never show up there), the
        /// Patches_Xnpc hit counters, the ammo resupply and swap-return counters (ScAmmo, ScArms) and the loaded members
        /// with a hire contract. sc xnpc &lt;id|name&gt;: one NPC in detail - the migration's main probe, since DevLink's
        /// cvar command only reads the player's cvars.
        /// </summary>
        /// <summary>sc trust &lt;id|name&gt; [forgive]: where a survivor stands with you (ScTrust); forgive clears a grudge (testing).</summary>
        private static void TrustCmd(List<string> p)
        {
            EntityPlayerLocal lp = ScCommunity.LocalPlayer;
            if (lp == null) { Output("[SC] no local player"); return; }
            if (p.Count < 2) { Output("[SC] usage: sc trust <id|name> [forgive] - " + ScTrust.Report()); return; }
            EntityAlive e = FindBodyNpc(p[1]);
            if (e == null) { Output("[SC] no loaded NPC '" + p[1] + "'"); return; }
            if (!ScTrust.Subject(e)) { Output("[SC] " + e.EntityName + " (" + e.entityId + ") has no trust: a member, stand-in, hired companion, raider, trader or not a survivor"); return; }
            if (p.Count >= 3 && p[2].Equals("forgive", StringComparison.OrdinalIgnoreCase))
            {
                Output("[SC] " + (ScTrust.Forgive(e, lp) ? e.EntityName + " forgives you (neutral again)" : e.EntityName + " held no grudge"));
                return;
            }
            Output("[SC] " + e.EntityName + " (" + e.entityId + "): " + ScTrust.Level(e, lp) + ", hostile to anyone " + ScTrust.IsHostileToAny(e)
                   + ", you can melee " + EntityTargetingUtilities.CanDamage(lp, e) + ", enemy of you " + EntityTargetingUtilities.IsEnemy(e, lp));
        }

        private static void XnpcCmd(List<string> p)
        {
            if (p.Count >= 2)
            {
                EntityAlive e = FindBodyNpc(p[1]);
                if (e == null) { Output("[SC] no loaded NPC '" + p[1] + "'"); return; }
                Output(XnpcDescribe(e));
                return;
            }
            Output("[SC] voice control: " + (Patches_VoiceControl.ChatType != null ? "loaded" : "not loaded") + " - intercepted: " + Patches_VoiceControl.Report());
            Output("[SC] body hooks that met another player-body NPC (0-XNPCCore's own classes) and stepped aside: " + Patches_Sdcs.ForeignReport());
            Output("[SC] 0-XNPCCore survivor hooks (Patches_Xnpc): " + Patches_Xnpc.Report());
            if (ScRegistry.Loaded) Output("[SC] ammo (D3): " + ScAmmo.Report());
            Output("[SC] weapons: " + ScArms.Report());
            Output("[SC] melee: " + ScMelee.Report());
            Output("[SC] shooting skill: " + ScShooting.Report());
            Output("[SC] get-ups: " + Patches_Sdcs.GetUp.Report());
            Output("[SC] relaxed (arms down, weapon hidden): " + ScRelax.Report());
            World w = GameManager.Instance != null ? GameManager.Instance.World : null;
            if (w == null) return;
            int ours = 0, contracts = 0, theirs = 0, managed = 0, raiders = 0;
            for (int i = 0; i < w.Entities.list.Count; i++)
            {
                EntityAlive a = w.Entities.list[i] as EntityAlive;
                if (a == null || a.Buffs == null || a is EntityPlayer) continue;
                if (a.emodel is EModelSDCS) { if (ScXnpc.IsRaider(a)) raiders++; else if (ScXnpc.IsBody(a)) managed++; else theirs++; }
                if (!Patches_VoiceControl.IsCommunityNpc(a)) continue;
                ours++;
                if (!a.Buffs.HasCustomVar("RetainerNextDueDay") && !a.Buffs.HasCustomVar("RetainerWeek")) continue;
                contracts++;
                Output("[SC]   hire contract on " + a.EntityName + " (" + a.entityId + ") - stripped at the next relink");
            }
            Output("[SC] loaded: " + ours + " member(s)/stand-in(s), " + contracts + " with a hire contract; " + managed
                   + " 0-XNPCCore survivor(s) of the community's classes, " + raiders + " raider(s) (sc raiders), " + theirs + " other player-body NPC(s) (0-XNPCCore's other classes)");
        }

        private static readonly string[] XnpcLevelCvars = { "SdcsNpcLevel", "SdcsLevelHealth", "SdcsLevelDamage", "SdcsLevelSpread", "SdcsLevelRegen" };
        private static readonly string[] XnpcBuffs = { "SdcsNpcLevelBonus", "SdcsNoWear", "SdcsStatusCheck", "SdcsIsHealing", "IsHealing" };
        private static readonly string[] XnpcOrderCvars = { "CurrentOrder", "Guarding", "Leader", "Owner", "vcEngage", "vcFollowMin", "vcFormationDist", "vcFormationAngle", "CrouchOverride" };

        /// <summary>One NPC as the XNPCCore migration sees it (design section 1.4).</summary>
        private static string XnpcDescribe(EntityAlive e)
        {
            var sb = new StringBuilder();
            string cls = ScCommunity.ClassName(e);
            Member m = ScRegistry.Loaded ? ScRegistry.GetMember(e.entityId) : null;
            Prospect pr = ScRegistry.Loaded ? ScRegistry.GetProspect(e.entityId) : null;
            sb.Append("[SC] ").Append(ScCommunity.DisplayName(e)).Append(" id ").Append(e.entityId).Append(' ').Append(e.GetType().Name)
              .Append(" class ").Append(cls).Append(" managed=").Append(ScXnpc.IsManagedClass(e.entityClass)).Append(" xnpcBody=").Append(ScXnpc.IsBody(e))
              .Append(" side=").Append(ScXnpc.IsRaiderClass(e.entityClass) ? "raider" : ScXnpc.IsSurvivorClass(e.entityClass) ? "survivor" : "-")
              .Append(" model=").Append(e.emodel != null ? e.emodel.GetType().Name : "null");
            sb.Append("\n  hostility: ").Append(Hostility(e));
            sb.Append("\n  uai: ").Append(UaiProbe(e));
            // the M11 fix 2 (STUCK_DIAG 5c): the move helper, the path and the path finder; and a member's post
            sb.Append("\n  nav: ").Append(ScCommunity.NavLine(e));
            if (m != null && m.Post != Vector3i.zero) sb.Append("\n  post: ").Append(ScCommunity.PostReport(m, e));
            sb.Append("\n  record: ").Append(m != null ? "member " + m.Name + " (" + m.Status + ", class " + m.Class + ")" : (pr != null ? "prospect (class " + pr.Class + ")" : "none"))
              .Append(" community=").Append(ScXnpc.IsCommunity(e));
            // looks and names
            EModelSDCS em = e.emodel as EModelSDCS;
            Archetype arch = em != null ? em.Archetype : null;
            sb.Append("\n  look: source=").Append(ScLooks.SourceOf(e.entityId)).Append(" archetype=").Append(arch != null ? arch.Name + " (" + arch.Sex + ")" : "-")
              .Append(" isMale=").Append(e.IsMale).Append(" - ").Append(ScLooks.Describe(ScLooks.Get(e)));
            sb.Append("\n  worn:");
            for (int slot = 0; slot < 4; slot++)
            {
                ItemValue iv = e.equipment != null ? e.equipment.GetSlotItem(slot) : null;
                // name:quality, then the wear (UseTimes of MaxUseTimes) so armor damage can be watched
                sb.Append(' ').Append(iv != null && !iv.IsEmpty() && iv.ItemClass != null
                    ? iv.ItemClass.GetItemName() + ":" + iv.Quality + " (" + iv.UseTimes.ToString("0.#") + "/" + iv.MaxUseTimesBase + ")" : "-");
            }
            EntityAliveSDXV4 v4 = e as EntityAliveSDXV4;
            sb.Append("\n  name: FirstName=").Append(v4 != null ? v4.FirstName : "-").Append(" display=").Append(ScCommunity.DisplayName(e))
              .Append(" registry=").Append(m != null ? m.Name : "-");
            // weapons
            var enter = new List<string>();
            if (e.itemsOnEnterGame != null)
                foreach (ItemStack st in e.itemsOnEnterGame)
                    if (st != null && !st.IsEmpty() && st.itemValue.ItemClass != null) enter.Add(st.itemValue.ItemClass.GetItemName() + "x" + st.count);
            ItemClass held = e.inventory != null ? e.inventory.holdingItem : null;
            sb.Append("\n  weapons: itemsOnEnterGame=[").Append(string.Join(", ", enter.ToArray())).Append("] held=").Append(held != null ? held.GetItemName() : "-")
              .Append(" _currentWeapon=").Append(ScNpc.CurrentWeapon(e) ?? "-").Append(" _defaultWeapon=").Append(ScNpc.DefaultWeapon(e) ?? "-");
            if (e.Buffs != null)
            {
                Loadout pair = ScLoadout.Current(e);
                sb.Append(" sc_loadout_r/m=").Append(e.Buffs.GetCustomVar(ScLoadout.RangedCvar)).Append('/').Append(e.Buffs.GetCustomVar(ScLoadout.MeleeCvar))
                  .Append(" pair=").Append(pair != null ? pair.ToString() : "-")
                  .Append(" record=").Append(m != null && m.Loadout != null ? m.Loadout.ToString() : "-")
                  .Append(" ammoSeeded=").Append(e.Buffs.GetCustomVar(ScLoadout.AmmoSeededCvar) > 0f)
                  .Append(" CurrentWeaponClass=").Append(e.Buffs.HasCustomVar("CurrentWeaponClass") ? e.Buffs.GetCustomVar("CurrentWeaponClass").ToString() : "-")
                  .Append(" SdcsReloading=").Append(e.Buffs.GetCustomVar("SdcsReloading"))
                  .Append(" SdcsLastMelee=").Append(e.Buffs.HasCustomVar("SdcsLastMelee") ? ItemName((int)e.Buffs.GetCustomVar("SdcsLastMelee")) : "-");
            }
            sb.Append("\n  ammo: ").Append(XnpcAmmo(e, held));
            if (ScRegistry.Loaded) sb.Append("\n  resupply: ").Append(ScAmmo.Describe(e));
            sb.Append("\n  overwhelmed: ").Append(ScArms.Describe(e));
            sb.Append("\n  ").Append(ScRelax.Describe(e));
            // XNPC kill levels and buffs
            if (e.Buffs != null)
            {
                sb.Append("\n  levels:");
                foreach (string c in XnpcLevelCvars) sb.Append(' ').Append(c).Append('=').Append(e.Buffs.HasCustomVar(c) ? e.Buffs.GetCustomVar(c).ToString("0.###") : "-");
                sb.Append(" Progression=").Append(e.Progression != null ? "level " + e.Progression.Level : "null");
                sb.Append("\n  buffs:");
                foreach (string b in XnpcBuffs) sb.Append(' ').Append(b).Append('=').Append(e.Buffs.HasBuff(b));
                sb.Append("\n  orders:");
                foreach (string c in XnpcOrderCvars) sb.Append(' ').Append(c).Append('=').Append(e.Buffs.HasCustomVar(c) ? e.Buffs.GetCustomVar(c).ToString("0.##") : "-");
                bool retainer = false;
                foreach (string c in Patches_VoiceControl.ContractCvars) if (e.Buffs.HasCustomVar(c)) retainer = true;
                sb.Append(" Retainer*=").Append(retainer ? "yes" : "none");
                // migration M2: how the community reads the order (a follower's voice order is a member state)
                sb.Append("\n  member state: ").Append(ScOrders.Describe(e));
                if (m != null)
                {
                    string word = ScOrders.StateWord(m);
                    sb.Append(" status=").Append(m.Status).Append(" heldOrder=").Append(m.HeldOrder).Append(word.Length > 0 ? " (" + word + ")" : "")
                      .Append(" followPending=").Append(m.FollowIssued > 0f);
                }
                sb.Append(" hiredBy=").Append(ScCommunity.HiredBy(e)).Append(" dynamicFollow=").Append(ScXnpc.HasDynamicFollow(e))
                  .Append(" ourDoorOpener=").Append(ScDoors.UsesOurOpener(e));
            }
            sb.Append("\n  move: onMission=").Append(ScNpc.IsOnMission(e)).Append(" moveSpeed=").Append(e.moveSpeed.ToString("0.##"))
              .Append(" moveSpeedPanic=").Append(e.moveSpeedPanic.ToString("0.##")).Append(" GetMoveSpeedPanic()=").Append(e.GetMoveSpeedPanic().ToString("0.##"))
              .Append(" passiveSpeedMod=").Append(e.GetPassiveEffectSpeedModifier().ToString("0.##")).Append(" running=").Append(e.MovementRunning);
            if (ScNpc.IsV4(e))   // migration M4.1: what a runner hands FindPath for RunnerRunMps / RunnerWalkMps
                sb.Append(" runnerPath run/walk=").Append(ScXnpc.PathSpeed(ScConfig.RunnerRunMps, true).ToString("0.##"))
                  .Append('/').Append(ScXnpc.PathSpeed(ScConfig.RunnerWalkMps, false).ToString("0.##"));
            sb.Append("\n  medic: ").Append(ScMedic.Describe(e));
            sb.Append("\n  hooks: ").Append(Patches_Xnpc.Report());
            return sb.ToString();
        }

        /// <summary>
        /// One NPC's side of a fight, for sc xnpc &lt;id&gt; (M10 probe P1): its faction, the class's enemy flag, whether
        /// SCore's targeting makes it an enemy of the local player and lets the player hurt it, its attack and revenge
        /// targets, whether it may be hired, its vanilla door flag, the no_voice tag and its AI packages.
        /// </summary>
        private static string Hostility(EntityAlive e)
        {
            var sb = new StringBuilder();
            try
            {
                Faction f = FactionManager.Instance != null ? FactionManager.Instance.GetFaction(e.factionId) : null;
                EntityClass ec;
                bool enemyEntity = EntityClass.list.TryGetValue(e.entityClass, out ec) && ec != null && ec.bIsEnemyEntity;
                sb.Append("faction=").Append(f != null ? f.Name : e.factionId.ToString()).Append(" IsEnemyEntity=").Append(enemyEntity);
                EntityPlayerLocal p = ScCommunity.LocalPlayer;
                if (p != null)
                    sb.Append(" enemyOfYou=").Append(EntityTargetingUtilities.IsEnemy(e, p))
                      .Append(" youCanHurt=").Append(EntityTargetingUtilities.CanTakeDamage(e, p))
                      .Append(" youCanMelee=").Append(EntityTargetingUtilities.CanDamage(p, e))
                      .Append(" trust=").Append(ScTrust.Subject(e) ? ScTrust.Level(e, p).ToString() : "n/a");
                sb.Append(" target=").Append(Who(e.GetAttackTarget())).Append(" revenge=").Append(Who(e.GetRevengeTarget()))
                  .Append(" hirable=").Append(ScNpc.IsHirable(e)).Append(" canOpenDoors=").Append(e.moveHelper != null && e.moveHelper.CanOpenDoors)
                  .Append(" lead=").Append(e.Buffs != null ? Loadout.LeadName((int)e.Buffs.GetCustomVar(ScLoadout.LeadCvar)) : "-")
                  .Append(" (").Append(ScArms.LeadState(e)).Append(')')
                  .Append(" no_voice=").Append(e.HasAnyTags(FastTags<TagGroup.Global>.Parse("no_voice")))
                  .Append(" pkgs=[").Append(e.AIPackages != null ? string.Join(",", e.AIPackages.ToArray()) : "").Append(']');
            }
            catch (Exception ex)
            {
                ScLog.Once("console-hostility", ex);
                sb.Append(" (failed: ").Append(ex.Message).Append(')');
            }
            return sb.ToString();
        }

        /// <summary>
        /// sc xnpc &lt;id&gt;'s uai: line (M11 fix; GUARD_DIAG cause 3, a resident that never fired): what SCore's utility AI
        /// runs now - the action, its task and its target - and, for the foe (that target when it is an enemy, else the
        /// attack or revenge target, else the nearest enemy within the see distance), whether it is in the AI's candidate
        /// box at all, and the two questions the ranged attack asks: CanSeeTarget (SCoreUtils.CanSee) and InWeaponRange
        /// (the weapon's range and SCoreUtils.CanShoot, the clear-shot ray). Then what each of those two rays hits first
        /// and how far the local player stands from the line - a player in the line fails CanSee while CanShoot passes -
        /// and the base defence's verdict on that foe (UAIConsiderationScIntruder.Why), what it would do about it (ScEngage:
        /// approach, hold, or hand over to the pack's attack, and why) and the resident's own distance from its post
        /// against the stray limit. Main thread. Read-only but for what SCore's CanSee itself does on success: it puts the
        /// foe in the NPC's see cache, as the AI's own CanSeeTarget does with the same answer every 0.2 s.
        /// </summary>
        private static string UaiProbe(EntityAlive e)
        {
            var sb = new StringBuilder();
            try
            {
                UAI.Context ctx = e.utilityAIContext;
                if (ctx == null) return "no utility AI context";
                UAI.UAIAction act = ctx.ActionData.Action;
                UAI.UAITaskBase task = ctx.ActionData.CurrentTask;
                object target = ctx.ActionData.Target;
                EntityAlive ta = target as EntityAlive;
                sb.Append("action=").Append(act != null ? act.Name : "-").Append(" task=").Append(task != null ? task.GetType().Name : "-")
                  .Append(ctx.ActionData.Started ? " started" : "").Append(ctx.ActionData.Executing ? " executing" : "").Append(" target=");
                if (ta != null) sb.Append(ta == e ? "self" : Who(ta) + " at " + e.GetDistance(ta).ToString("0.#") + " m");
                else if (target is Vector3) sb.Append(((Vector3)target).ToString("0.#"));
                else sb.Append('-');

                string from;
                EntityAlive foe = ProbeFoe(e, ta, out from);
                if (foe == null)
                {
                    sb.Append("; no foe (no enemy target and none within ").Append(e.GetSeeDistance().ToString("0")).Append(" m)");
                    return sb.ToString();
                }
                float dist = e.GetDistance(foe);
                // SCore's candidate box (UAIBase_considerAction): the NPC's bounds grown by its see distance - Bounds.Expand
                // adds half on each side - tested against the other entity's bounds, as World.GetEntitiesInBounds does
                Bounds box = e.boundingBox;
                box.Expand(e.GetSeeDistance());
                sb.Append("; foe ").Append(Who(foe)).Append(' ').Append(foe.EntityName).Append(" (").Append(from).Append(") at ")
                  .Append(dist.ToString("0.#")).Append(" m").Append(box.Intersects(foe.boundingBox) ? "" : " OUTSIDE the AI's search box")
                  .Append(": CanSee=").Append(UAI.SCoreUtils.CanSee(e, foe));
                // InWeaponRange as SCore computes it for action 0 (fire on a player gun, the swing on a melee weapon)
                ItemClass held = e.inventory != null ? e.inventory.holdingItem : null;
                ItemAction a0 = held != null && held.Actions != null && held.Actions.Length > 0 ? held.Actions[0] : null;
                ItemActionRanged ranged = a0 as ItemActionRanged;
                ItemInventoryData hd = e.inventory != null ? e.inventory.holdingItemData : null;
                ItemActionRanged.ItemActionDataRanged rd = ranged != null && hd != null && hd.actionData != null && hd.actionData.Count > 0
                    ? hd.actionData[0] as ItemActionRanged.ItemActionDataRanged : null;
                if (ranged != null && rd != null)
                {
                    float range = ranged.GetRange(rd);
                    float engage = ScShooting.CapRange(e, range, foe);
                    bool clear = UAI.SCoreUtils.CanShoot(e, foe, range);
                    sb.Append(" clearShot=").Append(clear).Append(" range=").Append(range.ToString("0.#"))
                      .Append(engage < range ? " engage=" + engage.ToString("0.#") : ScShooting.ReturnsFire(e, foe) ? " (returning fire)" : "")
                      .Append(" InWeaponRange=").Append(clear && dist <= engage)
                      .Append(" shotRay=").Append(FirstHit(e, foe, range, -1)).Append(" (").Append(ScShooting.Describe(e)).Append(')');
                }
                else
                {
                    // the reach SCore reads (the action's Range field, 1.5 m when 0) and the one SC gives it (ScMelee, the M11 fix 2)
                    float scoreReach = a0 != null && a0.Range != 0f ? a0.Range : ScMelee.SCoreFallbackReach;
                    sb.Append(' ').Append(ScMelee.Describe(e, dist, scoreReach));
                }
                sb.Append(" seeRay=").Append(FirstHit(e, foe, e.GetSeeDistance(), UAI.SCoreUtils.GetHitMaskByWeaponBuff(e)));
                EntityPlayerLocal p = ScCommunity.LocalPlayer;
                if (p != null) sb.Append(" player ").Append(PlayerFromLine(e, foe, p));
                string why = UAI.UAIConsiderationScIntruder.Why(e, foe);
                float rest = UAI.ScDefendState.RestLeft(e.entityId);
                float fromPost = UAI.UAIConsiderationScIntruder.FromPost(e);
                string engageWhy;
                UAI.ScEngage.Verdict verdict = UAI.ScEngage.Classify(e, foe, out engageWhy);
                sb.Append("; defend (radius ").Append(ScConfig.DefendBaseRadius).Append(" m): ").Append(why ?? "intruder")
                  .Append(rest > 0f ? " (rests " + rest.ToString("0") + " s more)" : "")
                  .Append(", engage=").Append(verdict == UAI.ScEngage.Verdict.Approach ? "approach (ScDefendBase)"
                                              : verdict == UAI.ScEngage.Verdict.Hold ? "hold (ScDefendHold)" : "hand over (the pack's attack)")
                  .Append(" - ").Append(engageWhy);
                if (fromPost >= 0f)
                {
                    sb.Append("; ").Append(fromPost.ToString("0.#")).Append(" m from its post");
                    if (UAI.UAIConsiderationScIntruder.Roams(ScRegistry.GetMember(e.entityId)))
                    {
                        // zone roaming (round hz): the stray limit is the zone + ResidentRoamZoneMargin - StrayMarginInside
                        float lim = Math.Max(0f, ScConfig.ResidentRoamZoneMargin - UAI.UAIConsiderationScIntruder.StrayMarginInside);
                        float outside = ScZone.DistanceOutside(e.position);
                        sb.Append(outside > lim ? " - BEYOND the stray limit, the zone + " + lim.ToString("0") + " m (the defence pauses)"
                                                : " (zone roaming: the stray limit is the zone + " + lim.ToString("0") + " m)");
                    }
                    else sb.Append(fromPost > UAI.UAIConsiderationScIntruder.StrayLimit ? " - BEYOND the " + UAI.UAIConsiderationScIntruder.StrayLimit.ToString("0") + " m stray limit (the defence pauses)" : "");
                }
            }
            catch (Exception ex)
            {
                ScLog.Once("console-uai", ex);
                sb.Append(" (failed: ").Append(ex.Message).Append(')');
            }
            return sb.ToString();
        }

        /// <summary>The probe's foe: the UAI target if it is an enemy, else the attack target, else the revenge target, else the nearest enemy in sight range.</summary>
        private static EntityAlive ProbeFoe(EntityAlive e, EntityAlive uaiTarget, out string from)
        {
            from = "uai target";
            if (uaiTarget != null && uaiTarget != e && !uaiTarget.IsDead() && EntityTargetingUtilities.IsEnemy(e, uaiTarget)) return uaiTarget;
            from = "attack target";
            EntityAlive t = e.GetAttackTarget();
            if (t != null && !t.IsDead()) return t;
            from = "revenge target";
            t = e.GetRevengeTarget();
            if (t != null && !t.IsDead()) return t;
            from = "nearest enemy";
            float see = e.GetSeeDistance(), best = float.MaxValue;
            EntityAlive found = null;
            var list = new List<Entity>();
            e.world.GetEntitiesInBounds(typeof(EntityAlive), new Bounds(e.position, new Vector3(2f * see, 2f * see, 2f * see)), list);
            foreach (Entity en in list)
            {
                EntityAlive a = en as EntityAlive;
                if (a == null || a == e || a.IsDead() || !EntityTargetingUtilities.IsEnemy(e, a)) continue;
                float d = (a.position - e.position).sqrMagnitude;
                if (d < best) { best = d; found = a; }
            }
            return found;
        }

        /// <summary>
        /// What a ray from e's head to t's head hits first, cast as SCore casts it: hitMask -1 = CanShoot's ray
        /// (Voxel.Raycast with transparent and non-collidable blocks: glass, cobwebs and plants stop it), otherwise
        /// CanSee's ray with that hit mask (it passes those). "target" is what both want; a block, the player or another
        /// entity in front of it fails the test (CanShoot passes on any entity, CanSee only on the target).
        /// </summary>
        private static string FirstHit(EntityAlive e, EntityAlive t, float length, int hitMask)
        {
            Vector3 from = e.getHeadPosition();
            Vector3 dir = t.getHeadPosition() - from;
            Ray ray = new Ray(from, dir);
            ray.origin += dir.normalized * 0.2f;
            bool hit = hitMask < 0 ? Voxel.Raycast(e.world, ray, length, true, true) : Voxel.Raycast(e.world, ray, length, hitMask, 0f);
            WorldRayHitInfo info = Voxel.voxelRayHitInfo;
            if (!hit || info == null) return "nothing";
            if (GameUtils.IsBlockOrTerrain(info.tag))
            {
                BlockValue bv = e.world.GetBlock(info.hit.blockPos);
                return "block " + (bv.Block != null ? bv.Block.GetBlockName() : "?") + " at " + info.hit.blockPos;
            }
            Transform root = info.transform != null ? GameUtils.GetHitRootTransform(info.tag, info.transform) : null;
            EntityAlive who = root != null ? root.GetComponent<EntityAlive>() : null;
            if (who == null) return "other (" + info.tag + ")";
            if (who == t) return "target";
            if (who == e) return "self";
            return (who is EntityPlayer ? "PLAYER " : "entity ") + who.entityId;
        }

        /// <summary>How far the player's chest is from the head-to-head line between e and t, and whether it is between them.</summary>
        private static string PlayerFromLine(EntityAlive e, EntityAlive t, EntityPlayer p)
        {
            Vector3 a = e.getHeadPosition(), b = t.getHeadPosition(), c = p.getChestPosition();
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float k = len2 > 0.0001f ? Vector3.Dot(c - a, ab) / len2 : 0f;
            if (k <= 0f || k >= 1f) return "not between them";
            float off = (a + ab * k - c).magnitude;
            return off.ToString("0.0") + " m from the line, between them" + (off < 0.6f ? " (IN THE LINE)" : "");
        }

        /// <summary>A target for the console: "-", an entity id, or "player &lt;id&gt;".</summary>
        private static string Who(EntityAlive a)
        {
            return a == null ? "-" : (a is EntityPlayer ? "player " : "") + a.entityId;
        }

        /// <summary>
        /// sc raiders [r] - every living raider (SC's raider classes on XNPC's body, M10) within r metres (40): id, name,
        /// class, its pair and lead, the rounds left for its gun (bag plus magazine, and with Locked &amp; Loaded its magazines
        /// and how many, M11; never resupplied), its XNPC level, its
        /// attack and revenge targets, vanilla's door flag, what it holds, the lead rule's state, its health, distance and
        /// speed, what killing it pays (kill XP from the pair, round 3) and its map marker (sleeper or not). The M10 probes
        /// read this line.
        /// </summary>
        private static void RaidersCmd(List<string> p)
        {
            World w = ScCommunity.World;
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            if (w == null || player == null) { Output("[SC] no world / player"); return; }
            float r = 40f;
            if (p.Count >= 2) float.TryParse(p[1], out r);
            r = Mathf.Clamp(r, 2f, 400f);
            var list = new List<Entity>();
            // both of SCore's NPC classes derive from EntityTrader; IsRaider keeps only ours
            w.GetEntitiesInBounds(typeof(EntityTrader), new Bounds(player.position, new Vector3(2f * r, 2f * r, 2f * r)), list);
            int n = 0, dead = 0;
            foreach (Entity en in list)
            {
                EntityAlive e = en as EntityAlive;
                if (e == null || !ScXnpc.IsRaider(e)) continue;
                float dist = (e.position - player.position).magnitude;
                if (dist > r) continue;
                if (e.IsDead()) { dead++; continue; }
                n++;
                Loadout l = ScLoadout.Current(e);
                ItemClass gun = ScAmmo.PairGun(e);
                ItemValue held = e.inventory != null ? e.inventory.holdingItemItemValue : null;
                string level = e.Buffs != null && e.Buffs.HasCustomVar("SdcsNpcLevel") ? e.Buffs.GetCustomVar("SdcsNpcLevel").ToString("0") : "-";
                string mags = gun != null ? ScMags.Describe(e, gun) : "";
                Output("[SC] " + e.entityId + " " + ScCommunity.DisplayName(e) + " (" + ScCommunity.ClassName(e) + ") "
                       + (l != null ? l.ToString() : "no pair")
                       + " rounds " + (gun != null ? ScAmmo.Rounds(e, gun).ToString() : "-") + (mags.Length > 0 ? " (" + mags + ")" : "")
                       + " level " + level + " target " + Who(e.GetAttackTarget()) + " revenge " + Who(e.GetRevengeTarget())
                       + " canOpenDoors " + (e.moveHelper != null && e.moveHelper.CanOpenDoors)
                       + " holding " + (held != null && !held.IsEmpty() && held.ItemClass != null ? held.ItemClass.GetItemName() : "nothing")
                       + " " + ScArms.LeadState(e) + " hp " + e.Health + "/" + e.GetMaxHealth() + " at " + dist.ToString("0.#") + " m speed " + e.GetVelocityPerSecond().magnitude.ToString("0.0") + " m/s"
                       + " killXp " + ScLoadout.KillXp(e) + " (" + ScLoadout.RaiderXpCvar + " " + (e.Buffs != null ? e.Buffs.GetCustomVar(ScLoadout.RaiderXpCvar).ToString("0.##") : "-") + ")"
                       + " marker " + (e.NavObject != null ? "on" : "off") + (e.IsSleeper ? " (sleeper)" : ""));
            }
            Output("[SC] " + n + " raider(s) within " + r + " m" + (dead > 0 ? " (" + dead + " dead)" : ""));
        }

        /// <summary>An item type id as its name (XNPC keeps SdcsLastMelee as one), or the number when it is no item.</summary>
        private static string ItemName(int type)
        {
            ItemClass ic = type > 0 ? ItemClass.GetForId(type) : null;
            return ic != null ? ic.GetItemName() : type.ToString();
        }

        /// <summary>
        /// For the held ranged weapon: the magazine, Locked &amp; Loaded's magazines for it (M11: how many, and their rounds
        /// with the gun's own - NpcMagazines.Rounds), and the reserve of each of its ammo types in every store.
        /// </summary>
        private static string XnpcAmmo(EntityAlive e, ItemClass held)
        {
            ItemActionRanged ranged = null;
            if (held != null && held.Actions != null)
                foreach (ItemAction a in held.Actions) { ranged = a as ItemActionRanged; if (ranged != null) break; }
            if (ranged == null || ranged.MagazineItemNames == null) return "not holding a ranged weapon";
            var sb = new StringBuilder();
            ItemValue hv = e.inventory.holdingItemItemValue;
            sb.Append("magazine ").Append(hv != null ? hv.Meta : 0);
            if (ScMags.Active)
            {
                string mags = ScMags.Describe(e, held);
                sb.Append("; Locked & Loaded: ").Append(mags.Length > 0 ? mags + " with the gun's own" : "no magazines (reloads from loose rounds)");
            }
            ItemStack[] window = HarvestManager.Has(e.entityId) ? ScNpc.Items(e) : null;
#if SC_V34
            // 3.4: the bag's and the toolbelt's grids (read only)
            ItemStack[] bag = e.bag != null ? e.bag.ItemGrid.items : null;
            ItemStack[] belt = e.inventory != null ? e.inventory.ItemGrid.items : null;
#else
            ItemStack[] bag = e.bag != null ? e.bag.GetSlots() : null;
            ItemStack[] belt = e.inventory != null ? e.inventory.GetSlots() : null;
#endif
            foreach (string name in ranged.MagazineItemNames)
            {
                ItemValue ammo = ItemClass.GetItem(name, true);
                if (ammo == null || ammo.IsEmpty()) continue;
                sb.Append("; ").Append(name).Append(": window ").Append(CountOf(window, ammo.type)).Append(" bag ").Append(CountOf(bag, ammo.type))
                  .Append(" toolbelt ").Append(CountOf(belt, ammo.type));
            }
            return sb.ToString();
        }

        private static int CountOf(ItemStack[] slots, int type)
        {
            if (slots == null) return 0;
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != null && !slots[i].IsEmpty() && slots[i].itemValue.type == type) n += slots[i].count;
            return n;
        }

        private static void Relink()
        {
            World w = ScCommunity.World;
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (w == null || p == null) { Output("[SC] no world / player"); return; }
            ScTicker.Relink(w, p);
            Output("[SC] relinked");
        }

        /// <summary>
        /// sc repost &lt;member&gt; (the M11 fix 2): a loaded resident gets a new post, as the stuck watchdog's loop protection
        /// gives one (ScCommunity.Repost: the old post is not given back this session, no teleport - it walks there). For
        /// testing and as a manual cure for a post that does not work. Host only.
        /// </summary>
        private static void RepostCmd(List<string> p)
        {
            if (!ScCommunity.IsHost) { Output("[SC] host only"); return; }
            Member m = p.Count > 1 ? FindMember(p[1]) : null;
            if (m == null) { Output("[SC] usage: sc repost <member id|name> - gives a resident a new post"); return; }
            if (m.Status != MemberStatus.Resident) { Output("[SC] " + m.Name + " is " + m.Status + ", not a resident at a post"); return; }
            if (ScWatch.OnWatch(m)) { Output("[SC] " + m.Name + " is on watch at " + ScWatch.Label(m.WatchPost) + " - sc watch relieve first"); return; }
            EntityAlive npc = ScCommunity.FindNpc(m.Id);
            if (npc == null) { Output("[SC] " + m.Name + " is not loaded"); return; }
            Vector3i old = m.Post;
            bool moved = ScCommunity.Repost(m, npc, "sc repost", ScCommunity.StuckDiag(m, npc), false);
            Output("[SC] " + m.Name + (moved ? ": post " + old + " -> " + m.Post + " (" + ScCommunity.PostLine(m, npc) + ")" : " keeps the post " + old + ": no other spot with a real floor within PostRadius"));
        }

        private static void Home()
        {
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            bool ok = ScZone.EnsureHome(p);
            Output("[SC] home " + (ok ? ScZone.Home.ToString() : "<none - place a land claim>"));
        }

        private static void Dump()
        {
            Status();
            MembersCmd();
            Output("[SC] config: MaxMembers=" + ScConfig.MaxMembers + " MaxFollowers=" + ScConfig.MaxFollowers + " traits=" + ScConfig.Traits.Count
                   + " parsed=" + ScConfig.Parsed + " questAvailable=" + ScQuests.Available
                   + " residentPackage=" + UAI.UAIBase.AIPackages.ContainsKey(ScCommunity.ResidentPackage));
        }
    }
}
