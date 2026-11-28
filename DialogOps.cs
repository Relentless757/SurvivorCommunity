using System;
using System.Collections.Generic;
using SurvivorCommunity;
using UnityEngine;

// Dialog operators. DialogFromXml resolves type="ScFavour, SurvivorCommunity" to the GLOBAL class
// DialogRequirementScFavour in this assembly (the "DialogRequirement" / "DialogAction" prefix is inserted
// after the last '.' before the comma), so these classes live outside the namespace. All of them find the
// NPC through the player cvar CurrentNPC that SCore sets when a conversation opens.
// Round MP2 (multiplayer, part M): requirements and actions run on the TALKING machine (XUiC_DialogResponseEntry,
// Dialog.SelectResponse). Each requirement keeps its code on the server (single player, the host's own player: unchanged)
// and has a client path that reads the talk context (ScTalkContext: what the server said at the talk's open), the copy of
// the registry and the client's own journal and inventory - no requirement changes anything on a client. Every action that
// changes the community is an ScActions request on every machine (ScActionsMembers' ops; inline on the server), and the
// answer's tooltip, sound and journal close are the callback's.

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
            // current body): the favour operators below then work for them as for anyone. Round MP6 (part relations): the asking
            // player's community's own favour - another community courting them rolls its own (the user's decision 2)
            Prospect p = ScCommunity.IsHost ? ScRegistry.OwnProspect(npc.entityId) : ScRegistry.GetProspect(npc.entityId);
            if (p != null) return p;
            // round MP1: rolling one is the server's (a client's copy of the prospects is read-only; round MP2 sends it there)
            if (!ScCommunity.IsHost) return null;
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
            ScCommunity.SetSynced(npc, "sc_favour", (int)p.Favour);      // round MP2 (E9): force-sent
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
            // round MP1: marking it done is the server's - on a client (a copy of the prospect) this stays the read it is there
            if (ScCommunity.IsHost && p != null && !p.Done && ScQuests.FavourQuestDone(player, p.Id)) MarkDone(npc, p);
            return p != null && p.Done;
        }

        /// <summary>
        /// Round MP2: the favour is done - the mark CheckDone, "already cleared", the hand-over and a client's journal verdict
        /// (ScTalkContext, member.join) share: Done, sc_favourDone (force-sent: E9), the leadership count. The caller saves when it
        /// did before (CheckDone never did: the next save takes it). Server only.
        /// </summary>
        internal static void MarkDone(EntityAlive npc, Prospect p)
        {
            if (p == null || p.Done || !ScRegistry.Loaded) return;
            p.Done = true;
            if (npc != null) ScCommunity.SetSynced(npc, "sc_favourDone", 1f);
            ScLeadership.Count(ScLeadership.FavourEvent);
        }

        /// <summary>
        /// Round MP2: the prospect a dialog row reads - the registry's on the server (as before), the talk context's view on a
        /// client (ScTalkContext.View: the copy's, with what the server said at the talk's open; a new object, never the copy).
        /// </summary>
        internal static Prospect Lookup(EntityAlive npc)
        {
            if (npc == null) return null;
            // round MP6 (part relations): the talker's community's own favour on the server (never another community's)
            return ScCommunity.IsHost ? ScRegistry.OwnProspect(npc.entityId) : ScTalkContext.View(npc.entityId);
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

/// <summary>
/// The NPC may be asked to join (host, hireable human, not hired by somebody else, not a member, roster not full). Round MP2: a
/// client reads the server's verdict from the talk's open (ScTalkContext.CanRecruit), unless its copy says they joined since.
/// MP7 (part U): the recruit dialog's favor rows (Config/dialogs.xml: taking a favor on, "I've already cleared that", handing the
/// supplies over) sit behind this and ScHasHome, so a player with only their own relations (PerPlayer/PerParty) never reaches them -
/// the server takes those ops from them (ScActions.PersonalOps; the supplies hand-over needs no home for them: ScActionsMembers).
/// Rows for them, with no join at the end, come with the talk facts' scNoCommunity.
/// </summary>
public class DialogRequirementScCanRecruit : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            if (!ScCommunity.IsHost) return ScTalkContext.CanRecruit(npc);       // round MP2: the client's path
            string why;
            return npc != null && ScCommunity.CanRecruit(npc, player, out why);
        }
        catch (Exception e) { ScLog.Once("req-canrecruit", e); return false; }
    }
}

/// <summary>
/// value="poi" | "supplies": the favour this NPC asks for (rolled on first sight). Round MP2 (E4): the roll is the server's -
/// at the talk's open for a client (ScTalkContext), which reads the prospect from its copy and the facts and never rolls one.
/// </summary>
public class DialogRequirementScFavour : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            if (npc == null) return false;
            if (!ScCommunity.IsHost)
            {
                // round MP2: the client's path - a read, no roll (E4)
                Prospect cp = ScTalkContext.View(npc.entityId);
                if (cp == null) return false;
                string cv = (Value ?? "").Trim().ToLowerInvariant();
                if (cv == "poi") return cp.Favour == FavourKind.Poi;
                if (cv == "supplies") return cp.Favour == FavourKind.Supplies;
                return cp.Favour != FavourKind.None;
            }
            // Never roll (and pin as persistent) an NPC that cannot join anyway - requirement evaluation
            // order is not something to rely on.
            string why;
            // round MP6 (part relations): the talker's community's own favour (another community's does not let them past CanRecruit)
            if (ScRegistry.OwnProspect(npc.entityId) == null && !ScCommunity.CanRecruit(npc, player, out why)) return false;
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

/// <summary>
/// The favour is complete (value="not" inverts). Round MP2: a client reads the prospect's Done (its copy, the facts) or its
/// own journal (the favour's quest past the clearing: the server marks it at the talk's open, or on the join - member.join).
/// </summary>
public class DialogRequirementScFavourDone : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            bool done;
            if (!ScCommunity.IsHost)
            {
                Prospect cp = npc != null ? ScTalkContext.View(npc.entityId) : null;     // round MP2: the client's path, a read
                done = cp != null && (cp.Done || ScQuests.FavourQuestDone(player, cp.Id));
            }
            else
            {
                Prospect p = npc != null ? ScRegistry.OwnProspect(npc.entityId) : null;      // round MP6 (part relations): the talker's community's own
                done = ScFavours.CheckDone(npc, player, p);
            }
            return (Value ?? "").Trim().Equals("not", StringComparison.OrdinalIgnoreCase) ? !done : done;
        }
        catch (Exception e) { ScLog.Once("req-favourdone", e); return false; }
    }
}

/// <summary>
/// The player's community has a home whose land claim stands (ScZone.EnsureHome: since round 32a it adopts no claim by itself
/// unless AutoHomeFirstClaim). value="not": there is none (round 32a's "start a community first" answer). Round MP2: a client
/// reads the server's EnsureHome from the talk's open, else its copy's home (ScTalkContext.HasHome) - never EnsureHome itself.
/// </summary>
public class DialogRequirementScHasHome : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            bool has;
            if (!ScCommunity.IsHost)
            {
                if (!ScRegistry.Mirrored) return false;                  // no copy yet: neither row, as before round MP2
                has = ScTalkContext.HasHome(player);                     // round MP2: the client's path
            }
            else has = ScZone.EnsureHome(player);
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
            Prospect p = ScFavours.Lookup(npc);          // round MP2: a client's view (its own inventory is the right one to count)
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
            Prospect p = ScFavours.Lookup(npc);          // round MP2: a client's view (its own journal is the right one to read)
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
/// the talk. Neither shows once the favour is done. Round MP2: a client reads the server's verdict on the volumes from the
/// talk's open (ScTalkFacts.PoiCleared), and its own journal.
/// </summary>
public class DialogRequirementScFavourPoiCleared : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = ScFavours.Lookup(npc);
            if (p == null || p.Favour != FavourKind.Poi || p.Done) return false;
            string why;
            bool cleared;
            if (!ScCommunity.IsHost)
            {
                ScTalkFacts f = ScTalkContext.Facts(npc.entityId);                // round MP2: the client's path
                cleared = (f != null && f.PoiCleared) || ScQuests.FavourQuestDone(player, p.Id);
            }
            else cleared = ScFavours.PoiReadsCleared(p, out why) || ScQuests.FavourQuestDone(player, p.Id);
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

/// <summary>
/// Tooltip describing the favour ("Secure X for me" / "Bring me N food items"). Round MP2: a client describes the favour the
/// server rolled at the talk's open (never rolls one: E4).
/// </summary>
public class DialogActionScFavourBrief : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = npc == null ? null : ScCommunity.IsHost ? ScFavours.Ensure(npc, player) : ScTalkContext.View(npc.entityId);
            if (p == null) return;
            ScCommunity.Tell(player, ScFavours.Describe(p) + (p.Done ? " " + Localization.Get("scFavourAlreadyDone") : ""), "");
        }
        catch (Exception e) { ScLog.Once("act-brief", e); }
    }
}

/// <summary>Marks the favour taken on (Prospect.Started) and saves, once. Round MP2: the favour.start request's (server only).</summary>
internal static class ScFavourStart
{
    public static void Mark(Prospect p)
    {
        if (p == null || p.Started || !ScRegistry.Loaded) return;     // round MP2 (E10): never on a client's copy
        p.Started = true;
        ScRegistry.SaveAtomic();
    }
}

/// <summary>
/// "Mark it": the POI favour's quest into the player's own journal, and favour.start marks it taken on. Round MP2: the host
/// starts the quest first and marks after, as before; a client sends the mark FIRST - the reliable channel keeps order, so the
/// server holds the POI as a favour's (Patches_Quest, E6) before the sleeper subscribe its AddQuest sends arrives - and drops
/// its quest again if the server refuses.
/// </summary>
public class DialogActionScStartPoiQuest : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = ScFavours.Lookup(npc);
            if (lp == null || p == null) return;
            int npcId = npc.entityId, pid = p.Id;
            string error;
            if (ScCommunity.IsHost)
            {
                // asked again with the quest already in the journal: it is marked, nothing to say
                if (ScQuests.Start(lp, p, out error) || error == "scQuestAlready")
                    ScActions.Request("favour.start", r => ScTalkContext.Tell(player, r, false), npcId, (int)FavourKind.Poi);   // marks and logs the POI's state
                else
                {
                    // round MP4 (part: tells): this file's tells go by localization key (ScCommunity.TellKey / ScPlayers.TellCommunityKey), so each player reads them in their own language
                    ScCommunity.TellKey(player, error ?? "scInternal", "ui_denied");
                    string why;
                    ScLog.Out(p.PoiName + " for " + p.Name + (ScFavours.PoiReadsCleared(p, out why) ? " already reads clear" : " is not clear yet: " + why));
                }
                return;
            }
            // round MP2 (review members MINOR 6): a refusal removes the quest only when THIS action put it in the journal - one
            // that was there already (asked again) stays. A refusal at once (no server to ask) adds none at all
            bool had = ScQuests.FindFavour(lp, pid) != null, refused = false;
            ScActions.Request("favour.start", r =>
            {
                if (r.Ok) ScTalkContext.Note(npcId, true, false);
                else
                {
                    refused = true;
                    if (!had) ScQuests.CloseFavour(lp, pid, false);            // the server said no: the quest this action added goes again
                }
                ScTalkContext.Tell(player, r, false);
            }, npcId, (int)FavourKind.Poi);
            if (refused) return;
            if (!ScQuests.Start(lp, p, out error) && error != "scQuestAlready") ScCommunity.TellKey(player, error ?? "scInternal", "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-startquest", e); }
    }
}

/// <summary>
/// "Sure, I'll see what I can find.": takes on the supplies favour and puts its quest in the journal. Round MP2: the quest
/// into the player's own journal first, then favour.start (Started); a client drops the quest again if the server refuses.
/// </summary>
public class DialogActionScStartSuppliesQuest : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = ScFavours.Lookup(npc);
            if (lp == null || p == null || p.Favour != FavourKind.Supplies || p.Done) return;
            string error;
            if (!ScQuests.StartSupplies(lp, p, out error) && error != "scQuestAlready")
            {
                ScCommunity.TellKey(player, error ?? "scInternal", "ui_denied");
                return;
            }
            bool added = error == null;
            int npcId = npc.entityId, pid = p.Id;
            ScActions.Request("favour.start", r =>
            {
                if (r.Ok) ScTalkContext.Note(npcId, true, false);
                else if (added && !ScCommunity.IsHost) ScQuests.CloseFavour(lp, pid, false);   // the server said no: the quest goes again
                ScTalkContext.Tell(player, r, false);
            }, npcId, (int)FavourKind.Supplies);
        }
        catch (Exception e) { ScLog.Once("act-startsupplies", e); }
    }
}

/// <summary>
/// "Actually, I've already cleared that.": the POI reads clear (checked again here - the list was built a moment
/// ago), so the favour is done. This survivor's quest closes as completed: "Mark it" just put it in the journal, and
/// on a clear POI vanilla has already moved it on to "return to &lt;name&gt;" - which is where the player is standing.
/// Round MP2: favour.poicleared decides on the server (the volumes there; a client sends its journal's verdict) and the
/// answer closes the quest on the player's own journal.
/// </summary>
public class DialogActionScFavourPoiCleared : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = ScFavours.Lookup(npc);
            if (p == null || p.Favour != FavourKind.Poi) return;
            int npcId = npc.entityId;
            bool journal = !ScCommunity.IsHost && ScQuests.FavourQuestDone(player, p.Id);     // the host reads its own on the server
            ScActions.Request("favour.poicleared", r =>
            {
                ScTalkContext.ApplyQuestHint(player, r);
                if (r.Ok) ScTalkContext.Note(npcId, false, true);
                ScTalkContext.Tell(player, r, false);
            }, npcId, journal ? 1 : 0);
        }
        catch (Exception e) { ScLog.Once("act-poicleared", e); }
    }
}

/// <summary>
/// The supplies hand-over. Round MP2, two steps (the player's bag is their own machine's): favour.items asks the server
/// whether they may take it now (the home, the roster - never the goods when the join that follows would be refused) and
/// what; this machine takes the items from its own bag; favour.given tells the server, which checks again and marks the
/// favour done - or refuses (another player did the favour meanwhile, the roster filled), and the items go back. On the
/// server (single player, the host) both steps run inline, the same frame, as the old action did.
/// </summary>
public class DialogActionScTakeFavourItems : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Prospect p = ScFavours.Lookup(npc);
            if (lp == null || p == null || p.Favour != FavourKind.Supplies || p.Done) return;
            // value="any": handed over in the talk that named them, before taking the favour on (ScHasFavourItems "any")
            bool any = (Value ?? "").Trim().Equals("any", StringComparison.OrdinalIgnoreCase);
            if (!p.Started && !any) return;
            int npcId = npc.entityId;
            // a client's row's ScJoin waits for it (the host's runs inline: it is through before ScJoin, as it always was)
            if (!ScCommunity.IsHost) ScTalkContext.BeginHandOver(npcId);
            ScActions.Request("favour.items", r => Step1(lp, npcId, any, r), npcId, any ? 1 : 0);
        }
        catch (Exception e) { ScLog.Once("act-takeitems", e); }
    }

    /// <summary>The server's go-ahead (what, how many): the items leave this machine's bag, then favour.given.</summary>
    private static void Step1(EntityPlayerLocal lp, int npcId, bool any, ScActionReply r)
    {
        if (!r.Ok)
        {
            ScTalkContext.Tell(lp, r, false);
            ScTalkContext.EndHandOver(npcId, false);
            return;
        }
        Resource res = (Resource)Math.Max(0, Math.Min(ScNames.ResourceCount - 1, r.HintInt(ScActionsMembers.HintResource)));
        int count = r.HintInt(ScActionsMembers.HintCount);
        if (ScCategories.CountInPlayer(lp, res) < count)
        {
            ScCommunity.TellKey(lp, "scFavourNotEnough", "ui_denied");
            ScTalkContext.EndHandOver(npcId, false);
            return;
        }
        var removed = new List<ItemStack>();
        // the host takes them as it always did; a client records what left, to give it back on a refusal
        int taken = ScCommunity.IsHost ? ScCategories.RemoveFromPlayer(lp, res, count) : ScTalkContext.TakeItems(lp, res, count, removed);
        if (taken >= count)
        {
            ScActions.Request("favour.given", r2 => Step2(lp, npcId, removed, r2), npcId, any ? 1 : 0, (int)res, taken);
            return;
        }
        ScLog.Warn("supplies favour: asked to remove " + count + " but removed " + taken);
        if (!ScCommunity.IsHost) ScTalkContext.Refund(lp, removed);         // round MP2: a client gives back what did leave
        ScTalkContext.EndHandOver(npcId, false);
    }

    /// <summary>The server's answer to the hand-over: done (the quest closes here), or refused (the items go back).</summary>
    private static void Step2(EntityPlayerLocal lp, int npcId, List<ItemStack> removed, ScActionReply r)
    {
        ScTalkContext.ApplyQuestHint(lp, r);
        if (!r.Ok)
        {
            // round MP2 (review): every refusal at step 2 means the server kept nothing - the handler's own (refund=1), the core's
            // before any handler ran (dead, denied, not ready ...), or a handler that threw before it marked the favour done -
            // so the items always go back
            ScTalkContext.Refund(lp, removed);
            ScTalkContext.Tell(lp, r, false);
            ScTalkContext.EndHandOver(npcId, false);
            return;
        }
        ScTalkContext.Note(npcId, false, true);
        ScTalkContext.EndHandOver(npcId, true);         // a client's ScJoin of the same row goes now
    }
}

/// <summary>
/// Joins the community once the favour is done (no dukes). Round MP2: member.join, with this machine's journal verdict (a
/// client's POI favour quest past the clearing). In a row whose supplies hand-over is still in flight on a client
/// (scGiveSupplies: ScTakeFavourItems, then ScJoin) it waits for the hand-over and goes after it (ScTalkContext).
/// </summary>
public class DialogActionScJoin : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            if (lp == null || npc == null) return;
            bool journal = !ScCommunity.IsHost && ScQuests.FavourQuestDone(lp, npc.entityId);
            if (!ScCommunity.IsHost && ScTalkContext.HandOverPending(npc.entityId)) { ScTalkContext.JoinAfterHandOver(npc.entityId, player, journal); return; }
            ScTalkContext.RequestJoin(npc.entityId, player, journal);
        }
        catch (Exception e) { ScLog.Once("act-join", e); }
    }
}

/// <summary>
/// Round 32a: "I could use someone like you in my community." with no usable home (scJoinNoHome) - the tooltip says how to
/// get one: no home yet, Start a community here on a land claim's radial; the home's claim gone, Set as community home.
/// Round MP2: a client says it from its copy of the community (which home it has).
/// </summary>
public class DialogActionScNeedHomeTip : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            if (player is EntityPlayerLocal && (ScCommunity.IsHost || ScRegistry.Mirrored)) ScCommunity.TellKey(player, ScZone.NeedHomeKey(), "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-needhometip", e); }
    }
}

/// <summary>
/// Member order: follow the player (refused when the party is full, or below RefuseOrdersBelowMood when that is set). Round
/// MP2: member.follow, the talk's mode (Follow only); the answer's tooltip either way, as before.
/// </summary>
public class DialogActionScFollow : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Member m = npc != null ? ScRegistry.GetMember(npc.entityId) : null;      // a client: its copy (the server checks again)
            if (lp == null || npc == null || m == null) return;
            ScActions.Request("member.follow", r => ScTalkContext.Tell(player, r, true), m.Id, ScActionsMembers.ModeTalk);
        }
        catch (Exception e) { ScLog.Once("act-follow", e); }
    }
}

/// <summary>Member order: back to the post (walks when near, teleports when the player is inside the base). Round MP2: member.home.</summary>
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
            ScActions.Request("member.home", r => ScTalkContext.Tell(player, r, true), m.Id, ScActionsMembers.ModeTalk);
        }
        catch (Exception e) { ScLog.Once("act-gohome", e); }
    }
}

/// <summary>
/// Member order, to a runner met out in the world: head home now (ScRunners.Recall). Round MP2: member.follow, the talk's
/// recall mode (in person: no radio needed).
/// </summary>
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
            ScActions.Request("member.follow", r => ScTalkContext.Tell(player, r, true), m.Id, ScActionsMembers.ModeTalkRecall);
        }
        catch (Exception e) { ScLog.Once("act-recall", e); }
    }
}

/// <summary>
/// Member order: a supply run for the resource named in the action's value (food|meds|ammo|mats|fuel), or (round 33c, M33, 3.4)
/// a mining run - mine | clay (ScMining.Send). Round MP2: member.run (member, kind or -1, job or -1); only a refusal is told,
/// as before (the run's own news says the rest).
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
#if SC_V34
            RunJob job;
            if (ScMining.TryJob(Value, out job))
            {
                ScActions.Request("member.run", r => ScTalkContext.Tell(player, r, false), m.Id, -1, (int)job);
                return;
            }
#endif
            Resource kind;
            if (!ScNames.TryResource(Value, out kind)) { ScLog.Err("dialog action ScRun: unknown resource '" + Value + "'"); return; }
            ScActions.Request("member.run", r => ScTalkContext.Tell(player, r, false), m.Id, (int)kind, -1);
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

/// <summary>
/// Member order: leave the community (after the dialog's confirmation statement). Round MP2: member.leave, the talk's mode -
/// Leave's own tell (to the whole community) is the answer; only a refusal with words of its own is told here.
/// </summary>
public class DialogActionScLeave : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Member m = npc != null ? ScRegistry.GetMember(npc.entityId) : null;      // a client: its copy (the server checks again)
            if (m == null) return;
            ScActions.Request("member.leave", r => ScTalkContext.Tell(player, r, false), m.Id, ScActionsMembers.ModeTalk);
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
            if (ScGates.IsForeignMember(npc, player)) return false;     // round MP6 (part G): another community's member takes no orders
            // round MP2 (E9): by the value - a client's copy keeps the 0 a dismissal sent it (ScCommunity.ClearSynced)
            if (npc.Buffs.GetCustomVar("sc_member") <= 0f) return true;
            return npc.Buffs.GetCustomVar("sc_following") > 0f;
        }
        catch (Exception e) { ScLog.Once("req-followerverb", e); return true; }
    }
}
