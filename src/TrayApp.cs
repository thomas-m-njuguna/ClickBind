using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
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
            _tray.Icon = TrayIcon.GetSmall();
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
                CaptureScreenshot();
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

        public void SetPadBounds(Rectangle bounds)
        {
            if (_hook != null)
                _hook.PadBounds = bounds;
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
            Config.HotCornerSide = "both";
            ApplySettings(true);
        }

        public List<ClipboardEntry> GetClipboard()
        {
            lock (_clipboard)
                return new List<ClipboardEntry>(_clipboard);
        }

        public void CopyClipboardEntry(int index)
        {
            List<ClipboardEntry> list = GetClipboard();
            if (index < 0 || index >= list.Count)
                return;

            ClipboardEntry entry = list[index];
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    if (entry.Png != null)
                    {
                        using (MemoryStream ms = new MemoryStream(entry.Png))
                        using (Image img = Image.FromStream(ms))
                            Clipboard.SetImage(new Bitmap(img));
                    }
                    else if (!string.IsNullOrEmpty(entry.Text))
                    {
                        Clipboard.SetText(entry.Text);
                    }
                    return;
                }
                catch
                {
                    Thread.Sleep(60);
                }
            }
        }

        private static bool SameImage(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

        private static byte[] ToPng(Image image)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                image.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        private static byte[] ThumbPng(Image image)
        {
            const int max = 360;
            double scale = Math.Min(1.0, Math.Min((double)max / image.Width, (double)max / image.Height));
            int w = Math.Max(1, (int)Math.Round(image.Width * scale));
            int h = Math.Max(1, (int)Math.Round(image.Height * scale));

            using (Bitmap thumb = new Bitmap(w, h))
            {
                using (Graphics g = Graphics.FromImage(thumb))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(image, 0, 0, w, h);
                }
                return ToPng(thumb);
            }
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

        // A snapshot lands on the clipboard only after the user picks a region in
        // the snipping overlay, so keep checking for a while before giving up.
        public void CaptureScreenshot()
        {
            uint baseline = Native.GetClipboardSequenceNumber();
            Thread watcher = new Thread(new ThreadStart(delegate
            {
                bool captured = false;
                for (int i = 0; i < 80; i++)
                {
                    Thread.Sleep(250);
                    // Ignore whatever was on the clipboard before the snapshot started.
                    if (Native.GetClipboardSequenceNumber() == baseline)
                        continue;
                    try
                    {
                        captured = Clipboard.ContainsImage();
                    }
                    catch
                    {
                    }
                    if (captured)
                        break;
                }
                if (captured)
                    RunOnUi(ReadClipboard);
            }));
            watcher.IsBackground = true;
            watcher.SetApartmentState(ApartmentState.STA);
            watcher.Start();
        }

        private void ReadClipboard()
        {
            string text = null;
            bool image = false;
            Image imageData = null;
            try
            {
                if (Clipboard.ContainsText())
                    text = Clipboard.GetText();
                if (string.IsNullOrEmpty(text) && Clipboard.ContainsImage())
                {
                    imageData = Clipboard.GetImage();
                    image = imageData != null;
                }
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
            if (imageData != null)
            {
                try
                {
                    entry.Png = ToPng(imageData);
                    entry.ThumbPng = ThumbPng(imageData);
                }
                catch
                {
                }
                finally
                {
                    imageData.Dispose();
                }
            }

            lock (_clipboard)
            {
                if (_clipboard.Count > 0)
                {
                    ClipboardEntry last = _clipboard[0];
                    if (last.IsImage == entry.IsImage)
                    {
                        if (entry.IsImage)
                        {
                            if (SameImage(last.Png, entry.Png))
                                return;
                        }
                        else if (string.Equals(last.Text ?? string.Empty, entry.Text ?? string.Empty))
                        {
                            return;
                        }
                    }
                }
                _clipboard.Insert(0, entry);
                if (_clipboard.Count > 15)
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
            else if (action == ActionId.Snapshot)
                CaptureScreenshot();
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
