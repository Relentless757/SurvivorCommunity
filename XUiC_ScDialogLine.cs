using System;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// The NPC's line, above the dialog's responses (Config/XUi_InGame/windows.xml windowScDialogLine, in the vanilla
    /// "dialog" group; the user's call, 2026-10-16: subtitles sit above the dialogue choices). Vanilla 3.2's dialog group
    /// has only the responses window, and SCore's subtitle shows a statement only for its older body type
    /// (EntityAliveSDX), so the player-body survivors' lines never reached the screen. This shows the current statement's
    /// text for any NPC but an EntityAliveSDX (SCore already shows those), and hands each new statement to ScVoice, which
    /// plays its clip when there is one. DialogLine=false hides the line; the clips still play. Bindings are non-null
    /// from the start: they are evaluated while the XML is parsed.
    /// </summary>
    [Preserve]
    public class XUiC_ScDialogLine : XUiController
    {
        private BaseStatement shown;
        private string line = "";
        private bool visible;

        public override void OnOpen()
        {
            base.OnOpen();
            Clear();
            RefreshBindings();
        }

        public override void OnClose()
        {
            base.OnClose();
            ScVoice.Stop();
            Clear();
        }

        private void Clear()
        {
            shown = null;
            line = "";
            visible = false;
        }

        public override void Update(float _dt)
        {
            base.Update(_dt);
            try
            {
                XUiC_DialogWindowGroup group = windowGroup != null ? windowGroup.Controller as XUiC_DialogWindowGroup : null;
                Dialog dialog = group != null ? group.CurrentDialog : null;
                DialogStatement st = dialog != null ? dialog.CurrentStatement : null;
                EntityAlive npc = xui != null && xui.Dialog != null ? xui.Dialog.Respondent : null;
                string text = st != null && npc != null && ScConfig.DialogLine && !ScNpc.IsSdx(npc) ? (st.Text ?? "") : "";
                bool newStatement = !ReferenceEquals(st, shown);
                if (!newStatement && text == line) return;
                shown = st;
                line = text;
                visible = line.Length > 0;
                RefreshBindings();
                if (newStatement && st != null && npc != null) ScVoice.OnLine(npc, dialog, st);
            }
            catch (Exception e)
            {
                ScLog.Once("dialogline", e);
            }
        }

        public override bool GetBindingValueInternal(ref string _value, string _bindingName)
        {
            switch (_bindingName)
            {
                case "sclinevisible": _value = visible ? "true" : "false"; return true;
                case "scline": _value = line; return true;
            }
            return base.GetBindingValueInternal(ref _value, _bindingName);
        }
    }
}
