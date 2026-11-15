using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// The linked storage strip under vanilla's loot grid (Config/XUi_InGame/windows.xml), shown only while
    /// the open container is the linked storage: boxes / stacks / free room, what did not fit, and Refresh.
    /// Round s1: SC's own linked storage window (Tools/generate_linked_storage_ui.py) has one too, under its grid or
    /// (AshfallUI's look) under its header; its group has vanilla's XUiC_LootWindowGroup, so Container() finds it alike.
    /// Refresh closes and reopens the container through the normal path (a re-bind of the slots under
    /// GridInventory's takeover broke its scrolling). Same shape as the packing strip; every binding is
    /// non-null before Init.
    /// </summary>
    [Preserve]
    public class XUiC_ScLinkedStrip : XUiController
    {
        private const float RefreshSeconds = 0.5f;
        private float nextRefresh;
        private bool visible;
        private string header = "";
        private bool reopen;
        /// <summary>Round s1: the claim SC's own window stands on (AnchorGone), and whether it stood on the first look.</summary>
        private bool anchorKnown, anchorStood;
        private Vector3i anchorPos;

        public override void Init()
        {
            base.Init();
            try
            {
                Hook("btnLinkedRefresh", () => { reopen = true; });
            }
            catch (Exception e)
            {
                ScLog.Once("linked-strip-init", e);
            }
        }

        private void Hook(string id, Action action)
        {
            XUiController c = GetChildById(id);
            if (c == null) { ScLog.Err("linked strip: no control named '" + id + "' in windows.xml"); return; }
            c.OnPress += (s, m) => { if (m == -1) action(); };
        }

        private ScLinkedContainer Container()
        {
            XUiC_LootWindowGroup lg = (windowGroup != null) ? windowGroup.Controller as XUiC_LootWindowGroup : null;
            return lg != null ? lg.te as ScLinkedContainer : null;
        }

        public override void OnOpen()
        {
            base.OnOpen();
            nextRefresh = 0f;
            reopen = false;
            anchorKnown = false;
        }

        /// <summary>
        /// Round s1: SC's own linked storage window lost what it stands on - the land claim it was opened at (the home's, or
        /// an outpost's) was picked up or destroyed while it showed, or (a view of the home's boxes) the community has no home
        /// any more. Vanilla closes only "looting" for that (TEFeatureStorage.OnDestroy, the claim's storage feature, calls
        /// XUiC_LootWindowGroup.CloseIfOpenAtPos at the view's position), so this window stayed until E, Escape or 32 m. Only a
        /// change while it shows counts, as vanilla's close: a home claim already gone when the view opened (the home record
        /// is kept) closes nothing. The claim test is the land claim map, as ScZone.EnsureHome's.
        /// </summary>
        private bool AnchorGone(ScLinkedContainer c)
        {
            if (windowGroup == null || windowGroup.Id != ScStorage.OwnWindowGroup) return false;   // "looting": vanilla closes it
            Vector3i pos = c.ToWorldPos();
            bool stands = ClaimStands(pos);
            if (!anchorKnown || pos != anchorPos)
            {
                anchorKnown = true;
                anchorPos = pos;
                anchorStood = stands;
                return false;
            }
            if (anchorStood && !stands) return true;
            return ScStorage.CurrentArea == null && !ScZone.HasHome;     // the home zone's boxes, and no home any more
        }

        /// <summary>A live land claim at this position (the host's land claim map); true when the map cannot tell.</summary>
        private static bool ClaimStands(Vector3i pos)
        {
            var pp = GameManager.Instance != null ? GameManager.Instance.persistentPlayers : null;
            return pp == null || pp.m_lpBlockMap == null || pp.m_lpBlockMap.ContainsKey(pos);
        }

        public override void OnClose()
        {
            base.OnClose();
            visible = false;
            header = "";
            RefreshBindings();
        }

        public override void Update(float _dt)
        {
            base.Update(_dt);
            try
            {
                if (reopen)
                {
                    // outside the press handler: the open path closes every modal window first
                    reopen = false;
                    EntityPlayerLocal p = (xui != null && xui.playerUI != null) ? xui.playerUI.entityPlayer : null;
                    if (Container() != null && p != null) ScStorage.OpenWindow(p, ScStorage.CurrentAnchor, ScStorage.CurrentArea, ScStorage.CurrentTitle);   // same view again
                    return;
                }
                float now = Time.unscaledTime;
                if (now < nextRefresh) return;
                nextRefresh = now + RefreshSeconds;
                ScLinkedContainer c = Container();
                if (c != null && AnchorGone(c))
                {
                    // a clean close through the window manager: the group's OnClose lets the view go (SetUserAccessing false,
                    // the backpack closed, the lock request released), as vanilla's CloseIfOpenAtPos does for "looting"
                    ScLog.Out("linked storage: the claim at " + anchorPos + " is gone or the community has no home - its window closes");
                    xui.playerUI.windowManager.Close(windowGroup);
                    return;
                }
                bool v = c != null;
                string h = "";
                if (v)
                {
                    // the boxes' whole free room, in the unit the grid is drawn in (the grid itself only lays out a
                    // band of empty rows under the stacks)
                    bool cellsUnit = c.GridDrawn && ScStorage.BoxFreeCells != null;
                    h = string.Format(Localization.Get(cellsUnit ? "scStorageHeaderCells" : "scStorageHeader"),
                                      ScStorage.Boxes, ScStorage.LiveStacks(), ScStorage.FreeFor(c.GridDrawn));
                    if (c.Hidden > 0) h += " - " + string.Format(Localization.Get("scStorageMore"), c.Hidden);
                    if (ScStorage.SkippedChunks > 0) h += " " + string.Format(Localization.Get("scStorageUnloaded"), ScStorage.SkippedChunks);
                }
                if (v != visible || h != header)
                {
                    visible = v; header = h;
                    RefreshBindings();
                }
            }
            catch (Exception e)
            {
                ScLog.Once("linked-strip-update", e);
            }
        }

        public override bool GetBindingValueInternal(ref string _value, string _bindingName)
        {
            switch (_bindingName)
            {
                case "sclvisible": _value = visible ? "true" : "false"; return true;
                case "sclheader": _value = header ?? ""; return true;
            }
            return base.GetBindingValueInternal(ref _value, _bindingName);
        }
    }
}
