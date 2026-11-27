using System;
using System.Windows.Forms;

namespace ClickBind
{
    /// <summary>
    /// Listens for Windows clipboard changes so copies made in any app are seen,
    /// not only the ones started by a ClickBind shortcut.
    /// </summary>
    internal sealed class ClipboardWatcher : NativeWindow, IDisposable
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        public event Action Changed;

        public ClipboardWatcher()
        {
            CreateHandle(new CreateParams());
            if (Handle != IntPtr.Zero)
                Native.AddClipboardFormatListener(Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                Action handler = Changed;
                if (handler != null)
                    handler();
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                Native.RemoveClipboardFormatListener(Handle);
                DestroyHandle();
            }
        }
    }
}
