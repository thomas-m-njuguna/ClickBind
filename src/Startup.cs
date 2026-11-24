using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClickBind
{
    internal static class Startup
    {
        private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string ValueName = "ClickBind";

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null)
                        return;
                    if (enabled)
                        key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\" --tray");
                    else
                        key.DeleteValue(ValueName, false);
                }
            }
            catch
            {
            }
        }

        public static void Refresh()
        {
            SetEnabled(true);
        }
    }
}
