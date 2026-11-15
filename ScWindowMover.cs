using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SurvivorCommunity
{
    /// <summary>
    /// Round n11 (t40): one of SC's movable windows - the talk card (XUiC_ScSnapshot, the user's t40 request: UI mods that
    /// lay the dialog out differently can have the card cover the responses) and the member inventory panel
    /// (XUiC_ScMemberInv, round n10). Drag the window's bar (an XUi sprite with on_press and on_drag) and the whole window
    /// follows, kept on screen; where it is let go is remembered in ScUiLayout under the window's key, across closing,
    /// reopening, worlds and saves. Right-click on the bar, or `sc ui reset`, puts it back in its default place.
    ///
    /// The window's position is its top-left corner in its anchor's space (XUi units), so any anchor works: the talk card
    /// is anchored at the screen's bottom, the panel at its top-left.
    /// </summary>
    internal sealed class ScWindowMover
    {
        /// <summary>The window's default place in its anchor's space; false when it cannot be told yet (nothing is moved).</summary>
        public delegate bool DefaultPlaceFn(XUiV_Window w, out Vector2i p);

        private readonly XUiController owner;
        private readonly string key;
        private readonly DefaultPlaceFn defaultPlace;
        private bool dragging;
        private Vector2 dragPos;

        public ScWindowMover(XUiController owner, string key, DefaultPlaceFn defaultPlace)
        {
            this.owner = owner;
            this.key = key;
            this.defaultPlace = defaultPlace;
        }

        private XUiV_Window Win { get { return owner.ViewComponent as XUiV_Window; } }

        /// <summary>Hooks the bar the window is dragged by (left-drag moves, right-click resets); false when there is none.</summary>
        public bool Attach(string barId)
        {
            XUiController bar = owner.GetChildById(barId);
            if (bar == null) return false;
            bar.OnDrag += OnDrag;
            bar.OnRightPress += (s, b) => Reset();
            return true;
        }

        /// <summary>Puts the window where it belongs: the player's saved place, else its default one - kept on screen either way.</summary>
        public void Place()
        {
            XUiV_Window w = Win;
            if (w == null || w.UiTransform == null) return;
            Vector2i p;
            if (!ScUiLayout.TryGet(key, out p) && (defaultPlace == null || !defaultPlace(w, out p))) return;
            p = Clamp(p);
            if (!p.Equals(w.Position)) w.Position = p;
        }

        /// <summary>Back to the default place, and the saved one forgotten (right-click on the bar, sc ui reset).</summary>
        public void Reset()
        {
            dragging = false;
            ScUiLayout.Clear(key);
            Place();
        }

        /// <summary>A drag under way (the panel re-places itself every frame only between drags).</summary>
        public bool Dragging { get { return dragging; } }

        /// <summary>Forgets a drag the window's closing cut short (no DragEnd comes then).</summary>
        public void EndDrag() { dragging = false; }

        /// <summary>For sc ui: where the window sits now.</summary>
        public string Where()
        {
            XUiV_Window w = Win;
            return w != null ? w.Position.ToString() : "?";
        }

        /// <summary>
        /// The window kept on screen: its top-left corner (its position, in its anchor's space) limited so the whole window
        /// shows - or its top, when the screen is shorter than the window. The screen's corners come from the UI camera, as
        /// vanilla's sign editor maps the mouse (ScreenToWorldPoint), so the anchor's place, the screen bounds option and
        /// the UI scale need no arithmetic here.
        /// </summary>
        public Vector2i Clamp(Vector2i p)
        {
            XUiV_Window w = Win;
            Camera cam = owner.xui != null && owner.xui.playerUI != null ? owner.xui.playerUI.camera : null;
            Transform parent = w != null && w.UiTransform != null ? w.UiTransform.parent : null;
            if (cam == null || parent == null) return p;
            Vector3 a = parent.InverseTransformPoint(cam.ScreenToWorldPoint(Vector3.zero));
            Vector3 b = parent.InverseTransformPoint(cam.ScreenToWorldPoint(new Vector3(cam.pixelWidth, cam.pixelHeight, 0f)));
            float minX = Mathf.Min(a.x, b.x), maxX = Mathf.Max(a.x, b.x), minY = Mathf.Min(a.y, b.y), maxY = Mathf.Max(a.y, b.y);
            float x = Mathf.Clamp(p.x, minX, Mathf.Max(minX, maxX - w.Size.x));
            float y = Mathf.Clamp(p.y, Mathf.Min(maxY, minY + w.Size.y), maxY);
            return new Vector2i(Mathf.RoundToInt(x), Mathf.RoundToInt(y));
        }

        private void OnDrag(XUiController _sender, EDragType _type, Vector2 _delta)
        {
            try
            {
                XUiV_Window w = Win;
                if (w == null) return;
                if (_type == EDragType.DragEnd)
                {
                    if (!dragging) return;
                    dragging = false;
                    ScUiLayout.Set(key, w.Position);
                    return;
                }
                if (!dragging)
                {
                    dragging = true;
                    dragPos = new Vector2(w.Position.x, w.Position.y);
                }
                // NGUI hands over screen pixels: XUi units are pixels times the pixel ratio factor; screen y grows upward, as XUi's
                dragPos += _delta * owner.xui.GetPixelRatioFactor();
                Vector2i p = Clamp(new Vector2i(Mathf.RoundToInt(dragPos.x), Mathf.RoundToInt(dragPos.y)));
                dragPos = new Vector2(Mathf.Clamp(dragPos.x, p.x - 0.5f, p.x + 0.5f), Mathf.Clamp(dragPos.y, p.y - 0.5f, p.y + 0.5f));
                if (!p.Equals(w.Position)) w.Position = p;
            }
            catch (Exception e)
            {
                ScLog.Once("ui-drag-" + key, e);
            }
        }
    }

    /// <summary>
    /// Round n10, n11: where the player put SC's movable windows (ScWindowMover), by key - the member inventory panel's and
    /// the talk card's top-left corners (XUi units from each window's anchor). One small text file in SC's user data folder
    /// (%APPDATA%\7DaysToDie\SurvivorCommunity\ui_layout.txt, beside the streamer names), so it is the player's own and holds
    /// across worlds and saves; read once, written on every drag's end and reset. A missing or unreadable file is no place.
    /// </summary>
    public static class ScUiLayout
    {
        public const string MemberInventory = "memberinv";
        public const string TalkCard = "talkcard";
        private const string FileName = "ui_layout.txt";

        private static Dictionary<string, Vector2i> places;

        private static string FilePath()
        {
            return Path.Combine(Path.Combine(GameIO.GetUserGameDataDir(), ScStreamerNames.FolderName), FileName);
        }

        private static void Load()
        {
            if (places != null) return;
            places = new Dictionary<string, Vector2i>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = FilePath();
                if (!File.Exists(path)) return;
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string[] xy = line.Substring(eq + 1).Split(',');
                    int x, y;
                    if (xy.Length != 2 || !int.TryParse(xy[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x)
                        || !int.TryParse(xy[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out y)) continue;
                    places[line.Substring(0, eq).Trim()] = new Vector2i(x, y);
                }
            }
            catch (Exception e)
            {
                ScLog.Once("ui-layout-read", "ui layout: could not read " + FileName + " (" + e.Message + ") - windows open in their default places");
            }
        }

        private static void Save()
        {
            try
            {
                string path = FilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var lines = new List<string> { "# Survivor Community: where you moved its windows (each window's top-left corner, XUi units from its anchor). Delete a line,",
                                               "# or type sc ui reset in the F1 console, to put a window back." };
                foreach (var kv in places)
                    lines.Add(kv.Key + "=" + kv.Value.x.ToString(CultureInfo.InvariantCulture) + "," + kv.Value.y.ToString(CultureInfo.InvariantCulture));
                File.WriteAllLines(path, lines.ToArray());
            }
            catch (Exception e)
            {
                ScLog.Once("ui-layout-write", "ui layout: could not write " + FileName + " (" + e.Message + ") - the window's place is kept for this session only");
            }
        }

        public static bool TryGet(string key, out Vector2i p)
        {
            Load();
            return places.TryGetValue(key, out p);
        }

        public static void Set(string key, Vector2i p)
        {
            Load();
            Vector2i old;
            if (places.TryGetValue(key, out old) && old.Equals(p)) return;
            places[key] = p;
            Save();
        }

        /// <summary>Forgets a window's place; true when it had one.</summary>
        public static bool Clear(string key)
        {
            Load();
            if (!places.Remove(key)) return false;
            Save();
            return true;
        }

        /// <summary>For sc ui: the file and its places.</summary>
        public static string Describe()
        {
            Load();
            var parts = new List<string>();
            foreach (var kv in places) parts.Add(kv.Key + "=" + kv.Value.x + "," + kv.Value.y);
            string path;
            try { path = FilePath(); } catch (Exception) { path = "?"; }
            return (parts.Count > 0 ? string.Join(" ", parts.ToArray()) : "no window moved (default places)") + " - " + path;
        }
    }
}
