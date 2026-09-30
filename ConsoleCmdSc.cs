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
        public override string getDescription() { return "SurvivorCommunity: status, members, day, morale <n>, give <food|meds|ammo|mats|fuel> <n>, xp <id|name> <cardio|wits|fighting|shooting|spec> <n>, injure <id|name>, scan, run <id|name> <res> | run <id|name> <recall|home|skip|offline>, runners, distress <id|name>, spawn [hostile] [male|female] [weapon] [Set] (sc weapons lists them), weapons, look <id|name>, gear <id|name> <Set|none>, anim <id|name>, portrait <id|name|all> | portrait frame [fov dist yaw eyelift aimlift], poi [near <r> | classify <prefab> | census], outpost [list | add [row] | remove <id> | yield | pending <id> <n> | reclassify <id> | state <id> <state> | post <id> <member> | recall <member> | upgrade <id> | level <id> <1-3> | mode <id> <normal|quiet|push> | security <id> | attack <id> [raid] | demand <id> | pay <id> | refuse <id> | repair <id>], scout <res>, barter <give> <get>, radio, restore <member>, relink, home, dump, save"; }
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
                    case "xp": Xp(_params); break;
                    case "injure": Injure(_params); break;
                    case "scan": Scan(); break;
                    case "run": Run(_params); break;
                    case "runners": RunnersCmd(); break;
                    case "distress": Distress(_params); break;
                    case "spawn": Spawn(_params); break;
                    case "weapons": Output("[SC] weapons: " + string.Join(", ", ScLooks.WeaponTokens().ToArray())); break;
                    case "look": LookCmd(_params); break;
                    case "gear": Gear(_params); break;
                    case "anim": Anim(_params); break;
                    case "portrait": PortraitCmd(_params); break;
                    case "poi": PoiCmd(_params); break;
                    case "outpost": case "outposts": OutpostCmd(_params); break;
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
                    case "relink": Relink(); break;
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
                   + " lastday=" + c.LastDay + " morale=" + c.Morale + " lowMoraleDays=" + c.LowMoraleDays + " hungry=" + c.Hungry
                   + " beds=" + c.Beds + " facilities=" + c.Facilities + " crops=" + c.Crops);
            var sb = new StringBuilder("[SC] stock:");
            for (int i = 0; i < 5; i++) sb.Append(' ').Append(ScNames.ResourceKeys[i]).Append('=').Append(c.Stock[i]);
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
                EntityAliveSDX npc = ScCommunity.FindNpc(m.Id);
                string live = npc == null ? (ScRunners.IsPhysical(m) && m.Offline ? "virtual at " + World.worldToBlockPos(ScRunners.Where(m)) : "unloaded") : ("at " + npc.GetBlockPosition() + " order " + EntityUtilities.GetCurrentOrder(m.Id)
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
                       + (npc != null ? " | melee+" + npc.Buffs.GetCustomVar("sc_dmgmelee") + " ranged+" + npc.Buffs.GetCustomVar("sc_dmgranged") + " speed+" + npc.Buffs.GetCustomVar("sc_speed") + " hp+" + npc.Buffs.GetCustomVar("sc_hp") + " maxhp " + npc.Stats.Health.Max + " buffs " + (npc.Buffs.HasBuff(ScSkills.BuffName) ? "skills " : "") + (npc.Buffs.HasBuff(ScSkills.HungryBuff) ? "hungry " : "") + (npc.Buffs.HasBuff(ScSkills.InjuredBuff) ? "injured " : "") + (npc.Buffs.GetCustomVar("$Timid") >= 1f ? "timid" : "") : ""));
            }
            foreach (var p in ScRegistry.Prospects.Values)
            {
                Output("[SC] prospect " + p.Id + " " + p.Name + " (" + p.Class + ") favour " + p.Favour
                       + (p.Favour == FavourKind.Poi ? " " + p.PoiName + " at " + p.PoiPos : " " + p.ItemCount + " " + p.ItemCategory) + (p.Done ? " DONE" : ""));
            }
            if (ScRegistry.Members.Count == 0 && ScRegistry.Prospects.Count == 0) Output("[SC] no members or prospects");
        }

        private static void Day()
        {
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (!ScCommunity.IsHost || p == null) { Output("[SC] host only"); return; }
            Community c = ScRegistry.Community;
            c.LastDay++;
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
            Output("[SC] lowMoraleDays=" + c.LowMoraleDays + " threat=" + c.ThreatMemberId + " deathPenalty=" + c.DeathPenalty);
        }

        private static void Give(List<string> p)
        {
            Resource r; int n;
            if (p.Count < 3 || !ScNames.TryResource(p[1], out r) || !int.TryParse(p[2], out n)) { Output("[SC] give <food|meds|ammo|mats|fuel> <n>"); return; }
            ScRegistry.Community.Add(r, n);
            ScRegistry.SaveAtomic();
            Output("[SC] " + r + " = " + ScRegistry.Community.Get(r));
        }

        /// <summary>sc spawn [hostile] [male|female] [weapon] [Set] (sc weapons lists them), weapons - a survivor wearing the player's body, in front of the player.</summary>
        private static void Spawn(List<string> p)
        {
            World w = ScCommunity.World;
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            if (w == null || player == null) { Output("[SC] no world / player"); return; }
            string sex = null, weapon = null, set = null;
            bool hostile = false;
            List<string> tokens = ScLooks.WeaponTokens();
            for (int i = 1; i < p.Count; i++)
            {
                string a = p[i].ToLowerInvariant();
                if (a == "male" || a == "female") { sex = a; continue; }
                if (a == "hostile" || a == "raider") { hostile = true; continue; }
                if (a == "ak47") a = "rifle";                         // the legacy AK-47 class is named Rifle
                string tok = tokens.Find(t => string.Equals(t, a, StringComparison.OrdinalIgnoreCase));
                if (tok == null)
                {
                    // a partial name ("sledge", "knuckles", "bow") picks among the weapons that contain it
                    List<string> partial = tokens.FindAll(t => t.IndexOf(a, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (partial.Count > 0) tok = partial[w.GetGameRandom().RandomRange(0, partial.Count)];
                }
                if (tok != null) weapon = tok;
                else set = p[i];
            }
            GameRandom rnd = w.GetGameRandom();
            string cls = ScLooks.PickFrom(hostile ? ScLooks.RaiderClasses() : ScLooks.SurvivorClasses(), rnd, sex, weapon, int.MaxValue);
            if (cls == null) { Output("[SC] no " + (hostile ? "scRaider" : "scSurvivor") + "* entity classes are loaded (Config/entityclasses.xml)"); return; }
            int cid = EntityClass.FromString(cls);
            Vector3 fwd = player.GetForwardVector(); fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 at = player.position + fwd * 3f;
            Vector3 pos = ScRadio.SpotNear(w, new Vector3i(Mathf.RoundToInt(at.x), Mathf.RoundToInt(at.y), Mathf.RoundToInt(at.z)), 3);
            Look look = ScLooks.Roll(ScLooks.ClassIsMale(cls), rnd, true);
            if (set != null && !ScLooks.WearSet(look, set, rnd)) { Output("[SC] no armor set '" + set + "' (armor" + set + "Helmet...)"); return; }
            EntityAlive npc;                                              // a raider is an EntityNPCBandit, not SDX
            try { ScLooks.Pending = look; npc = EntityFactory.CreateEntity(cid, pos) as EntityAlive; }
            finally { ScLooks.Pending = null; }
            if (npc == null) { Output("[SC] " + cls + " did not spawn as an EntityAlive"); return; }
            w.SpawnEntityInWorld(npc);
            npc.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
            if (npc is EntityAliveSDX) npc.Buffs.SetCustomVar("Persist", 1f);
            string shown = ScCommunity.DisplayName(npc);
            Output("[SC] spawned " + cls + " (" + ScLooks.WeaponOf(cls) + ") id " + npc.entityId + " '" + shown + "': " + ScLooks.Describe(look));
        }

        /// <summary>A loaded NPC by member id/name, entity id, or display-name substring (any survivor within 200 m).</summary>
        private static EntityAliveSDX FindSurvivor(string key)
        {
            Member m = FindMember(key);
            if (m != null) { EntityAliveSDX e = ScCommunity.FindNpc(m.Id); if (e != null) return e; }
            World w = ScCommunity.World;
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            if (w == null || player == null) return null;
            int id;
            if (int.TryParse(key, out id)) return w.GetEntity(id) as EntityAliveSDX;
            var list = new List<Entity>();
            w.GetEntitiesInBounds(typeof(EntityAliveSDX), new Bounds(player.position, new Vector3(400f, 400f, 400f)), list);
            for (int i = 0; i < list.Count; i++)
            {
                EntityAliveSDX s = list[i] as EntityAliveSDX;
                if (s != null && !s.IsDead() && ScCommunity.DisplayName(s).IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return s;
            }
            return null;
        }

        /// <summary>sc look <id|name> - a new face (same sex, same clothes) for a loaded survivor wearing the player's body.</summary>
        private static void LookCmd(List<string> p)
        {
            if (p.Count < 2) { Output("[SC] look <id|name>"); return; }
            EntityAliveSDX npc = FindSurvivor(p[1]);
            if (npc == null) { Output("[SC] no loaded NPC '" + p[1] + "'"); return; }
            if (!ScLooks.IsSurvivor(npc)) { Output("[SC] " + ScCommunity.DisplayName(npc) + " does not wear the player's body"); return; }
            Look old = ScLooks.Get(npc);
            Look look = ScLooks.Roll(old != null ? old.Male : npc.IsMale, ScCommunity.World.GetGameRandom(), false);
            if (old != null) for (int i = 0; i < 4; i++) { look.Gear[i] = old.Gear[i]; look.Quality[i] = old.Quality[i]; }
            Member m = ScRegistry.GetMember(npc.entityId);
            if (m != null) m.Look = look;
            Prospect pr = ScRegistry.GetProspect(npc.entityId);
            if (pr != null) pr.Look = look;
            ScLooks.Apply(npc, look);
            ScRegistry.SaveAtomic();
            Output("[SC] " + ScCommunity.DisplayName(npc) + ": " + ScLooks.Describe(look));
        }

        /// <summary>sc gear <id|name> <Set|none> - dress a loaded survivor in a whole vanilla armor set (or strip them).</summary>
        private static void Gear(List<string> p)
        {
            if (p.Count < 3) { Output("[SC] gear <id|name> <Set|none>   sets: " + string.Join(",", ScConfig.SurvivorGearSets.ToArray())); return; }
            EntityAliveSDX npc = FindSurvivor(p[1]);
            if (npc == null) { Output("[SC] no loaded NPC '" + p[1] + "'"); return; }
            Look look = ScLooks.Get(npc);
            if (look == null) { Output("[SC] " + ScCommunity.DisplayName(npc) + " does not wear the player's body"); return; }
            if (!ScLooks.WearSet(look, p[2], ScCommunity.World.GetGameRandom())) { Output("[SC] no armor set '" + p[2] + "' (armor" + p[2] + "Helmet...)"); return; }
            ScLooks.SyncGear(npc, look);
            ScRegistry.SaveAtomic();
            Output("[SC] " + ScCommunity.DisplayName(npc) + " wears [" + look.EncodeGear() + "]");
        }

        /// <summary>sc anim <id|name> - what drives a survivor's animator (diagnostic).</summary>
        private static void Anim(List<string> p)
        {
            if (p.Count < 2) { Output("[SC] anim <id|name>"); return; }
            EntityAliveSDX npc = FindSurvivor(p[1]);
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
            Output("[SC] " + n + " runner(s) out; physical runs " + (ScConfig.PhysicalRuns ? "on" : "OFF") + ", walk " + ScConfig.RunWalkMps + " m/s, "
                   + ScConfig.RunMinDist + "-" + ScConfig.RunMaxDist + " m, " + ScConfig.RunSiteHours + " h at the site");
        }

        /// <summary>sc run &lt;member&gt; recall | home | skip | offline - drive a physical run (tests).</summary>
        private static void RunControl(Member m, string sub)
        {
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            World w = ScCommunity.World;
            if (!ScRunners.IsPhysical(m) || w == null) { Output("[SC] " + m.Name + " is not on a physical run"); return; }
            EntityAliveSDX npc = m.Offline ? null : w.GetEntity(m.Id) as EntityAliveSDX;
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
            if (p.Count < 3 || !ScNames.TryResource(p[2], out r)) { Output("[SC] run <id|name> <food|meds|ammo|mats|fuel>"); return; }
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
            foreach (string res in new[] { "food", "meds", "ammo", "mats", "fuel", "scavenge", "none", "ineligible" })
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
            float[] total = new float[5];
            foreach (Outpost o in ScOutposts.All)
            {
                if (!o.Holding) continue;
                float[] raw = ScOutposts.RawYield(o), est = ScOutposts.Estimate(o);
                for (int i = 0; i < 5; i++) total[i] += est[i];
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

        private static void Relink()
        {
            World w = ScCommunity.World;
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (w == null || p == null) { Output("[SC] no world / player"); return; }
            ScTicker.Relink(w, p);
            Output("[SC] relinked");
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
