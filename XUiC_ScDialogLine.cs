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
    /// plays its clip when there is one. DialogLine=false hides the line (read at each new statement); the clips still
    /// play, a request line's too (vo2: its tokens are filled for its key, unseen; with VoiceLines=false as well nothing
    /// is read). Bindings are non-null from the start: they are evaluated while the XML is parsed.
    /// E2: an enclave respondent's line has its tokens filled here ({enc}, {leader}, {count}, {poi}, ... - ScEncDialog.Fill)
    /// once per new statement, so the statement's shared Text is never written and the voice key stays its id. vo2: a line
    /// an enclave request picks at runtime ({reqask} and the like) comes back with its own key, which ScVoice tries first;
    /// closing the talk stops SC's clip, but not a line said as it closes (ScVoice.SayAfterTalk).
    /// V1: each frame it tells ScVoice whom the talk is open on (TalkOn), and its close ends that (OnTalkClosed): while the
    /// talk is open on one of the community's survivors, Voice Control keeps quiet on them (ScVoice.HoldsVoice).
    /// </summary>
    [Preserve]
    public class XUiC_ScDialogLine : XUiController
    {
        private BaseStatement shown;
        /// <summary>The statement's text as it was last seen (tokens unfilled): the per-frame change test.</summary>
        private string raw = "";
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
            ScVoice.OnTalkClosed();
            Clear();
        }

        private void Clear()
        {
            shown = null;
            raw = "";
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
                // V1: the voice gate holds Voice Control on this NPC while the talk is open (ScVoice.HoldsVoice; OnClose lets go)
                ScVoice.TalkOn(npc);
                // vo2: read and filled with the panel off too (DialogLine=false) while the voices are on - a request line's voice
                // key comes from the fill; with both off nothing is read, so a fill's side effects (a thanks spent, an
                // infestation made known) wait for a line that is shown or heard (the vo2 review's L2)
                string text = st != null && npc != null && (ScConfig.DialogLine || ScConfig.VoiceLines) && !ScNpc.IsSdx(npc) ? (st.Text ?? "") : "";
                bool newStatement = !ReferenceEquals(st, shown);
                if (!newStatement && text == raw) return;
                shown = st;
                raw = text;
                string voiceKey = null;
                line = text.IndexOf('{') >= 0 ? ScEncDialog.Fill(text, npc, ScCommunity.LocalPlayer, out voiceKey) : text;
                if (!ScConfig.DialogLine) line = "";
                visible = line.Length > 0;
                RefreshBindings();
                if (newStatement && st != null && npc != null) ScVoice.OnLine(npc, dialog, st, voiceKey);
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
