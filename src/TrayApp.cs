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
        private HotkeyManager _hotkeys;
        private string _hotkeySignature;
        private readonly List<ClipboardEntry> _clipboard = new List<ClipboardEntry>();
        private ClipboardWatcher _clipboardWatch;
        private bool _copyPending;
        private DateTime _copyPendingAt;
        private bool _snapshotPending;
        private DateTime _snapshotPendingAt;
        private long _appCopyToastUntilTicks;
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

            _hotkeys = new HotkeyManager();
            ApplyKeyShortcuts();

            // The Windows clipboard is the source of truth: every copy, from any
            // app, is recorded, and a shortcut that copied nothing records nothing.
            _clipboardWatch = new ClipboardWatcher();
            _clipboardWatch.Changed += OnClipboardChanged;

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
            ApplyKeyShortcuts();
            if (save)
                Config.Save();
            UpdateTrayText();
        }

        // Re-registering on every settings change would be wasteful, so only touch the
        // global hotkeys when an assigned app actually changed.
        private void ApplyKeyShortcuts()
        {
            if (_hotkeys == null)
                return;

            System.Text.StringBuilder signature = new System.Text.StringBuilder();
            foreach (KeyShortcutDef def in KeyShortcuts.Definitions)
            {
                string target;
                Config.KeyTargets.TryGetValue(def.Id, out target);
                signature.Append(def.Id).Append('=').Append(target ?? string.Empty).Append('|');
            }

            string next = signature.ToString();
            if (next == _hotkeySignature)
                return;

            _hotkeySignature = next;
            _hotkeys.Apply(Config.KeyTargets, ReportHotkeyError);
            SyncForm();
        }

        private void ReportHotkeyError(string message)
        {
            RunOnUi(delegate
            {
                bool showing = _form != null && !_form.IsDisposed && _form.Visible;
                if (showing)
                    _form.PostToast("Shortcut failed", message);
                else
                    ShowBalloon("Shortcut failed", message);
            });
        }

        public string KeyTarget(string id)
        {
            string target;
            if (Config.KeyTargets != null && Config.KeyTargets.TryGetValue(id, out target) && target != null)
                return target;
            return string.Empty;
        }

        public string KeyTargetName(string id)
        {
            return AppPickerDialog.FriendlyName(KeyTarget(id));
        }

        public string KeyTargetIcon(string id)
        {
            return AppPickerDialog.IconDataUrl(KeyTarget(id));
        }

        public bool KeyShortcutAvailable(string id)
        {
            return _hotkeys == null || !_hotkeys.Unavailable.Contains(id);
        }

        public void SetKeyTarget(string id, string target)
        {
            if (KeyShortcuts.Find(id) == null)
                return;

            if (Config.KeyTargets == null)
                Config.KeyTargets = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(target) || target.Trim().Length == 0)
                Config.KeyTargets.Remove(id);
            else
                Config.KeyTargets[id] = target.Trim();

            ApplySettings(true);
            SyncForm();
        }

        public void PickKeyTarget(string id)
        {
            KeyShortcutDef def = KeyShortcuts.Find(id);
            if (def == null)
                return;

            using (AppPickerDialog dialog = new AppPickerDialog(KeyShortcuts.KeysText(def)))
            {
                if (dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
                    SetKeyTarget(id, dialog.SelectedPath);
            }
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
            Config.KeyTargets = new Dictionary<string, string>();
            _hotkeySignature = null;
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
            // Our own write also raises the clipboard event; the interface already
            // confirms it, so do not add a second notification.
            _appCopyToastUntilTicks = DateTime.UtcNow.AddSeconds(1).Ticks;
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

        private void OnClipboardChanged()
        {
            DateTime now = DateTime.UtcNow;
            bool copyNotice = _copyPending && (now - _copyPendingAt).TotalSeconds < 3.0;
            bool snapshotNotice = _snapshotPending && (now - _snapshotPendingAt).TotalSeconds < 30.0;

            ClipboardEntry entry = ReadClipboard();
            if (entry == null)
                return;

            _copyPending = false;
            if (copyNotice || !snapshotNotice || entry.IsImage)
                _snapshotPending = false;

            if (!copyNotice && !(snapshotNotice && entry.IsImage))
                return;

            if (now.Ticks <= _appCopyToastUntilTicks)
                return;

            bool showing = _form != null && !_form.IsDisposed && _form.Visible;
            if (showing)
                _form.PostToast("Copied", "Saved in the ClickBind clipboard.");
            else
                ShowBalloon("Copied", "Saved in the ClickBind clipboard.");
        }

        // The snipping overlay writes the image when the user finishes the snip;
        // the clipboard watcher picks it up, so here we only mark that we wait.
        public void CaptureScreenshot()
        {
            _snapshotPending = true;
            _snapshotPendingAt = DateTime.UtcNow;
        }

        /// <summary>Reads what is on the Windows clipboard, or null when it holds nothing usable.</summary>
        private ClipboardEntry ReadClipboard()
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
                if (string.IsNullOrEmpty(text) && !image && Clipboard.ContainsFileDropList())
                {
                    System.Collections.Specialized.StringCollection files = Clipboard.GetFileDropList();
                    List<string> paths = new List<string>();
                    foreach (string file in files)
                    {
                        if (!string.IsNullOrEmpty(file))
                            paths.Add(file);
                    }
                    if (paths.Count > 0)
                        text = string.Join(Environment.NewLine, paths.ToArray());
                }
            }
            catch
            {
                if (imageData != null)
                    imageData.Dispose();
                return null;
            }

            if (string.IsNullOrEmpty(text) && !image)
                return null;

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
                bool duplicate = false;
                if (_clipboard.Count > 0)
                {
                    ClipboardEntry last = _clipboard[0];
                    if (last.IsImage == entry.IsImage)
                    {
                        if (entry.IsImage)
                            duplicate = SameImage(last.Png, entry.Png);
                        else
                            duplicate = string.Equals(last.Text ?? string.Empty, entry.Text ?? string.Empty);
                    }
                }

                // Entries stay until the user deletes them or Windows restarts.
                if (!duplicate)
                    _clipboard.Insert(0, entry);
            }

            if (_form != null && !_form.IsDisposed)
                _form.OnActivityChanged();

            return entry;
        }

        public void DeleteClipboardEntry(int index)
        {
            lock (_clipboard)
            {
                if (index < 0 || index >= _clipboard.Count)
                    return;
                _clipboard.RemoveAt(index);
            }

            if (_form != null && !_form.IsDisposed)
                _form.OnActivityChanged();
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

            if (action == ActionId.Copy)
            {
                _copyPending = true;
                _copyPendingAt = DateTime.UtcNow;
            }
            else if (action == ActionId.Snapshot)
            {
                _snapshotPending = true;
                _snapshotPendingAt = DateTime.UtcNow;
            }

            Actions.SendShortcut(action);

            if (action == ActionId.Snapshot)
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
            if (_hotkeys != null)
            {
                _hotkeys.Dispose();
                _hotkeys = null;
            }
            if (_clipboardWatch != null)
            {
                _clipboardWatch.Dispose();
                _clipboardWatch = null;
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
                if (_hotkeys != null) _hotkeys.Dispose();
                if (_clipboardWatch != null) _clipboardWatch.Dispose();
                if (_tray != null) _tray.Dispose();
                if (_form != null && !_form.IsDisposed) _form.Dispose();
                if (_invoker != null) _invoker.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
