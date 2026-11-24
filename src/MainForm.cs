using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClickBind
{
    public class MainForm : Form
    {
        private readonly TrayApp _app;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly Timer _saveTimer;
        private WebView2 _web;
        private bool _ready;
        private bool _maximized;
        private Rectangle _restoreBounds;

        public MainForm(TrayApp app)
        {
            _app = app;
            Text = "ClickBind";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1180, 760);
            MinimumSize = new Size(960, 620);
            BackColor = Color.White;
            Icon = TrayIcon.Get();
            ShowInTaskbar = true;
            KeyPreview = true;

            _saveTimer = new Timer();
            _saveTimer.Interval = 450;
            _saveTimer.Tick += delegate
            {
                _saveTimer.Stop();
                _app.ApplySettings(true);
            };

            _web = new WebView2();
            _web.Dock = DockStyle.Fill;
            _web.DefaultBackgroundColor = Color.FromArgb(249, 249, 255);
            Controls.Add(_web);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // A borderless window drops these styles, which makes Windows ignore
                // the taskbar button's minimize/restore request. Put them back so the
                // taskbar icon behaves like any other app.
                cp.Style |= 0x00080000; // WS_SYSMENU
                cp.Style |= 0x00020000; // WS_MINIMIZEBOX
                return cp;
            }
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            int w = Math.Min(Width, area.Width);
            int h = Math.Min(Height, area.Height);
            SetBounds(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);

            // Never sit above other applications.
            TopMost = false;
            Native.SetWindowPos(Handle, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002);

            try
            {
                await InitWebView();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "ClickBind could not start its embedded browser.\n\n" + ex.Message,
                    "ClickBind", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task InitWebView()
        {
            string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClickBind", "WebView2");
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userData);
            await _web.EnsureCoreWebView2Async(environment);

            CoreWebView2 core = _web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;

            core.WebMessageReceived += OnWebMessage;
            core.NavigationCompleted += delegate
            {
                _ready = true;
                PostState();
            };

            string ui = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui");
            if (!File.Exists(Path.Combine(ui, "index.html")))
                throw new FileNotFoundException("The ClickBind interface folder is missing.", Path.Combine(ui, "index.html"));

            core.SetVirtualHostNameToFolderMapping("clickbind.local", ui, CoreWebView2HostResourceAccessKind.Allow);
            core.Navigate("https://clickbind.local/index.html");
        }

        public void SyncAll()
        {
            PostState();
        }

        public void OnActivityChanged()
        {
            PostState();
        }

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> msg;
            try
            {
                msg = _json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson);
            }
            catch
            {
                return;
            }
            if (msg == null)
                return;

            string cmd = Str(msg, "cmd");
            switch (cmd)
            {
                case "ready":
                    PostState();
                    break;

                case "setActive":
                    _app.SetActive(Bool(msg, "value"));
                    PostState();
                    break;

                case "bind":
                    Bind(Str(msg, "trigger"), Str(msg, "action"));
                    break;

                case "toggle":
                    SetTriggerEnabled(Str(msg, "trigger"), Bool(msg, "enabled"));
                    break;

                case "timing":
                    ApplyTiming(Int(msg, "doubleMs"), Int(msg, "chordMs"));
                    break;

                case "startup":
                    _app.Config.StartWithWindows = Bool(msg, "value");
                    Startup.SetEnabled(_app.Config.StartWithWindows);
                    _app.ApplySettings(true);
                    PostState();
                    break;

                case "hotCorner":
                    _app.Config.HotCornerEnabled = Bool(msg, "value");
                    _app.ApplySettings(true);
                    PostState();
                    break;

                case "theme":
                    string theme = Str(msg, "value");
                    if (theme == "light" || theme == "dark" || theme == "system")
                    {
                        _app.Config.Theme = theme;
                        _app.ApplySettings(true);
                    }
                    PostState();
                    break;

                case "padRect":
                {
                    int pw = Int(msg, "width");
                    int ph = Int(msg, "height");
                    if (pw <= 0 || ph <= 0)
                    {
                        _app.SetPadBounds(Rectangle.Empty);
                        break;
                    }
                    Point origin = _web.PointToScreen(new Point(Int(msg, "left"), Int(msg, "top")));
                    _app.SetPadBounds(new Rectangle(origin, new Size(pw, ph)));
                    break;
                }

                case "reset":
                    _app.ResetDefaults();
                    PostState();
                    break;

                case "snapshot":
                    Actions.SendShortcut(ActionId.Snapshot);
                    break;

                case "window":
                    WindowCommand(Str(msg, "action"));
                    break;

                case "drag":
                    DragWindow();
                    break;
            }
        }

        private void Bind(string triggerId, string actionId)
        {
            Trigger trigger = ParseTrigger(triggerId);
            ActionId action = ParseAction(actionId);

            _app.Config.Bindings[trigger] = action;
            _app.Config.Disabled.Remove(trigger);
            _app.ApplySettings(true);
            PostState();
        }

        private void SetTriggerEnabled(string triggerId, bool enabled)
        {
            Trigger trigger = ParseTrigger(triggerId);
            if (enabled)
                _app.Config.Disabled.Remove(trigger);
            else
                _app.Config.Disabled.Add(trigger);
            _app.ApplySettings(true);
            PostState();
        }

        private void ApplyTiming(int doubleMs, int chordMs)
        {
            if (doubleMs >= 120 && doubleMs <= 600)
                _app.Config.DoubleClickMs = doubleMs;
            if (chordMs >= 30 && chordMs <= 200)
                _app.Config.ChordMs = chordMs;
            _app.ApplySettings(false);

            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void WindowCommand(string action)
        {
            if (action == "minimize")
            {
                WindowState = FormWindowState.Minimized;
            }
            else if (action == "maximize")
            {
                ToggleMaximize();
            }
            else if (action == "close")
            {
                HideToTray(true);
            }
            else if (action == "tray")
            {
                HideToTray(false);
            }
        }

        private void ToggleMaximize()
        {
            if (!_maximized)
            {
                _restoreBounds = Bounds;
                MaximizedBounds = Screen.FromControl(this).WorkingArea;
                WindowState = FormWindowState.Maximized;
                _maximized = true;
            }
            else
            {
                WindowState = FormWindowState.Normal;
                Bounds = _restoreBounds;
                _maximized = false;
            }
            PostState();
        }

        private void DragWindow()
        {
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
        }

        public void HideToTray(bool notify)
        {
            Hide();
            if (notify)
                _app.NotifyHidden();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray(true);
                return;
            }
            base.OnFormClosing(e);
        }

        private void PostState()
        {
            if (!_ready || _web == null || _web.CoreWebView2 == null)
                return;

            Dictionary<string, object> st = new Dictionary<string, object>();
            st["active"] = _app.Config.Active;
            st["doubleMs"] = _app.Config.DoubleClickMs;
            st["chordMs"] = _app.Config.ChordMs;
            st["startWithWindows"] = _app.Config.StartWithWindows;
            st["hotCorner"] = _app.Config.HotCornerEnabled;
            st["theme"] = _app.Config.Theme ?? "light";
            st["maximized"] = _maximized;

            List<object> bindings = new List<object>();
            foreach (Trigger t in Labels.Triggers)
            {
                ActionId action = _app.Config.Binding(t);
                if (action == ActionId.None)
                    continue;

                Dictionary<string, object> b = new Dictionary<string, object>();
                b["trigger"] = TriggerId(t);
                b["action"] = ActionIdString(action);
                b["enabled"] = _app.Config.IsEnabled(t);
                bindings.Add(b);
            }
            st["bindings"] = bindings;

            List<object> clipboard = new List<object>();
            foreach (ClipboardEntry entry in _app.GetClipboard())
            {
                Dictionary<string, object> c = new Dictionary<string, object>();
                c["text"] = entry.Text ?? string.Empty;
                c["image"] = entry.IsImage;
                c["at"] = ToEpochMs(entry.At);
                clipboard.Add(c);
            }
            st["clipboard"] = clipboard;

            Dictionary<string, object> msg = new Dictionary<string, object>();
            msg["type"] = "state";
            msg["state"] = st;

            try
            {
                _web.CoreWebView2.PostWebMessageAsJson(_json.Serialize(msg));
            }
            catch
            {
            }
        }

        public void PostToast(string title, string message)
        {
            if (!_ready || _web == null || _web.CoreWebView2 == null)
                return;
            Dictionary<string, object> msg = new Dictionary<string, object>();
            msg["type"] = "toast";
            msg["title"] = title;
            msg["message"] = message;
            try
            {
                _web.CoreWebView2.PostWebMessageAsJson(_json.Serialize(msg));
            }
            catch
            {
            }
        }

        private static long ToEpochMs(DateTime at)
        {
            DateTime utc = at.ToUniversalTime();
            return (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        private static string Str(Dictionary<string, object> msg, string key)
        {
            object value;
            if (msg.TryGetValue(key, out value) && value != null)
                return Convert.ToString(value);
            return null;
        }

        private static bool Bool(Dictionary<string, object> msg, string key)
        {
            object value;
            if (msg.TryGetValue(key, out value) && value != null)
            {
                try { return Convert.ToBoolean(value); }
                catch { return false; }
            }
            return false;
        }

        private static int Int(Dictionary<string, object> msg, string key)
        {
            object value;
            if (msg.TryGetValue(key, out value) && value != null)
            {
                try { return Convert.ToInt32(value); }
                catch { return 0; }
            }
            return 0;
        }

        private static Trigger ParseTrigger(string id)
        {
            switch (id)
            {
                case "double-right": return Trigger.DoubleRight;
                case "double-left": return Trigger.DoubleLeft;
                case "both-buttons": return Trigger.LeftRight;
                case "middle-click": return Trigger.MiddleClick;
                case "side-back": return Trigger.SideBack;
                case "side-forward": return Trigger.SideForward;
                case "hold-left": return Trigger.HoldLeft;
                case "hold-right": return Trigger.HoldRight;
                case "hold-both": return Trigger.HoldBoth;
            }
            return Trigger.DoubleRight;
        }

        private static ActionId ParseAction(string id)
        {
            switch (id)
            {
                case "copy": return ActionId.Copy;
                case "paste": return ActionId.Paste;
                case "snapshot": return ActionId.Snapshot;
                case "cut": return ActionId.Cut;
                case "undo": return ActionId.Undo;
                case "redo": return ActionId.Redo;
                case "selectall": return ActionId.SelectAll;
                case "task-view": return ActionId.TaskView;
            }
            return ActionId.None;
        }

        private static string TriggerId(Trigger t)
        {
            switch (t)
            {
                case Trigger.DoubleRight: return "double-right";
                case Trigger.DoubleLeft: return "double-left";
                case Trigger.LeftRight: return "both-buttons";
                case Trigger.MiddleClick: return "middle-click";
                case Trigger.SideBack: return "side-back";
                case Trigger.SideForward: return "side-forward";
                case Trigger.HotCorner: return "hot-corner";
                case Trigger.HoldLeft: return "hold-left";
                case Trigger.HoldRight: return "hold-right";
                case Trigger.HoldBoth: return "hold-both";
            }
            return "double-right";
        }

        private static string ActionIdString(ActionId a)
        {
            switch (a)
            {
                case ActionId.Copy: return "copy";
                case ActionId.Paste: return "paste";
                case ActionId.Snapshot: return "snapshot";
                case ActionId.Cut: return "cut";
                case ActionId.Undo: return "undo";
                case ActionId.Redo: return "redo";
                case ActionId.SelectAll: return "selectall";
                case ActionId.TaskView: return "task-view";
            }
            return "none";
        }
    }
}
