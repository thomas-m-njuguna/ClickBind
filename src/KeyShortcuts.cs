using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace ClickBind
{
    public sealed class KeyShortcutDef
    {
        public readonly string Id;
        public readonly string Label;
        public readonly string[] Keys;
        public readonly uint Modifiers;
        public readonly uint Vk;

        public KeyShortcutDef(string id, string label, uint vk, string keyName)
        {
            Id = id;
            Label = label;
            Vk = vk;
            // Alt + Shift is only three keys to press, and Alt + Shift + a letter is
            // rarely taken by Windows, browsers or other apps.
            Modifiers = Native.MOD_ALT | Native.MOD_SHIFT;
            Keys = new string[] { "Alt", "Shift", keyName };
        }
    }

    public static class KeyShortcuts
    {
        public static readonly KeyShortcutDef[] Definitions = new KeyShortcutDef[]
        {
            new KeyShortcutDef("slot-1", "Shortcut 1", 0x4E, "N"),
            new KeyShortcutDef("slot-2", "Shortcut 2", 0x43, "C"),
            new KeyShortcutDef("slot-3", "Shortcut 3", 0x45, "E"),
            new KeyShortcutDef("slot-4", "Shortcut 4", 0x42, "B"),
            new KeyShortcutDef("slot-5", "Shortcut 5", 0x54, "T"),
            new KeyShortcutDef("slot-6", "Shortcut 6", 0x53, "S")
        };

        public static KeyShortcutDef Find(string id)
        {
            foreach (KeyShortcutDef def in Definitions)
            {
                if (def.Id == id)
                    return def;
            }
            return null;
        }

        public static string KeysText(KeyShortcutDef def)
        {
            return string.Join(" + ", def.Keys);
        }
    }

    /// <summary>Hidden window that receives WM_HOTKEY from the UI thread message loop.</summary>
    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        public event Action<int> Pressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                Action<int> handler = Pressed;
                if (handler != null)
                    handler(m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
                DestroyHandle();
        }
    }

    public sealed class HotkeyManager : IDisposable
    {
        private readonly HotkeyWindow _window = new HotkeyWindow();
        private readonly Dictionary<int, string> _targets = new Dictionary<int, string>();
        private readonly List<string> _unavailable = new List<string>();
        private Action<string> _reportError;

        public HotkeyManager()
        {
            _window.Pressed += OnPressed;
        }

        public List<string> Unavailable
        {
            get { return new List<string>(_unavailable); }
        }

        /// <summary>
        /// Registers every assigned shortcut. Unassigned slots keep their keys free.
        /// </summary>
        public void Apply(Dictionary<string, string> targets, Action<string> reportError)
        {
            _reportError = reportError;
            _unavailable.Clear();
            _targets.Clear();

            foreach (int id in new List<int>(_registeredIds))
                Native.UnregisterHotKey(_window.Handle, id);
            _registeredIds.Clear();

            for (int i = 0; i < KeyShortcuts.Definitions.Length; i++)
            {
                KeyShortcutDef def = KeyShortcuts.Definitions[i];
                string target;
                if (targets == null || !targets.TryGetValue(def.Id, out target) || string.IsNullOrEmpty(target) || target.Trim().Length == 0)
                    continue;

                int slot = i + 1;
                if (!Native.RegisterHotKey(_window.Handle, slot, def.Modifiers | Native.MOD_NOREPEAT, def.Vk))
                {
                    _unavailable.Add(def.Id);
                    continue;
                }

                _registeredIds.Add(slot);
                _targets[slot] = target.Trim();
            }
        }

        private readonly List<int> _registeredIds = new List<int>();

        private void OnPressed(int id)
        {
            string target;
            if (!_targets.TryGetValue(id, out target))
                return;

            ThreadPool.QueueUserWorkItem(delegate { Launch(target); });
        }

        private void Launch(string target)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(target);
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                Action<string> report = _reportError;
                if (report != null)
                {
                    string message = "Could not start \"" + target + "\": " + ex.Message;
                    if (message.Length > 220)
                        message = message.Substring(0, 217) + "...";
                    report(message);
                }
            }
        }

        public void Dispose()
        {
            foreach (int id in _registeredIds)
                Native.UnregisterHotKey(_window.Handle, id);
            _registeredIds.Clear();
            _targets.Clear();
            _window.Dispose();
        }
    }
}
