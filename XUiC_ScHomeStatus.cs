using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Scripting;

namespace SurvivorCommunity
{
    /// <summary>
    /// The Home Status panel (Config/XUi_InGame/windows.xml windowScHomeStatus, in the HUD's toolbelt group): at the top, right
    /// of the compass (left of the quest tracker), while the player is inside the home zone - morale (dot + word), six resources (luxuries only show in the window; red below what the next rollover
    /// wants, ScStockpile.NeedForRollover), the member count and the base's noise (ScNoise). Hidden while a modal window is
    /// open, and with HomeStatusHud=false. Host only, like the registry it reads. Every binding is non-null from the
    /// constructor: bindings are evaluated while the XML is parsed, before Init.
    /// </summary>
    [Preserve]
    public class XUiC_ScHomeStatus : XUiController
    {
        private const float RefreshSeconds = 0.5f;
        private const string White = "255,255,255,255";
        private const string Red = "235,72,60,255";

        /// <summary>What the next rollover wants changes slowly and costs a little to work out (outposts' yields).</summary>
        private const float NeedSeconds = 5f;

        private float nextRefresh, nextNeed;
        private int[] need = new int[ScNames.ResourceCount];
        private bool visible;
        private string mood = "", moodColor = White, members = "0", noise = "", noiseColor = White;
        private bool noiseVisible;
        private readonly string[] stock = { "0", "0", "0", "0", "0", "0", "0" };
        private readonly string[] stockColor = { White, White, White, White, White, White, White };

        public override void Update(float _dt)
        {
            base.Update(_dt);
            float now = Time.unscaledTime;
            if (now < nextRefresh) return;
            nextRefresh = now + RefreshSeconds;
            try
            {
                if (Recompute()) RefreshBindings();
            }
            catch (Exception e)
            {
                ScLog.Once("home-status", e);
            }
        }

        /// <summary>Fresh values; true when anything shown changed.</summary>
        private bool Recompute()
        {
            bool show = Wanted();
            bool changed = show != visible;
            visible = show;
            if (!show) return changed;

            Community c = ScRegistry.Community;
            changed |= Set(ref mood, Capital(ScMorale.MoodWord(c.Morale)));
            changed |= Set(ref moodColor, MoodColor(c.Morale));
            float now = Time.unscaledTime;
            if (changed || now >= nextNeed)
            {
                nextNeed = now + NeedSeconds;
                // a failure here only costs the red/white colouring, never the rest of the panel
                try { need = ScStockpile.NeedForRollover(); }
                catch (Exception e) { need = new int[ScNames.ResourceCount]; ScLog.Once("home-status-need", e); }
            }
            for (int i = 0; i < ScNames.ResourceCount; i++)
            {
                int have = c.Get((Resource)i);
                changed |= Set(ref stock[i], have.ToString(CultureInfo.InvariantCulture));
                changed |= Set(ref stockColor[i], have < need[i] ? Red : White);
            }
            changed |= Set(ref members, ScRegistry.Members.Count.ToString(CultureInfo.InvariantCulture));

            bool nv = ScConfig.BaseNoise;
            changed |= nv != noiseVisible;
            noiseVisible = nv;
            if (nv)
            {
                float n = ScNoise.Current();
                ScNoise.Level level = ScNoise.LevelOf(n);
                string word = ScNoise.Word(level);
                int att = Mathf.FloorToInt(c.Attention);
                string text = att > 0 ? string.Format(Localization.Get("scNoiseHudAttention"), word, att) : string.Format(Localization.Get("scNoiseHud"), word);
                changed |= Set(ref noise, text);
                changed |= Set(ref noiseColor, NoiseColor(level));
            }
            return changed;
        }

        private bool Wanted()
        {
            if (!ScConfig.HomeStatusHud || !ScCommunity.IsHost || !ScRegistry.Loaded || !ScRegistry.Community.HasHome) return false;
            EntityPlayerLocal player = (xui != null && xui.playerUI != null) ? xui.playerUI.entityPlayer : null;
            if (player == null || player.IsDead()) return false;
            if (xui.playerUI.windowManager != null && xui.playerUI.windowManager.IsModalWindowOpen()) return false;
            return ScZone.Contains(player.GetBlockPosition());
        }

        /// <summary>The mood words are lower case for use inside sentences; the panel shows one on its own.</summary>
        private static string Capital(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return char.ToUpper(s[0], CultureInfo.CurrentCulture) + s.Substring(1);
        }

        private static bool Set(ref string field, string value)
        {
            if (field == value) return false;
            field = value;
            return true;
        }

        /// <summary>The mood bands of ScMorale.MoodWord: happy green, content light, uneasy amber, miserable red.</summary>
        private static string MoodColor(int morale)
        {
            if (morale >= 75) return "96,200,96,255";
            if (morale >= 50) return "205,205,190,255";
            if (morale >= 25) return "232,170,60,255";
            return Red;
        }

        private static string NoiseColor(ScNoise.Level level)
        {
            switch (level)
            {
                case ScNoise.Level.VeryLoud: return Red;
                case ScNoise.Level.Loud: return "232,170,60,255";
                case ScNoise.Level.Noticeable: return "225,215,150,255";
                default: return White;
            }
        }

        public override bool GetBindingValueInternal(ref string _value, string _bindingName)
        {
            switch (_bindingName)
            {
                case "scvisible": _value = visible ? "true" : "false"; return true;
                case "scmood": _value = mood; return true;
                case "scmoodcolor": _value = moodColor; return true;
                case "scfood": _value = stock[(int)Resource.Food]; return true;
                case "scfoodcolor": _value = stockColor[(int)Resource.Food]; return true;
                case "scmeds": _value = stock[(int)Resource.Meds]; return true;
                case "scmedscolor": _value = stockColor[(int)Resource.Meds]; return true;
                case "scammo": _value = stock[(int)Resource.Ammo]; return true;
                case "scammocolor": _value = stockColor[(int)Resource.Ammo]; return true;
                case "scmats": _value = stock[(int)Resource.Materials]; return true;
                case "scmatscolor": _value = stockColor[(int)Resource.Materials]; return true;
                case "scfuel": _value = stock[(int)Resource.Fuel]; return true;
                case "scfuelcolor": _value = stockColor[(int)Resource.Fuel]; return true;
                case "scwater": _value = stock[(int)Resource.Water]; return true;
                case "scwatercolor": _value = stockColor[(int)Resource.Water]; return true;
                case "scmembers": _value = members; return true;
                case "scnoise": _value = noise; return true;
                case "scnoisecolor": _value = noiseColor; return true;
                case "scnoisevisible": _value = noiseVisible ? "true" : "false"; return true;
                default: return base.GetBindingValueInternal(ref _value, _bindingName);
            }
        }
    }
}
