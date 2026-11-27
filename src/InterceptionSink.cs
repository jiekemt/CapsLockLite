using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// Optional injection backend that goes through the Interception keyboard/mouse
    /// filter driver.
    ///
    /// Why it exists: SendInput events are injected above the raw input path, so a
    /// program that reads raw devices (most first-person games, DirectInput
    /// titles) never sees them. A driver stroke is delivered below that path and is
    /// indistinguishable from hardware, which is what makes Caps shortcuts work
    /// there. Capture is NOT done here on purpose: the low-level hooks keep the
    /// Caps layer exactly as it is, the driver only sends.
    ///
    /// Everything is optional. Without the driver and a 64-bit interception.dll
    /// next to the executable this sink never activates and InputPipeline falls
    /// back to SendInput.
    /// </summary>
    internal sealed class InterceptionSink : IInputSink
    {
        // Interception numbers devices 1..10 (keyboards) and 11..20 (mice).
        private const int KeyboardDevice = 1;
        private const int MouseDevice = 11;

        private const ushort KeyStateDown = 0x00;
        private const ushort KeyStateUp = 0x01;
        private const ushort KeyStateE0 = 0x02;

        private const ushort MouseLeftDown = 0x001;
        private const ushort MouseLeftUp = 0x002;
        private const ushort MouseRightDown = 0x004;
        private const ushort MouseRightUp = 0x008;
        private const ushort MouseMiddleDown = 0x010;
        private const ushort MouseMiddleUp = 0x020;
        private const ushort MouseWheel = 0x400;

        private static readonly object _gate = new object();
        private static InterceptionSink _instance;
        private static bool _probed;
        private static string _probeError = "";

        private IntPtr _context = IntPtr.Zero;

        public string Name
        {
            get { return "Interception"; }
        }

        /// <summary>
        /// The driver injects below dwExtraInfo, so the hooks cannot see the marker;
        /// InputPipeline books every stroke in SyntheticLedger instead.
        /// </summary>
        public bool MarksOwnEvents
        {
            get { return false; }
        }

        /// <summary>
        /// Returns the shared driver sink, or null with a reason. The probe result is
        /// cached; passing forceProbe re-checks so a driver installed while the app
        /// runs is picked up without a restart.
        /// </summary>
        public static IInputSink Acquire(bool forceProbe, out string error)
        {
            lock (_gate)
            {
                if (_instance != null)
                {
                    error = "";
                    return _instance;
                }
                if (_probed && !forceProbe)
                {
                    error = _probeError;
                    return null;
                }
                _probed = true;
                if (!TryOpen(out _probeError))
                {
                    error = _probeError;
                    return null;
                }
                error = "";
                return _instance;
            }
        }

        private static bool TryOpen(out string error)
        {
            InterceptionSink sink = new InterceptionSink();
            try
            {
                sink._context = InterceptionNative.CreateContext();
            }
            catch (DllNotFoundException)
            {
                error = "未找到 interception.dll（需要 64 位版本，放在 CapsLockLite.exe 同目录）";
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                error = "interception.dll 版本不匹配";
                return false;
            }
            catch (BadImageFormatException)
            {
                error = "interception.dll 位数与程序不符（需要 64 位）";
                return false;
            }
            if (sink._context == IntPtr.Zero)
            {
                error = "Interception 驱动未安装或无权访问";
                return false;
            }
            _instance = sink;
            error = "";
            return true;
        }

        public bool SendStrokes(IList<InputStroke> strokes)
        {
            if (strokes == null || strokes.Count == 0) return true;
            if (_context == IntPtr.Zero) return false;

            InterceptionNative.KeyStroke[] buffer = new InterceptionNative.KeyStroke[strokes.Count];
            for (int i = 0; i < strokes.Count; i++)
            {
                ushort code;
                ushort state;
                if (!ToKeyStroke(strokes[i].Key, strokes[i].Down, out code, out state)) return false;
                buffer[i].Code = code;
                buffer[i].State = state;
            }
            try
            {
                return InterceptionNative.SendKeys(_context, KeyboardDevice, buffer, (uint)buffer.Length)
                    == buffer.Length;
            }
            catch (Exception ex)
            {
                ConfigStore.Log("Interception 发送按键失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// The driver speaks scan codes, not virtual keys. MapVirtualKey translates
        /// through the current layout and returns 0 for keys that have no scan code,
        /// which is the one thing this backend cannot send.
        /// </summary>
        internal static bool ToKeyStroke(Keys key, bool down, out ushort code, out ushort state)
        {
            code = 0;
            state = down ? KeyStateDown : KeyStateUp;
            Keys baseKey = key & Keys.KeyCode;
            if (baseKey == Keys.None) return false;
            if (MouseKeys.IsMouseKey(baseKey)) return false;
            // Pause is the one E1 key on a standard keyboard; this stroke model only
            // carries E0, and its scan code (0x45) would arrive as NumLock. Refusing
            // it makes the pipeline fall back to SendInput, which sends it properly.
            if (baseKey == Keys.Pause) return false;
            uint scan = Native.MapVirtualKey((uint)baseKey, 0);
            if (scan == 0) return false;
            code = (ushort)scan;
            if (Native.IsExtendedKey(baseKey)) state |= KeyStateE0;
            return true;
        }

        public bool SendText(string text)
        {
            // The driver has no Unicode channel; InputPipeline sends text through
            // SendInput, which types the same characters.
            return false;
        }

        public bool SendMouseButton(Keys key, bool up)
        {
            if (_context == IntPtr.Zero) return false;
            ushort state;
            switch (key & Keys.KeyCode)
            {
                case (Keys)MouseKeys.Left:
                    state = up ? MouseLeftUp : MouseLeftDown;
                    break;
                case (Keys)MouseKeys.Right:
                    state = up ? MouseRightUp : MouseRightDown;
                    break;
                case (Keys)MouseKeys.Middle:
                    state = up ? MouseMiddleUp : MouseMiddleDown;
                    break;
                default:
                    return false;
            }
            return SendMouseStroke(state, 0);
        }

        public bool SendWheel(int delta)
        {
            if (delta == 0) return true;
            if (_context == IntPtr.Zero) return false;
            return SendMouseStroke(MouseWheel, (short)delta);
        }

        private bool SendMouseStroke(ushort state, short rolling)
        {
            InterceptionNative.MouseStroke stroke = new InterceptionNative.MouseStroke();
            stroke.State = state;
            stroke.Rolling = rolling;
            InterceptionNative.MouseStroke[] one = new InterceptionNative.MouseStroke[1];
            one[0] = stroke;
            try
            {
                return InterceptionNative.SendMouse(_context, MouseDevice, one, 1) == 1;
            }
            catch (Exception ex)
            {
                ConfigStore.Log("Interception 发送鼠标事件失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>Thin P/Invoke surface; the DLL is resolved on first use.</summary>
        private static class InterceptionNative
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct KeyStroke
            {
                public ushort Code;
                public ushort State;
                // The native struct carries a third field (information); without it
                // every stroke after the first would be read at the wrong stride.
                public uint Information;
            }

            [StructLayout(LayoutKind.Sequential)]
            internal struct MouseStroke
            {
                public ushort State;
                public ushort Flags;
                public short Rolling;
                public int X;
                public int Y;
                public uint Information;
            }

            [DllImport("interception.dll", EntryPoint = "interception_create_context",
                CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr CreateContext();

            [DllImport("interception.dll", EntryPoint = "interception_destroy_context",
                CallingConvention = CallingConvention.Cdecl)]
            internal static extern void DestroyContext(IntPtr context);

            [DllImport("interception.dll", EntryPoint = "interception_send",
                CallingConvention = CallingConvention.Cdecl)]
            internal static extern int SendKeys(IntPtr context, int device, KeyStroke[] strokes, uint count);

            [DllImport("interception.dll", EntryPoint = "interception_send",
                CallingConvention = CallingConvention.Cdecl)]
            internal static extern int SendMouse(IntPtr context, int device, MouseStroke[] strokes, uint count);
        }
    }
}
