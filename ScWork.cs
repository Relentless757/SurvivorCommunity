using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SurvivorCommunity;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// C1 (round 22, 3.4): a resident's work shift (design section 9, option b2). 0-XNPCCore's crafting needs a leader for
    /// everything that matters - the Craft... row, the order's authority, which stations are near, whose chests and claims count -
    /// and every one of those checks asks SCore's GetLeaderOrOwner, which falls back on the Owner cvar. So a shift links a
    /// resident to the player through the Owner cvar alone (no Leader: 0-XNPCCore's hire-only behaviours, SCore's SetLeader
    /// side effects and Voice Control's hire roster never start), and 0-XNPCCore's tested craft job runs unchanged.
    ///
    ///  - Start: the dialog's "I have work for you." (she waits where she stands, the Craft... menu opens) or the Community tab's
    ///    "Call to work" (she walks to the player's block on her Guard order). Status stays Resident: she keeps her home duties,
    ///    the daily bonuses and the base's defence, and takes no follower slot. The body carries sc_shift = 1 for the dialog.
    ///  - While on it: the resident ticker hands her to Tick instead of re-stationing her (she has an owner) or self-healing her post;
    ///    SC's roam, leash, at-post and quiet-night actions stand down (ScNotOnShift, Config/utilityai.xml), so 0-XNPCCore's Guard
    ///    holds her at the craft spot and its CraftSdcsHere runs; her defence and flight stay on. What she makes (Patches_Craft's
    ///    ledger) moves from her inventory window into the home crates every DepositSeconds.
    ///  - End: called without an order for WorkCallSeconds, idle after her last job for WorkIdleSeconds (at once at night), "That's
    ///    all for now.", "End work", any other order, the Owner link gone, a watch posting - then ScCommunity.Station: Detach drops
    ///    the Owner link and 0-XNPCCore's job (unmade items' ingredients come back), and she walks back to her post. Follow, a run,
    ///    an outpost posting and Leave end it first without the walk (EndFor). Shifts live in memory only: after a reload the body's
    ///    Owner cvar makes the resident rule station her.
    /// </summary>
    public static class ScWork
    {
        public const string ShiftCvar = "sc_shift";
        /// <summary>The window-to-crates move runs this often (real seconds of game time) while she is on a shift.</summary>
        private const float DepositSeconds = 5f;
        /// <summary>0-XNPCCore's work package (Voice Control's JobOrderRules.WorkPackage): without it she takes no job order.</summary>
        private const string WorkPackage = "SdcsNpcLoot";

        private enum Phase { Called, Working, Idle }

        private sealed class Shift
        {
            public float Started, PhaseSince, NextDeposit;
            public Phase Phase;
            public bool Called;
            public int Jobs, Deposited, Crafts;
            public bool SaidFull;
            /// <summary>Item type -> how many of what she made on this shift are still to go to the crates.</summary>
            public readonly Dictionary<int, int> Made = new Dictionary<int, int>();
        }

        private static readonly Dictionary<int, Shift> shifts = new Dictionary<int, Shift>();
        private static readonly List<int> scratch = new List<int>();
        private static int starts, ends, staleCleared, depositedTotal;
        private static readonly Dictionary<string, int> endReasons = new Dictionary<string, int>();

        public static void Reset()
        {
            shifts.Clear();
            starts = ends = staleCleared = depositedTotal = 0;
            endReasons.Clear();
        }

        public static bool OnShift(int memberId) { return shifts.ContainsKey(memberId); }

        public static int Count { get { return shifts.Count; } }

        // ------------------------------------------------------------------ start ----

        /// <summary>Can this member start a shift now (the dialog's ScCanWork, the tab's Call to work)? error: a Localization key.</summary>
        public static bool CanStart(Member m, EntityAlive npc, EntityPlayer player, out string error)
        {
            error = null;
            if (!ScConfig.WorkShiftsEnabled) { error = "scWorkOff"; return false; }
            if (!ScCommunity.IsHost) { error = "scHostOnly"; return false; }
            if (m == null || player == null) { error = "scInternal"; return false; }
            if (OnShift(m.Id)) { error = "scWorkAlready"; return false; }
            if (m.Status != MemberStatus.Resident) { error = "scWorkNotResident"; return false; }
            if (ScWatch.OnWatch(m)) { error = "scWatchBusy"; return false; }
            if (npc == null || npc.IsDead() || npc.Buffs == null) { error = "scNotLoaded"; return false; }
            if (!ScXnpc.HasXnpcLevels(npc) || !HasWorkPackage(npc)) { error = "scWorkCantCraft"; return false; }
            if (!ScZone.HasHome || !ScZone.Contains(npc.GetBlockPosition()) || !ScZone.Contains(player.GetBlockPosition())) { error = "scWorkNotHome"; return false; }
            return true;
        }

        /// <summary>Voice Control's own job test (VCCommandRouter.CannotRunJob -> JobOrderRules.Refuses): her AI packages hold SdcsNpcLoot.</summary>
        private static bool HasWorkPackage(EntityAlive npc)
        {
            if (npc.AIPackages == null) return false;
            for (int i = 0; i < npc.AIPackages.Count; i++)
                if (npc.AIPackages[i] != null && string.Equals(npc.AIPackages[i].Trim(), WorkPackage, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Puts her on a shift: the Owner link (the cvars ScNpc's V4 branch writes for a follower, no Leader), SCore's leader cache
        /// dropped so the link is seen at once, sc_shift, and the spot she holds on her Guard order until an order sets its own:
        /// the player's block when called, else where she stands.
        /// </summary>
        public static bool Start(Member m, EntityAlive npc, EntityPlayerLocal player, bool call, out string error)
        {
            if (!CanStart(m, npc, player, out error)) return false;
            try
            {
                npc.Buffs.SetCustomVar("Owner", player.entityId);
                player.Buffs.SetCustomVar("EntityID", player.entityId);
                SphereCache.LeaderCache.Remove(npc.entityId);
                npc.Buffs.SetCustomVar(ShiftCvar, 1f);
                ScCommunity.ApplyPost(npc, EntityUtilities.CenterPosition(call ? player.position : npc.position));
                if (call) ScCommunity.DropPath(npc);
                float now = Time.time;
                shifts[m.Id] = new Shift { Started = now, PhaseSince = now, NextDeposit = now + DepositSeconds, Phase = Phase.Called, Called = call };
                starts++;
                ScLog.Out(m.Name + " starts a work shift (" + (call ? "called from the Community tab" : "talked to") + ") at " + npc.GetBlockPosition()
                          + " - " + m.Specialty + " " + m.SpecLevel + " stars, roles " + ScCraft.MaskText(ScCraft.ReadMask(npc)));
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("work-start", e);
                error = "scInternal";
                return false;
            }
        }

        // ------------------------------------------------------------------ end ----

        /// <summary>
        /// Ends her shift: the last move of what she made into the crates, sc_shift off, then ScCommunity.Station (station: back to
        /// her post; Detach drops the Owner link and 0-XNPCCore's job, which refunds what was not made) or ScCommunity.Detach alone
        /// (EndFor: a verb that moves her on itself). npc may be null (nothing to do on a body then).
        /// </summary>
        public static void End(Member m, EntityAlive npc, string reason, bool station, bool tell = true)
        {
            if (m == null) return;
            Shift s;
            shifts.TryGetValue(m.Id, out s);
            shifts.Remove(m.Id);
            try
            {
                if (npc != null && npc.Buffs != null && !npc.IsDead())
                {
                    if (s != null) Deposit(m, npc, s, true);
                    npc.Buffs.RemoveCustomVar(ShiftCvar);
                    if (station) ScCommunity.Station(m, npc);
                    else ScCommunity.Detach(npc, ScCommunity.LocalPlayer);
                    EntityAliveSDXV4 v4 = npc as EntityAliveSDXV4;
                    if (v4 != null) v4.HandleNavObject();          // SCore drew a hire marker for the owner link: it goes with it
                }
                else DropHireMark(m.Id);
                ends++;
                int n;
                endReasons.TryGetValue(reason ?? "?", out n);
                endReasons[reason ?? "?"] = n + 1;
                ScLog.Out(m.Name + "'s work shift ends (" + reason + ")" + (s != null ? " after " + (int)(Time.time - s.Started) + " s, " + s.Jobs + " job(s), "
                          + s.Crafts + " item(s) made, " + s.Deposited + " put in the crates" : ""));
                if (station && tell && npc != null) ScCommunity.Tell(ScCommunity.LocalPlayer, string.Format(Localization.Get("scWorkBack"), m.Name), "");
            }
            catch (Exception e)
            {
                ScLog.Once("work-end", e);
            }
        }

        /// <summary>
        /// Follow, a supply run, an outpost posting, a watch posting and Leave: a member on a shift comes off it first (her job
        /// dropped, unmade items refunded) without the walk back to her post - the verb moves her on itself.
        /// </summary>
        public static void EndFor(Member m, string verb)
        {
            if (m == null || !OnShift(m.Id)) return;
            End(m, ScCommunity.FindNpc(m.Id), verb, false, false);
        }

        /// <summary>ScCommunity.Station (any stationing path) and CleanupStale: the shift's state goes, with no further step.</summary>
        public static void Forget(int memberId, EntityAlive npc)
        {
            if (shifts.Remove(memberId)) ScLog.Verbose("work shift of " + memberId + " forgotten (stationed or cleaned up)");
            if (npc != null && npc.Buffs != null && npc.Buffs.HasCustomVar(ShiftCvar)) npc.Buffs.RemoveCustomVar(ShiftCvar);
        }

        // ------------------------------------------------------------------ the ticker ----

        /// <summary>
        /// ScTicker's Resident branch, first thing: true when she is on a shift (ticked here; the branch's re-station, post check,
        /// self-heal and watchdog are skipped), false otherwise - a stale sc_shift (a shift a reload ended) is taken off then.
        /// </summary>
        public static bool Resident(Member m, EntityAlive npc, EntityPlayerLocal player)
        {
            Shift s;
            if (m == null || npc == null || !shifts.TryGetValue(m.Id, out s))
            {
                if (npc != null && npc.Buffs != null && npc.Buffs.HasCustomVar(ShiftCvar)) { npc.Buffs.RemoveCustomVar(ShiftCvar); staleCleared++; }
                return false;
            }
            try
            {
                Tick(m, npc, s);
            }
            catch (Exception e)
            {
                ScLog.Once("work-tick", e);
            }
            return true;
        }

        private static void Tick(Member m, EntityAlive npc, Shift s)
        {
            float now = Time.time;
            if (npc.Buffs.GetCustomVar("Owner") <= 0f) { End(m, npc, "the owner link is gone", true); return; }
            if (ScWatch.OnWatch(m)) { End(m, npc, "posted on watch", true); return; }
            int mode = SdcsLootMode.Mode(npc);
            EntityUtilities.Orders order;
            if (mode == 0 && ScOrders.TryCurrent(npc, out order) && order != EntityUtilities.Orders.Guard)
            {
                End(m, npc, "given the order " + order, true);
                return;
            }
            bool working = mode != 0 || SdcsCraftJob.Get(m.Id) != null;
            if (working)
            {
                if (s.Phase != Phase.Working) { s.Phase = Phase.Working; s.PhaseSince = now; s.Jobs++; ScLog.Verbose(m.Name + ": at work (job " + s.Jobs + ", mode " + mode + ")"); }
            }
            else if (s.Phase == Phase.Working)
            {
                s.Phase = Phase.Idle;
                s.PhaseSince = now;
            }
            World w = ScCommunity.World;
            if (s.Phase == Phase.Called && now - s.PhaseSince >= Math.Max(10, ScConfig.WorkCallSeconds))
            {
                End(m, npc, "no order in " + ScConfig.WorkCallSeconds + " s", true);
                return;
            }
            if (s.Phase == Phase.Idle)
            {
                bool night = w != null && !w.IsDaytime();
                if (night || now - s.PhaseSince >= Math.Max(10, ScConfig.WorkIdleSeconds))
                {
                    End(m, npc, night ? "done, and it is night" : "done", true);
                    return;
                }
            }
            if (now >= s.NextDeposit)
            {
                s.NextDeposit = now + DepositSeconds;
                Deposit(m, npc, s, false);
            }
        }

        /// <summary>
        /// ScTicker, once a second: a shift whose member left, died, is no longer a resident (a path that did not end it) or whose
        /// body unloaded is dropped - 0-XNPCCore forgets an unloaded body's order, and the resident rule stations her when she loads.
        /// </summary>
        public static void Sweep(EntityPlayerLocal player)
        {
            if (shifts.Count == 0) return;
            scratch.Clear();
            scratch.AddRange(shifts.Keys);
            foreach (int id in scratch)
            {
                Member m = ScRegistry.GetMember(id);
                EntityAlive npc = m != null ? ScCommunity.FindNpc(id) : null;
                if (m != null && m.Status == MemberStatus.Resident && npc != null) continue;
                shifts.Remove(id);
                string why = m == null ? "no longer a member" : m.Status != MemberStatus.Resident ? "now " + m.Status : "her body unloaded";
                if (npc != null && npc.Buffs != null)
                {
                    npc.Buffs.RemoveCustomVar(ShiftCvar);
                    if (m != null && m.Status != MemberStatus.Following && m.Status != MemberStatus.PendingStation) ScCommunity.Detach(npc, player);
                }
                else if (m == null || (m.Status != MemberStatus.Following && m.Status != MemberStatus.PendingStation)) DropHireMark(id);
                ends++;
                ScLog.Out("work shift of " + (m != null ? m.Name : id.ToString()) + " dropped (" + why + ")");
            }
        }

        /// <summary>
        /// SCore's V4 leader component writes hired_&lt;id&gt; on the player every frame while the owner link stands; Detach takes it off
        /// again. With no body to detach (unloaded, gone) it is taken off here, as Detach would.
        /// </summary>
        private static void DropHireMark(int id)
        {
            EntityPlayerLocal p = ScCommunity.LocalPlayer;
            if (p == null || p.Buffs == null || !p.Buffs.HasCustomVar("hired_" + id)) return;
            p.Buffs.RemoveCustomVar("hired_" + id);
            EntityUtilities.CheckForDanglingHires(p.entityId);
        }

        // ------------------------------------------------------------------ what she made ----

        /// <summary>Patches_Craft's XP hooks: an item made on a shift (type and count) is owed to the home crates.</summary>
        public static void NoteMade(int memberId, int itemType, int count)
        {
            Shift s;
            if (itemType <= 0 || count <= 0 || !shifts.TryGetValue(memberId, out s)) return;
            int n;
            s.Made.TryGetValue(itemType, out n);
            s.Made[itemType] = n + count;
            s.Crafts += count;
        }

        /// <summary>
        /// Q4 (a): from her inventory window into the home zone's linked storage, only what she made on this shift (the ledger), so
        /// her gear, ammo and meds stay. Never while her window is open (on the host's screen, a remote player's, 0-XNPCCore's stock
        /// trade); what does not fit stays in the window.
        /// </summary>
        private static void Deposit(Member m, EntityAlive npc, Shift s, bool final)
        {
            if (!ScConfig.WorkDepositOutput || s.Made.Count == 0 || !HarvestManager.Has(npc.entityId)) return;
            SCoreLootContainer window = HarvestManager.GetOrCreate(npc.entityId);
            if (window == null || window.IsUserAccessing() || ScAmmo.IsRemoteOpen(npc.entityId) || ScAmmo.XnpcTradeOpen(npc.entityId)) return;
            ItemStack[] items = window.items;
            if (items == null) return;
            int moved = 0;
            bool full = false;
            for (int i = 0; i < items.Length && !full; i++)
            {
                ItemStack st = items[i];
                if (st == null || st.IsEmpty() || st.itemValue == null) continue;
                int owed;
                if (!s.Made.TryGetValue(st.itemValue.type, out owed) || owed <= 0) continue;
                int k = Math.Min(st.count, owed);
                Vector3i box;
                if (!ScStorage.DepositInZone(new ItemStack(st.itemValue.Clone(), k), out box)) { full = true; break; }
                s.Made[st.itemValue.type] = owed - k;
                moved += k;
                // 3.4: the window's slots are bound to its grid - emptied in place, never replaced (ScAmmo's rule)
                if (k >= st.count) HarvestManager.SetSlot(items, i, null);
                else st.count -= k;
            }
            if (moved > 0)
            {
                window.Parent?.NotifyListeners();
                s.Deposited += moved;
                depositedTotal += moved;
                ScLog.Verbose(m.Name + ": " + moved + " item(s) she made put in the home crates" + (final ? " (end of the shift)" : ""));
            }
            if (full && !s.SaidFull)
            {
                s.SaidFull = true;
                ScCommunity.Tell(ScCommunity.LocalPlayer, string.Format(Localization.Get("scWorkCratesFull"), m.Name), "");
            }
        }

        // ------------------------------------------------------------------ the Community tab ----

        /// <summary>The tab's Call to work / End work.</summary>
        public static void CallOrEnd(Member m, EntityPlayerLocal player, out string message, out bool ok)
        {
            ok = false;
            message = "";
            if (m == null || player == null) return;
            if (OnShift(m.Id))
            {
                End(m, ScCommunity.FindNpc(m.Id), "ended from the Community tab", true, false);
                message = string.Format(Localization.Get("scWorkBack"), m.Name);
                ok = true;
                return;
            }
            string err;
            EntityAlive npc = ScCommunity.FindNpc(m.Id);
            if (!Start(m, npc, player, true, out err)) { message = Localization.Get(err ?? "scInternal"); return; }
            ScCommunity.Tell(player, string.Format(Localization.Get("scEncCallSays"), m.Name, Localization.Get("scWorkCallSaid")), "");
            message = string.Format(Localization.Get("scWorkComing"), m.Name);
            ok = true;
        }

        private const string Body = "226,220,208,255", Dim = "140,134,124,255", Good = "142,208,108,255";

        /// <summary>The tab's work-and-roles bindings (XUiC_ScCommunity.WorkBinding): always a string, before a world exists too.</summary>
        public static string UiValue(string name, Member sel, bool runPicking, bool workOpen)
        {
            switch (name)
            {
                case "workmenuvisible": return (sel != null && !runPicking && sel.Status != MemberStatus.Arriving).ToString();
                case "workmenulabel": return Localization.Get(workOpen && sel != null ? "scUiWorkClose" : "scUiWorkMenu");
                case "workpicking": return (workOpen && sel != null && !runPicking).ToString();
                case "msgvisible": return (!runPicking && !(workOpen && sel != null)).ToString();
                case "worklabel": return Localization.Get(sel != null && OnShift(sel.Id) ? "scUiWorkEnd" : "scUiWorkCall");
                case "workcolor":
                {
                    if (sel == null) return Dim;
                    if (OnShift(sel.Id)) return Body;
                    string why;
                    return CanStart(sel, ScCommunity.FindNpc(sel.Id), ScCommunity.LocalPlayer, out why) ? Body : Dim;
                }
            }
            if (name.Length == 10 && name.StartsWith("role") && name.EndsWith("color"))
            {
                int r = name[4] - '0';
                if (sel == null || r < 0 || r > 3) return Dim;
                EntityAlive npc = ScCommunity.FindNpc(sel.Id);
                int mask = npc != null ? ScCraft.ReadMask(npc) : sel.CraftRoles;
                return mask >= 0 && (mask & (1 << r)) != 0 ? Good : Dim;
            }
            return null;
        }

        // ------------------------------------------------------------------ console ----

        public static string Describe(Member m, EntityAlive npc)
        {
            Shift s;
            string cvar = npc != null && npc.Buffs != null ? " (sc_shift=" + (npc.Buffs.HasCustomVar(ShiftCvar) ? "1" : "-") + " Owner="
                          + npc.Buffs.GetCustomVar("Owner").ToString("0", CultureInfo.InvariantCulture) + " Leader=" + npc.Buffs.GetCustomVar("Leader").ToString("0", CultureInfo.InvariantCulture)
                          + " SdcsLootMode=" + npc.Buffs.GetCustomVar("SdcsLootMode").ToString("0", CultureInfo.InvariantCulture) + ")" : "";
            if (!shifts.TryGetValue(m.Id, out s)) return "none" + cvar;
            float now = Time.time;
            var made = new StringBuilder();
            foreach (var kv in s.Made)
            {
                ItemClass ic = ItemClass.GetForId(kv.Key);
                made.Append(made.Length > 0 ? ", " : "").Append(ic != null ? ic.GetItemName() : kv.Key.ToString()).Append(" x").Append(kv.Value);
            }
            return s.Phase + " for " + (int)(now - s.PhaseSince) + " s (shift " + (int)(now - s.Started) + " s, " + (s.Called ? "called" : "talked to") + "), jobs " + s.Jobs
                   + ", made " + s.Crafts + ", in the crates " + s.Deposited + ", still owed to the crates [" + made + "]" + cvar
                   + "; craft order " + (SdcsCraftJob.Get(m.Id) != null ? "yes" : "no");
        }

        public static string Summary()
        {
            var parts = new List<string>();
            foreach (var kv in endReasons) parts.Add(kv.Key + " " + kv.Value);
            return "work shifts: " + shifts.Count + " now, " + starts + " started, " + ends + " ended (" + string.Join(", ", parts.ToArray()) + "), stale sc_shift cleared "
                   + staleCleared + ", items put in the crates " + depositedTotal;
        }
    }
}

// ---------------------------------------------------------------- dialog operators (global, as DialogOps.cs) ----

/// <summary>C1: the NPC is a resident who can start a work shift now (ScWork.CanStart).</summary>
public class DialogRequirementScCanWork : BaseDialogRequirement
{
    public override bool CheckRequirement(EntityPlayer player, EntityNPC talkingTo)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Member m = npc != null ? ScRegistry.GetMember(npc.entityId) : null;
            string why;
            return m != null && ScWork.CanStart(m, npc, player, out why);
        }
        catch (Exception e) { ScLog.Once("req-canwork", e); return false; }
    }
}

/// <summary>C1: "I have work for you." - the shift starts where she stands; the response's next action opens 0-XNPCCore's Craft menu.</summary>
public class DialogActionScWorkStart : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityPlayerLocal lp = player as EntityPlayerLocal;
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Member m = npc != null ? ScRegistry.GetMember(npc.entityId) : null;
            if (lp == null || m == null) return;
            string err;
            if (!ScWork.Start(m, npc, lp, false, out err)) ScCommunity.Tell(player, Localization.Get(err ?? "scInternal"), "ui_denied");
        }
        catch (Exception e) { ScLog.Once("act-workstart", e); }
    }
}

/// <summary>C1: "That's all for now." - the shift ends, she says so as the talk closes and walks back to her post.</summary>
public class DialogActionScWorkEnd : BaseDialogAction
{
    public override void PerformAction(EntityPlayer player)
    {
        try
        {
            EntityAlive npc = ScFavours.CurrentNpc(player);
            Member m = npc != null ? ScRegistry.GetMember(npc.entityId) : null;
            if (m == null || !ScWork.OnShift(m.Id)) return;
            ScWork.End(m, npc, "That's all for now.", true, false);
            ScCommunity.Tell(player, string.Format(Localization.Get("scEncCallSays"), m.Name, Localization.Get("scWorkEndSaid")), "");
            ScVoice.SayAfterTalk(npc, "scWorkEndSaid");
        }
        catch (Exception e) { ScLog.Once("act-workend", e); }
    }
}

namespace UAI
{
    /// <summary>
    /// C1: 1 unless the entity is a member on a work shift (ScWork). Gates SC's resident roam, leash, at-post and quiet night
    /// (Config/utilityai.xml, 3.4 block) so 0-XNPCCore's Guard holds her at the craft spot and its craft task runs.
    /// </summary>
    public class UAIConsiderationScNotOnShift : UAIConsiderationBase
    {
        public override float GetScore(Context _context, object target)
        {
            return ScWork.OnShift(_context.Self.entityId) ? 0f : 1f;
        }
    }
}
