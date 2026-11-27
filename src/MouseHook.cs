using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// One mouse button or wheel notch seen by the low-level mouse hook. Wheel
    /// notches are reported as a press only (there is no key-up for a notch);
    /// <see cref="Delta"/> carries the raw wheel delta so the caller can decide how
    /// many notches one message actually means.
    /// </summary>
    internal sealed class MouseButtonEventArgs : EventArgs
    {
        public Keys Key { get; private set; }
        public bool IsDown { get; private set; }
        public bool IsUp { get; private set; }
        public bool IsWheel { get; private set; }
        public bool IsInjected { get; private set; }
        public int Delta { get; private set; }

        public MouseButtonEventArgs(Keys key, bool isDown, bool isUp, bool isWheel, bool injected, int delta)
        {
            Key = key;
            IsDown = isDown;
            IsUp = isUp;
            IsWheel = isWheel;
            IsInjected = injected;
            Delta = delta;
        }
    }

    /// <summary>
    /// Low-level mouse hook. Only the three buttons and the vertical wheel are
    /// translated; everything else (movement, horizontal wheel, X buttons) is
    /// passed through untouched so the Caps layer never breaks normal pointing.
    /// </summary>
    internal sealed class MouseHook : IDisposable
    {
        private Native.LowLevelMouseProc _proc;
        private IntPtr _hook = IntPtr.Zero;

        public event Func<MouseButtonEventArgs, bool> MouseEvent;
        public int LastError { get; private set; }

        public bool IsInstalled
        {
            get { return _hook != IntPtr.Zero; }
        }

        public bool Install()
        {
            if (_hook != IntPtr.Zero) return true;
            _proc = HookCallback;
            IntPtr module = Native.GetModuleHandle(null);
            _hook = Native.SetWindowsHookExMouse(Native.WH_MOUSE_LL, _proc, module, 0);
            if (_hook == IntPtr.Zero)
            {
                LastError = Marshal.GetLastWin32Error();
                ConfigStore.Log("安装鼠标钩子失败，Win32 错误码：" + LastError);
                return false;
            }
            return true;
        }

        public void Uninstall()
        {
            if (_hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && MouseEvent != null)
                {
                    Native.MSLLHOOKSTRUCT data = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(
                        lParam, typeof(Native.MSLLHOOKSTRUCT));
                    // Clicks this app synthesized for the "鼠标点击" action must never
                    // come back as triggers, even when injected events are enabled.
                    if (data.dwExtraInfo == Native.SyntheticMarker)
                        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

                    // A pointer action pins the cursor so a moving hand cannot drag it
                    // away mid-click. Moves on the pinned point (this app's own
                    // SetCursorPos) are let through; everything else is swallowed.
                    if (wParam.ToInt32() == Native.WM_MOUSEMOVE)
                    {
                        if (!PointerPin.ShouldPass(data.pt.x, data.pt.y)) return new IntPtr(1);
                        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
                    }

                    Keys key;
                    bool down, up, wheel;
                    int delta;
                    if (TryTranslate(wParam.ToInt32(), data.mouseData, out key, out down, out up,
                            out wheel, out delta))
                    {
                        bool injected = (data.flags & Native.LLMHF_INJECTED) != 0;
                        if (MouseEvent(new MouseButtonEventArgs(key, down, up, wheel, injected, delta)))
                            return new IntPtr(1);
                    }
                }
            }
            catch (Exception ex)
            {
                ConfigStore.Log("鼠标钩子回调异常：" + ex);
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private static bool TryTranslate(int message, uint mouseData, out Keys key, out bool down,
            out bool up, out bool wheel, out int delta)
        {
            key = Keys.None;
            down = up = wheel = false;
            delta = 0;
            switch (message)
            {
                case Native.WM_LBUTTONDOWN:
                    key = (Keys)MouseKeys.Left;
                    down = true;
                    return true;
                case Native.WM_LBUTTONUP:
                    key = (Keys)MouseKeys.Left;
                    up = true;
                    return true;
                case Native.WM_RBUTTONDOWN:
                    key = (Keys)MouseKeys.Right;
                    down = true;
                    return true;
                case Native.WM_RBUTTONUP:
                    key = (Keys)MouseKeys.Right;
                    up = true;
                    return true;
                case Native.WM_MBUTTONDOWN:
                    key = (Keys)MouseKeys.Middle;
                    down = true;
                    return true;
                case Native.WM_MBUTTONUP:
                    key = (Keys)MouseKeys.Middle;
                    up = true;
                    return true;
                case Native.WM_MOUSEWHEEL:
                    // mouseData's high word is a signed delta; positive means away
                    // from the user ("up").
                    delta = (short)((mouseData >> 16) & 0xFFFF);
                    if (delta == 0) return false;
                    key = delta > 0 ? (Keys)MouseKeys.WheelUp : (Keys)MouseKeys.WheelDown;
                    down = true;
                    wheel = true;
                    return true;
            }
            return false;
        }

        public void Dispose()
        {
            Uninstall();
            _proc = null;
        }
    }
}
