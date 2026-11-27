using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>One key transition handed to the active input sink.</summary>
    internal struct InputStroke
    {
        public Keys Key;
        public bool Down;

        public InputStroke(Keys key, bool down)
        {
            Key = key;
            Down = down;
        }
    }

    /// <summary>
    /// Where synthetic input is delivered. The capture side (the low-level hooks)
    /// is deliberately NOT part of this interface: a backend only sends, so a
    /// driver can be plugged in without touching the Caps layer state machine.
    /// </summary>
    internal interface IInputSink
    {
        string Name { get; }

        /// <summary>
        /// True when every event this sink sends carries Native.SyntheticMarker, so
        /// the hooks recognize it by value. A driver injects below that marker and
        /// reports false; InputPipeline then books the strokes in SyntheticLedger.
        /// </summary>
        bool MarksOwnEvents { get; }

        bool SendStrokes(IList<InputStroke> strokes);
        bool SendText(string text);
        bool SendMouseButton(Keys key, bool up);
        bool SendWheel(int delta);
    }

    /// <summary>
    /// The default sink: user-mode SendInput / mouse_event, every event tagged with
    /// Native.SyntheticMarker. Works everywhere except programs that read raw
    /// device input (Raw Input / DirectInput), which never see injected events.
    /// </summary>
    internal sealed class SendInputSink : IInputSink
    {
        public string Name
        {
            get { return "SendInput"; }
        }

        public bool MarksOwnEvents
        {
            get { return true; }
        }

        public bool SendStrokes(IList<InputStroke> strokes)
        {
            if (strokes == null || strokes.Count == 0) return true;
            // One SendInput call for the whole sequence: the target consumes the
            // chord in order, with no sleep between steps for a slow app to reorder.
            Native.InputBatch batch = new Native.InputBatch();
            for (int i = 0; i < strokes.Count; i++)
            {
                if (strokes[i].Down) batch.Down(strokes[i].Key);
                else batch.Up(strokes[i].Key);
            }
            return batch.Send();
        }

        public bool SendText(string text)
        {
            Native.SendUnicodeText(text);
            return true;
        }

        public bool SendMouseButton(Keys key, bool up)
        {
            uint flag = MouseFlag(key, up);
            if (flag == 0) return false;
            Native.mouse_event(flag, 0, 0, 0, Marker());
            return true;
        }

        /// <summary>
        /// mouse_event flag for one mouse button, or 0 for anything else (a wheel
        /// notch carries a delta instead, see ActionExecutor.MouseWheelDelta).
        /// </summary>
        internal static uint MouseFlag(Keys key, bool up)
        {
            switch (key & Keys.KeyCode)
            {
                case (Keys)MouseKeys.Left:
                    return up ? Native.MOUSEEVENTF_LEFTUP : Native.MOUSEEVENTF_LEFTDOWN;
                case (Keys)MouseKeys.Right:
                    return up ? Native.MOUSEEVENTF_RIGHTUP : Native.MOUSEEVENTF_RIGHTDOWN;
                case (Keys)MouseKeys.Middle:
                    return up ? Native.MOUSEEVENTF_MIDDLEUP : Native.MOUSEEVENTF_MIDDLEDOWN;
            }
            return 0;
        }

        public bool SendWheel(int delta)
        {
            if (delta == 0) return true;
            Native.mouse_event(Native.MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta), Marker());
            return true;
        }

        /// <summary>Marks the click as ours so the mouse hook never reads it back.</summary>
        private static UIntPtr Marker()
        {
            return (UIntPtr)(ulong)Native.SyntheticMarker.ToInt64();
        }
    }

    /// <summary>
    /// Routes every synthetic stroke to the configured backend and falls back to
    /// SendInput when a driver backend refuses one, so a missing or half-installed
    /// driver can never swallow a shortcut.
    /// </summary>
    internal static class InputPipeline
    {
        public const string BackendAuto = "auto";
        public const string BackendSystem = "system";
        public const string BackendDriver = "driver";

        private static readonly SendInputSink _system = new SendInputSink();
        private static IInputSink _backend = _system;
        private static string _lastError = "";

        /// <summary>Name shown in the settings page and in the status line.</summary>
        public static string BackendName
        {
            get { return _backend.Name; }
        }

        public static bool UsingDriver
        {
            get { return !ReferenceEquals(_backend, _system); }
        }

        /// <summary>
        /// True when strokes are booked in SyntheticLedger before they are sent, so
        /// WaitDelivered can tell when the target has actually received them.
        /// </summary>
        public static bool TracksDelivery
        {
            get { return !_backend.MarksOwnEvents; }
        }

        /// <summary>
        /// Empty when the active backend is the requested one; otherwise why the
        /// request fell back to SendInput.
        /// </summary>
        public static string LastError
        {
            get { return _lastError; }
        }

        /// <summary>
        /// Picks the backend for these settings. Safe to call repeatedly: the choice
        /// only changes when the configuration or the driver availability does.
        /// </summary>
        public static void Apply(AppSettings settings)
        {
            string mode = settings == null || string.IsNullOrEmpty(settings.InputBackend)
                ? BackendAuto
                : settings.InputBackend;
            if (mode == BackendSystem)
            {
                SwitchTo(_system, "");
                return;
            }

            // "auto" probes once and remembers; an explicit "driver" request probes
            // again, so installing the driver while the app runs needs no restart.
            bool retry = mode == BackendDriver;
            string error;
            IInputSink driver = InterceptionSink.Acquire(retry, out error);
            if (driver != null)
            {
                SwitchTo(driver, "");
                return;
            }
            // Without the driver "auto" is the normal case, not a failure.
            SwitchTo(_system, mode == BackendDriver ? error : "");
        }

        private static void SwitchTo(IInputSink sink, string error)
        {
            _lastError = error == null ? "" : error;
            if (ReferenceEquals(_backend, sink)) return;
            _backend = sink;
            // Claims belong to the previous backend; a held key there has already
            // been released by whoever switched.
            SyntheticLedger.Clear();
            ConfigStore.Log("输入注入方式：" + sink.Name
                + (_lastError.Length == 0 ? "" : "（" + _lastError + "）"));
        }

        public static bool SendStrokes(IList<InputStroke> strokes)
        {
            if (strokes == null || strokes.Count == 0) return true;
            IInputSink sink = _backend;
            if (!sink.MarksOwnEvents)
            {
                for (int i = 0; i < strokes.Count; i++)
                    SyntheticLedger.Record(strokes[i].Key, strokes[i].Down);
            }
            if (sink.SendStrokes(strokes)) return true;
            if (ReferenceEquals(sink, _system)) return false;

            // The driver refused the batch (a key it cannot express, or the driver
            // went away). Withdraw the claims it will never see and retry through
            // the system sink so the shortcut still fires.
            SyntheticLedger.Forget(strokes);
            ConfigStore.Log("驱动注入失败，本次回退到 SendInput。");
            return _system.SendStrokes(strokes);
        }

        public static bool SendKeyState(Keys key, bool up)
        {
            InputStroke[] one = new InputStroke[1];
            one[0] = new InputStroke(key, !up);
            return SendStrokes(one);
        }

        public static bool Tap(Keys key)
        {
            InputStroke[] pair = new InputStroke[2];
            pair[0] = new InputStroke(key, true);
            pair[1] = new InputStroke(key, false);
            return SendStrokes(pair);
        }

        public static bool SendCombo(Keys key, bool ctrl, bool alt, bool shift, bool win, int repeat)
        {
            if (key == Keys.None) return true;
            if (repeat < 1) repeat = 1;
            if (repeat > 100) repeat = 100;

            List<InputStroke> strokes = new List<InputStroke>();
            if (ctrl) strokes.Add(new InputStroke(Keys.LControlKey, true));
            if (alt) strokes.Add(new InputStroke(Keys.LMenu, true));
            if (shift) strokes.Add(new InputStroke(Keys.LShiftKey, true));
            if (win) strokes.Add(new InputStroke(Keys.LWin, true));
            for (int i = 0; i < repeat; i++)
            {
                strokes.Add(new InputStroke(key, true));
                strokes.Add(new InputStroke(key, false));
            }
            // Modifiers come up in reverse order, like fingers leaving a chord.
            if (win) strokes.Add(new InputStroke(Keys.LWin, false));
            if (shift) strokes.Add(new InputStroke(Keys.LShiftKey, false));
            if (alt) strokes.Add(new InputStroke(Keys.LMenu, false));
            if (ctrl) strokes.Add(new InputStroke(Keys.LControlKey, false));
            return SendStrokes(strokes);
        }

        public static bool SendText(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            IInputSink sink = _backend;
            if (sink.SendText(text)) return true;
            // A driver backend has no Unicode channel; text always has a system
            // fallback that types exactly the same characters.
            if (ReferenceEquals(sink, _system)) return false;
            return _system.SendText(text);
        }

        public static bool MouseButton(Keys key, bool up)
        {
            IInputSink sink = _backend;
            // A booking only makes sense while the mouse hook is installed: with no
            // hook nothing claims the stroke, and the leftover entry would later be
            // matched against the user's own click (and block the delivery barrier).
            bool book = !sink.MarksOwnEvents && SyntheticLedger.MouseClaimsActive;
            if (!sink.MarksOwnEvents && !up) SyntheticLedger.NoteMouseClick();
            if (book) SyntheticLedger.Record(key, !up);
            if (sink.SendMouseButton(key, up)) return true;
            if (ReferenceEquals(sink, _system)) return false;
            InputStroke[] one = new InputStroke[1];
            one[0] = new InputStroke(key, !up);
            if (book) SyntheticLedger.Forget(one);
            return _system.SendMouseButton(key, up);
        }

        public static bool MouseWheel(int delta)
        {
            if (delta == 0) return true;
            Keys key = delta > 0 ? (Keys)MouseKeys.WheelUp : (Keys)MouseKeys.WheelDown;
            IInputSink sink = _backend;
            bool book = !sink.MarksOwnEvents && SyntheticLedger.MouseClaimsActive;
            if (book) SyntheticLedger.Record(key, true);
            if (sink.SendWheel(delta)) return true;
            if (ReferenceEquals(sink, _system)) return false;
            InputStroke[] one = new InputStroke[1];
            one[0] = new InputStroke(key, true);
            if (book) SyntheticLedger.Forget(one);
            return _system.SendWheel(delta);
        }
    }
}
