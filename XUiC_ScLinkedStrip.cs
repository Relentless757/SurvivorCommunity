using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// The linked storage strip under vanilla's loot grid (Config/XUi_InGame/windows.xml), shown only while
    /// the open container is the linked storage: boxes / stacks / free room, what did not fit, and Refresh.
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
