using System;
using System.Threading;
using System.Windows.Forms;

namespace ClickBind
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "ClickBind_SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("ClickBind is already running. Look for its icon in the system tray.",
                        "ClickBind", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
                catch
                {
                    try { Native.SetProcessDPIAware(); }
                    catch { }
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool showWindow = false;
                if (args != null)
                {
                    foreach (string arg in args)
                    {
                        if (string.Equals(arg, "--show", StringComparison.OrdinalIgnoreCase))
                            showWindow = true;
                    }
                }

                try
                {
                    Application.Run(new TrayApp(!showWindow));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message + "\n\n" + ex.StackTrace, "ClickBind error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
