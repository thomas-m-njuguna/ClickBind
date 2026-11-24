using System;

namespace ClickBind
{
    public static class Actions
    {
        public static void SendShortcut(ActionId action)
        {
            switch (action)
            {
                case ActionId.Copy: Native.SendCtrlKey(0x43); break;
                case ActionId.Paste: Native.SendCtrlKey(0x56); break;
                case ActionId.Cut: Native.SendCtrlKey(0x58); break;
                case ActionId.Undo: Native.SendCtrlKey(0x5A); break;
                case ActionId.Redo: Native.SendCtrlKey(0x59); break;
                case ActionId.SelectAll: Native.SendCtrlKey(0x41); break;
                case ActionId.Snapshot: Native.SendWinShiftKey(0x53); break;
                case ActionId.TaskView: Native.SendWinKey(0x09); break;
            }
        }
    }
}
