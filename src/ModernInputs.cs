using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// Word wrapping shared by the custom text surfaces. Measuring with the
    /// renderer keeps the result identical to what is painted a moment later.
    /// </summary>
    internal static class MTextLayout
    {
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        public static List<string> Wrap(string text, Font font, int width, bool wrap)
        {
            List<string> lines = new List<string>();
            string normalized = Normalize(text);
            string[] raw = normalized.Split('\n');
            int limit = Math.Max(Theme.S(24), width);
            for (int i = 0; i < raw.Length; i++)
            {
                string line = raw[i];
                if (!wrap || line.Length == 0)
                {
                    lines.Add(line);
                    continue;
                }
                WrapOne(line, font, limit, lines);
            }
            if (lines.Count == 0) lines.Add(string.Empty);
            return lines;
        }

        private static void WrapOne(string line, Font font, int limit, List<string> output)
        {
            int start = 0;
            while (start < line.Length)
            {
                int fit = Fit(line, start, font, limit);
                if (fit <= start) fit = start + 1;
                if (fit < line.Length)
                {
                    int space = line.LastIndexOf(' ', fit - 1, fit - start);
                    if (space > start) fit = space + 1;
                }
                output.Add(line.Substring(start, fit - start).TrimEnd());
                start = fit;
            }
            if (output.Count == 0) output.Add(string.Empty);
        }

        private static int Fit(string line, int start, Font font, int limit)
        {
            int low = start + 1;
            int high = line.Length;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                Size size = TextRenderer.MeasureText(line.Substring(start, mid - start), font);
                if (size.Width <= limit) low = mid;
                else high = mid - 1;
            }
            return low;
        }

        public static int LineHeight(Font font)
        {
            return Math.Max(Theme.S(14), TextRenderer.MeasureText("Ag", font).Height);
        }

        public static int Height(List<string> lines, Font font)
        {
            return lines.Count * LineHeight(font);
        }
    }

    /// <summary>
    /// Rounded, themed replacement for a plain single-line TextBox. The real
    /// edit control is borderless inside the painted shell; focus is shown by
    /// an accent ring instead of the system focus rectangle.
    /// </summary>
    internal sealed class MTextBox : Control
    {
        private readonly TextBox _inner;
        private bool _focused;

        public MTextBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            TabStop = false;
            Font = Theme.Body;
            BackColor = Theme.Background;

            _inner = new TextBox();
            _inner.BorderStyle = BorderStyle.None;
            _inner.Font = Theme.Body;
            _inner.BackColor = Theme.Surface;
            _inner.ForeColor = Theme.TextPrimary;
            _inner.TextChanged += delegate { Invalidate(); OnTextChanged(EventArgs.Empty); };
            _inner.GotFocus += delegate { _focused = true; Invalidate(); };
            _inner.LostFocus += delegate { _focused = false; Invalidate(); };
            _inner.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                KeyEventHandler handler = InnerKeyDown;
                if (handler != null) handler(this, e);
            };
            _inner.HandleCreated += delegate
            {
                // The cue banner needs a real edit handle: setting Cue before the
                // inner box is created would otherwise be lost.
                if (_cue.Length > 0) Native.SetCueBanner(_inner, _cue);
            };
            Controls.Add(_inner);
            Height = Theme.S(Theme.CtrlHeight);
        }

        public TextBox Inner { get { return _inner; } }

        public event KeyEventHandler InnerKeyDown;

        public override string Text
        {
            get { return _inner.Text; }
            set { _inner.Text = value == null ? string.Empty : value; }
        }

        public string Cue
        {
            get { return _cue; }
            set
            {
                _cue = value == null ? string.Empty : value;
                if (_inner.IsHandleCreated) Native.SetCueBanner(_inner, _cue);
            }
        }

        private string _cue = string.Empty;

        public bool ReadOnlyField
        {
            get { return _inner.ReadOnly; }
            set
            {
                _inner.ReadOnly = value;
                _inner.BackColor = Theme.Surface;
                Invalidate();
            }
        }

        public void ApplyPalette()
        {
            Font = Theme.Body;
            _inner.Font = Theme.Body;
            _inner.BackColor = Theme.Surface;
            _inner.ForeColor = Theme.TextPrimary;
            BackColor = Theme.Background;
            Height = Theme.S(Theme.CtrlHeight);
            LayoutInner();
            Invalidate();
        }

        public void FocusInner()
        {
            _inner.Focus();
            _inner.SelectAll();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_cue.Length > 0 && _inner.IsHandleCreated) Native.SetCueBanner(_inner, _cue);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutInner();
        }

        private void LayoutInner()
        {
            if (_inner == null) return;
            int pad = Theme.S(9);
            int height = _inner.PreferredHeight;
            _inner.SetBounds(pad, Math.Max(Theme.S(2), (Height - height) / 2),
                Math.Max(Theme.S(10), Width - pad * 2), height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            RectangleF shell = new RectangleF(0, 0, Width, Height);
            Theme.FillRound(e.Graphics, shell, Theme.SF(Theme.RadiusControl), Theme.Surface);
            Theme.StrokeRound(e.Graphics, shell, Theme.SF(Theme.RadiusControl),
                _focused ? Theme.Accent : Theme.Stroke, _focused ? 1.6f : 1f);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            _inner.Focus();
        }
    }

    /// <summary>
    /// Multiline field with a painted slim scrollbar. The inner TextBox keeps
    /// the caret, selection and IME behaviour; only the chrome is ours.
    /// </summary>
    internal sealed class MTextArea : Control
    {
        private readonly TextBox _inner;
        private bool _focused;
        private bool _barHover;
        private bool _barDrag;
        private int _dragGrab;
        private bool _monospace;

        public MTextArea()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            Font = Theme.Body;
            BackColor = Theme.Background;

            _inner = new TextBox();
            _inner.BorderStyle = BorderStyle.None;
            _inner.Multiline = true;
            _inner.ScrollBars = ScrollBars.None;
            _inner.WordWrap = true;
            _inner.Font = Theme.Body;
            _inner.BackColor = Theme.Surface;
            _inner.ForeColor = Theme.TextPrimary;
            _inner.TextChanged += delegate { Invalidate(); OnTextChanged(EventArgs.Empty); };
            _inner.GotFocus += delegate { _focused = true; Invalidate(); };
            _inner.LostFocus += delegate { _focused = false; Invalidate(); };
            _inner.KeyUp += delegate { Invalidate(); };
            _inner.MouseUp += delegate { Invalidate(); };
            Controls.Add(_inner);
            Height = Theme.S(92);
        }

        public TextBox Inner { get { return _inner; } }

        public override string Text
        {
            get { return _inner.Text; }
            set { _inner.Text = value == null ? string.Empty : value; }
        }

        public bool ReadOnlyField
        {
            get { return _inner.ReadOnly; }
            set { _inner.ReadOnly = value; Invalidate(); }
        }

        public bool Monospace
        {
            get { return _monospace; }
            set
            {
                _monospace = value;
                Font = value ? Theme.Mono : Theme.Body;
                _inner.Font = Font;
                _inner.BackColor = Theme.Surface;
                Invalidate();
            }
        }

        public void ApplyPalette()
        {
            Font = _monospace ? Theme.Mono : Theme.Body;
            _inner.Font = Font;
            _inner.BackColor = Theme.Surface;
            _inner.ForeColor = Theme.TextPrimary;
            BackColor = Theme.Background;
            PositionInner();
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            PositionInner();
        }

        private int BarWidth { get { return Theme.S(6); } }
        private int BarZone { get { return Theme.S(12); } }
        private int Pad { get { return Theme.S(10); } }

        private void PositionInner()
        {
            if (_inner == null) return;
            _inner.SetBounds(Pad, Theme.S(8),
                Math.Max(Theme.S(20), Width - Pad * 2 - Theme.S(4)),
                Math.Max(Theme.S(16), Height - Theme.S(16)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            RectangleF shell = new RectangleF(0, 0, Width, Height);
            Theme.FillRound(e.Graphics, shell, Theme.SF(Theme.RadiusControl), Theme.Surface);
            Theme.StrokeRound(e.Graphics, shell, Theme.SF(Theme.RadiusControl),
                _focused ? Theme.Accent : Theme.Stroke, _focused ? 1.6f : 1f);
            PaintBar(e.Graphics);
        }

        private int LineCount()
        {
            return Math.Max(1, Native.EditLineCount(_inner));
        }

        private int VisibleLines()
        {
            int lineHeight = MTextLayout.LineHeight(_inner.Font);
            return Math.Max(1, (_inner.ClientSize.Height + lineHeight - 1) / lineHeight);
        }

        private RectangleF BarTrack()
        {
            return new RectangleF(Width - BarZone + Theme.S(3), Theme.S(8),
                BarWidth, Math.Max(Theme.S(10), Height - Theme.S(16)));
        }

        private RectangleF BarThumb()
        {
            int total = LineCount();
            int visible = VisibleLines();
            RectangleF track = BarTrack();
            if (total <= visible) return RectangleF.Empty;
            float ratio = visible / (float)total;
            float height = Math.Max(Theme.S(24), track.Height * ratio);
            int first = Native.EditFirstVisibleLine(_inner);
            float maxScroll = Math.Max(1, total - visible);
            float y = track.Y + (track.Height - height) * Math.Min(1f, Math.Max(0f, first / maxScroll));
            return new RectangleF(track.X, y, track.Width, height);
        }

        private void PaintBar(Graphics g)
        {
            RectangleF thumb = BarThumb();
            if (thumb.IsEmpty) return;
            Theme.FillRound(g, thumb, BarWidth / 2f,
                _barHover || _barDrag ? Theme.ScrollThumbHover : Theme.ScrollThumb);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            foreach (Control child in Controls) child.Focus();
            RectangleF thumb = BarThumb();
            if (thumb.IsEmpty) return;
            if (e.X < Width - BarZone) return;
            if (thumb.Contains(e.X, e.Y))
            {
                _barDrag = true;
                _dragGrab = (int)(e.Y - thumb.Y);
                Capture = true;
                Invalidate();
            }
            else
            {
                int total = LineCount();
                int visible = VisibleLines();
                int target = (int)((e.Y - BarTrack().Y) / BarTrack().Height * (total - visible));
                int delta = target - Native.EditFirstVisibleLine(_inner);
                Native.EditScrollLines(_inner, delta);
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = e.X >= Width - BarZone && !BarThumb().IsEmpty;
            if (hover != _barHover) { _barHover = hover; Invalidate(); }
            if (!_barDrag) return;
            RectangleF track = BarTrack();
            RectangleF thumb = BarThumb();
            int total = LineCount();
            int visible = VisibleLines();
            float travel = Math.Max(1f, track.Height - (thumb.IsEmpty ? 0f : thumb.Height));
            float ratio = Math.Min(1f, Math.Max(0f, (e.Y - _dragGrab - track.Y) / travel));
            int target = (int)Math.Round(ratio * Math.Max(0, total - visible));
            int delta = target - Native.EditFirstVisibleLine(_inner);
            Native.EditScrollLines(_inner, delta);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_barDrag) return;
            _barDrag = false;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_barHover && !_barDrag) return;
            _barHover = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Native.EditScrollLines(_inner, e.Delta > 0 ? -3 : 3);
            Invalidate();
        }
    }

    /// <summary>
    /// Numeric field painted in the app palette: the native spinner arrows are
    /// replaced by two chevron zones that step the value.
    /// </summary>
    internal sealed class MNumberBox : Control
    {
        private int _value = 1;
        private int _minimum = 1;
        private int _maximum = 100;
        private int _increment = 1;
        private bool _hoverUp;
        private bool _hoverDown;
        private bool _focused;
        private string _edit;
        private bool _replaceOnType;
        private readonly Timer _repeat;

        public MNumberBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            Font = Theme.Body;
            Height = Theme.S(28);
            BackColor = Theme.Background;
            _repeat = new Timer();
            // Hold to repeat: a single click must step exactly once, so the first
            // repeat tick only arrives after a deliberate hold.
            _repeat.Interval = 450;
            _repeat.Tick += delegate
            {
                if (_repeat.Interval != 70)
                {
                    _repeat.Interval = 70;
                    return;
                }
                Step(_repeat.Tag != null && (string)_repeat.Tag == "down" ? -1 : 1);
            };
        }

        public event EventHandler ValueChanged;

        public int Value
        {
            get { return _value; }
            set
            {
                int next = Math.Max(_minimum, Math.Min(_maximum, value));
                if (next == _value) return;
                _value = next;
                Invalidate();
                EventHandler handler = ValueChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public int Minimum
        {
            get { return _minimum; }
            set { _minimum = value; if (_value < _minimum) Value = _minimum; }
        }

        public int Maximum
        {
            get { return _maximum; }
            set { _maximum = value; if (_value > _maximum) Value = _maximum; }
        }

        public int Increment
        {
            get { return _increment; }
            set { _increment = Math.Max(1, value); }
        }

        public void ApplyPalette()
        {
            Font = Theme.Body;
            Height = Theme.S(28);
            Invalidate();
        }

        private Rectangle UpZone()
        {
            int w = Theme.S(20);
            return new Rectangle(Width - w, 0, w, Height / 2);
        }

        private Rectangle DownZone()
        {
            int w = Theme.S(20);
            return new Rectangle(Width - w, Height / 2, w, Height - Height / 2);
        }

        private int TextRight
        {
            get { return Width - Theme.S(24); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            Graphics g = e.Graphics;
            RectangleF shell = new RectangleF(0, 0, Width, Height);
            Theme.FillRound(g, shell, Theme.SF(Theme.RadiusControl), Theme.Surface);
            Theme.StrokeRound(g, shell, Theme.SF(Theme.RadiusControl),
                _focused ? Theme.Accent : Theme.Stroke, _focused ? 1.6f : 1f);

            Rectangle up = UpZone();
            Rectangle down = DownZone();
            if (_hoverUp) Theme.FillRound(g, new RectangleF(up.X, Theme.S(1), up.Width - Theme.S(1), up.Height - Theme.S(2)), Theme.SF(3), Theme.SurfaceHover);
            if (_hoverDown) Theme.FillRound(g, new RectangleF(down.X, down.Y + Theme.S(1), down.Width - Theme.S(1), down.Height - Theme.S(2)), Theme.SF(3), Theme.SurfaceHover);

            Color arrow = Theme.TextSecondary;
            using (Pen pen = new Pen(arrow, 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                int cx = up.X + up.Width / 2;
                int cy = up.Y + up.Height / 2;
                g.DrawLines(pen, new Point[] {
                    new Point(cx - Theme.S(3), cy + Theme.S(2)),
                    new Point(cx, cy - Theme.S(2)),
                    new Point(cx + Theme.S(3), cy + Theme.S(2)) });
                int dx = down.X + down.Width / 2;
                int dy = down.Y + down.Height / 2;
                g.DrawLines(pen, new Point[] {
                    new Point(dx - Theme.S(3), dy - Theme.S(2)),
                    new Point(dx, dy + Theme.S(2)),
                    new Point(dx + Theme.S(3), dy - Theme.S(2)) });
            }

            string text = _edit == null ? _value.ToString() : _edit;
            Rectangle area = new Rectangle(Theme.S(9), 0, Math.Max(Theme.S(8), TextRight - Theme.S(9)), Height);
            TextRenderer.DrawText(g, text, Font, area, Theme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool up = UpZone().Contains(e.Location);
            bool down = DownZone().Contains(e.Location);
            if (up != _hoverUp || down != _hoverDown)
            {
                _hoverUp = up;
                _hoverDown = down;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverUp = false;
            _hoverDown = false;
            _repeat.Stop();
            _repeat.Interval = 450;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (UpZone().Contains(e.Location))
            {
                Step(1);
                _repeat.Tag = "up";
                _repeat.Interval = 450;
                _repeat.Start();
            }
            else if (DownZone().Contains(e.Location))
            {
                Step(-1);
                _repeat.Tag = "down";
                _repeat.Interval = 450;
                _repeat.Start();
            }
            else
            {
                // Clicking the field starts a fresh edit: the first digit replaces
                // the old value, later digits append (the previous version committed
                // after every keystroke, so only the last digit survived).
                Focus();
                _edit = null;
                _replaceOnType = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _repeat.Stop();
            _repeat.Interval = 450;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Step(e.Delta > 0 ? 1 : -1);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            // Enter must reach OnKeyDown (commit + drop focus) instead of being
            // turned into the dialog's AcceptButton by the message loop.
            if ((keyData & Keys.KeyCode) == Keys.Enter) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Up) { Step(1); e.Handled = true; return; }
            if (e.KeyCode == Keys.Down) { Step(-1); e.Handled = true; return; }
            if (e.KeyCode == Keys.Enter)
            {
                CommitEdit();
                e.Handled = true;
                e.SuppressKeyPress = true;
                Form owner = FindForm();
                if (owner != null) owner.ActiveControl = null;
                Invalidate();
                return;
            }
            if (e.KeyCode == Keys.Escape)
            {
                _edit = null;
                Invalidate();
                e.Handled = true;
                return;
            }
            if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9)
            {
                Append((char)('0' + (e.KeyCode - Keys.D0)));
                e.Handled = true;
                return;
            }
            if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9)
            {
                Append((char)('0' + (e.KeyCode - Keys.NumPad0)));
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Back)
            {
                // The shown number must be editable at once: first Backspace seeds
                // the edit buffer from the current value, then removes a digit.
                if (_edit == null) _edit = _value.ToString();
                if (_edit.Length > 0) _edit = _edit.Substring(0, _edit.Length - 1);
                _replaceOnType = false;
                Invalidate();
                e.Handled = true;
            }
        }

        private void Append(char c)
        {
            if (_replaceOnType || _edit == null)
            {
                _edit = c.ToString();
                _replaceOnType = false;
            }
            else
            {
                if (_edit.Length >= 5) return;
                _edit += c;
            }
            Invalidate();
        }

        private void CommitEdit()
        {
            if (_edit == null) return;
            int parsed;
            if (int.TryParse(_edit, out parsed)) Value = parsed;
            _edit = null;
            Invalidate();
        }

        private void Step(int direction)
        {
            CommitEdit();
            Value = _value + direction * _increment;
            _edit = null;
            _replaceOnType = false;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            _focused = true;
            _edit = null;
            _replaceOnType = true;
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            _focused = false;
            _repeat.Stop();
            _repeat.Interval = 450;
            CommitEdit();
            Invalidate();
        }
    }

    /// <summary>
    /// Read-only text surface that paints wrapped lines and its own scrollbar.
    /// Used for plugin details, JSON previews and escape results so no native
    /// scrollbar ever appears inside a card.
    /// </summary>
    internal sealed class MTextView : Control
    {
        private string _text = string.Empty;
        private List<string> _lines = new List<string>();
        private int _firstLine;
        private int _lineHeight = 16;
        private bool _barHover;
        private bool _barDrag;
        private int _dragGrab;
        private Font _font;

        public MTextView()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            TabStop = false;
            Font = Theme.Body;
            _font = Theme.Body;
            BackColor = Theme.Surface;
            _lineHeight = MTextLayout.LineHeight(Theme.Body);
        }

        public override string Text
        {
            get { return _text; }
            set
            {
                _text = value == null ? string.Empty : value;
                Rewrap();
                Invalidate();
            }
        }

        public bool Monospace
        {
            get { return _font == Theme.Mono; }
            set
            {
                _font = value ? Theme.Mono : Theme.Body;
                Font = _font;
                Rewrap();
                Invalidate();
            }
        }

        public void ApplyPalette()
        {
            _font = Font == null ? Theme.Body : Font;
            _lineHeight = MTextLayout.LineHeight(_font);
            BackColor = Theme.Surface;
            Rewrap();
            Invalidate();
        }

        private int BarWidth { get { return Theme.S(6); } }
        private int BarZone { get { return Theme.S(14); } }
        private int Pad { get { return Theme.S(10); } }

        private int ContentWidth
        {
            get { return Math.Max(Theme.S(40), Width - Pad * 2 - BarZone); }
        }

        private int VisibleLines
        {
            get { return Math.Max(1, (Height - Pad * 2) / _lineHeight); }
        }

        private void Rewrap()
        {
            _lineHeight = MTextLayout.LineHeight(_font);
            _lines = MTextLayout.Wrap(_text, _font, ContentWidth, true);
            int maxFirst = Math.Max(0, _lines.Count - VisibleLines);
            if (_firstLine > maxFirst) _firstLine = maxFirst;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Rewrap();
        }

        private RectangleF BarTrack()
        {
            return new RectangleF(Width - BarZone + Theme.S(4), Pad,
                BarWidth, Math.Max(Theme.S(10), Height - Pad * 2));
        }

        private RectangleF BarThumb()
        {
            RectangleF track = BarTrack();
            int total = _lines.Count;
            int visible = VisibleLines;
            if (total <= visible) return RectangleF.Empty;
            float ratio = visible / (float)total;
            float height = Math.Max(Theme.S(22), track.Height * ratio);
            float maxScroll = Math.Max(1, total - visible);
            float y = track.Y + (track.Height - height) * Math.Min(1f, Math.Max(0f, _firstLine / maxScroll));
            return new RectangleF(track.X, y, track.Width, height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            Graphics g = e.Graphics;
            Theme.FillRound(g, new RectangleF(0, 0, Width, Height),
                Theme.SF(Theme.RadiusControl), Theme.Surface);

            Rectangle clip = new Rectangle(Pad, Pad, Math.Max(0, Width - Pad * 2 - BarZone), Math.Max(0, Height - Pad * 2));
            Region old = g.Clip;
            g.SetClip(clip);
            int y = Pad;
            int last = Math.Min(_lines.Count, _firstLine + VisibleLines + 1);
            for (int i = _firstLine; i < last; i++)
            {
                TextRenderer.DrawText(g, _lines[i], _font,
                    new Point(Pad, y), Theme.TextPrimary,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                y += _lineHeight;
            }
            g.Clip = old;

            RectangleF thumb = BarThumb();
            if (!thumb.IsEmpty)
            {
                Theme.FillRound(g, thumb, BarWidth / 2f,
                    _barHover || _barDrag ? Theme.ScrollThumbHover : Theme.ScrollThumb);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            RectangleF thumb = BarThumb();
            if (thumb.IsEmpty || e.X < Width - BarZone) return;
            if (thumb.Contains(e.X, e.Y))
            {
                _barDrag = true;
                _dragGrab = (int)(e.Y - thumb.Y);
                Capture = true;
                Invalidate();
            }
            else
            {
                int target = (int)((e.Y - BarTrack().Y) / BarTrack().Height * (_lines.Count - VisibleLines));
                ScrollTo(target);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = e.X >= Width - BarZone && !BarThumb().IsEmpty;
            if (hover != _barHover) { _barHover = hover; Invalidate(); }
            if (!_barDrag) return;
            RectangleF track = BarTrack();
            RectangleF thumb = BarThumb();
            float travel = Math.Max(1f, track.Height - (thumb.IsEmpty ? 0f : thumb.Height));
            float ratio = Math.Min(1f, Math.Max(0f, (e.Y - _dragGrab - track.Y) / travel));
            ScrollTo((int)Math.Round(ratio * Math.Max(0, _lines.Count - VisibleLines)));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_barDrag) return;
            _barDrag = false;
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _barHover = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollTo(_firstLine + (e.Delta > 0 ? -3 : 3));
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Down) { ScrollTo(_firstLine + 1); e.Handled = true; }
            else if (e.KeyCode == Keys.Up) { ScrollTo(_firstLine - 1); e.Handled = true; }
            else if (e.KeyCode == Keys.PageDown) { ScrollTo(_firstLine + VisibleLines); e.Handled = true; }
            else if (e.KeyCode == Keys.PageUp) { ScrollTo(_firstLine - VisibleLines); e.Handled = true; }
            else if (e.KeyCode == Keys.Home) { ScrollTo(0); e.Handled = true; }
            else if (e.KeyCode == Keys.End) { ScrollTo(_lines.Count); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                try { Clipboard.SetText(_text); } catch (Exception) { }
                e.Handled = true;
            }
        }

        private void ScrollTo(int line)
        {
            int maxFirst = Math.Max(0, _lines.Count - VisibleLines);
            int next = Math.Max(0, Math.Min(maxFirst, line));
            if (next == _firstLine) return;
            _firstLine = next;
            Invalidate();
        }
    }

    /// <summary>
    /// Inline editor for the key combination a "send keys" action emits: four
    /// modifier chips, a key capture box and a live preview. It used to open a
    /// separate modal dialog, which made the shortcut editor feel like a dialog
    /// inside a dialog; keeping it inline also keeps focus in one window, so Esc
    /// and Tab behave predictably.
    /// </summary>
    internal sealed class MKeyComboField : Control
    {
        private readonly MChipToggle _ctrl;
        private readonly MChipToggle _alt;
        private readonly MChipToggle _shift;
        private readonly MChipToggle _win;
        private readonly MKeyCapture _key;
        private readonly Label _preview;

        public event EventHandler ComboChanged;

        public MKeyComboField()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = Theme.S(74);

            _ctrl = MakeChip("Ctrl");
            _alt = MakeChip("Alt");
            _shift = MakeChip("Shift");
            _win = MakeChip("Win");

            _key = new MKeyCapture();
            Controls.Add(_key);

            _preview = Theme.MakeLine("", new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
                Theme.Accent, 0, 0, Theme.S(160));
            _preview.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_preview);

            EventHandler changed = delegate { UpdatePreview(); RaiseChanged(); };
            _ctrl.CheckedChanged += changed;
            _alt.CheckedChanged += changed;
            _shift.CheckedChanged += changed;
            _win.CheckedChanged += changed;
            _key.KeyChanged += changed;
            UpdatePreview();
        }

        public string ComboText
        {
            get
            {
                if (_key.KeyCode == Keys.None) return "";
                return KeyText.BuildComboParam(_ctrl.Checked, _alt.Checked, _shift.Checked,
                    _win.Checked, _key.KeyCode);
            }
            set
            {
                bool c, a, s, w;
                Keys k;
                if (!KeyText.TryParseCombo(value, out c, out a, out s, out w, out k))
                {
                    // An empty (or unreadable) combo means "nothing chosen yet":
                    // do not pre-check Ctrl, which used to silently turn a blank
                    // field into Ctrl + C.
                    c = a = s = w = false;
                    k = Keys.None;
                }
                _ctrl.Checked = c;
                _alt.Checked = a;
                _shift.Checked = s;
                _win.Checked = w;
                _key.KeyCode = k;
                UpdatePreview();
            }
        }

        public Keys KeyCode
        {
            get { return _key.KeyCode; }
        }

        private MChipToggle MakeChip(string text)
        {
            MChipToggle chip = new MChipToggle();
            chip.Text = text;
            chip.FitToText();
            Controls.Add(chip);
            return chip;
        }

        private void RaiseChanged()
        {
            EventHandler h = ComboChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        private void UpdatePreview()
        {
            string mods = KeyText.FormatModifiers(false, _ctrl.Checked, _alt.Checked,
                _shift.Checked, _win.Checked);
            string keyName = _key.KeyCode == Keys.None ? "请按主键" : KeyText.FormatKey(_key.KeyCode);
            _preview.Text = mods.Length == 0 ? keyName : mods + " + " + keyName;
            _preview.ForeColor = _key.KeyCode == Keys.None ? Theme.TextTertiary : Theme.Accent;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_ctrl == null || _key == null || _preview == null) return;
            int x = 0;
            MChipToggle[] chips = new MChipToggle[] { _ctrl, _alt, _shift, _win };
            for (int i = 0; i < chips.Length; i++)
            {
                chips[i].Location = new Point(x, 0);
                x = chips[i].Right + Theme.S(8);
            }
            int rowY = Theme.S(36);
            _key.SetBounds(0, rowY, Theme.S(150), Theme.S(32));
            _preview.SetBounds(Theme.S(160), rowY + Theme.S(2),
                Math.Max(Theme.S(60), Width - Theme.S(160)), Theme.S(28));
        }
    }
}
