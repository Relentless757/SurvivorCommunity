using System;
using System.Collections.Generic;
using SurvivorCommunity;
using UnityEngine;

// Dialog operators. DialogFromXml resolves type="ScFavour, SurvivorCommunity" to the GLOBAL class
// DialogRequirementScFavour in this assembly (the "DialogRequirement" / "DialogAction" prefix is inserted
// after the last '.' before the comma), so these classes live outside the namespace. All of them find the
// NPC through the player cvar CurrentNPC that SCore sets when a conversation opens.

namespace SurvivorCommunity
{
    /// <summary>Shared helpers for the dialog operators.</summary>
    public static class ScFavours
    {
        public static EntityAlive CurrentNpc(EntityPlayer player)
        {
            try
            {
                if (player == null || !player.Buffs.HasCustomVar("CurrentNPC")) return null;
                int id = (int)player.Buffs.GetCustomVar("CurrentNPC");
                return ScCommunity.FindNpc(id);
            }
            catch (Exception) { return null; }
        }

        /// <summary>The prospect record for this NPC, rolling the favour the first time the player asks.</summary>
        public static Prospect Ensure(EntityAlive npc, EntityPlayer player)
        {
            if (npc == null) return null;
            // an enclave person's favour too, once E3's enclave path rolled it (ScEnclaveDeals.EnsureFavour, kept on their
            // current body): the favour operators below then work for them as for anyone
            Prospect p = ScRegistry.GetProspect(npc.entityId);
            if (p != null) return p;
            // never rolled here for one of an enclave's people (E2): a prospect from here pins the body (Persist,
            // StaticSpawner, a wait order) - E3 rolls theirs, unpinned, through their enclave's recruit path
            if (ScEnclavePeople.IsEnclaveNpc(npc)) return null;
            // nor for one of a band on the road (E13a): a prospect would pin a body that is never saved
            if (ScBands.IsBandNpc(npc)) return null;
            ScConfig.EnsureParsed();
            p = new Prospect { Id = npc.entityId, Class = ScCommunity.ClassName(npc), Name = ScCommunity.DisplayName(npc) };
            p.Look = ScLooks.Get(npc);        // null unless they wear the player's body
            p.Loadout = ScLoadout.Current(npc);   // their two weapons (null unless they carry a pair), handed on when they join
            World w = ScCommunity.World;
            Roll(p, npc.position, npc.rand ?? (w != null ? w.GetGameRandom() : null));
            ScRegistry.Prospects[p.Id] = p;
            npc.Buffs.SetCustomVar("Persist", 1f);                       // a prospect must survive the chunk
            npc.Buffs.SetCustomVar("sc_favour", (int)p.Favour);
            npc.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
            // they wait here (SCore guard order: fights what comes, walks back) so the player can find them again - unless
            // they are the player's own hired companion (decision D9: they may join), who keeps whatever order the player
            // gave them: a companion asked about the community mid-trip must not be left guarding the spot
            p.NpcPos = npc.GetBlockPosition();
            if (ScCommunity.HiredBy(npc) <= 0
                && EntityUtilities.GetCurrentOrder(npc.entityId) != EntityUtilities.Orders.Guard && !npc.Buffs.HasBuff("buffOrderGuard")) npc.Buffs.AddBuff("buffOrderGuard");
            ScRegistry.SaveAtomic();
            ScLog.Out(p.Name + " asks for a favour: " + Describe(p));
            return p;
        }

        /// <summary>
        /// The favour a new prospect asks (Ensure's roll; E3's enclave people use it too): half the time the nearest real POI
        /// within PoiFavourRange of 'near' to secure, else supplies from SuppliesFavours.
        /// </summary>
        internal static void Roll(Prospect p, Vector3 near, GameRandom rnd)
        {
            World w = ScCommunity.World;
            bool wantPoi = rnd == null || rnd.RandomFloat < 0.5f;
            PrefabInstance pi = wantPoi ? ScSecurity.NearestRealPoi(near, ScConfig.PoiFavourRange, w) : null;
            if (pi != null)
            {
                p.Favour = FavourKind.Poi;
                p.PoiPos = pi.boundingBoxPosition;
                p.PoiName = ScSecurity.DisplayName(pi);
                p.PoiWidth = pi.boundingBoxSize.x;
                p.PoiDepth = pi.boundingBoxSize.z;
                // a POI that already reads clear is still a favour to take: the player says so in the dialog
                // (scPoiAlreadyCleared), it never completes on its own
            }
            else
            {
                p.Favour = FavourKind.Supplies;
                var choices = ScConfig.SuppliesFavours;
                var pick = choices.Count > 0 ? choices[rnd != null ? rnd.RandomRange(0, choices.Count) : 0] : new KeyValuePair<Resource, int>(Resource.Food, 10);
                p.ItemCategory = pick.Key;
                p.ItemCount = Math.Max(1, pick.Value);
            }
        }

        /// <summary>
        /// The favour is done, marked so the moment its quest is past the clearing (the same dialog that completes the quest
        /// can offer the join): the ScFavourDone requirement's test, and E3's enclave recruit's. False without a prospect.
        /// </summary>
        public static bool CheckDone(EntityAlive npc, EntityPlayer player, Prospect p)
        {
            if (p != null && !p.Done && ScQuests.FavourQuestDone(player, p.Id))
            {
                p.Done = true;
                if (npc != null && npc.Buffs != null) npc.Buffs.SetCustomVar("sc_favourDone", 1f);
                ScLeadership.Count(ScLeadership.FavourEvent);
            }
            return p != null && p.Done;
        }

        /// <summary>
        /// The POI favour's building reads clear now: by vanilla's own quest test (ScSecurity.QuestWouldClear - the
        /// favour quest would move on at once, so the line must agree with it), or by SC's stricter one (which also
        /// covers a POI without sleeper volumes, where vanilla's quest never finishes). 'why' says what is left.
        /// </summary>
        public static bool PoiReadsCleared(Prospect p, out string why)
        {
            why = null;
            World w = ScCommunity.World;
            PrefabInstance pi = p != null && p.Favour == FavourKind.Poi ? ScSecurity.PoiByKey(p.PoiPos) : null;
            if (w == null || pi == null) { why = "no POI"; return false; }
            bool strict = ScSecurity.IsCleared(w, pi, out why);
            return strict || ScSecurity.QuestWouldClear(w, pi);
        }

        public static string Describe(Prospect p)
        {
            if (p == null) return "";
            if (p.Favour == FavourKind.Poi) return string.Format(Localization.Get("scFavourPoiText"), p.PoiName);
            return string.Format(Localization.Get("scFavourSuppliesText"), p.ItemCount, Localization.Get(ScNames.LocKey(p.ItemCategory)));
        }
    }
}

// ---------------------------------------------------------------- requirements ----

/// <summary>The NPC may be asked to join (host, hireable human, not hired by somebody else, not a member, roster not full).</summary>
public class DialogRequirementScCanRecruit : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            string why;
            return npc != null && ScCommunity.CanRecruit(npc, player, out why);
        }
        catch (Exception e) { ScLog.Once("req-canrecruit", e); return false; }
    }
}

/// <summary>value="poi" | "supplies": the favour this NPC asks for (rolled on first sight).</summary>
public class DialogRequirementScFavour : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            if (npc == null || !ScCommunity.IsHost) return false;
            // Never roll (and pin as persistent) an NPC that cannot join anyway - requirement evaluation
            // order is not something to rely on.
            string why;
            if (ScRegistry.GetProspect(npc.entityId) == null && !ScCommunity.CanRecruit(npc, player, out why)) return false;
            Prospect p = ScFavours.Ensure(npc, player);
            if (p == null) return false;
            string v = (Value ?? "").Trim().ToLowerInvariant();
            if (v == "poi") return p.Favour == FavourKind.Poi;
            if (v == "supplies") return p.Favour == FavourKind.Supplies;
            return p.Favour != FavourKind.None;
        }
        catch (Exception e) { ScLog.Once("req-favour", e); return false; }
    }
}

/// <summary>The favour is complete (value="not" inverts).</summary>
public class DialogRequirementScFavourDone : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = npc != null ? ScRegistry.GetProspect(npc.entityId) : null;
            bool done = ScFavours.CheckDone(npc, player, p);
            return (Value ?? "").Trim().Equals("not", StringComparison.OrdinalIgnoreCase) ? !done : done;
        }
        catch (Exception e) { ScLog.Once("req-favourdone", e); return false; }
    }
}

/// <summary>
/// The player's community has a home whose land claim stands (ScZone.EnsureHome: since round 32a it adopts no claim by itself
/// unless AutoHomeFirstClaim). value="not": there is none (round 32a's "start a community first" answer); on a client, neither.
/// </summary>
public class DialogRequirementScHasHome : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            if (!ScCommunity.IsHost) return false;
            bool has = ScZone.EnsureHome(player);
            return (Value ?? "").Trim().Equals("not", StringComparison.OrdinalIgnoreCase) ? !has : has;
        }
        catch (Exception e) { ScLog.Once("req-hashome", e); return false; }
    }
}

/// <summary>
/// The player carries the supplies the NPC asked for, and has taken on the favour ("Sure, I'll see what I can find.");
/// value="any": straight after hearing it, in the same talk, taken on or not.
/// </summary>
public class DialogRequirementScHasFavourItems : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = npc != null ? ScRegistry.GetProspect(npc.entityId) : null;
            if (p == null || p.Favour != FavourKind.Supplies) return false;
            if (!p.Started && !(Value ?? "").Trim().Equals("any", StringComparison.OrdinalIgnoreCase)) return false;
            return ScCategories.CountInPlayer(player, p.ItemCategory) >= p.ItemCount;
        }
        catch (Exception e) { ScLog.Once("req-hasitems", e); return false; }
    }
}

/// <summary>
/// This survivor's favour quest is in the player's journal, in progress; value="not" inverts. Gates "Sure, I'll see
/// what I can find." on the journal, not on Prospect.Started: a quest removed by hand, a failed start, or a prospect
/// marked started=1 by the previous build (where it meant "heard") can still take it on.
/// </summary>
public class DialogRequirementScFavourQuestOpen : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = npc != null ? ScRegistry.GetProspect(npc.entityId) : null;
            if (p == null) return false;
            bool open = ScQuests.FindFavour(player, p.Id) != null;
            return (Value ?? "").Trim().Equals("not", StringComparison.OrdinalIgnoreCase) ? !open : open;
        }
        catch (Exception e) { ScLog.Once("req-favourquestopen", e); return false; }
    }
}

/// <summary>
/// The POI favour's building already reads clear (ScFavours.PoiReadsCleared, from the saved sleeper volumes, which the
/// world keeps for every POI, loaded or not), or its quest is past the clearing. Chooses the "Mark it" that leads to
/// the survivor's answer and "Actually, I've already cleared that."; value="not" picks the "Mark it" that just ends
/// the talk. Neither shows once the favour is done.
/// </summary>
public class DialogRequirementScFavourPoiCleared : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = npc != null && ScCommunity.IsHost ? ScRegistry.GetProspect(npc.entityId) : null;
            if (p == null || p.Favour != FavourKind.Poi || p.Done) return false;
            string why;
            bool cleared = ScFavours.PoiReadsCleared(p, out why) || ScQuests.FavourQuestDone(player, p.Id);
            return (Value ?? "").Trim().Equals("not", StringComparison.OrdinalIgnoreCase) ? !cleared : cleared;
        }
        catch (Exception e)
        {
            ScLog.Once("req-poicleared", e);
            // unknown counts as not clear, so the plain "Mark it" still shows
            return (Value ?? "").Trim().Equals("not", StringComparison.OrdinalIgnoreCase);
        }
    }
}

// ---------------------------------------------------------------- actions ----

/// <summary>Tooltip describing the favour ("Secure X for me" / "Bring me N food items").</summary>
public class DialogActionScFavourBrief : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = npc != null ? ScFavours.Ensure(npc, player) : null;
            if (p == null) return;
            ScCommunity.Tell(player, ScFavours.Describe(p) + (p.Done ? " " + Localization.Get("scFavourAlreadyDone") : ""), "");
        }
        catch (Exception e) { ScLog.Once("act-brief", e); }
    }
}

/// <summary>Marks the favour taken on (Prospect.Started) and saves, once.</summary>
internal static class ScFavourStart
{
    public static void Mark(Prospect p)
    {
        if (p == null || p.Started) return;
        p.Started = true;
        ScRegistry.SaveAtomic();
    }
}

public class DialogActionScStartPoiQuest : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = npc != null ? ScRegistry.GetProspect(npc.entityId) : null;
            if (lp == null || p == null) return;
            string error;
            // asked again with the quest already in the journal: it is marked, nothing to say
            if (ScQuests.Start(lp, p, out error) || error == "scQuestAlready") ScFavourStart.Mark(p);
            else ScCommunity.Tell(player, Localization.Get(error ?? "scInternal"), "ui_denied");
            string why;
            ScLog.Out(p.PoiName + " for " + p.Name + (ScFavours.PoiReadsCleared(p, out why) ? " already reads clear" : " is not clear yet: " + why));
        }
        catch (Exception e) { ScLog.Once("act-startquest", e); }
    }
}

/// <summary>"Sure, I'll see what I can find.": takes on the supplies favour and puts its quest in the journal.</summary>
public class DialogActionScStartSuppliesQuest : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = npc != null ? ScRegistry.GetProspect(npc.entityId) : null;
            if (lp == null || p == null || p.Favour != FavourKind.Supplies || p.Done) return;
            string error;
            if (ScQuests.StartSupplies(lp, p, out error) || error == "scQuestAlready") ScFavourStart.Mark(p);
            else ScCommunity.Tell(player, Localization.Get(error ?? "scInternal"), "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-startsupplies", e); }
    }
}

/// <summary>
/// "Actually, I've already cleared that.": the POI reads clear (checked again here - the list was built a moment
/// ago), so the favour is done. This survivor's quest closes as completed: "Mark it" just put it in the journal, and
/// on a clear POI vanilla has already moved it on to "return to &lt;name&gt;" - which is where the player is standing.
/// </summary>
public class DialogActionScFavourPoiCleared : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = npc != null ? ScRegistry.GetProspect(npc.entityId) : null;
            if (p == null || p.Favour != FavourKind.Poi) return;
            if (!p.Done)
            {
                string why;
                if (!ScFavours.PoiReadsCleared(p, out why) && !ScQuests.FavourQuestDone(player, p.Id))
                {
                    ScCommunity.Tell(player, string.Format(Localization.Get("scFavourPoiNotClear"), p.PoiName, why), "ui_denied");
                    return;
                }
                p.Done = true;
                if (npc.Buffs != null) npc.Buffs.SetCustomVar("sc_favourDone", 1f);
                ScLeadership.Count(ScLeadership.FavourEvent);
                ScRegistry.SaveAtomic();
                ScLog.Out("favour done: " + p.PoiName + " was already clear (the player said so to " + p.Name + ")");
            }
            ScQuests.CloseFavour(player, p.Id, true);
        }
        catch (Exception e) { ScLog.Once("act-poicleared", e); }
    }
}

public class DialogActionScTakeFavourItems : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = npc != null ? ScRegistry.GetProspect(npc.entityId) : null;
            if (lp == null || p == null || p.Favour != FavourKind.Supplies || p.Done) return;
            // value="any": handed over in the talk that named them, before taking the favour on (ScHasFavourItems "any")
            if (!p.Started && !(Value ?? "").Trim().Equals("any", StringComparison.OrdinalIgnoreCase)) return;
            // never take the goods when the join that follows would be refused
            if (!ScZone.EnsureHome(player)) { ScCommunity.Tell(player, Localization.Get(ScZone.NeedHomeKey()), "ui_denied"); return; }
            if (ScRegistry.Members.Count >= ScConfig.MaxMembers) { ScCommunity.Tell(player, Localization.Get("scRosterFull"), "ui_denied"); return; }
            if (ScCategories.CountInPlayer(player, p.ItemCategory) < p.ItemCount)
            {
                ScCommunity.Tell(player, Localization.Get("scFavourNotEnough"), "ui_denied");
                return;
            }
            int taken = ScCategories.RemoveFromPlayer(lp, p.ItemCategory, p.ItemCount);
            if (taken >= p.ItemCount)
            {
                p.Done = true;
                npc.Buffs.SetCustomVar("sc_favourDone", 1f);
                ScLeadership.Count(ScLeadership.FavourEvent);
                ScRegistry.SaveAtomic();
                ScQuests.CloseFavour(player, p.Id, true);
                ScLog.Out(p.Name + " received " + taken + " " + p.ItemCategory + " item(s)");
            }
            else
            {
                ScLog.Warn("supplies favour: asked to remove " + p.ItemCount + " but removed " + taken);
            }
        }
        catch (Exception e) { ScLog.Once("act-takeitems", e); }
    }
}

/// <summary>Joins the community once the favour is done (no dukes).</summary>
public class DialogActionScJoin : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            if (lp == null || npc == null) return;
            Prospect p = ScRegistry.GetProspect(npc.entityId);
            if (p != null && !p.Done) { ScCommunity.Tell(player, Localization.Get("scFavourNotDone"), "ui_denied"); return; }
            string error;
            if (!ScCommunity.Join(npc, lp, out error)) ScCommunity.Tell(player, Localization.Get(error ?? "scInternal"), "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-join", e); }
    }
}

/// <summary>
/// Round 32a: "I could use someone like you in my community." with no usable home (scJoinNoHome) - the tooltip says how to
/// get one: no home yet, Start a community here on a land claim's radial; the home's claim gone, Set as community home.
/// </summary>
public class DialogActionScNeedHomeTip : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (player is EntityPlayerLocal && ScCommunity.IsHost) ScCommunity.Tell(player, Localization.Get(ScZone.NeedHomeKey()), "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-needhometip", e); }
    }
}

/// <summary>
/// The NPC holds a weapon in its inventory window that it could carry (ScArms.HandedWeapon: a gun or bow, or a melee
/// weapon). Gates the weapon menu's "Use the weapons I gave you".
/// </summary>
public class DialogRequirementScHandedWeapon : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            int slot;
            return ScCommunity.IsHost && npc != null && (ScArms.HandedWeapon(npc, true, out slot) != null || ScArms.HandedWeapon(npc, false, out slot) != null);
        }
        catch (Exception e) { ScLog.Once("req-handed", e); return false; }
    }
}

/// <summary>The weapon menu's "Use the weapons I gave you" (ScArms.UseHanded).</summary>
public class DialogActionScUseHanded : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            if (npc == null) return;
            ScArms.UseHanded(npc, player);
        }
        catch (Exception e) { ScLog.Once("act-use-handed", e); }
    }
}

/// <summary>Member order: follow the player (refused when the party is full, or below RefuseOrdersBelowMood when that is set).</summary>
public class DialogActionScFollow : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Member m = npc != null ? ScRegistry.GetMember(npc.entityId) : null;
            if (lp == null || npc == null || m == null) return;
            string error;
            if (ScCommunity.Follow(m, npc, lp, out error)) ScCommunity.Tell(player, string.Format(Localization.Get("scWinNowFollowing"), m.Name), "");
            else ScCommunity.Tell(player, Localization.Get(error ?? "scInternal"), "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-follow", e); }
    }
}

/// <summary>Member order: back to the post (walks when near, teleports when the player is inside the base).</summary>
public class DialogActionScGoHome : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Member m = npc != null ? ScRegistry.GetMember(npc.entityId) : null;
            if (lp == null || npc == null || m == null) return;
            string error;
            if (ScCommunity.ReturnHome(m, npc, lp, out error)) ScCommunity.Tell(player, string.Format(Localization.Get("scStationed"), m.Name), "");
            else ScCommunity.Tell(player, Localization.Get(error ?? "scInternal"), "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-gohome", e); }
    }
}

/// <summary>Member order, to a runner met out in the world: head home now (ScRunners.Recall).</summary>
public class DialogActionScRecall : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Member m = npc != null ? ScRegistry.GetMember(npc.entityId) : null;
            if (lp == null || npc == null || m == null) return;
            string error;
            if (ScRunners.Recall(m, lp, out error)) ScCommunity.Tell(player, string.Format(Localization.Get("scRunRecalled"), m.Name), "");
            else ScCommunity.Tell(player, Localization.Get(error ?? "scInternal"), "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-recall", e); }
    }
}

/// <summary>
/// Member order: a supply run for the resource named in the action's value (food|meds|ammo|mats|fuel), or (round 33c, M33, 3.4)
/// a mining run - mine | clay (ScMining.Send).
/// </summary>
public class DialogActionScRun : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Member m = npc != null ? ScRegistry.GetMember(npc.entityId) : null;
            if (lp == null || npc == null || m == null) return;
            string error;
#if SC_V34
            RunJob job;
            if (ScMining.TryJob(Value, out job))
            {
                if (!ScMining.Send(m, npc, job, lp, out error)) ScCommunity.Tell(player, Localization.Get(error ?? "scInternal"), "ui_denied");
                return;
            }
#endif
            Resource kind;
            if (!ScNames.TryResource(Value, out kind)) { ScLog.Err("dialog action ScRun: unknown resource '" + Value + "'"); return; }
            if (!ScRuns.Send(m, npc, kind, lp, out error)) ScCommunity.Tell(player, Localization.Get(error ?? "scInternal"), "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-run", e); }
    }
}

/// <summary>
/// Round 33c (M33): the talk's Mine and Dig clay rows show while MiningRuns is on (3.4; the 3.2 build has no mining runs and
/// always hides them). Reads the config only (any machine).
/// </summary>
public class DialogRequirementScMiningRuns : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
#if SC_V34
        return ScConfig.MiningRuns;
#else
        return false;
#endif
    }
}

/// <summary>Member order: leave the community (after the dialog's confirmation statement).</summary>
public class DialogActionScLeave : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Member m = npc != null ? ScRegistry.GetMember(npc.entityId) : null;
            if (m == null) return;
            ScCommunity.Leave(m, "scLeft");
        }
        catch (Exception e) { ScLog.Once("act-leave", e); }
    }
}

/// <summary>Opens the community window (also reachable through OpenWindowSDX, kept for a self-contained dialog).</summary>
public class DialogActionScOpenCommunity : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        ScCommunity.OpenWindow(player as EntityPlayerLocal);
    }
}

/// <summary>
/// Round 28 (X3): 0-XNPCCore 3.4.04's "Come here" and "Order wheel" are a follower's verbs (the user's decision, 2026-11-10).
/// True for any NPC that is none of SC's people (a hired stranger keeps them) and for a member who follows the player
/// (sc_following = 1: Following or PendingStation); false for a member who is not following - on a resident, Come here pulls
/// her off her post and the wheel's orders end her talk or shift - and never for an outpost stand-in, one of an enclave's
/// people or one of a band on the road. An OR the cvar requirements cannot say. Reads the body's cvars (any machine).
/// </summary>
public class DialogRequirementScFollowerVerb : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc = talkingTo;
            if (npc == null && player != null && player.Buffs != null && player.Buffs.HasCustomVar("CurrentNPC"))
                npc = ScCommunity.World != null ? ScCommunity.World.GetEntity((int)player.Buffs.GetCustomVar("CurrentNPC")) as EntityAlive : null;
            if (npc == null || npc.Buffs == null) return true;
            if (npc.Buffs.HasCustomVar(ScRadio.StandInCvar) || ScEnclavePeople.IsEnclaveNpc(npc) || ScBands.IsBandNpc(npc)) return false;
            if (!npc.Buffs.HasCustomVar("sc_member")) return true;
            return npc.Buffs.GetCustomVar("sc_following") > 0f;
        }
        catch (Exception e) { ScLog.Once("req-followerverb", e); return true; }
    }
}
