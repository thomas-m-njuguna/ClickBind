using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ClickBind
{
    /// <summary>
    /// Session-wide low-level mouse hook. Detects double clicks, dual-button chords,
    /// side-button presses and the top-right hot corner, and swallows the clicks that
    /// belong to a gesture so the foreground app does not react to them.
    /// </summary>
    public class MouseHook : IDisposable
    {
        private const int BtnLeft = 0;
        private const int BtnRight = 1;
        private const int BtnMiddle = 2;
        private const int BtnBack = 3;
        private const int BtnForward = 4;

        private Native.LowLevelMouseProc _proc;
        private IntPtr _hook;
        private Thread _thread;
        private uint _threadId;
        private volatile RuntimeConfig _cfg;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private readonly bool[] _phys = new bool[5];
        private readonly bool[] _swallowUp = new bool[5];
        private readonly long[] _lastDown = new long[5];
        private readonly bool[] _pending = new bool[5];
        private readonly Point[] _pendingPt = new Point[5];
        private readonly System.Threading.Timer[] _timers = new System.Threading.Timer[5];
        private readonly System.Threading.Timer[] _holdTimers = new System.Threading.Timer[5];
        private readonly Point[] _holdPt = new Point[5];
        private System.Threading.Timer _holdBothTimer;
        private Point _holdBothPt;
        private const int HoldMs = 450;
        private bool _comboLatched;
        private readonly object _sync = new object();

        private int _right;
        private long _boundsAt;
        private bool _inCorner;
        private long _lastCornerAt;

        /// <summary>Raised on a pool thread when a mouse gesture fires.</summary>
        public event Action<Trigger> Triggered;

        /// <summary>Raised on a pool thread when the pointer enters the top-right corner.</summary>
        public event Action HotCorner;

        public bool HookFailed { get; private set; }

        /// <summary>
        /// Window whose clicks are plain UI interaction, never gestures. Set to the
        /// settings window so trying a gesture in the test pad does not also run it.
        /// </summary>
        public IntPtr IgnoreWindow { get; set; }

        public MouseHook(RuntimeConfig config)
        {
            _cfg = config;
        }

        public void SetConfig(RuntimeConfig config)
        {
            _cfg = config;
        }

        public void Start()
        {
            if (_thread != null)
                return;
            _thread = new Thread(HookThread);
            _thread.IsBackground = true;
            _thread.Name = "ClickBindHook";
            _thread.Start();
        }

        private void HookThread()
        {
            _threadId = Native.GetCurrentThreadId();
            _proc = HookCallback;
            _hook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc, Native.GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                HookFailed = true;
                return;
            }

            SyncPhysicalState();

            Native.MSG msg;
            while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessage(ref msg);
            }

            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    Native.MSLLHOOKSTRUCT data = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    if ((data.flags & Native.LLMHF_INJECTED) == 0)
                    {
                        if (Process(wParam.ToInt32(), data))
                            return (IntPtr)1;
                    }
                }
                catch
                {
                }
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private bool Process(int msg, Native.MSLLHOOKSTRUCT data)
        {
            RuntimeConfig cfg = _cfg;
            if (cfg == null || !cfg.Active)
                return false;

            // Clicks inside ClickBind's own window just drive its interface.
            IntPtr own = IgnoreWindow;
            if (own != IntPtr.Zero && Native.GetForegroundWindow() == own)
                return false;

            if (msg == Native.WM_MOUSEMOVE)
            {
                HandleMove(data.pt.X, data.pt.Y, cfg);
                return false;
            }

            int button;
            bool down;
            switch (msg)
            {
                case Native.WM_LBUTTONDOWN: button = BtnLeft; down = true; break;
                case Native.WM_LBUTTONUP: button = BtnLeft; down = false; break;
                case Native.WM_RBUTTONDOWN: button = BtnRight; down = true; break;
                case Native.WM_RBUTTONUP: button = BtnRight; down = false; break;
                case Native.WM_MBUTTONDOWN: button = BtnMiddle; down = true; break;
                case Native.WM_MBUTTONUP: button = BtnMiddle; down = false; break;
                case Native.WM_XBUTTONDOWN:
                    button = ((data.mouseData >> 16) & 0xFFFF) == 1 ? BtnBack : BtnForward;
                    down = true;
                    break;
                case Native.WM_XBUTTONUP:
                    button = ((data.mouseData >> 16) & 0xFFFF) == 1 ? BtnBack : BtnForward;
                    down = false;
                    break;
                default:
                    return false;
            }

            if (down)
                return OnDown(button, new Point(data.pt.X, data.pt.Y), cfg);
            return OnUp(button);
        }

        private bool OnDown(int b, Point pt, RuntimeConfig cfg)
        {
            lock (_sync)
            {
                _phys[b] = true;
                long now = _clock.ElapsedMilliseconds;

                if (b == BtnLeft || b == BtnRight)
                {
                    ArmHold(b, pt, cfg);
                    if (_phys[BtnLeft] && _phys[BtnRight] && cfg.IsBound(Trigger.HoldBoth))
                        ArmHoldBoth(pt, cfg);
                    else
                        CancelHoldBoth();
                }

                if (_comboLatched && (b == BtnLeft || b == BtnRight))
                {
                    _swallowUp[b] = true;
                    return true;
                }

                if (b == BtnBack || b == BtnForward)
                {
                    Trigger side = b == BtnBack ? Trigger.SideBack : Trigger.SideForward;
                    if (cfg.IsBound(side))
                    {
                        _swallowUp[b] = true;
                        Fire(side);
                        return true;
                    }
                    return false;
                }

                if (b == BtnMiddle)
                {
                    if (cfg.IsBound(Trigger.MiddleClick))
                    {
                        _swallowUp[b] = true;
                        Fire(Trigger.MiddleClick);
                        return true;
                    }
                    return false;
                }

                int other = b == BtnLeft ? BtnRight : BtnLeft;
                if (_phys[other] && !_comboLatched
                    && cfg.IsBound(Trigger.LeftRight)
                    && now - _lastDown[other] >= 0
                    && now - _lastDown[other] <= cfg.ChordMs)
                {
                    _comboLatched = true;
                    CancelHold(b);
                    CancelHold(other);
                    Disarm(b, false);
                    Disarm(other, false);
                    _swallowUp[b] = true;
                    _lastDown[b] = 0;
                    _lastDown[other] = 0;
                    Fire(Trigger.LeftRight);
                    return true;
                }

                Trigger dt = b == BtnLeft ? Trigger.DoubleLeft : Trigger.DoubleRight;
                long prev = _lastDown[b];

                if (cfg.DoubleClickMs > 0 && prev != 0 && now - prev <= cfg.DoubleClickMs)
                {
                    if (cfg.IsBound(dt))
                    {
                        Disarm(b, false);
                        _lastDown[b] = 0;
                        _swallowUp[b] = true;
                        Fire(dt);
                        return true;
                    }

                    Disarm(b, true);
                    _lastDown[b] = now;
                    return false;
                }

                _lastDown[b] = now;

                // A single left click must never be delayed; right clicks wait for a
                // possible second click (or a hold) and are replayed if none arrives.
                bool holdRight = b == BtnRight && cfg.IsBound(Trigger.HoldRight);
                if (b == BtnRight && (cfg.IsBound(dt) || holdRight))
                {
                    int delay = holdRight ? Math.Max(cfg.DoubleClickMs, HoldMs + 20) : cfg.DoubleClickMs;
                    Arm(b, pt, delay);
                    _swallowUp[b] = true;
                    return true;
                }

                return false;
            }
        }

        private bool OnUp(int b)
        {
            lock (_sync)
            {
                _phys[b] = false;
                if (b == BtnLeft || b == BtnRight)
                    _comboLatched = false;

                CancelHold(b);
                if (b == BtnLeft || b == BtnRight)
                    CancelHoldBoth();

                bool block = _swallowUp[b];
                _swallowUp[b] = false;
                return block;
            }
        }

        private void HandleMove(int x, int y, RuntimeConfig cfg)
        {
            if (!cfg.HotCorner)
            {
                _inCorner = false;
                return;
            }

            long now = _clock.ElapsedMilliseconds;
            if (now - _boundsAt > 4000)
            {
                Screen screen = Screen.PrimaryScreen;
                if (screen == null)
                    return;
                _right = screen.Bounds.Right;
                _boundsAt = now;
            }
            if (_right <= 0)
                return;

            bool inside = x >= _right - 3 && x <= _right && y >= 0 && y <= 3;

            if (inside)
            {
                if (!_inCorner && now - _lastCornerAt >= 1200)
                {
                    _inCorner = true;
                    _lastCornerAt = now;
                    Action handler = HotCorner;
                    if (handler != null)
                    {
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            try { handler(); }
                            catch { }
                        });
                    }
                }
            }
            else if (x < _right - 40 || y > 40)
            {
                _inCorner = false;
            }
        }

        private void Arm(int b, Point pt, int ms)
        {
            _pending[b] = true;
            _pendingPt[b] = pt;
            if (ms < 120)
                ms = 120;
            _timers[b] = new System.Threading.Timer(ReplayCallback, b, ms + 15, Timeout.Infinite);
        }

        private void ArmHold(int b, Point pt, RuntimeConfig cfg)
        {
            Trigger hold = b == BtnLeft ? Trigger.HoldLeft : Trigger.HoldRight;
            if (!cfg.IsBound(hold))
                return;

            // While both buttons are down the combined hold wins.
            if (_phys[BtnLeft] && _phys[BtnRight] && cfg.IsBound(Trigger.HoldBoth))
                return;

            _holdPt[b] = pt;
            CancelHold(b);
            _holdTimers[b] = new System.Threading.Timer(HoldCallback, b, HoldMs, Timeout.Infinite);
        }

        private void CancelHold(int b)
        {
            if (_holdTimers[b] != null)
            {
                _holdTimers[b].Dispose();
                _holdTimers[b] = null;
            }
        }

        private void HoldCallback(object state)
        {
            int b = (int)state;
            lock (_sync)
            {
                if (!_phys[b] || _comboLatched)
                    return;

                RuntimeConfig cfg = _cfg;
                Trigger hold = b == BtnLeft ? Trigger.HoldLeft : Trigger.HoldRight;
                if (cfg == null || !cfg.Active || !cfg.IsBound(hold))
                    return;

                int other = b == BtnLeft ? BtnRight : BtnLeft;
                if (_phys[other])
                    return;

                Native.POINT now;
                if (!Native.GetCursorPos(out now))
                    return;
                if (Math.Abs(now.X - _holdPt[b].X) > 8 || Math.Abs(now.Y - _holdPt[b].Y) > 8)
                    return;

                Disarm(b, false);
                _swallowUp[b] = true;
                Fire(hold);
            }
        }

        private void ArmHoldBoth(Point pt, RuntimeConfig cfg)
        {
            CancelHoldBoth();
            _holdBothPt = pt;
            _holdBothTimer = new System.Threading.Timer(HoldBothCallback, null, HoldMs, Timeout.Infinite);
        }

        private void CancelHoldBoth()
        {
            if (_holdBothTimer != null)
            {
                _holdBothTimer.Dispose();
                _holdBothTimer = null;
            }
        }

        private void HoldBothCallback(object state)
        {
            lock (_sync)
            {
                if (!_phys[BtnLeft] || !_phys[BtnRight] || _comboLatched)
                    return;

                RuntimeConfig cfg = _cfg;
                if (cfg == null || !cfg.Active || !cfg.IsBound(Trigger.HoldBoth))
                    return;

                Native.POINT now;
                if (!Native.GetCursorPos(out now))
                    return;
                if (Math.Abs(now.X - _holdBothPt.X) > 8 || Math.Abs(now.Y - _holdBothPt.Y) > 8)
                    return;

                CancelHold(BtnLeft);
                CancelHold(BtnRight);
                Disarm(BtnLeft, false);
                Disarm(BtnRight, false);
                _lastDown[BtnLeft] = 0;
                _lastDown[BtnRight] = 0;
                _swallowUp[BtnLeft] = true;
                _swallowUp[BtnRight] = true;
                Fire(Trigger.HoldBoth);
            }
        }

        private void Disarm(int b, bool replay)
        {
            bool wasPending = _pending[b];
            if (wasPending)
            {
                _pending[b] = false;
                if (_timers[b] != null)
                {
                    _timers[b].Dispose();
                    _timers[b] = null;
                }
            }
            if (replay && wasPending)
                Replay(b);
        }

        private void ReplayCallback(object state)
        {
            int b = (int)state;
            lock (_sync)
            {
                if (!_pending[b])
                    return;
                _pending[b] = false;
                if (_timers[b] != null)
                {
                    _timers[b].Dispose();
                    _timers[b] = null;
                }
                Replay(b);
            }
        }

        private void Replay(int b)
        {
            Native.SetCursorPos(_pendingPt[b].X, _pendingPt[b].Y);
            Native.ClickDeferredButton(b);
        }

        private void Fire(Trigger t)
        {
            Action<Trigger> handler = Triggered;
            if (handler != null)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { handler(t); }
                    catch { }
                });
            }
        }

        private void SyncPhysicalState()
        {
            _phys[BtnLeft] = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;
            _phys[BtnRight] = (Native.GetAsyncKeyState(0x02) & 0x8000) != 0;
            _phys[BtnMiddle] = (Native.GetAsyncKeyState(0x04) & 0x8000) != 0;
            _phys[BtnBack] = (Native.GetAsyncKeyState(0x05) & 0x8000) != 0;
            _phys[BtnForward] = (Native.GetAsyncKeyState(0x06) & 0x8000) != 0;
        }

        public void Dispose()
        {
            try
            {
                if (_threadId != 0)
                    Native.PostThreadMessage(_threadId, (uint)Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
                if (_thread != null)
                    _thread.Join(700);
            }
            catch
            {
            }
            lock (_sync)
            {
                for (int i = 0; i < 5; i++)
                {
                    if (_timers[i] != null)
                    {
                        _timers[i].Dispose();
                        _timers[i] = null;
                    }
                    if (_holdTimers[i] != null)
                    {
                        _holdTimers[i].Dispose();
                        _holdTimers[i] = null;
                    }
                    if (i == 0)
                        CancelHoldBoth();
                    _pending[i] = false;
                }
            }
            _thread = null;
            _threadId = 0;
        }
    }
}
