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
            Prospect p = ScRegistry.GetProspect(npc.entityId);
            if (p != null) return p;
            ScConfig.EnsureParsed();
            p = new Prospect { Id = npc.entityId, Class = ScCommunity.ClassName(npc), Name = ScCommunity.DisplayName(npc) };
            p.Look = ScLooks.Get(npc);        // null unless they wear the player's body
            World w = ScCommunity.World;
            GameRandom rnd = npc.rand ?? (w != null ? w.GetGameRandom() : null);
            bool wantPoi = rnd == null || rnd.RandomFloat < 0.5f;
            PrefabInstance pi = wantPoi ? ScSecurity.NearestRealPoi(npc.position, ScConfig.PoiFavourRange, w) : null;
            if (pi != null)
            {
                p.Favour = FavourKind.Poi;
                p.PoiPos = pi.boundingBoxPosition;
                p.PoiName = ScSecurity.DisplayName(pi);
                p.PoiWidth = pi.boundingBoxSize.x;
                p.PoiDepth = pi.boundingBoxSize.z;
                string why;
                if (w != null && ScSecurity.IsCleared(w, pi, out why)) { p.Done = true; ScLeadership.Count(ScLeadership.FavourEvent); }   // already clear: lucky
            }
            else
            {
                p.Favour = FavourKind.Supplies;
                var choices = ScConfig.SuppliesFavours;
                var pick = choices.Count > 0 ? choices[rnd != null ? rnd.RandomRange(0, choices.Count) : 0] : new KeyValuePair<Resource, int>(Resource.Food, 10);
                p.ItemCategory = pick.Key;
                p.ItemCount = Math.Max(1, pick.Value);
            }
            ScRegistry.Prospects[p.Id] = p;
            npc.Buffs.SetCustomVar("Persist", 1f);                       // a prospect must survive the chunk
            npc.Buffs.SetCustomVar("sc_favour", (int)p.Favour);
            if (p.Done) npc.Buffs.SetCustomVar("sc_favourDone", 1f);
            npc.SetSpawnerSource(EnumSpawnerSource.StaticSpawner);
            // they wait here (SCore guard order: fights what comes, walks back) so the player can find them again
            p.NpcPos = npc.GetBlockPosition();
            if (EntityUtilities.GetCurrentOrder(npc.entityId) != EntityUtilities.Orders.Guard && !npc.Buffs.HasBuff("buffOrderGuard")) npc.Buffs.AddBuff("buffOrderGuard");
            ScRegistry.SaveAtomic();
            ScLog.Out(p.Name + " asks for a favour: " + Describe(p));
            return p;
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

/// <summary>The NPC may be asked to join (host, hireable human, not hired, not a member, roster not full).</summary>
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
            if (p != null && !p.Done && ScQuests.FavourQuestDone(player, p.Id))
            {
                p.Done = true;                                   // the same dialog that completes the quest can offer the join
                if (npc.Buffs != null) npc.Buffs.SetCustomVar("sc_favourDone", 1f);
                ScLeadership.Count(ScLeadership.FavourEvent);
            }
            bool done = p != null && p.Done;
            return (Value ?? "").Trim().Equals("not", StringComparison.OrdinalIgnoreCase) ? !done : done;
        }
        catch (Exception e) { ScLog.Once("req-favourdone", e); return false; }
    }
}

/// <summary>The player has a land claim for the community to live in (adopting the first one when needed).</summary>
public class DialogRequirementScHasHome : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try { return ScCommunity.IsHost && ScZone.EnsureHome(player); }
        catch (Exception e) { ScLog.Once("req-hashome", e); return false; }
    }
}

/// <summary>The player carries the supplies the NPC asked for.</summary>
public class DialogRequirementScHasFavourItems : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = npc != null ? ScRegistry.GetProspect(npc.entityId) : null;
            if (p == null || p.Favour != FavourKind.Supplies) return false;
            return ScCategories.CountInPlayer(player, p.ItemCategory) >= p.ItemCount;
        }
        catch (Exception e) { ScLog.Once("req-hasitems", e); return false; }
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
            if (!ScQuests.Start(lp, p, out error)) ScCommunity.Tell(player, Localization.Get(error ?? "scInternal"), "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-startquest", e); }
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
            // never take the goods when the join that follows would be refused
            if (!ScZone.EnsureHome(player)) { ScCommunity.Tell(player, Localization.Get("scNeedHome"), "ui_denied"); return; }
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

/// <summary>Member order: follow the player (refused below mood 25 or when the party is full).</summary>
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

/// <summary>Member order: a supply run for the resource named in the action's value (food|meds|ammo|mats|fuel).</summary>
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
            Resource kind;
            if (!ScNames.TryResource(Value, out kind)) { ScLog.Err("dialog action ScRun: unknown resource '" + Value + "'"); return; }
            string error;
            if (!ScRuns.Send(m, npc, kind, lp, out error)) ScCommunity.Tell(player, Localization.Get(error ?? "scInternal"), "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-run", e); }
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
