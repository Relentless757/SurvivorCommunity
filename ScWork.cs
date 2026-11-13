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
    ///  - Start: a craft order or a job given while you talk to her (round 28: the talk hold below turns into a shift) or the
    ///    Community tab's "Call to work" (she walks to the player's block on her Guard order). Status stays Resident: she keeps
    ///    her home duties, the daily bonuses and the base's defence, and takes no follower slot. The body carries sc_shift = 1
    ///    for the dialog.
    ///  - Round 28 (X3, the user's decision 1, 2026-11-10: "talk link"): talking to a resident at home links her to the
    ///    player the same way, in a fourth phase, Talk (StartTalk, from Patches_Craft's dialog-open postfix): the Owner link
    ///    and a Guard spot where she stands, no sc_shift (so "That's all for now." stays hidden and the supply run and "Ask to
    ///    leave" stay offered). 0-XNPCCore's hired rows (Craft..., Jobs and gear, Show me your inventory) need that link. It
    ///    lasts while the talk or a window opened from it is open (TalkOpen) and ends quietly TalkGraceSeconds after the last
    ///    of them closed (she walks back to her post), or TalkCapSeconds at the latest; a craft order or a job given meanwhile
    ///    makes it an ordinary shift (Promote). This replaced the dialog's "I have work for you." (decision 2: "Craft..." for
    ///    every member) and with it the Called phase's 3-minute leftover after the craft window was closed without an order.
    ///  - While on it: the resident ticker hands her to Tick instead of re-stationing her (she has an owner) or self-healing her post;
    ///    SC's roam, leash, at-post and quiet-night actions stand down (ScNotOnShift, Config/utilityai.xml), so 0-XNPCCore's Guard
    ///    holds her at the craft spot and its CraftSdcsHere runs; her defence and flight stay on. What she makes (Patches_Craft's
    ///    ledger) moves from her inventory window into the home crates every DepositSeconds.
    ///  - End: called without an order for WorkCallSeconds, idle after her last job for WorkIdleSeconds (at once at night), "That's
    ///    all for now.", "End work", any other order, the Owner link gone, a watch posting - then ScCommunity.Station: Detach drops
    ///    the Owner link and 0-XNPCCore's job (unmade items' ingredients come back), and she walks back to her post. Follow, a run,
    ///    an outpost posting and Leave end it first without the walk (EndFor). Shifts live in memory only: after a reload the body's
    ///    Owner cvar makes the resident rule station her.
    ///  - Round 29a (J29, the user's decisions of 2026-11-11): home jobs only for residents - Chop wood, Pick, Butcher, Base repair,
    ///    Combat repair and Craft; Follow + loot, Loot here, Mine and Dig clay are refused (ScJobs; a field job that slips through
    ///    is taken back here). What she gathers on a home job is noted (ScJobs' WindowAdd hook, NoteGathered) and goes to the
    ///    crates with what she makes, a full pack is emptied there and the job carries on (ResumeAfterFullPack, from C1f). Base
    ///    repair and Combat repair (modes 8, 9) are a duty (DutyTick): what is missing is said at its start, it ends after
    ///    RepairDutyIdleMinutes with nothing she can repair (Base repair at once at night; Combat repair never at night and not
    ///    while a foe is near - review M1), and the residents' stuck rescue may act on her meanwhile (AllowsWatchdog, round 29b's
    ///    hook).
    /// </summary>
    public static class ScWork
    {
        public const string ShiftCvar = "sc_shift";
        /// <summary>The window-to-crates move runs this often (real seconds of game time) while she is on a shift.</summary>
        private const float DepositSeconds = 5f;
        /// <summary>0-XNPCCore's work package (Voice Control's JobOrderRules.WorkPackage): without it she takes no job order.</summary>
        private const string WorkPackage = "SdcsNpcLoot";

        private enum Phase { Called, Working, Idle, Talk }

        /// <summary>Round 28: a talk hold ends this many real seconds after the last talk window closed (the ring's close-and-reopen hop fits).</summary>
        private const float TalkGraceSeconds = 2f;
        /// <summary>Round 28: whatever TalkOpen says, a talk hold ends after this many real seconds (a window group another mod renamed).</summary>
        private const float TalkCapSeconds = 600f;
        /// <summary>Round 28: the window groups a talk with her can open (vanilla's dialog and looting, Voice Control's craft window and wheel).</summary>
        private const string DialogGroup = "dialog", CraftGroup = "NpcCraftGroup", WheelGroup = "VCFormationRadialGroup", LootGroup = "looting";

        private sealed class Shift
        {
            public float Started, PhaseSince, NextDeposit;
            /// <summary>Round 28, Talk only (real time): when the hold started, and when a talk window on her was last seen open.</summary>
            public float TalkSince, TalkSeen;
            /// <summary>Round 29a (round 28's Fable glance, LOW A): the TalkCapSeconds cap's own clock - restarted while the talk with her is open.</summary>
            public float CapSince;
            public Phase Phase;
            public bool Called;
            public int Jobs, Deposited, Crafts;
            public bool SaidFull;
            /// <summary>Item type -> how many of what she made on this shift are still to go to the crates.</summary>
            public readonly Dictionary<int, int> Made = new Dictionary<int, int>();
            /// <summary>
            /// Round 33a (round 29 glance LOW 2): item type -> how many she made at a station that were still in its output when
            /// they were noted (Patches_Craft C1d / C1e, NoteMadeAtStation). Not owed yet: they move onto Made as they reach her
            /// window (ScJobs' WindowAdd hook, NoteArrived), so TrimLedger never drops them and an item is owed only once it is
            /// there. PendingSeen: the last note, or the last Deposit with her craft order still alive (AgePending).
            /// </summary>
            public readonly Dictionary<int, int> Pending = new Dictionary<int, int>();
            public float PendingSeen;
            /// <summary>Round 29a (J29): what she gathered on home jobs (ledgered with Made), pack-full resumes, the full pack said.</summary>
            public int Gathered, Resumes;
            public bool SaidPackFull;
            /// <summary>Round 29a: on a Base repair / Combat repair duty (job mode 8 or 9), its mode, and its clocks (game time).</summary>
            public bool Duty;
            public int DutyMode;
            public float DutyStart, DutyIdleSince, NextDutyCheck;
            public bool DutyChecked;
            /// <summary>Round 29a: the last check's answer, for the console ("can repair", "nothing damaged" ...).</summary>
            public string DutyState = "";
        }

        private static readonly Dictionary<int, Shift> shifts = new Dictionary<int, Shift>();
        private static readonly List<int> scratch = new List<int>();
        private static int starts, ends, staleCleared, depositedTotal, jobsDropped, windowCancels;
        private static int talkStarts, talkPromoted, talkEnds, talkSkipped;
        private static int gatheredTotal, packResumes, packEnds, fieldJobsTaken, dutiesStarted, dutyIdleEnds;
        /// <summary>Round 29 review L4: ledger items no longer in her window (TrimLedger), and its scratch tables.</summary>
        private static int ledgerTrimmed;
        /// <summary>Round 33a (round 29 glance LOW 2): station output noted ahead of its items, those that reached her window, those given up on.</summary>
        private static int pendingNoted, pendingArrived, pendingExpired;
        /// <summary>Round 33a: station output still expected this long after her craft order is gone (it moves the output to her window).</summary>
        private const float PendingKeepSeconds = 120f;
        private static readonly Dictionary<int, int> inWindow = new Dictionary<int, int>();
        private static readonly List<int> ledgerKeys = new List<int>();
        private static bool talkGroupsLogged;
        private static readonly Dictionary<string, int> endReasons = new Dictionary<string, int>();

        public static void Reset()
        {
            shifts.Clear();
            starts = ends = staleCleared = depositedTotal = jobsDropped = windowCancels = 0;
            talkStarts = talkPromoted = talkEnds = talkSkipped = 0;
            gatheredTotal = packResumes = packEnds = fieldJobsTaken = dutiesStarted = dutyIdleEnds = 0;
            ledgerTrimmed = 0;
            pendingNoted = pendingArrived = pendingExpired = 0;
            talkGroupsLogged = false;
            endReasons.Clear();
            ScJobs.Reset();
        }

        /// <summary>On a shift or a talk hold (round 28): the Owner link stands, and every shift rule (the ticker hand-off, the UAI
        /// stand-downs, EngagesAtWill, ShiftOwned, EndFor) applies.</summary>
        public static bool OnShift(int memberId) { return shifts.ContainsKey(memberId); }

        /// <summary>Round 28: on a talk hold - the link of a talk, not yet a work shift.</summary>
        public static bool InTalk(int memberId)
        {
            Shift s;
            return shifts.TryGetValue(memberId, out s) && s.Phase == Phase.Talk;
        }

        /// <summary>On a real work shift (the Community tab's Work button): a talk hold does not count.</summary>
        public static bool OnWorkShift(int memberId) { return OnShift(memberId) && !InTalk(memberId); }

        public static int Count { get { return shifts.Count; } }

        // ------------------------------------------------------------------ start ----

        /// <summary>Can this member start a shift now (the dialog's ScCanWork, the tab's Call to work)? error: a Localization key.</summary>
        public static bool CanStart(Member m, EntityAlive npc, EntityPlayer player, out string error)
        {
            error = null;
            if (!ScConfig.WorkShiftsEnabled) { error = "scWorkOff"; return false; }
            if (!ScCommunity.IsHost) { error = "scHostOnly"; return false; }
            if (m == null || player == null) { error = "scInternal"; return false; }
            if (OnWorkShift(m.Id)) { error = "scWorkAlready"; return false; }
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
            // round 28: a talk hold (the Community tab opened from her talk) gives way to the call - the link is written again below
            if (InTalk(m.Id))
            {
                shifts.Remove(m.Id);
                talkEnds++;
                ScLog.Verbose(m.Name + "'s talk hold gives way to a call to work");
            }
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

        // ------------------------------------------------------------------ the talk hold (round 28) ----

        /// <summary>
        /// Round 28 (X3): can a talk link her now? A resident at home, not on a shift or on watch, loaded and alive, both of you
        /// inside the home zone (CanStart's rules but the work package: Craft... keeps its own NPCCanCraft), TalkHold and
        /// WorkShiftsEnabled on, the host. why: null when she simply needs none (already on a shift), else the reason, for the log.
        /// </summary>
        public static bool CanHold(Member m, EntityAlive npc, EntityPlayer player, out string why)
        {
            why = null;
            if (m == null || npc == null || player == null) { why = "no member, body or player"; return false; }
            if (m.Status != MemberStatus.Resident) { why = "not a resident at home (" + m.Status + ")"; return false; }
            if (OnShift(m.Id)) return false;                                                    // linked already (a shift, a hold)
            if (!ScConfig.TalkHold || !ScConfig.WorkShiftsEnabled) { why = "TalkHold or WorkShiftsEnabled is off"; return false; }
            if (!ScCommunity.IsHost) { why = "not the host"; return false; }
            if (ScWatch.OnWatch(m)) { why = "on watch"; return false; }
            if (npc.IsDead() || npc.Buffs == null) { why = "no live body"; return false; }
            if (!ScXnpc.HasXnpcLevels(npc)) { why = "not a 0-XNPCCore body"; return false; }   // review L3
            if (!ScZone.HasHome || !ScZone.Contains(npc.GetBlockPosition()) || !ScZone.Contains(player.GetBlockPosition())) { why = "you or she outside the home zone"; return false; }
            return true;
        }

        /// <summary>
        /// Round 28 (X3): the talk hold. Patches_Craft's postfix on XUiC_DialogWindowGroup.OnOpen calls this as a talk with a
        /// member opens (her rows are filled on the list's next Update, so they see the link). The cvars Start writes - Owner,
        /// the player's EntityID, SCore's leader cache dropped - and a Guard spot where she stands (SC's roam and leash stand
        /// down on a shift: she stays while you talk), but no sc_shift. No log line but a verbose one: it is every talk.
        /// </summary>
        public static bool StartTalk(Member m, EntityAlive npc, EntityPlayerLocal player)
        {
            string why;
            if (!CanHold(m, npc, player, out why))
            {
                if (why != null && m != null && m.Status == MemberStatus.Resident)
                {
                    talkSkipped++;
                    ScLog.Verbose((m != null ? m.Name : "?") + ": no talk hold - " + why);
                }
                return false;
            }
            try
            {
                npc.Buffs.SetCustomVar("Owner", player.entityId);
                player.Buffs.SetCustomVar("EntityID", player.entityId);
                SphereCache.LeaderCache.Remove(npc.entityId);
                ScCommunity.ApplyPost(npc, EntityUtilities.CenterPosition(npc.position));
                float now = Time.time, real = Time.unscaledTime;
                shifts[m.Id] = new Shift { Started = now, PhaseSince = now, NextDeposit = now + DepositSeconds, Phase = Phase.Talk, TalkSince = real, TalkSeen = real, CapSince = real };
                talkStarts++;
                LogTalkGroupsOnce(player);
                ScLog.Verbose(m.Name + ": talk hold - linked to you while the talk and its windows are open (at " + npc.GetBlockPosition() + ")");
                return true;
            }
            catch (Exception e)
            {
                ScLog.Once("work-talk-start", e);
                return false;
            }
        }

        /// <summary>The window groups TalkOpen watches, once a session: a group another mod renamed would leave a hold to its cap.</summary>
        private static void LogTalkGroupsOnce(EntityPlayerLocal player)
        {
            if (talkGroupsLogged) return;
            talkGroupsLogged = true;
            try
            {
                GUIWindowManager wm = player != null && player.PlayerUI != null ? player.PlayerUI.windowManager : null;
                if (wm == null) return;
                var missing = new List<string>();
                foreach (string g in new[] { DialogGroup, CraftGroup, WheelGroup, LootGroup })
                    if (wm.GetWindow(g) == null) missing.Add(g);
                string line = "talk hold: a talk links a resident while one of these is open on her: " + DialogGroup + ", " + CraftGroup + ", " + WheelGroup + ", " + LootGroup;
                if (missing.Count == 0) ScLog.Out(line);
                else ScLog.Warn(line + " - NOT FOUND: " + string.Join(", ", missing.ToArray()) + " (Voice Control not loaded, or renamed: those windows do not hold the link)");
            }
            catch (Exception e)
            {
                ScLog.Once("work-talk-groups", e);
            }
        }

        /// <summary>
        /// Round 28: is a talk with her, or a window opened from it, open on the host's screen? The dialog with her as its
        /// respondent; or, while the player's CurrentNPC cvar is her (SCore sets it at every talk, Voice Control's hold-E ring
        /// too): Voice Control's craft window, the order wheel / her hold-E ring page, or the looting window bound to her SCore
        /// inventory (HarvestManager's container: "Show me your inventory", the ring's Inventory).
        /// </summary>
        private static bool TalkOpen(EntityAlive npc)
        {
            try
            {
                EntityPlayerLocal player = ScCommunity.LocalPlayer;
                LocalPlayerUI ui = player != null ? player.PlayerUI : null;
                GUIWindowManager wm = ui != null ? ui.windowManager : null;
                if (wm == null || npc == null) return false;
                if (DialogWithHer(ui, wm, npc)) return true;
                if (player.Buffs == null || (int)player.Buffs.GetCustomVar("CurrentNPC") != npc.entityId) return false;
                if (wm.IsWindowOpen(CraftGroup) || wm.IsWindowOpen(WheelGroup)) return true;
                return wm.IsWindowOpen(LootGroup) && LootBoundTo(wm, npc);
            }
            catch (Exception e)
            {
                ScLog.Once("work-talk-open", e);    // review L5: a throw here must not stop the grace and the cap
                return false;
            }
        }

        /// <summary>Round 28: the talk window is open with her as the respondent.</summary>
        private static bool DialogWithHer(LocalPlayerUI ui, GUIWindowManager wm, EntityAlive npc)
        {
            if (ui == null || wm == null || npc == null || !wm.IsWindowOpen(DialogGroup)) return false;
            EntityNPC r = ui.xui != null && ui.xui.Dialog != null ? ui.xui.Dialog.Respondent : null;
            return r != null && r.entityId == npc.entityId;
        }

        /// <summary>The looting window shows her SCore inventory (Voice Control's own test: the group's or its loot window's te).</summary>
        private static bool LootBoundTo(GUIWindowManager wm, EntityAlive npc)
        {
            try
            {
                if (!HarvestManager.Has(npc.entityId)) return false;
                XUiWindowGroup g = wm.GetWindow(LootGroup) as XUiWindowGroup;
                XUiC_LootWindowGroup lg = g != null ? g.Controller as XUiC_LootWindowGroup : null;
                if (lg == null) return false;
                TEFeatureStorage bound = lg.lootWindow != null && lg.lootWindow.te != null ? lg.lootWindow.te : lg.te;
                return bound != null && ReferenceEquals(bound, HarvestManager.GetOrCreate(npc.entityId));
            }
            catch (Exception e)
            {
                ScLog.Once("work-talk-loot", e);
                return false;
            }
        }

        /// <summary>Round 28: Tick, a talk hold with no job yet - it holds while TalkOpen, then ends quietly (she walks back to her post).</summary>
        private static void TalkTick(Member m, EntityAlive npc, Shift s)
        {
            float real = Time.unscaledTime;
            if (TalkOpen(npc)) s.TalkSeen = real;
            else if (real - s.TalkSeen >= TalkGraceSeconds)
            {
                End(m, npc, "the talk is over", true, false);
                return;
            }
            if (real - s.CapSince < TalkCapSeconds) return;
            EntityPlayerLocal player = ScCommunity.LocalPlayer;
            LocalPlayerUI ui = player != null ? player.PlayerUI : null;
            GUIWindowManager wm = ui != null ? ui.windowManager : null;
            // review L1: never under an open talk with her. Round 29a (round 28's Fable glance, LOW A): the cap's clock starts
            // again while the talk is open, so a window picked from a long talk (Craft..., her inventory, the wheel) gets the
            // whole cap (its own clock: TalkSince stays the talk's start, for the log and sc craft)
            if (DialogWithHer(ui, wm, npc)) { s.CapSince = real; return; }
            // review L1: a craft window, wheel or her inventory still open on her would be left without the link. Round 29a (LOW B):
            // only her windows - the craft window and the wheel while the player's CurrentNPC is her, the looting window while it
            // shows her inventory
            if (wm != null)
            {
                bool hers = player != null && player.Buffs != null && (int)player.Buffs.GetCustomVar("CurrentNPC") == npc.entityId;
                if (hers && wm.IsWindowOpen(CraftGroup)) wm.Close(CraftGroup);
                if (hers && wm.IsWindowOpen(WheelGroup)) wm.Close(WheelGroup);
                if (wm.IsWindowOpen(LootGroup) && LootBoundTo(wm, npc)) wm.Close(LootGroup);
            }
            End(m, npc, "the talk hold's " + (int)TalkCapSeconds + " s cap", true, false);
        }

        /// <summary>Round 28: her first craft order or job of a talk hold makes it a work shift (from here on today's rules).</summary>
        private static void Promote(Member m, EntityAlive npc, Shift s, int mode)
        {
            float now = Time.time;
            npc.Buffs.SetCustomVar(ShiftCvar, 1f);
            s.Phase = Phase.Working;
            s.Started = now;
            s.PhaseSince = now;
            s.Jobs++;
            starts++;
            talkPromoted++;
            ScLog.Out(m.Name + " starts a work shift (from the talk) at " + npc.GetBlockPosition() + " - " + m.Specialty + " " + m.SpecLevel
                      + " stars, roles " + ScCraft.MaskText(ScCraft.ReadMask(npc)) + " (job mode " + mode + ")");
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
            bool talk = s != null && s.Phase == Phase.Talk;     // round 28: a talk hold ends quietly (no tell, a verbose line)
            try
            {
                if (npc != null && npc.Buffs != null && !npc.IsDead())
                {
                    if (s != null) Deposit(m, npc, s, true);
                    npc.Buffs.RemoveCustomVar(ShiftCvar);
                    if (station) ScCommunity.Station(m, npc, !talk);   // Detach drops her craft order first (DropJob); review L6: no teleport after a talk
                    else ScCommunity.Detach(npc, ScCommunity.LocalPlayer);
                    DropMarker(npc);                                // SCore drew a hire marker for the owner link: it goes with it
                }
                else DropHireMark(m.Id);
                GiveUpPending(m, s, "her shift ended first");        // round 33 review L3: counted, not dropped unseen
                ScCraft.FlushXp(true);                              // the shift's XP is saved with its end (review #6)
                if (talk)
                {
                    talkEnds++;
                    ScLog.Verbose(m.Name + "'s talk hold ends (" + reason + ") after " + (int)(Time.unscaledTime - s.TalkSince) + " s");
                    return;
                }
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
        /// dropped, unmade items refunded) without the walk back to her post - the verb moves her on itself. A talk hold too
        /// (round 28: "Follow me", the supply run and "Ask to leave" are picked in the talk that holds her).
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
            DropMarker(npc);       // review #5: after Station's Detach (Go home, a reload's leftover link) the companion marker goes too
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
                if (npc != null && npc.Buffs != null && npc.Buffs.HasCustomVar(ShiftCvar))
                {
                    // review #2: a reload ended the shift (in memory) but the body kept its job state - 0-XNPCCore's craft-here
                    // mode and buff - beside the Owner cvar the resident rule now removes: they go too, so its craft task cannot
                    // pick it up again (C1f keeps a resident's ended job where she is meanwhile)
                    npc.Buffs.RemoveCustomVar(ShiftCvar);
                    SdcsLootMode.Clear(npc);
                    if (npc.Buffs.HasBuff(SdcsLootMode.EnableCraftHereBuff)) npc.Buffs.RemoveBuff(SdcsLootMode.EnableCraftHereBuff);
                    DropJob(npc, "a shift a reload ended");
                    staleCleared++;
                }
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
            // round 29a (J29, the user's decision 2): residents do home jobs only. A field job that got past ScJobs' refusals (a
            // save from before this round, another caller) is taken back: Follow + loot ends the shift (it told her to follow),
            // Loot here, Mine and Dig clay leave her without a job (Idle, then home; a talk hold just goes on)
            if (ScJobs.IsFieldJob(mode))
            {
                SdcsLootMode.Clear(npc);
                fieldJobsTaken++;
                ScJobs.TellFieldJob(m, npc, mode);
                ScLog.Out(m.Name + ": job mode " + mode + " is a follower's job - taken back on her shift");
                if (mode == 1) { End(m, npc, "Follow + loot is a follower's job", true, false); return; }
                mode = 0;
            }
            EntityUtilities.Orders order;
            // any order but Guard ends the shift (a voice Follow, a Stay ...) and a talk hold (round 28). X2c: the craft window's
            // Cancel, which Voice Control sends as a Stay, never gets here for a shift - Patches_Craft C1n drops only the job
            // (CancelJobOnShift)
            if (mode == 0 && ScOrders.TryCurrent(npc, out order) && order != EntityUtilities.Orders.Guard)
            {
                End(m, npc, "given the order " + order, true);
                return;
            }
            // review #1: a craft order whose mode is gone (it would never end by itself) is cleared, its holdings refunded;
            // working = a craft order under the craft-here mode, or another 0-XNPCCore job (loot, harvest ... by voice)
            bool hasJob = SdcsCraftJob.Get(m.Id) != null;
            if (hasJob && mode != 10) { DropJob(npc, "its job mode is gone"); hasJob = false; }
            bool working = mode == 10 ? hasJob : mode != 0;
            // round 28 (X3): a talk hold becomes a work shift with her first craft order or job; until then it lasts while the talk is open
            if (s.Phase == Phase.Talk)
            {
                if (!working)
                {
                    TalkTick(m, npc, s);
                    return;
                }
                Promote(m, npc, s, mode);
            }
            if (working)
            {
                if (s.Phase != Phase.Working) { s.Phase = Phase.Working; s.PhaseSince = now; s.Jobs++; ScLog.Verbose(m.Name + ": at work (job " + s.Jobs + ", mode " + mode + ")"); }
            }
            else if (s.Phase == Phase.Working)
            {
                s.Phase = Phase.Idle;
                s.PhaseSince = now;
            }
            // round 29a (J29): Base repair and Combat repair never end by themselves - a duty with its own end
            if (mode == 8 || mode == 9) { if (DutyTick(m, npc, s, mode, now)) return; }
            else if (s.Duty) { s.Duty = false; s.DutyState = ""; }
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

        /// <summary>Round 29a: a repair duty's first look comes this long after it starts (0-XNPCCore registers the duty on its first tick).</summary>
        private const float DutyFirstCheckSeconds = 6f;
        /// <summary>Round 29a: then the duty asks 0-XNPCCore's own test this often (its repairable answer is cached for as long).</summary>
        private const float DutyCheckSeconds = 5f;

        /// <summary>
        /// Round 29a (J29): Base repair / Combat repair as a duty. 0-XNPCCore's base keeper never ends either mode, repairs only
        /// with a repair tool and materials it can reach, and does nothing without them, without a line or a log (its task never
        /// starts). Every DutyCheckSeconds its own test (ScJobs.RepairCheck: UAITaskBaseKeepSdcs.Repairable and PickRepairTool)
        /// says whether there is anything she can repair now. The first look (DutyFirstCheckSeconds after the start, once
        /// 0-XNPCCore has registered the duty) tells the player what is missing - a repair tool, the materials or the skill for a
        /// damaged block - or that nothing is damaged yet. With nothing she can repair the duty ends after RepairDutyIdleMinutes,
        /// a Base repair duty at once at night, through End: she walks back to her post and SC's night routine takes over. A
        /// Combat repair duty (review M1) never ends at night, and a live foe near her keeps its idle clock at 0. True when it
        /// ended the shift.
        /// </summary>
        private static bool DutyTick(Member m, EntityAlive npc, Shift s, int mode, float now)
        {
            if (!s.Duty || s.DutyMode != mode)
            {
                s.Duty = true;
                s.DutyMode = mode;
                s.DutyStart = now;
                s.DutyIdleSince = now;
                s.DutyChecked = false;
                s.NextDutyCheck = now + DutyFirstCheckSeconds;
                s.DutyState = "starting";
                dutiesStarted++;
                ScStuck.ResetGain(m);   // round 29 review L5: the duty's stuck rescue starts with no held move back from before it
                ScCommunity.ResetStill(m);
                ScLog.Out(m.Name + " starts a " + (mode == 9 ? "Combat repair" : "Base repair") + " duty at " + npc.GetBlockPosition() + " - it ends after "
                          + ScConfig.RepairDutyIdleMinutes + " min with nothing she can repair" + (mode == 9 ? " and no foe near (not at once at night)" : " (at once at night)"));
                return false;
            }
            if (now < s.NextDutyCheck) return false;
            s.NextDutyCheck = now + DutyCheckSeconds;
            ScJobs.RepairFacts f = ScJobs.RepairCheck(npc);
            s.DutyState = f.State;
            World w = ScCommunity.World;
            // round 29 review M1: Combat repair is for the fights - before a horde and between its waves nothing is damaged
            // yet, so it never ends at night (Base repair still does), and while she has a live foe near (LiveFoe within
            // FightRange) its idle clock starts again: only RepairDutyIdleMinutes of quiet with nothing to repair end it
            bool combat = mode == 9;
            bool night = !combat && w != null && !w.IsDaytime();
            if (!s.DutyChecked)
            {
                s.DutyChecked = true;
                ScJobs.TellDutyStart(m, f, night);
            }
            if (f.CanRepair) { s.DutyIdleSince = now; return false; }
            if (combat && ScCommunity.LiveFoe(npc, ScCommunity.FightRange) != null)
            {
                s.DutyIdleSince = now;
                s.DutyState = f.State + ", a foe near";
                return false;
            }
            if (!night && now - s.DutyIdleSince < ScConfig.RepairDutyIdleMinutes * 60f) return false;
            dutyIdleEnds++;
            End(m, npc, night ? "nothing to repair, and it is night (" + f.State + ")" : "nothing to repair for " + ScConfig.RepairDutyIdleMinutes + " min (" + f.State + ")", true);
            return true;
        }

        /// <summary>
        /// Round 29a (J29), the hook for round 29b's residents' no-progress stuck rescue (ScCommunity's watchdog): may it act on
        /// this member now? True when she is on no shift or talk hold (the resident rules, as ever) and during a Base repair /
        /// Combat repair duty (she walks about the base to her repairs - the wreck findings' item c); false while a talk, a craft
        /// or any other job holds her at her spot on purpose.
        /// </summary>
        public static bool AllowsWatchdog(Member m)
        {
            Shift s;
            if (m == null || !shifts.TryGetValue(m.Id, out s)) return true;
            return s.Duty;
        }

        /// <summary>Round 29a: on a Base repair / Combat repair duty (job mode 8 or 9 on a work shift).</summary>
        public static bool OnDuty(int memberId)
        {
            Shift s;
            return shifts.TryGetValue(memberId, out s) && s.Duty;
        }

        /// <summary>
        /// ScTicker, once a second: a shift whose member left, died, is no longer a resident (a path that did not end it) or whose
        /// body unloaded is dropped - 0-XNPCCore forgets an unloaded body's order, and the resident rule stations her when she loads.
        /// Round 29a: ScJobs' own second (replanting, the work kit's once-per-body check) runs from here first.
        /// </summary>
        public static void Sweep(EntityPlayerLocal player)
        {
            ScCraft.FlushXp(false);   // review #6: craft XP is saved here at most every FlushSeconds, not on every point
            ScJobs.Tick(player);
            if (shifts.Count == 0) return;
            scratch.Clear();
            scratch.AddRange(shifts.Keys);
            foreach (int id in scratch)
            {
                Member m = ScRegistry.GetMember(id);
                EntityAlive npc = m != null ? ScCommunity.FindNpc(id) : null;
                if (m != null && m.Status == MemberStatus.Resident && npc != null) continue;
                Shift gone;
                bool talk = shifts.TryGetValue(id, out gone) && gone.Phase == Phase.Talk;
                shifts.Remove(id);
                string why = m == null ? "no longer a member" : m.Status != MemberStatus.Resident ? "now " + m.Status : "her body unloaded";
                if (npc != null && npc.Buffs != null)
                {
                    npc.Buffs.RemoveCustomVar(ShiftCvar);
                    if (m != null && m.Status != MemberStatus.Following && m.Status != MemberStatus.PendingStation)
                    {
                        ScCommunity.Detach(npc, player);
                        DropMarker(npc);
                    }
                }
                else if (m == null || (m.Status != MemberStatus.Following && m.Status != MemberStatus.PendingStation)) DropHireMark(id);
                if (talk)
                {
                    talkEnds++;
                    ScLog.Verbose("talk hold of " + (m != null ? m.Name : id.ToString()) + " dropped (" + why + ")");
                    continue;
                }
                ends++;
                ScLog.Out("work shift of " + (m != null ? m.Name : id.ToString()) + " dropped (" + why + ")");
            }
        }

        /// <summary>
        /// Review #1: 0-XNPCCore's craft order (SdcsCraftJob, in memory) is cleared only from its own task and station paths, and
        /// those need SdcsLootMode 10 - which Detach (ScOrders.ClearVoiceMovement) takes off. So whenever SC detaches a loaded member
        /// (a shift's end, Go home, any stationing, Leave) the order is cleared here first: SdcsCraftJob.Clear takes it out of its
        /// table before it refunds (a second Clear, or 0-XNPCCore's own later, finds nothing: no double refund), gives back the
        /// ingredients a backpack item holds (SdcsCraft.Refund over Order.Held) and, for a station order, cancels it
        /// (SdcsStationCraft.Cancel: the unmade items' ingredients back). X2c: since 0-XNPCCore 3.4.03 a station order takes its
        /// ingredients when it is given (SdcsStationCraft.QueueAt sets AtStation and holds them as PendingQueue while she walks
        /// there, AwaitsArrival); Cancel gives those back through RefundPending, so an order dropped on the way refunds too.
        /// </summary>
        public static void DropJob(EntityAlive npc, string why)
        {
            try
            {
                if (npc == null || SdcsCraftJob.Get(npc.entityId) == null) return;
                SdcsCraftJob.Clear(npc.entityId);
                jobsDropped++;
                ScLog.Verbose(ScCommunity.DisplayName(npc) + ": 0-XNPCCore's craft order dropped (" + why + ") - what it held goes back");
            }
            catch (Exception e)
            {
                ScLog.Once("work-dropjob", e);
            }
        }

        /// <summary>
        /// X2c, finding 5 (Patches_Craft C1n): the craft window's Cancel on a resident on a shift. Voice Control's Cancel sends
        /// VCCommand.Stay (MutateStay: buffOrderStay, the job mode cleared), and Tick ends the whole shift on any order but
        /// Guard. Here only the job goes, as Stay would take it: the order dropped and refunded (DropJob), the job mode cleared
        /// (SdcsLootMode.Clear, as MutateStay). Her Guard order at the craft spot stays, so the shift goes on: Idle, then home
        /// WorkIdleSeconds later unless she is given another order. False when she is not on a shift (Voice Control's Stay then).
        /// Round 28: on a talk hold she has no job yet: nothing to cancel, and no Stay either (she stays linked while you talk).
        /// </summary>
        public static bool CancelJobOnShift(Member m, EntityAlive npc, out bool dropped)
        {
            dropped = false;
            if (m == null || npc == null || npc.Buffs == null || !OnShift(m.Id)) return false;
            if (SdcsCraftJob.Get(m.Id) == null) return true;   // review finding 6: nothing to cancel (the window's own guard)
            dropped = true;
            DropJob(npc, "the craft window's Cancel");
            SdcsLootMode.Clear(npc);
            windowCancels++;
            ScLog.Out(m.Name + ": the craft window's Cancel ends her job, not her work shift");
            return true;
        }

        /// <summary>
        /// Review #3: an owner link that is no hire - a resident member with the Owner cvar (on a shift, or one a reload left
        /// before the ticker stations her). X21 and the any-weapon guards (Patches_Craft C1g) treat her as the resident she is.
        /// </summary>
        public static bool ShiftOwned(EntityAlive e)
        {
            if (e == null || e.Buffs == null || e.Buffs.GetCustomVar("Owner") <= 0f) return false;
            Member m = ScRegistry.GetMember(e.entityId);
            return m != null && m.Status == MemberStatus.Resident;
        }

        /// <summary>Review #5: SCore's V4 leader component drew a companion marker for the owner link; with no link it goes.</summary>
        public static void DropMarker(EntityAlive npc)
        {
            try
            {
                EntityAliveSDXV4 v4 = npc as EntityAliveSDXV4;
                if (v4 != null && v4.NavObject != null && EntityUtilities.GetLeaderOrOwner(v4.entityId) == null) v4.HandleNavObject();
            }
            catch (Exception e)
            {
                ScLog.Once("work-dropmarker", e);
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

        /// <summary>
        /// Patches_Craft's backpack hook (C1c): an item made on a shift (type and count) is owed to the home crates. Only for
        /// items already in her window (0-XNPCCore's backpack craft puts them there before it pays): station output goes
        /// through NoteMadeAtStation.
        /// </summary>
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
        /// Round 33a (round 29 glance LOW 2): Patches_Craft's station hooks (C1d, the prefix of 0-XNPCCore's station PayXp; C1e,
        /// the player opening a station): items she made on a shift that are still in the station's output - her order moves
        /// them to her window afterwards (0-XNPCCore's MoveOut, as many as fit, through SdcsStores.WindowAdd), or the player
        /// opened the station first. They are expected (Pending), not owed: NoteArrived makes them owed as they arrive, so
        /// TrimLedger's clamp to the window no longer drops them before they get there. Never owed twice: an arrival moves at
        /// most what arrived and what is expected.
        /// </summary>
        public static void NoteMadeAtStation(int memberId, int itemType, int count)
        {
            Shift s;
            if (itemType <= 0 || count <= 0 || !shifts.TryGetValue(memberId, out s)) return;
            int n;
            s.Pending.TryGetValue(itemType, out n);
            s.Pending[itemType] = n + count;
            s.PendingSeen = Time.time;
            s.Crafts += count;
            pendingNoted += count;
        }

        /// <summary>Round 33a: station output of this item is expected in her window (ScJobs' WindowAdd prefix asks before it records the add).</summary>
        public static bool Expects(int memberId, int itemType)
        {
            Shift s;
            int n;
            return shifts.TryGetValue(memberId, out s) && s.Pending.TryGetValue(itemType, out n) && n > 0;
        }

        /// <summary>
        /// Round 33a: ScJobs' WindowAdd hook - added items of this type went into her window on a shift. What is expected from a
        /// station (Pending) becomes owed first, up to what arrived; the rest is a home job's harvest when gathering (job mode
        /// 2-7, NoteGathered as before). Returns how many were station output.
        /// </summary>
        public static int NoteArrived(int memberId, int itemType, int added, bool gathering)
        {
            Shift s;
            if (itemType <= 0 || added <= 0 || !shifts.TryGetValue(memberId, out s)) return 0;
            int expected, promoted = 0;
            if (s.Pending.TryGetValue(itemType, out expected) && expected > 0)
            {
                promoted = Math.Min(expected, added);
                if (expected > promoted) s.Pending[itemType] = expected - promoted;
                else s.Pending.Remove(itemType);
                int n;
                s.Made.TryGetValue(itemType, out n);
                s.Made[itemType] = n + promoted;
                pendingArrived += promoted;
            }
            if (gathering && added > promoted) NoteGathered(memberId, itemType, added - promoted);
            return promoted;
        }

        /// <summary>
        /// Round 33a: what is still expected from a station is given up PendingKeepSeconds after her craft order is gone (the
        /// order is what moves the output; what never fit went to 0-XNPCCore's base materials, or stays for the player). From
        /// Deposit, every DepositSeconds; the shift's end drops it with the shift.
        /// </summary>
        private static void AgePending(Member m, Shift s)
        {
            if (s.Pending.Count == 0) return;
            float now = Time.time;
            if (SdcsCraftJob.Get(m.Id) != null) { s.PendingSeen = now; return; }
            if (now - s.PendingSeen < PendingKeepSeconds) return;
            GiveUpPending(m, s, PendingKeepSeconds.ToString("0") + " s after her craft order");
        }

        /// <summary>Round 33a: what is still expected from a station is no longer expected for the crates (counted, a verbose line).
        /// Round 33 review L3: End calls it too - with the default WorkIdleSeconds (90) the shift ends before PendingKeepSeconds.</summary>
        private static void GiveUpPending(Member m, Shift s, string why)
        {
            if (s == null || s.Pending.Count == 0) return;
            int n = 0;
            foreach (var kv in s.Pending) n += kv.Value;
            s.Pending.Clear();
            pendingExpired += n;
            ScLog.Verbose(m.Name + ": " + n + " item(s) she made at a station never reached her inventory window (" + why
                          + ") - no longer expected for the crates");
        }

        /// <summary>
        /// Round 29a (J29, the user's decision 3): ScJobs' hook on 0-XNPCCore's SdcsStores.WindowAdd - what she put in her
        /// inventory window on a home job (job modes 2-7: wood, crops and plants, meat ...) while on a work shift is owed to the
        /// home crates, on the same ledger as what she makes (Deposit moves it every DepositSeconds). On a talk hold too: the
        /// job's first payouts can come before Tick has made the hold a shift (and End deposits a hold's ledger as well).
        /// </summary>
        public static void NoteGathered(int memberId, int itemType, int count)
        {
            Shift s;
            if (itemType <= 0 || count <= 0 || !shifts.TryGetValue(memberId, out s)) return;
            int n;
            s.Made.TryGetValue(itemType, out n);
            s.Made[itemType] = n + count;
            s.Gathered += count;
            gatheredTotal += count;
        }

        /// <summary>Round 29a: one of the ledger's items left her window another way (a seed she replanted with): no longer owed.</summary>
        public static void Unnote(int memberId, int itemType, int count)
        {
            Shift s;
            int n;
            if (count <= 0 || !shifts.TryGetValue(memberId, out s) || !s.Made.TryGetValue(itemType, out n) || n <= 0) return;
            s.Made[itemType] = Math.Max(0, n - count);
        }

        /// <summary>
        /// Q4 (a): from her inventory window into the home zone's linked storage, only what she made on this shift (the ledger), so
        /// her gear, ammo and meds stay. Never while her window is open (on the host's screen, a remote player's, 0-XNPCCore's stock
        /// trade); what does not fit stays in the window. Round 29a: what she gathered on a home job too (NoteGathered), and onto
        /// the crates' stacks of the same item first (ScStorage.TopUpInZone), so a job that brings a few logs at a time does not
        /// take a new slot every DepositSeconds. Round 29 review L4: the ledger is trimmed to what the window holds first
        /// (TrimLedger). Round 33a: station output still on its way (Pending) is aged here (AgePending), never deposited.
        /// </summary>
        private static void Deposit(Member m, EntityAlive npc, Shift s, bool final)
        {
            if (!ScConfig.WorkDepositOutput) return;
            AgePending(m, s);
            if (s.Made.Count == 0 || !HarvestManager.Has(npc.entityId)) return;
            SCoreLootContainer window = HarvestManager.GetOrCreate(npc.entityId);
            if (window == null || window.IsUserAccessing() || ScAmmo.IsRemoteOpen(npc.entityId) || ScAmmo.XnpcTradeOpen(npc.entityId)) return;
            ItemStack[] items = window.items;
            if (items == null) return;
            TrimLedger(m, s, items);
            int moved = 0;
            bool full = false;
            for (int i = 0; i < items.Length && !full; i++)
            {
                ItemStack st = items[i];
                if (st == null || st.IsEmpty() || st.itemValue == null) continue;
                int owed;
                if (!s.Made.TryGetValue(st.itemValue.type, out owed) || owed <= 0) continue;
                int k = Math.Min(st.count, owed);
                int put = ScStorage.TopUpInZone(new ItemStack(st.itemValue.Clone(), k));
                if (put < k)
                {
                    Vector3i box;
                    if (ScStorage.DepositInZone(new ItemStack(st.itemValue.Clone(), k - put), out box)) put = k;
                    else full = true;
                }
                if (put <= 0) break;
                s.Made[st.itemValue.type] = owed - put;
                moved += put;
                // 3.4: the window's slots are bound to its grid - emptied in place, never replaced (ScAmmo's rule)
                if (put >= st.count) HarvestManager.SetSlot(items, i, null);
                else st.count -= put;
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

        /// <summary>
        /// Round 29 review L4: the ledger never owes more of an item than her inventory window holds. Whatever left the window
        /// another way - the player took it out, she used it (repair materials, a seed), a station's output that never fit in -
        /// is no longer owed, so items of that kind handed over later (repair materials for a duty) are not swept to the crates.
        /// Deposit runs it whenever the window is closed, so the first Deposit after the player closes it (at most DepositSeconds
        /// later) trims what they took. Entries at 0 go. Not covered: taking her gathered items out and handing over as many of
        /// the same item in one visit to her window (PORT_34, round 29 review). Round 33a (round 29 glance LOW 2): only items
        /// already in her window are on Made - station output noted while it is still in the station (C1d, C1e) waits on
        /// Pending until it arrives (NoteArrived), so the clamp no longer drops it on the way.
        /// </summary>
        private static void TrimLedger(Member m, Shift s, ItemStack[] items)
        {
            inWindow.Clear();
            for (int i = 0; i < items.Length; i++)
            {
                ItemStack st = items[i];
                if (st == null || st.IsEmpty() || st.itemValue == null) continue;
                int n;
                inWindow.TryGetValue(st.itemValue.type, out n);
                inWindow[st.itemValue.type] = n + st.count;
            }
            ledgerKeys.Clear();
            ledgerKeys.AddRange(s.Made.Keys);
            int trimmed = 0;
            foreach (int type in ledgerKeys)
            {
                int owed = s.Made[type], have;
                inWindow.TryGetValue(type, out have);
                if (owed > have) trimmed += owed - have;
                if (owed <= 0 || have <= 0) s.Made.Remove(type);
                else if (owed > have) s.Made[type] = have;
            }
            if (trimmed <= 0) return;
            ledgerTrimmed += trimmed;
            ScLog.Verbose(m.Name + ": " + trimmed + " item(s) on the work shift's ledger are no longer in her inventory window (taken out or used) - no longer owed to the crates");
        }

        /// <summary>
        /// Round 29a (J29, the user's decision 3: "when her pack fills she drops it off and carries on"). Patches_Craft's C1f asks
        /// this when 0-XNPCCore ends a resident's job for a full pack ("loot.full"; it has paused the job, MarkFull, and would
        /// end that pause on its next tick for want of a Follow order). On a work shift with a home job (modes 2-7) what she
        /// gathered goes to the crates now; with room for 0-XNPCCore's own resume rule (LootResumeFreeSlots free slots) the
        /// pause is cleared and the job mode, her Guard order and its pin stay, so she carries on (true: C1f skips the end).
        /// False - the job ends as before - when the crates take too little (they are full, or her pack is full of her own
        /// things), said to the player once a shift.
        /// </summary>
        public static bool ResumeAfterFullPack(Member m, EntityAlive npc)
        {
            Shift s;
            if (!ScConfig.WorkDepositOutput || m == null || npc == null || npc.Buffs == null || !shifts.TryGetValue(m.Id, out s) || s.Phase == Phase.Talk) return false;
            int mode = SdcsLootMode.Mode(npc);
            if (mode < 2 || mode > 7) return false;
            int before = s.Deposited;
            Deposit(m, npc, s, false);
            int free = ScJobs.WindowFreeSlots(npc), need = ScJobs.ResumeFreeSlots();
            if (free < need)
            {
                packEnds++;
                ScLog.Out(m.Name + "'s pack is full (job mode " + mode + "): " + (s.Deposited - before) + " item(s) to the home crates, " + free + " free slot(s) of "
                          + need + " needed - the job ends");
                if (!s.SaidPackFull)
                {
                    s.SaidPackFull = true;
                    ScCommunity.Tell(ScCommunity.LocalPlayer, string.Format(Localization.Get("scWorkPackFull"), m.Name), "");
                }
                return false;
            }
            SdcsLootMode.ClearPaused(npc);
            if (npc.Buffs.GetCustomVar("SdcsLootFull") >= 1f) npc.Buffs.SetCustomVar("SdcsLootFull", 0f);
            s.Resumes++;
            packResumes++;
            ScLog.Out(m.Name + "'s pack was full: " + (s.Deposited - before) + " item(s) to the home crates, back to work (job mode " + mode + ", " + free + " free slots)");
            return true;
        }

        // ------------------------------------------------------------------ the Community tab ----

        /// <summary>The tab's Call to work / End work.</summary>
        public static void CallOrEnd(Member m, EntityPlayerLocal player, out string message, out bool ok)
        {
            ok = false;
            message = "";
            if (m == null || player == null) return;
            if (OnWorkShift(m.Id))
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
                case "worklabel": return Localization.Get(sel != null && OnWorkShift(sel.Id) ? "scUiWorkEnd" : "scUiWorkCall");
                case "workcolor":
                {
                    if (sel == null) return Dim;
                    if (OnWorkShift(sel.Id)) return Body;
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
            var coming = new StringBuilder();       // round 33a: station output on its way to her window
            foreach (var kv in s.Pending)
            {
                ItemClass ic = ItemClass.GetForId(kv.Key);
                coming.Append(coming.Length > 0 ? ", " : "").Append(ic != null ? ic.GetItemName() : kv.Key.ToString()).Append(" x").Append(kv.Value);
            }
            if (s.Phase == Phase.Talk)
                return "Talk (round 28's talk hold) for " + (int)(Time.unscaledTime - s.TalkSince) + " s, a talk window last seen open " + (int)(Time.unscaledTime - s.TalkSeen)
                       + " s ago" + cvar;
            return s.Phase + " for " + (int)(now - s.PhaseSince) + " s (shift " + (int)(now - s.Started) + " s, " + (s.Called ? "called" : "talked to") + "), jobs " + s.Jobs
                   + ", made " + s.Crafts + ", gathered " + s.Gathered + ", in the crates " + s.Deposited + ", still owed to the crates [" + made + "]"
                   + (coming.Length > 0 ? ", on its way from a station [" + coming + "]" : "")
                   + (s.Resumes > 0 ? ", pack emptied and back to work " + s.Resumes + "x" : "")
                   + (s.Duty ? "; " + (s.DutyMode == 9 ? "Combat" : "Base") + " repair duty for " + (int)(now - s.DutyStart) + " s, nothing to repair for "
                               + (int)(now - s.DutyIdleSince) + " s of " + ScConfig.RepairDutyIdleMinutes * 60 + " (" + s.DutyState + ")" : "") + cvar
                   + "; craft order " + (SdcsCraftJob.Get(m.Id) != null ? "yes" : "no");
        }

        public static string Summary()
        {
            var parts = new List<string>();
            foreach (var kv in endReasons) parts.Add(kv.Key + " " + kv.Value);
            return "work shifts: " + shifts.Count + " now, " + starts + " started, " + ends + " ended (" + string.Join(", ", parts.ToArray()) + "), stale sc_shift cleared "
                   + staleCleared + ", items put in the crates " + depositedTotal + ", craft orders dropped " + jobsDropped
                   + ", window cancels kept on the shift " + windowCancels + "; talk holds: " + talkStarts + " started, " + talkPromoted + " became a work shift, "
                   + talkEnds + " ended, " + talkSkipped + " not started (a resident who could not be held)"
                   + "; home jobs (round 29a): gathered " + gatheredTotal + ", pack emptied and back to work " + packResumes + ", pack full and ended " + packEnds
                   + ", field jobs taken back " + fieldJobsTaken + ", repair duties " + dutiesStarted + " started, " + dutyIdleEnds + " ended with nothing to repair"
                   + ", ledger items no longer in a window " + ledgerTrimmed
                   + ", station output noted ahead " + pendingNoted + " (reached her window " + pendingArrived + ", given up " + pendingExpired + "); "
                   + ScJobs.Report();
        }
    }
}

// ---------------------------------------------------------------- dialog operators (global, as DialogOps.cs) ----

// Round 28 (X3, decision 2): "I have work for you." (ScCanWork / ScWorkStart) is gone - 0-XNPCCore's Craft... serves every
// member, a resident through the talk hold (ScWork.StartTalk); a craft order given there makes the work shift (ScWork.Promote).

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
