using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>Which mouse button "鼠标点击" presses.</summary>
    internal enum MouseClickButton
    {
        Left = 0,
        Middle = 1,
        Right = 2
    }

    /// <summary>
    /// Parameter of the "鼠标点击" action: one button plus a desktop point in
    /// virtual-screen coordinates, and the modifier keys to hold while clicking.
    /// Stored as "L;x;y" or, with modifiers, "L;x;y;CA" inside
    /// <see cref="KeyMapping.Param"/>.
    /// </summary>
    internal struct MouseClickPoint
    {
        public MouseClickButton Button;
        public int X;
        public int Y;
        public bool Ctrl;
        public bool Alt;
        public bool Shift;
        public bool Win;

        public static readonly MouseClickPoint Empty = new MouseClickPoint
        {
            Button = MouseClickButton.Left,
            X = 0,
            Y = 0
        };

        public bool HasModifiers
        {
            get { return Ctrl || Alt || Shift || Win; }
        }

        /// <summary>"Ctrl + Shift" for display, or "" when no modifier is held.</summary>
        public string ModifierText
        {
            get { return KeyText.FormatModifiers(false, Ctrl, Alt, Shift, Win); }
        }

        public string ButtonName
        {
            get
            {
                switch (Button)
                {
                    case MouseClickButton.Middle: return "中键";
                    case MouseClickButton.Right: return "右键";
                    default: return "左键";
                }
            }
        }

        public static string Format(MouseClickPoint point)
        {
            string prefix = point.Button == MouseClickButton.Middle ? "M"
                : point.Button == MouseClickButton.Right ? "R" : "L";
            string text = prefix + ";" + point.X + ";" + point.Y;
            string mods = FormatModifierFlags(point);
            // The trailing segment is only written when something is held, so
            // old values ("L;12;34") keep their exact form after a round trip.
            return mods.Length == 0 ? text : text + ";" + mods;
        }

        private static string FormatModifierFlags(MouseClickPoint point)
        {
            string flags = "";
            if (point.Ctrl) flags += "C";
            if (point.Alt) flags += "A";
            if (point.Shift) flags += "S";
            if (point.Win) flags += "W";
            return flags;
        }

        /// <summary>
        /// Accepts "L;12;34", "L;12;34;CS", and the legacy "12,34" (a left click
        /// without modifiers). Unknown modifier letters are ignored.
        /// </summary>
        public static bool TryParse(string text, out MouseClickPoint point)
        {
            point = Empty;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split(new char[] { ';', ',' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
            {
                int px;
                int py;
                if (!int.TryParse(parts[0].Trim(), out px)) return false;
                if (!int.TryParse(parts[1].Trim(), out py)) return false;
                point.X = Clamp(px);
                point.Y = Clamp(py);
                return true;
            }
            if (parts.Length < 3) return false;

            int x;
            int y;
            if (!int.TryParse(parts[1].Trim(), out x)) return false;
            if (!int.TryParse(parts[2].Trim(), out y)) return false;
            string prefix = parts[0].Trim().ToUpperInvariant();
            point.Button = prefix == "M" || prefix == "中" ? MouseClickButton.Middle
                : prefix == "R" || prefix == "右" ? MouseClickButton.Right
                : MouseClickButton.Left;
            point.X = Clamp(x);
            point.Y = Clamp(y);
            if (parts.Length >= 4)
            {
                string mods = parts[3].Trim().ToUpperInvariant();
                point.Ctrl = mods.IndexOf('C') >= 0;
                point.Alt = mods.IndexOf('A') >= 0;
                point.Shift = mods.IndexOf('S') >= 0;
                point.Win = mods.IndexOf('W') >= 0;
            }
            return true;
        }

        public static MouseClickPoint Parse(string text)
        {
            MouseClickPoint point;
            if (TryParse(text, out point)) return point;
            return Empty;
        }

        private static int Clamp(int value)
        {
            if (value < -32768) return -32768;
            if (value > 32767) return 32767;
            return value;
        }
    }

    /// <summary>
    /// Hides every visible app window, waits for CapsLock (Esc cancels), then hands
    /// the cursor position back. CapsLock is swallowed so it neither toggles the
    /// lock nor fires the app's own hotkey while the window is hidden.
    /// </summary>
    internal static class MousePointCapture
    {
        private const int CapsHotkeyId = 0x4C01;
        private const int EscHotkeyId = 0x4C02;

        private static IntPtr _hook;
        private static Native.LowLevelKeyboardProc _proc;
        private static CaptureWindow _window;
        private static bool _capsHotkey;
        private static bool _escHotkey;
        private static Form _owner;
        private static Action<Point> _completed;
        private static List<Form> _hidden;
        private static List<Rectangle> _hiddenBounds;
        private static List<FormWindowState> _hiddenStates;

        public static bool IsActive { get { return _owner != null; } }

        public static void Begin(Form owner, Action<Point> completed)
        {
            if (_owner != null || owner == null || !owner.IsHandleCreated) return;

            _owner = owner;
            _completed = completed;

            // Try a real hotkey first: it keeps working while every window is hidden
            // and the system swallows the key for us (CapsLock does not toggle).
            _window = new CaptureWindow();
            _capsHotkey = Native.RegisterHotKey(_window.Handle, CapsHotkeyId, 0, (uint)Keys.CapsLock);
            _escHotkey = Native.RegisterHotKey(_window.Handle, EscHotkeyId, 0, (uint)Keys.Escape);
            if (!_capsHotkey)
            {
                // Someone else owns CapsLock; fall back to the low level hook.
                _proc = HookCallback;
                _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc,
                    Native.GetModuleHandle(null), 0);
            }

            // Move the windows off screen instead of hiding them. Hiding a form that
            // is shown with ShowDialog leaves its modal loop in a state where a
            // later Show() throws or the dialog never comes back.
            _hidden = new List<Form>();
            _hiddenBounds = new List<Rectangle>();
            _hiddenStates = new List<FormWindowState>();
            foreach (Form form in Application.OpenForms)
            {
                if (form == null || !form.Visible || form.IsDisposed || !form.IsHandleCreated) continue;
                if (form.WindowState == FormWindowState.Minimized) continue;

                _hidden.Add(form);
                _hiddenBounds.Add(form.WindowState == FormWindowState.Maximized
                    ? form.RestoreBounds : form.Bounds);
                _hiddenStates.Add(form.WindowState);
                if (form.WindowState == FormWindowState.Maximized)
                {
                    // A maximized window ignores SetWindowPos, so hide it normally; the
                    // main window is not modal and comes back with Show().
                    form.Hide();
                }
                else
                {
                    Native.SetWindowPos(form.Handle, IntPtr.Zero, -32000, -32000, 0, 0,
                        Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                }
            }

            if (!_capsHotkey && _hook == IntPtr.Zero)
            {
                RestoreWindows();
                Uninstall();
            }
        }

        /// <summary>Called by the keyboard hook (CapsLock) while the picker is open.</summary>
        public static void ConfirmAtCursor()
        {
            Complete(Cursor.Position, true);
        }

        /// <summary>Called by the keyboard hook (Esc) while the picker is open.</summary>
        public static void Cancel()
        {
            Complete(Point.Empty, false);
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                Native.KBDLLHOOKSTRUCT data =
                    (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                int message = wParam.ToInt32();
                bool down = message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN;
                if (down)
                {
                    if (data.vkCode == (uint)Keys.CapsLock)
                    {
                        Point position = Cursor.Position;
                        Post(delegate { Complete(position, true); });
                        return new IntPtr(1);
                    }
                    if (data.vkCode == (uint)Keys.Escape)
                    {
                        Post(delegate { Complete(Point.Empty, false); });
                        return new IntPtr(1);
                    }
                }
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        /// <summary>Schedules the UI part outside the hook callback.</summary>
        private static void Post(Action action)
        {
            Form owner = _owner;
            if (owner == null || owner.IsDisposed || !owner.IsHandleCreated) return;
            owner.BeginInvoke(action);
        }

        /// <summary>Runs the UI part outside the input callback.</summary>
        private static void Complete(Point position, bool accepted)
        {
            Form owner = _owner;
            Action<Point> completed = _completed;
            if (owner == null) return;
            _owner = null;
            _completed = null;
            Uninstall();
            RestoreWindows();
            if (owner != null && !owner.IsDisposed)
            {
                owner.Activate();
                if (owner.Visible) owner.Focus();
            }
            if (accepted && completed != null) completed(position);
        }

        private static void RestoreWindows()
        {
            if (_hidden == null) return;
            for (int i = 0; i < _hidden.Count; i++)
            {
                Form form = _hidden[i];
                if (form == null || form.IsDisposed) continue;
                Rectangle bounds = _hiddenBounds[i];
                if (_hiddenStates[i] == FormWindowState.Maximized)
                {
                    form.Show();
                    form.WindowState = FormWindowState.Maximized;
                    continue;
                }
                if (!form.IsHandleCreated) continue;
                Native.SetWindowPos(form.Handle, IntPtr.Zero, bounds.X, bounds.Y,
                    bounds.Width, bounds.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            }
            _hidden = null;
            _hiddenBounds = null;
            _hiddenStates = null;
        }

        private static void Uninstall()
        {
            if (_window != null)
            {
                if (_capsHotkey) Native.UnregisterHotKey(_window.Handle, CapsHotkeyId);
                if (_escHotkey) Native.UnregisterHotKey(_window.Handle, EscHotkeyId);
                _capsHotkey = false;
                _escHotkey = false;
                _window.DestroyHandle();
                _window = null;
            }
            if (_hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
            _proc = null;
        }

        /// <summary>Hidden message window that receives WM_HOTKEY.</summary>
        private sealed class CaptureWindow : NativeWindow
        {
            public CaptureWindow()
            {
                CreateParams cp = new CreateParams();
                cp.Caption = "CapsLockLite.Capture";
                CreateHandle(cp);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_HOTKEY)
                {
                    int id = m.WParam.ToInt32();
                    if (id == CapsHotkeyId)
                    {
                        MousePointCapture.ConfirmAtCursor();
                        return;
                    }
                    if (id == EscHotkeyId)
                    {
                        MousePointCapture.Cancel();
                        return;
                    }
                }
                base.WndProc(ref m);
            }
        }
    }

    internal sealed class MMousePointField : Control
    {
        private readonly MChipToggle _ctrl;
        private readonly MChipToggle _alt;
        private readonly MChipToggle _shift;
        private readonly MChipToggle _win;
        private readonly MSegmented _buttons = new MSegmented();
        private readonly MNumberBox _x = new MNumberBox();
        private readonly MNumberBox _y = new MNumberBox();
        private readonly MButton _capture = new MButton();
        private readonly Label _xLabel;
        private readonly Label _yLabel;

        public MMousePointField()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;

            // Same chips as the send-keys field: a click can be a Ctrl-click,
            // Shift-click, etc., and both the shortcut page and macro steps must
            // offer the same set.
            _ctrl = MakeChip("Ctrl");
            _alt = MakeChip("Alt");
            _shift = MakeChip("Shift");
            _win = MakeChip("Win");

            _buttons.Items.Add("左键");
            _buttons.Items.Add("中键");
            _buttons.Items.Add("右键");
            _buttons.Font = Theme.Body;
            Controls.Add(_buttons);

            _xLabel = Theme.MakeLabel("X", Theme.Caption, Theme.TextTertiary, 0, 0, Theme.S(14), Theme.S(22));
            _yLabel = Theme.MakeLabel("Y", Theme.Caption, Theme.TextTertiary, 0, 0, Theme.S(14), Theme.S(22));
            Controls.Add(_xLabel);
            Controls.Add(_yLabel);

            _x.Minimum = -32768;
            _x.Maximum = 32767;
            _y.Minimum = -32768;
            _y.Maximum = 32767;
            Controls.Add(_x);
            Controls.Add(_y);

            _capture.Text = "抓取坐标";
            _capture.Kind = MButtonKind.Secondary;
            _capture.Click += delegate { CapturePoint(); };
            Controls.Add(_capture);

            Height = Theme.S(112);
        }

        private MChipToggle MakeChip(string text)
        {
            MChipToggle chip = new MChipToggle();
            chip.Text = text;
            chip.FitToText();
            Controls.Add(chip);
            return chip;
        }

        public void ApplyPalette()
        {
            BackColor = Color.Transparent;
            _buttons.ApplyPalette();
            Invalidate(true);
        }

        public string Value
        {
            get
            {
                MouseClickPoint point = new MouseClickPoint();
                point.Button = (MouseClickButton)Math.Max(0, Math.Min(2, _buttons.SelectedIndex));
                point.X = _x.Value;
                point.Y = _y.Value;
                point.Ctrl = _ctrl.Checked;
                point.Alt = _alt.Checked;
                point.Shift = _shift.Checked;
                point.Win = _win.Checked;
                return MouseClickPoint.Format(point);
            }
            set
            {
                MouseClickPoint point = MouseClickPoint.Parse(value);
                _buttons.SelectedIndex = (int)point.Button;
                _x.Value = point.X;
                _y.Value = point.Y;
                _ctrl.Checked = point.Ctrl;
                _alt.Checked = point.Alt;
                _shift.Checked = point.Shift;
                _win.Checked = point.Win;
            }
        }

        /// <summary>Captures a point once the user presses CapsLock.</summary>
        private void CapturePoint()
        {
            Form owner = FindForm();
            MousePointCapture.Begin(owner, delegate(Point position)
            {
                _x.Value = position.X;
                _y.Value = position.Y;
            });
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int height = Theme.S(30);
            int x = 0;
            MChipToggle[] chips = new MChipToggle[] { _ctrl, _alt, _shift, _win };
            for (int i = 0; i < chips.Length; i++)
            {
                chips[i].Location = new Point(x, 0);
                x = chips[i].Right + Theme.S(8);
            }

            int rowY = Theme.S(38);
            int captureWidth = Theme.S(92);
            int buttonsWidth = Math.Max(Theme.S(120), Width - captureWidth - Theme.S(8));
            _buttons.SetBounds(0, rowY, buttonsWidth, height);
            _capture.SetBounds(Width - captureWidth, rowY, captureWidth, height);

            int pointY = rowY + height + Theme.S(12);
            _xLabel.SetBounds(0, pointY + Theme.S(3), Theme.S(14), Theme.S(22));
            _x.SetBounds(Theme.S(18), pointY, Theme.S(96), Theme.S(28));
            _yLabel.SetBounds(Theme.S(126), pointY + Theme.S(3), Theme.S(14), Theme.S(22));
            _y.SetBounds(Theme.S(144), pointY, Theme.S(96), Theme.S(28));
        }
    }
}
