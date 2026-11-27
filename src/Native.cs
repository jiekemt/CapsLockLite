using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CapsLockLite
{
    internal static class Native
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;

        public const int WM_MOUSEMOVE = 0x0200;
        public const int WM_LBUTTONDOWN = 0x0201;
        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONDOWN = 0x0204;
        public const int WM_RBUTTONUP = 0x0205;
        public const int WM_MBUTTONDOWN = 0x0207;
        public const int WM_MBUTTONUP = 0x0208;
        public const int WM_MOUSEWHEEL = 0x020A;

        public const uint LLMHF_INJECTED = 0x01;

        public const int WM_KEYDOWN = 0x0100;
        public const int WM_KEYUP = 0x0101;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_SYSKEYUP = 0x0105;
        public const int WM_HOTKEY = 0x0312;
        public const int WM_CLOSE = 0x0010;
        public const int WM_DPICHANGED = 0x02E0;

        public const uint LLKHF_EXTENDED = 0x01;
        public const uint LLKHF_INJECTED = 0x10;
        public const uint LLKHF_UP = 0x80;

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        public const uint KEYEVENTF_KEYUP = 0x0002;
        public const uint KEYEVENTF_UNICODE = 0x0004;

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int LWA_ALPHA = 0x00000002;

        public const int SW_HIDE = 0;
        public const int SW_SHOW = 5;
        public const int SW_RESTORE = 9;
        public const int SW_MAXIMIZE = 3;
        public const int SW_MINIMIZE = 6;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_NOACTIVATE = 0x0010;
        public static readonly IntPtr HWND_TOP = IntPtr.Zero;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const int VK_CAPITAL = 0x14;
        public const int VK_LCONTROL = 0xA2;
        public const int VK_LMENU = 0xA4;
        public const int VK_LSHIFT = 0xA0;
        public const int VK_LWIN = 0x5B;

        public const int EM_SETCUEBANNER = 0x1501;
        public const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        public const int EM_GETLINECOUNT = 0x00BA;
        public const int EM_LINESCROLL = 0x00B6;

        public static int EditFirstVisibleLine(TextBox box)
        {
            if (box == null || !box.IsHandleCreated) return 0;
            return (int)SendMessage(box.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
        }

        public static int EditLineCount(TextBox box)
        {
            if (box == null || !box.IsHandleCreated) return 0;
            return (int)SendMessage(box.Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero);
        }

        public static void EditScrollLines(TextBox box, int lines)
        {
            if (box == null || !box.IsHandleCreated || lines == 0) return;
            SendMessage(box.Handle, EM_LINESCROLL, IntPtr.Zero, new IntPtr(lines));
        }

        public static readonly IntPtr SyntheticMarker = new IntPtr(0x434C4C54);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", EntryPoint = "SetWindowsHookEx", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookExMouse(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, int dx, int dy, uint dwData, UIntPtr dwExtraInfo);

        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        public const uint MOUSEEVENTF_WHEEL = 0x0800;

        [DllImport("user32.dll")]
        public static extern bool ShowScrollBar(IntPtr hWnd, int wBar, bool bShow);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint crKey, out byte bAlpha, out uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern uint RegisterWindowMessage(string lpString);

        /// <summary>
        /// Posted by a second instance to the running one, which then shows its
        /// own window. Poking ShowWindow from the outside would leave
        /// Form.Visible false while the window is on screen, and the next close
        /// then cannot hide it any more.
        /// </summary>
        public static readonly int WM_ACTIVATE_INSTANCE = unchecked((int)RegisterWindowMessage("CapsLockLite.ActivateInstance"));

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();

        /// <summary>
        /// Polls the clipboard sequence number until the copy the caller just
        /// requested has landed, or the timeout expires. Replaces the fixed sleep
        /// that made slow applications hand back the previous clipboard content.
        /// </summary>
        public static bool WaitForClipboardChange(uint before, int timeoutMs)
        {
            if (timeoutMs < 0) timeoutMs = 0;
            int waited = 0;
            while (true)
            {
                if (GetClipboardSequenceNumber() != before) return true;
                if (waited >= timeoutMs) return false;
                Thread.Sleep(15);
                waited += 15;
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        /// <summary>
        /// A sleep that really lasts <paramref name="milliseconds"/>. A plain
        /// Thread.Sleep(1) waits for the next 15.6 ms tick, which is the difference
        /// between a macro step costing ~3 ms and 15 ms.
        /// </summary>
        public static void SleepPrecise(int milliseconds)
        {
            if (milliseconds <= 0) return;
            TimeBeginPeriod(1);
            try
            {
                Thread.Sleep(milliseconds);
            }
            finally
            {
                TimeEndPeriod(1);
            }
        }

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        public static extern uint TimeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        public static extern uint TimeEndPeriod(uint milliseconds);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int value = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int));
            }
            catch { }
        }

        /// <summary>Windows 11 rounded window corners (DWMWA_WINDOW_CORNER_PREFERENCE).</summary>
        public static void SetRoundedCorners(IntPtr hwnd, bool round)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int value = round ? 2 : 1;
                DwmSetWindowAttribute(hwnd, 33, ref value, sizeof(int));
            }
            catch { }
        }

        public static void SetCueBanner(TextBox box, string text)
        {
            if (box == null || !box.IsHandleCreated) return;
            SendMessage(box.Handle, EM_SETCUEBANNER, new IntPtr(1), text);
        }

        /// <summary>
        /// True when the key is sent with the E0 prefix (KEYEVENTF_EXTENDEDKEY).
        /// The driver backend sends scan codes only, so a missing E0 makes a key
        /// arrive as a different key or not at all -- the Windows key, for one,
        /// silently stopped Win+Space from working.
        /// </summary>
        internal static bool IsExtendedKey(Keys key)
        {
            key = key & Keys.KeyCode;
            switch (key)
            {
                case Keys.LWin:
                case Keys.RWin:
                case Keys.Apps:
                case Keys.Insert:
                case Keys.Delete:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Up:
                case Keys.Down:
                case Keys.Left:
                case Keys.Right:
                case Keys.NumLock:
                case Keys.PrintScreen:
                case Keys.Divide:
                case Keys.RControlKey:
                case Keys.RMenu:
                case Keys.MediaNextTrack:
                case Keys.MediaPreviousTrack:
                case Keys.MediaPlayPause:
                case Keys.VolumeUp:
                case Keys.VolumeDown:
                case Keys.VolumeMute:
                case Keys.BrowserBack:
                case Keys.BrowserForward:
                case Keys.BrowserRefresh:
                case Keys.BrowserStop:
                case Keys.BrowserSearch:
                case Keys.BrowserFavorites:
                case Keys.BrowserHome:
                case Keys.LaunchMail:
                case Keys.SelectMedia:
                case Keys.LaunchApplication1:
                case Keys.LaunchApplication2:
                    return true;
            }
            return false;
        }

        private static INPUT KeyboardInput(ushort vk, ushort scan, uint flags)
        {
            INPUT input = new INPUT();
            input.type = INPUT_KEYBOARD;
            input.U.ki = new KEYBDINPUT();
            input.U.ki.wVk = vk;
            input.U.ki.wScan = scan;
            input.U.ki.dwFlags = flags;
            input.U.ki.time = 0;
            input.U.ki.dwExtraInfo = SyntheticMarker;
            return input;
        }

        private static INPUT KeyInput(Keys key, bool up)
        {
            ushort vk = (ushort)(key & Keys.KeyCode);
            ushort scan = (ushort)MapVirtualKey(vk, 0);
            uint flags = up ? KEYEVENTF_KEYUP : 0;
            if (IsExtendedKey(key)) flags |= KEYEVENTF_EXTENDEDKEY;
            return KeyboardInput(vk, scan, flags);
        }

        /// <summary>
        /// Collects synthetic key events and submits them in one SendInput call.
        /// One call means the target application consumes the whole chord in
        /// order; the previous per-step calls had to be separated by sleeps to
        /// stay ordered, which raced with slow applications.
        /// </summary>
        internal sealed class InputBatch
        {
            private readonly List<INPUT> _inputs = new List<INPUT>();

            public int Count { get { return _inputs.Count; } }

            public void Down(Keys key)
            {
                if (key == Keys.None) return;
                _inputs.Add(KeyInput(key, false));
            }

            public void Up(Keys key)
            {
                if (key == Keys.None) return;
                _inputs.Add(KeyInput(key, true));
            }

            public void Tap(Keys key)
            {
                Down(key);
                Up(key);
            }

            public bool Send()
            {
                if (_inputs.Count == 0) return true;
                INPUT[] inputs = _inputs.ToArray();
                uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
                return sent == inputs.Length;
            }
        }

        public static bool SendVirtualKey(Keys key)
        {
            INPUT[] inputs = new INPUT[2];
            inputs[0] = KeyInput(key, false);
            inputs[1] = KeyInput(key, true);
            return SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT))) == 2;
        }

        public static void SendUnicodeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            List<INPUT> batch = new List<INPUT>();
            foreach (char c in text)
            {
                if (c == '\r') continue;
                if (c == '\n')
                {
                    batch.Add(KeyInput(Keys.Return, false));
                    batch.Add(KeyInput(Keys.Return, true));
                }
                else if (c == '\t')
                {
                    batch.Add(KeyInput(Keys.Tab, false));
                    batch.Add(KeyInput(Keys.Tab, true));
                }
                else
                {
                    batch.Add(KeyboardInput(0, (ushort)c, KEYEVENTF_UNICODE));
                    batch.Add(KeyboardInput(0, (ushort)c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
                }

                if (batch.Count >= 64)
                {
                    INPUT[] chunk = batch.ToArray();
                    SendInput((uint)chunk.Length, chunk, Marshal.SizeOf(typeof(INPUT)));
                    batch.Clear();
                }
            }
            if (batch.Count > 0)
            {
                INPUT[] chunk = batch.ToArray();
                SendInput((uint)chunk.Length, chunk, Marshal.SizeOf(typeof(INPUT)));
            }
        }

        public static void ToggleCapsLock()
        {
            SendVirtualKey(Keys.CapsLock);
        }
    }

    internal static class WindowTools
    {
        private static readonly HashSet<IntPtr> _transparent = new HashSet<IntPtr>();

        public static IntPtr GetActiveWindow()
        {
            return Native.GetForegroundWindow();
        }

        public static void ToggleTopmost(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            bool isTop = (ex & Native.WS_EX_TOPMOST) != 0;
            Native.SetWindowPos(hwnd, isTop ? Native.HWND_NOTOPMOST : Native.HWND_TOPMOST,
                0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public static bool ToggleTransparency(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_LAYERED) == 0)
            {
                Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_LAYERED);
                ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
                if ((ex & Native.WS_EX_LAYERED) == 0) return false;
            }

            bool currently = _transparent.Contains(hwnd);
            byte alpha = currently ? (byte)255 : (byte)210;
            if (!Native.SetLayeredWindowAttributes(hwnd, 0, alpha, Native.LWA_ALPHA))
                return false;
            if (currently) _transparent.Remove(hwnd);
            else _transparent.Add(hwnd);
            return true;
        }

        public static void ToggleMaximize(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            if (Native.IsZoomed(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
            else Native.ShowWindow(hwnd, Native.SW_MAXIMIZE);
        }

        public static void Minimize(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero) Native.ShowWindow(hwnd, Native.SW_MINIMIZE);
        }

        public static void Close(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero) Native.PostMessage(hwnd, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        public static void Snap(IntPtr hwnd, bool left)
        {
            if (hwnd == IntPtr.Zero) return;
            Rectangle area = Screen.FromHandle(hwnd).WorkingArea;
            int half = area.Width / 2;
            int x = left ? area.Left : area.Left + half;
            int width = left ? half : area.Width - half;
            Native.SetWindowPos(hwnd, Native.HWND_TOP, x, area.Top, width, area.Height,
                Native.SWP_NOACTIVATE);
        }

        public static string GetWindowTitle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return "";
            int len = Native.GetWindowTextLength(hwnd);
            StringBuilder sb = new StringBuilder(len + 2);
            Native.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}
