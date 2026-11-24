using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace ClickBind
{
    public class TrayApp : ApplicationContext
    {
        public readonly Settings Config;

        private RuntimeConfig _runtime;
        private MouseHook _hook;
        private NotifyIcon _tray;
        private ToolStripMenuItem _pauseItem;
        private MainForm _form;
        private Control _invoker;
        private readonly List<ClipboardEntry> _clipboard = new List<ClipboardEntry>();
        private long _lastTrayClickAt;

        public TrayApp(bool startHidden)
        {
            Config = Settings.Load();

            // Start with Windows by default, and keep the registry path current.
            Startup.SetEnabled(Config.StartWithWindows);

            _runtime = new RuntimeConfig(Config);

            _invoker = new Control();
            IntPtr forced = _invoker.Handle;

            _hook = new MouseHook(_runtime);
            _hook.Triggered += OnGesture;
            _hook.HotCorner += OnHotCorner;
            _hook.Start();

            BuildTray();

            if (startHidden)
                NotifyHidden();
            else
            {
                ShowSettings();
            }
        }

        private void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = TrayIcon.Get();
            _tray.Text = "ClickBind";
            _tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            MenuTheme.Apply(menu);

            ToolStripMenuItem open = new ToolStripMenuItem("Open ClickBind");
            open.Font = new Font(menu.Font, FontStyle.Bold);
            open.Click += delegate { ShowSettings(); };

            _pauseItem = new ToolStripMenuItem("Pause gestures");
            _pauseItem.Click += delegate
            {
                Config.Active = !Config.Active;
                ApplySettings(true);
                SyncForm();
            };

            ToolStripMenuItem snip = new ToolStripMenuItem("Open Windows snipping tool");
            snip.Click += delegate
            {
                ThreadPool.QueueUserWorkItem(delegate { Actions.SendShortcut(ActionId.Snapshot); });
            };

            ToolStripMenuItem exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate { ExitApp(); };

            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_pauseItem);
            menu.Items.Add(snip);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exit);

            _tray.ContextMenuStrip = menu;
            _tray.MouseClick += delegate(object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left)
                    return;

                // Ignore the second half of a double-click so it does not toggle back.
                long now = Environment.TickCount;
                if (now - _lastTrayClickAt < SystemInformation.DoubleClickTime)
                    return;
                _lastTrayClickAt = now;
                ToggleWindow();
            };

            UpdateTrayText();
        }

        private void UpdateTrayText()
        {
            if (_tray == null) return;
            if (_pauseItem != null)
                _pauseItem.Text = Config.Active ? "Pause gestures" : "Resume gestures";
            _tray.Text = Config.Active ? "ClickBind - active" : "ClickBind - paused";
        }

        private void ToggleWindow()
        {
            if (_form != null && !_form.IsDisposed && _form.Visible
                && Native.GetForegroundWindow() == _form.Handle)
            {
                _form.HideToTray(false);
            }
            else
            {
                ShowSettings();
            }
        }

        public void ShowSettings()
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new MainForm(this);
                _form.FormClosed += delegate
                {
                    _form = null;
                    if (_hook != null)
                        _hook.IgnoreWindow = IntPtr.Zero;
                };
            }
            if (!_form.Visible)
                _form.Show();
            if (_hook != null)
                _hook.IgnoreWindow = _form.Handle;
            if (_form.WindowState == FormWindowState.Minimized)
                _form.WindowState = FormWindowState.Normal;
            _form.Activate();
            _form.BringToFront();
            _form.SyncAll();
        }

        private void SyncForm()
        {
            if (_form != null && !_form.IsDisposed)
                _form.SyncAll();
        }

        public void ApplySettings(bool save)
        {
            _runtime = new RuntimeConfig(Config);
            if (_hook != null)
                _hook.SetConfig(_runtime);
            if (save)
                Config.Save();
            UpdateTrayText();
        }

        public void SetActive(bool active)
        {
            Config.Active = active;
            ApplySettings(true);
        }

        public void ResetDefaults()
        {
            Config.Bindings = Settings.DefaultBindings();
            Config.Disabled = new HashSet<Trigger>();
            Config.DoubleClickMs = 280;
            Config.ChordMs = 90;
            Config.Active = true;
            Config.HotCornerEnabled = true;
            ApplySettings(true);
        }

        public List<ClipboardEntry> GetClipboard()
        {
            lock (_clipboard)
                return new List<ClipboardEntry>(_clipboard);
        }

        private void CaptureClipboard()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                // Give the foreground app a moment to fill the clipboard.
                Thread.Sleep(180);
                RunOnUi(ReadClipboard);
            });
        }

        private void ReadClipboard()
        {
            string text = null;
            bool image = false;
            try
            {
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText();
                if (string.IsNullOrEmpty(text) && Clipboard.ContainsImage())
                    image = true;
            }
            catch
            {
                return;
            }

            if (string.IsNullOrEmpty(text) && !image)
                return;

            ClipboardEntry entry = new ClipboardEntry();
            entry.At = DateTime.Now;
            entry.Text = text;
            entry.IsImage = image;

            lock (_clipboard)
            {
                if (_clipboard.Count > 0)
                {
                    ClipboardEntry last = _clipboard[0];
                    if (last.IsImage == entry.IsImage
                        && string.Equals(last.Text ?? string.Empty, entry.Text ?? string.Empty))
                        return;
                }
                _clipboard.Insert(0, entry);
                if (_clipboard.Count > 25)
                    _clipboard.RemoveAt(_clipboard.Count - 1);
            }

            string preview = image ? "Image copied to the Windows clipboard." : PreviewText(text);
            bool showing = _form != null && !_form.IsDisposed && _form.Visible;
            if (showing)
                _form.PostToast("Copied to clipboard", preview);
            else
                ShowBalloon("Copied to clipboard", preview);

            if (_form != null && !_form.IsDisposed)
                _form.OnActivityChanged();
        }

        private static string PreviewText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            string s = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (s.Length > 90)
                s = s.Substring(0, 90).TrimEnd() + "...";
            return s;
        }

        private void ShowBalloon(string title, string message)
        {
            if (_tray == null)
                return;
            try
            {
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = message;
                _tray.BalloonTipIcon = ToolTipIcon.Info;
                _tray.ShowBalloonTip(2500);
            }
            catch
            {
            }
        }

        private void OnGesture(Trigger t)
        {
            RuntimeConfig cfg = _runtime;
            if (!cfg.IsBound(t))
                return;

            ActionId action = cfg.Binding(t);
            Actions.SendShortcut(action);

            if (action == ActionId.Copy)
                CaptureClipboard();
        }

        private void OnHotCorner()
        {
            RuntimeConfig cfg = _runtime;
            if (!cfg.Active || !cfg.HotCorner)
                return;

            Actions.SendShortcut(ActionId.TaskView);
        }

        public void SnapshotNow()
        {
            ThreadPool.QueueUserWorkItem(delegate { Actions.SendShortcut(ActionId.Snapshot); });
        }

        private void RunOnUi(MethodInvoker action)
        {
            try
            {
                if (_invoker != null && _invoker.IsHandleCreated)
                    _invoker.BeginInvoke(action);
            }
            catch
            {
            }
        }

        public void NotifyHidden()
        {
            if (_tray == null)
                return;
            try
            {
                _tray.BalloonTipTitle = "ClickBind is running in the tray";
                _tray.BalloonTipText = "Your mouse shortcuts stay active in the background. Click the tray icon to open settings.";
                _tray.BalloonTipIcon = ToolTipIcon.Info;
                _tray.ShowBalloonTip(3000);
            }
            catch
            {
            }
        }

        private void ExitApp()
        {
            if (_hook != null)
            {
                _hook.Dispose();
                _hook = null;
            }
            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_hook != null) _hook.Dispose();
                if (_tray != null) _tray.Dispose();
                if (_form != null && !_form.IsDisposed) _form.Dispose();
                if (_invoker != null) _invoker.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
