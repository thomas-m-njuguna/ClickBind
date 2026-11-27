using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace ClickBind
{
    internal sealed class AppEntry
    {
        public string Name;
        public string Path;
        public string Group;
        public string Target;
        public Bitmap Icon;
        public int ImageIndex = -1;
    }

    /// <summary>
    /// Lets people pick an installed app by its name instead of typing a file path.
    /// Lists the shortcuts in the Start menu for all users and for the current user.
    /// </summary>
    internal sealed class AppPickerDialog : Form
    {
        private readonly TextBox _search;
        private readonly ListView _list;
        private readonly Label _hint;
        private readonly Button _choose;
        private readonly ImageList _icons = new ImageList();
        private readonly List<AppEntry> _apps = new List<AppEntry>();
        private bool _loaded;

        public string SelectedPath;

        public AppPickerDialog(string keysText)
        {
            Text = "Choose an app for " + keysText;
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.FromArgb(250, 249, 252);
            ClientSize = new Size(560, 540);
            MinimumSize = new Size(420, 380);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            MinimizeBox = false;

            _icons.ImageSize = new Size(20, 20);
            _icons.ColorDepth = ColorDepth.Depth32Bit;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 4;
            layout.Padding = new Padding(16, 16, 16, 12);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

            Panel searchRow = new Panel();
            searchRow.Dock = DockStyle.Fill;
            searchRow.Margin = new Padding(0, 0, 0, 6);

            Label searchLabel = new Label();
            searchLabel.Text = "Search";
            searchLabel.AutoSize = false;
            searchLabel.Dock = DockStyle.Left;
            searchLabel.Width = 56;
            searchLabel.TextAlign = ContentAlignment.MiddleLeft;

            _search = new TextBox();
            _search.Dock = DockStyle.Fill;
            _search.BorderStyle = BorderStyle.FixedSingle;

            searchRow.Controls.Add(_search);
            searchRow.Controls.Add(searchLabel);

            _hint = new Label();
            _hint.Dock = DockStyle.Fill;
            _hint.TextAlign = ContentAlignment.MiddleLeft;
            _hint.ForeColor = Color.FromArgb(95, 94, 104);
            _hint.Text = "Looking for installed apps\u2026";

            _list = new ListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.MultiSelect = false;
            _list.HideSelection = false;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            _list.SmallImageList = _icons;
            _list.BorderStyle = BorderStyle.FixedSingle;
            _list.Columns.Add("App", 320);
            _list.Columns.Add("Folder", 190);

            FlowLayoutPanel footer = new FlowLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.FlowDirection = FlowDirection.RightToLeft;
            footer.WrapContents = false;
            footer.Margin = new Padding(0, 8, 0, 0);

            _choose = MakeButton("Choose app", 110);
            _choose.Enabled = false;
            Button browse = MakeButton("Browse\u2026", 92);
            Button cancel = MakeButton("Cancel", 84);

            footer.Controls.Add(_choose);
            footer.Controls.Add(browse);
            footer.Controls.Add(cancel);

            layout.Controls.Add(searchRow, 0, 0);
            layout.Controls.Add(_hint, 0, 1);
            layout.Controls.Add(_list, 0, 2);
            layout.Controls.Add(footer, 0, 3);

            Controls.Add(layout);

            AcceptButton = _choose;
            CancelButton = cancel;

            _search.TextChanged += delegate { ApplyFilter(); };
            _search.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Down && _list.Items.Count > 0)
                {
                    _list.Focus();
                    if (_list.SelectedItems.Count == 0)
                        _list.Items[0].Selected = true;
                    e.Handled = true;
                }
            };
            _list.DoubleClick += delegate { Choose(); };
            _choose.Click += delegate { Choose(); };
            browse.Click += delegate { Browse(); };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            FormClosed += delegate { _icons.Dispose(); };
        }

        private static Button MakeButton(string text, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = width;
            button.Height = 30;
            button.Margin = new Padding(6, 0, 0, 0);
            button.FlatStyle = FlatStyle.System;
            return button;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _search.Focus();
            ThreadPool.QueueUserWorkItem(delegate { LoadApps(); });
        }

        private void LoadApps()
        {
            List<AppEntry> found = new List<AppEntry>();
            try
            {
                found = FindApps();
            }
            catch
            {
            }

            // Fill the list with names first; icons arrive a moment later so the
            // dialog never looks empty while the shell loads them.
            try
            {
                if (IsDisposed || !IsHandleCreated)
                    return;

                BeginInvoke((MethodInvoker)delegate { Populate(found); });
            }
            catch
            {
                return;
            }

            foreach (AppEntry entry in found)
            {
                try
                {
                    Icon icon = Icon.ExtractAssociatedIcon(entry.Path);
                    if (icon == null)
                        continue;

                    using (icon)
                    {
                        Bitmap bitmap = new Bitmap(20, 20);
                        using (Graphics g = Graphics.FromImage(bitmap))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(icon.ToBitmap(), new Rectangle(0, 0, 20, 20));
                        }
                        entry.Icon = bitmap;
                    }
                }
                catch
                {
                }
            }

            try
            {
                if (IsDisposed || !IsHandleCreated)
                    return;

                BeginInvoke((MethodInvoker)delegate { ApplyIcons(); });
            }
            catch
            {
            }
        }

        private void Populate(List<AppEntry> apps)
        {
            if (IsDisposed)
                return;

            _apps.Clear();
            _apps.AddRange(apps);

            _icons.Images.Clear();

            _loaded = true;
            ApplyFilter();
        }

        /// <summary>Adds the app icons once the shell has produced them.</summary>
        private void ApplyIcons()
        {
            if (IsDisposed)
                return;

            for (int i = 0; i < _apps.Count; i++)
            {
                AppEntry entry = _apps[i];
                if (entry.Icon == null)
                    continue;

                _icons.Images.Add(entry.Icon);
                entry.ImageIndex = _icons.Images.Count - 1;
            }

            for (int i = 0; i < _list.Items.Count; i++)
            {
                AppEntry entry = _list.Items[i].Tag as AppEntry;
                if (entry != null && entry.ImageIndex >= 0)
                    _list.Items[i].ImageIndex = entry.ImageIndex;
            }
        }

        private void ApplyFilter()
        {
            string query = _search.Text == null ? string.Empty : _search.Text.Trim();

            _list.BeginUpdate();
            _list.Items.Clear();

            int shown = 0;
            for (int i = 0; i < _apps.Count; i++)
            {
                AppEntry entry = _apps[i];
                if (!Matches(entry, query))
                    continue;

                ListViewItem item = new ListViewItem(entry.Name);
                item.SubItems.Add(entry.Group ?? string.Empty);
                item.Tag = entry;
                if (entry.ImageIndex >= 0)
                    item.ImageIndex = entry.ImageIndex;

                _list.Items.Add(item);
                shown++;
            }
            _list.EndUpdate();

            if (!_loaded)
            {
                _hint.Text = "Looking for installed apps\u2026";
                _choose.Enabled = false;
                return;
            }

            if (_apps.Count == 0)
                _hint.Text = "No apps found. Use Browse\u2026 to pick the .exe yourself.";
            else if (query.Length > 0)
                _hint.Text = shown + " of " + _apps.Count + " apps match \u201c" + query + "\u201d.";
            else
                _hint.Text = _apps.Count + " apps found. Double-click one to use it.";

            _choose.Enabled = shown > 0;
        }

        private static bool Matches(AppEntry entry, string query)
        {
            if (query.Length == 0)
                return true;

            if (entry.Name != null && entry.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return entry.Group != null && entry.Group.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Choose()
        {
            AppEntry entry = null;

            if (_list.SelectedItems.Count > 0)
                entry = _list.SelectedItems[0].Tag as AppEntry;

            if (entry == null && _list.Items.Count > 0)
                entry = _list.Items[0].Tag as AppEntry;

            if (entry == null)
                return;

            SelectedPath = entry.Path;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Browse()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose an app";
                dialog.Filter = "Applications (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|All files (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                SelectedPath = dialog.FileName;
                DialogResult = DialogResult.OK;
                Close();
            }
        }

        private static bool _canResolve;

        private static List<AppEntry> FindApps()
        {
            _canResolve = Type.GetTypeFromProgID("WScript.Shell") != null;

            List<AppEntry> apps = new List<AppEntry>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] roots = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
            };

            for (int i = 0; i < roots.Length; i++)
            {
                string root = roots[i];
                if (!Directory.Exists(root))
                    continue;

                Walk(root, root, apps, seen);
            }

            apps.Sort(delegate(AppEntry a, AppEntry b)
            {
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            // One entry per program: a second shortcut that starts the same .exe
            // ("VLC media player skinned", "... (safe mode)") is dropped.
            List<AppEntry> unique = new List<AppEntry>();
            HashSet<string> targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < apps.Count; i++)
            {
                AppEntry entry = apps[i];
                if (!string.IsNullOrEmpty(entry.Target) && !targets.Add(entry.Target))
                    continue;

                unique.Add(entry);
            }
            return unique;
        }

        private static void Walk(string directory, string root, List<AppEntry> apps, HashSet<string> seen)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(directory, "*.lnk");
            }
            catch
            {
                files = new string[0];
            }

            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                string name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrEmpty(name) || Skip(name))
                    continue;

                string group = FolderLabel(root, file);
                if (SkipGroup(group))
                    continue;

                // Keep the list to real apps: Windows tools, control panel items and
                // shortcuts that only open a folder or a web page are dropped.
                string target = ResolveTarget(file);
                if (IsSystemTarget(target))
                    continue;

                if (!seen.Add(name))
                    continue;

                AppEntry entry = new AppEntry();
                entry.Name = name;
                entry.Path = file;
                entry.Group = group;
                entry.Target = target;
                apps.Add(entry);
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch
            {
                children = new string[0];
            }

            for (int i = 0; i < children.Length; i++)
                Walk(children[i], root, apps, seen);
        }

        private static string FolderLabel(string root, string file)
        {
            try
            {
                string directory = Path.GetDirectoryName(file);
                if (string.IsNullOrEmpty(directory) || directory.Length <= root.Length)
                    return string.Empty;

                return directory.Substring(root.Length).TrimStart('\\');
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>Shortcuts nobody wants in an app list.</summary>
        private static bool Skip(string name)
        {
            string lower = name.ToLowerInvariant();
            string[] words = new string[]
            {
                "uninstall", "readme", "read me", "help", "documentation", "manual",
                "release notes", "changelog", "change log", "license", "licence",
                "website", "homepage", "repair", "modify", "remove ", "update",
                "setup", "install", "registration", "activate", "sample", "reset"
            };

            for (int i = 0; i < words.Length; i++)
            {
                if (lower.IndexOf(words[i], StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>Start menu folders that only hold Windows tools.</summary>
        private static bool SkipGroup(string group)
        {
            if (string.IsNullOrEmpty(group))
                return false;

            string lower = group.ToLowerInvariant();
            string[] words = new string[]
            {
                "administrative tools", "system tools", "accessories", "accessibility",
                "ease of access", "windows system", "windows powershell", "windows kits",
                "windows administrative", "startup"
            };

            for (int i = 0; i < words.Length; i++)
            {
                if (lower.IndexOf(words[i], StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>True when the shortcut points at Windows itself, a folder or a web page.</summary>
        private static bool IsSystemTarget(string target)
        {
            if (!_canResolve)
                return false;

            // No target: the shortcut opens a folder, a website or a setting.
            if (string.IsNullOrEmpty(target))
                return true;

            string extension = Path.GetExtension(target).ToLowerInvariant();
            if (extension != ".exe" && extension != ".bat" && extension != ".cmd")
                return true;

            string fileName = Path.GetFileNameWithoutExtension(target).ToLowerInvariant();
            if (fileName.StartsWith("unins", StringComparison.Ordinal) || fileName.IndexOf("setup", StringComparison.Ordinal) >= 0)
                return true;

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (windows.Length > 0 && target.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;

            return target.IndexOf("\\Windows Kits\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static object _shell;
        private static Type _shellType;

        /// <summary>Follows a .lnk to the program it starts, using the Windows shell.</summary>
        private static string ResolveTarget(string shortcutPath)
        {
            if (!_canResolve)
                return string.Empty;

            try
            {
                if (_shellType == null)
                    _shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (_shellType == null)
                    return string.Empty;

                if (_shell == null)
                    _shell = Activator.CreateInstance(_shellType);

                object link = _shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, _shell, new object[] { shortcutPath });
                object target = _shellType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null);
                return target as string ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static readonly Dictionary<string, string> IconCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>PNG data URL of the app a stored shortcut target opens, or an empty string.</summary>
        public static string IconDataUrl(string target)
        {
            if (string.IsNullOrEmpty(target))
                return string.Empty;

            string key = target.Trim();
            if (key.Length == 0)
                return string.Empty;

            lock (IconCache)
            {
                string cached;
                if (IconCache.TryGetValue(key, out cached))
                    return cached;
            }

            string data = LoadIconDataUrl(key);
            lock (IconCache)
                IconCache[key] = data;
            return data;
        }

        private static string LoadIconDataUrl(string target)
        {
            try
            {
                string path = target;
                if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_canResolve)
                        _canResolve = Type.GetTypeFromProgID("WScript.Shell") != null;

                    string resolved = ResolveTarget(path);
                    if (!string.IsNullOrEmpty(resolved))
                        path = resolved;
                }

                if (!File.Exists(path))
                    return string.Empty;

                using (Icon icon = Icon.ExtractAssociatedIcon(path))
                {
                    if (icon == null)
                        return string.Empty;

                    using (Bitmap bitmap = new Bitmap(32, 32))
                    {
                        using (Graphics g = Graphics.FromImage(bitmap))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.SmoothingMode = SmoothingMode.HighQuality;
                            g.DrawImage(icon.ToBitmap(), new Rectangle(0, 0, 32, 32));
                        }

                        using (MemoryStream stream = new MemoryStream())
                        {
                            bitmap.Save(stream, ImageFormat.Png);
                            return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
                        }
                    }
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>User-facing name for a stored shortcut target.</summary>
        public static string FriendlyName(string target)
        {
            if (string.IsNullOrEmpty(target))
                return string.Empty;

            string value = target.Trim();
            if (value.Length == 0)
                return string.Empty;

            try
            {
                if (value.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    return Path.GetFileNameWithoutExtension(value);

                if (!value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return value;

                string name = Path.GetFileNameWithoutExtension(value);
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(value);
                string description = info == null ? null : info.FileDescription;
                if (!string.IsNullOrEmpty(description))
                {
                    description = description.Trim();
                    if (description.Length > 0 && description.Length < 60)
                        return description;
                }

                return string.IsNullOrEmpty(name) ? value : name;
            }
            catch
            {
                return value;
            }
        }
    }
}
