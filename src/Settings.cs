using System;
using System.Collections.Generic;
using System.IO;

namespace ClickBind
{
    public enum Trigger
    {
        DoubleRight = 0,
        DoubleLeft = 1,
        LeftRight = 2,
        MiddleClick = 3,
        SideBack = 4,
        SideForward = 5,
        HotCorner = 6,
        HoldLeft = 7,
        HoldRight = 8,
        HoldBoth = 9
    }

    public enum ActionId
    {
        None = 0,
        Copy = 1,
        Paste = 2,
        Snapshot = 3,
        Cut = 4,
        Undo = 5,
        Redo = 6,
        SelectAll = 7,
        TaskView = 8
    }

    public class Settings
    {
        public bool Active;
        public bool StartWithWindows;
        public bool HotCornerEnabled;
        public string HotCornerSide;
        public string Theme;
        public int DoubleClickMs;
        public int ChordMs;
        public Dictionary<Trigger, ActionId> Bindings;
        public HashSet<Trigger> Disabled;

        public Settings()
        {
            Active = true;
            StartWithWindows = true;
            HotCornerEnabled = true;
            HotCornerSide = "both";
            Theme = "light";
            DoubleClickMs = 280;
            ChordMs = 90;
            Bindings = DefaultBindings();
            Disabled = new HashSet<Trigger>();
        }

        public static Dictionary<Trigger, ActionId> DefaultBindings()
        {
            Dictionary<Trigger, ActionId> d = new Dictionary<Trigger, ActionId>();
            d[Trigger.DoubleRight] = ActionId.Copy;
            d[Trigger.DoubleLeft] = ActionId.Paste;
            d[Trigger.LeftRight] = ActionId.Snapshot;
            d[Trigger.MiddleClick] = ActionId.None;
            d[Trigger.SideBack] = ActionId.None;
            d[Trigger.SideForward] = ActionId.None;
            return d;
        }

        public ActionId Binding(Trigger t)
        {
            ActionId a;
            if (Bindings != null && Bindings.TryGetValue(t, out a))
                return a;
            return ActionId.None;
        }

        public bool IsEnabled(Trigger t)
        {
            return Binding(t) != ActionId.None && (Disabled == null || !Disabled.Contains(t));
        }

        public static string FilePath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClickBind");
            return Path.Combine(dir, "settings.txt");
        }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                string path = FilePath();
                if (!File.Exists(path))
                    return s;

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (key == "active")
                        s.Active = val == "true";
                    else if (key == "start_with_windows")
                        s.StartWithWindows = val == "true";
                    else if (key == "hot_corner")
                        s.HotCornerEnabled = val == "true";
                    else if (key == "hot_corner_side" && (val == "left" || val == "right" || val == "both"))
                        s.HotCornerSide = val;
                    else if (key == "theme" && (val == "light" || val == "dark" || val == "system"))
                        s.Theme = val;
                    else if (key == "double_click_ms")
                    {
                        int n;
                        if (int.TryParse(val, out n))
                            s.DoubleClickMs = Math.Min(600, Math.Max(120, n));
                    }
                    else if (key == "chord_ms")
                    {
                        int n;
                        if (int.TryParse(val, out n))
                            s.ChordMs = Math.Min(200, Math.Max(30, n));
                    }
                    else if (key.StartsWith("bind."))
                    {
                        try
                        {
                            Trigger t = (Trigger)Enum.Parse(typeof(Trigger), key.Substring(5), true);
                            s.Bindings[t] = (ActionId)Enum.Parse(typeof(ActionId), val, true);
                        }
                        catch
                        {
                        }
                    }
                    else if (key.StartsWith("off."))
                    {
                        try
                        {
                            Trigger t = (Trigger)Enum.Parse(typeof(Trigger), key.Substring(4), true);
                            s.Disabled.Add(t);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
            return s;
        }

        public bool Save()
        {
            try
            {
                string path = FilePath();
                string dir = Path.GetDirectoryName(path);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                List<string> lines = new List<string>();
                lines.Add("active=" + (Active ? "true" : "false"));
                lines.Add("start_with_windows=" + (StartWithWindows ? "true" : "false"));
                lines.Add("hot_corner=" + (HotCornerEnabled ? "true" : "false"));
                lines.Add("hot_corner_side=" + (HotCornerSide ?? "both"));
                lines.Add("theme=" + (Theme ?? "light"));
                lines.Add("double_click_ms=" + DoubleClickMs.ToString());
                lines.Add("chord_ms=" + ChordMs.ToString());
                foreach (Trigger t in Labels.Triggers)
                {
                    lines.Add("bind." + t.ToString() + "=" + Binding(t).ToString());
                    if (Disabled != null && Disabled.Contains(t))
                        lines.Add("off." + t.ToString() + "=true");
                }

                File.WriteAllLines(path, lines.ToArray());
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Immutable snapshot read by the hook thread, swapped atomically on every change.</summary>
    public sealed class RuntimeConfig
    {
        private readonly ActionId[] _bindings;
        private readonly bool[] _disabled;

        public readonly bool Active;
        public readonly int DoubleClickMs;
        public readonly int ChordMs;
        public readonly bool HotCorner;
        public readonly bool HotCornerLeft;
        public readonly bool HotCornerRight;

        public RuntimeConfig(Settings s)
        {
            _bindings = new ActionId[10];
            _disabled = new bool[10];
            Active = s.Active;
            DoubleClickMs = s.DoubleClickMs;
            ChordMs = s.ChordMs;
            HotCorner = s.HotCornerEnabled;
            string cornerSide = s.HotCornerSide ?? "both";
            HotCornerLeft = cornerSide == "both" || cornerSide == "left";
            HotCornerRight = cornerSide == "both" || cornerSide == "right";
            foreach (Trigger t in Labels.Triggers)
            {
                _bindings[(int)t] = s.Binding(t);
                _disabled[(int)t] = s.Disabled != null && s.Disabled.Contains(t);
            }
        }

        public ActionId Binding(Trigger t)
        {
            return _bindings[(int)t];
        }

        public bool IsBound(Trigger t)
        {
            return _bindings[(int)t] != ActionId.None && !_disabled[(int)t];
        }
    }

    public static class Labels
    {
        public static readonly Trigger[] Triggers = new Trigger[]
        {
            Trigger.DoubleRight,
            Trigger.DoubleLeft,
            Trigger.LeftRight,
            Trigger.MiddleClick,
            Trigger.SideBack,
            Trigger.SideForward,
            Trigger.HoldLeft,
            Trigger.HoldRight,
            Trigger.HoldBoth
        };

        public static readonly ActionId[] Actions = new ActionId[]
        {
            ActionId.Copy,
            ActionId.Paste,
            ActionId.Snapshot,
            ActionId.Cut,
            ActionId.Undo,
            ActionId.TaskView,
            ActionId.None
        };

        public static string TriggerTitle(Trigger t)
        {
            switch (t)
            {
                case Trigger.DoubleRight: return "Double right click";
                case Trigger.DoubleLeft: return "Double left click";
                case Trigger.LeftRight: return "Left + right click";
                case Trigger.MiddleClick: return "Middle wheel click";
                case Trigger.SideBack: return "Thumb Back button (4)";
                case Trigger.SideForward: return "Thumb Forward button (5)";
                case Trigger.HotCorner: return "Top screen corner";
                case Trigger.HoldLeft: return "Hold left button";
                case Trigger.HoldRight: return "Hold right button";
                case Trigger.HoldBoth: return "Hold left + right buttons";
            }
            return t.ToString();
        }

        public static string ActionName(ActionId a)
        {
            switch (a)
            {
                case ActionId.None: return "No action";
                case ActionId.Copy: return "Copy selection";
                case ActionId.Paste: return "Paste from clipboard";
                case ActionId.Snapshot: return "Take screen snapshot";
                case ActionId.Cut: return "Cut selection";
                case ActionId.Undo: return "Undo last action";
                case ActionId.Redo: return "Redo last action";
                case ActionId.SelectAll: return "Select all";
                case ActionId.TaskView: return "Open Task View";
            }
            return a.ToString();
        }

        public static string ActionKeys(ActionId a)
        {
            switch (a)
            {
                case ActionId.Copy: return "Ctrl + C";
                case ActionId.Paste: return "Ctrl + V";
                case ActionId.Snapshot: return "Win + Shift + S";
                case ActionId.Cut: return "Ctrl + X";
                case ActionId.Undo: return "Ctrl + Z";
                case ActionId.Redo: return "Ctrl + Y";
                case ActionId.SelectAll: return "Ctrl + A";
                case ActionId.TaskView: return "Win + Tab";
            }
            return "";
        }
    }

    public class ClipboardEntry
    {
        public DateTime At;
        public string Text;
        public bool IsImage;
        public byte[] Png;
        public byte[] ThumbPng;
    }
}
