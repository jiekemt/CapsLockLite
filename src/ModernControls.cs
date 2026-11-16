using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapsLockLite
{
    internal enum MButtonKind
    {
        Primary,
        Secondary,
        Subtle,
        Danger
    }

    /// <summary>Fluent style push button: 32 px tall, 4 px corners, clear states.</summary>
    internal sealed class MButton : Control
    {
        private bool _hover;
        private bool _pressed;
        private MButtonKind _kind = MButtonKind.Secondary;

        public MButtonKind Kind
        {
            get { return _kind; }
            set { _kind = value; Invalidate(); }
        }

        /// <summary>Runs the Click handlers as if the button had been pressed.</summary>
        public void PerformClick()
        {
            if (Enabled) OnClick(EventArgs.Empty);
        }

        /// <summary>First opaque colour behind this button, used by the subtle look.</summary>
        private Color ParentSurface()
        {
            Control parent = Parent;
            while (parent != null && parent.BackColor.A < 255) parent = parent.Parent;
            return parent == null ? Theme.Background : parent.BackColor;
        }

        public MButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            // Control raises Click from WmMouseUp when StandardClick is on; the custom
            // pressed-state logic below raises Click itself, so switch it off to avoid
            // firing every button action twice.
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
            Font = Theme.BodyStrong;
            Cursor = Cursors.Hand;
            TabStop = true;
            Size = new Size(Theme.S(84), Theme.S(Theme.CtrlHeight));
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float radius = Theme.SF(Theme.RadiusControl);

            Color fill;
            Color border = Color.Empty;
            Color fore;

            switch (_kind)
            {
                case MButtonKind.Primary:
                    fill = !Enabled ? Theme.Blend(Theme.Accent, Theme.Surface, 0.62f)
                        : _pressed ? Theme.AccentPressed
                        : _hover ? Theme.AccentHover : Theme.Accent;
                    fore = Enabled ? Theme.OnAccent : Theme.TextDisabled;
                    break;
                case MButtonKind.Danger:
                    fill = _pressed ? Theme.DangerSoft : _hover ? Theme.DangerSoft : Theme.Surface;
                    border = Theme.Blend(Theme.Danger, Theme.Stroke, 0.55f);
                    fore = Enabled ? Theme.Danger : Theme.TextDisabled;
                    break;
                case MButtonKind.Subtle:
                    // Subtle buttons still paint an opaque plate: drawing ClearType
                    // glyphs over a transparent control leaves colour fringes behind
                    // and makes the label look thinner than its neighbours.
                    fill = _pressed ? Theme.SurfacePressed
                        : _hover ? Theme.SurfaceHover : ParentSurface();
                    fore = Enabled ? Theme.TextPrimary : Theme.TextDisabled;
                    break;
                default:
                    fill = !Enabled ? Theme.Layer
                        : _pressed ? Theme.SurfacePressed
                        : _hover ? Theme.SurfaceHover : Theme.Surface;
                    border = Theme.Stroke;
                    fore = Enabled ? Theme.TextPrimary : Theme.TextDisabled;
                    break;
            }

            if (fill.A > 0) Theme.FillRound(g, r, radius, fill);
            if (border.A > 0 && Enabled) Theme.StrokeRound(g, r, radius, border, 1f);

            // The ring is a keyboard affordance only: after a mouse click the
            // control keeps focus, and a permanent outline reads as "still active".
            if (Focused && Enabled && ShowFocusCues)
                Theme.StrokeRound(g, new RectangleF(Theme.SF(2f), Theme.SF(2f),
                    Width - Theme.SF(4f), Height - Theme.SF(4f)), Math.Max(1f, radius - 1f),
                    Theme.Alpha(Theme.TextPrimary, 90), 1f);

            // Inset the text so long labels ellipsize inside the rounded frame
            // instead of running into the border.
            Rectangle textRect = new Rectangle(Theme.S(8), 0,
                Math.Max(Theme.S(8), Width - Theme.S(16)), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _pressed = true; Focus(); Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool fire = _pressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
            _pressed = false;
            Invalidate();
            if (fire) OnClick(EventArgs.Empty);
            base.OnMouseUp(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    }

    /// <summary>Square icon-only button, used for the overflow menu.</summary>
    internal sealed class MIconButton : Control
    {
        private bool _hover;
        private bool _pressed;

        public string Glyph { get; set; }

        public MIconButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            Glyph = "\uE712";
            Cursor = Cursors.Hand;
            Size = new Size(Theme.S(Theme.CtrlHeight), Theme.S(Theme.CtrlHeight));
            BackColor = Color.Transparent;
            TabStop = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            if (_pressed) Theme.FillRound(g, r, Theme.SF(Theme.RadiusControl), Theme.SurfacePressed);
            else if (_hover) Theme.FillRound(g, r, Theme.SF(Theme.RadiusControl), Theme.SurfaceHover);
            Theme.StrokeRound(g, r, Theme.SF(Theme.RadiusControl), Theme.Stroke, 1f);
            using (Font f = new Font("Segoe MDL2 Assets", 10f, FontStyle.Regular, GraphicsUnit.Point))
            {
                TextRenderer.DrawText(g, Glyph, f, ClientRectangle, Theme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            if (Focused)
                Theme.StrokeRound(g, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f),
                    Theme.SF(Theme.RadiusControl), Theme.Alpha(Theme.TextPrimary, 90), 1f);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Focus(); Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool fire = _pressed && ClientRectangle.Contains(e.Location);
            _pressed = false;
            Invalidate();
            if (fire) OnClick(EventArgs.Empty);
            base.OnMouseUp(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>Windows 11 style switch: 40x20 track, 12 px knob.</summary>
    internal sealed class MToggle : Control
    {
        private bool _checked;
        private bool _hover;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value) return;
                _checked = value;
                Invalidate();
                EventHandler h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public MToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(Theme.S(40), Theme.S(20));
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            TabStop = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            float h = Height - Theme.SF(1f);
            RectangleF track = new RectangleF(0.5f, 0.5f, Width - 1f, h);
            float radius = track.Height / 2f;

            if (_checked)
            {
                Color fill = !Enabled ? Theme.Blend(Theme.Accent, Theme.Background, 0.6f)
                    : _hover ? Theme.AccentHover : Theme.Accent;
                Theme.FillRound(g, track, radius, fill);
            }
            else
            {
                Theme.FillRound(g, track, radius, Theme.Layer);
                Color stroke = !Enabled ? Theme.Stroke
                    : _hover ? Theme.TextSecondary : Theme.StrokeStrong;
                Theme.StrokeRound(g, track, radius, stroke, 1f);
            }

            float knob = track.Height - Theme.SF(4f);
            float x = _checked ? track.Right - knob - Theme.SF(2f) : track.Left + Theme.SF(2f);
            RectangleF circle = new RectangleF(x, track.Top + Theme.SF(2f), knob, knob);
            Color knobColor = !Enabled ? Theme.TextDisabled
                : _checked ? Theme.OnAccent
                : (_hover ? Theme.TextPrimary : Theme.TextSecondary);
            using (SolidBrush b = new SolidBrush(knobColor))
                g.FillEllipse(b, circle);

            if (Focused)
                Theme.StrokeRound(g, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f),
                    radius, Theme.Alpha(Theme.TextPrimary, 110), 1f);
        }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled) { Checked = !Checked; Focus(); }
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                if (Enabled) Checked = !Checked;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>Left navigation entry used by the settings shell.</summary>
    internal sealed class MNavItem : Control
    {
        private bool _selected;
        private bool _hover;

        public string Glyph { get; set; }

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        public MNavItem()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            Height = Theme.S(40);
            BackColor = Color.Transparent;
            TabStop = true;
            Glyph = "";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            float pad = Theme.SF(4f);
            RectangleF pill = new RectangleF(pad, Theme.SF(2f), Width - pad * 2f, Height - Theme.SF(4f));
            float radius = Theme.SF(Theme.RadiusChip);

            if (_selected) Theme.FillRound(g, pill, radius, Theme.SurfaceAlt);
            else if (_hover) Theme.FillRound(g, pill, radius, Theme.NavHover);

            if (_selected)
            {
                RectangleF bar = new RectangleF(pill.Left + Theme.SF(1f),
                    pill.Top + (pill.Height - Theme.SF(16f)) / 2f, Theme.SF(3f), Theme.SF(16f));
                Theme.FillRound(g, bar, Theme.SF(1.5f), Theme.Accent);
            }

            int textX = Theme.S(20);
            if (!string.IsNullOrEmpty(Glyph))
            {
                int glyphX = _selected ? Theme.S(18) : Theme.S(16);
                using (Font f = new Font("Segoe MDL2 Assets", 11f, FontStyle.Regular, GraphicsUnit.Point))
                {
                    TextRenderer.DrawText(g, Glyph, f,
                        new Rectangle(glyphX, 0, Theme.S(20), Height),
                        _selected ? Theme.Accent : Theme.TextSecondary,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
                }
                textX = Theme.S(44);
            }

            TextRenderer.DrawText(g, Text, _selected ? Theme.BodyStrong : Theme.Body,
                new Rectangle(textX, 0, Width - textX - Theme.S(10), Height),
                _selected ? Theme.TextPrimary : Theme.TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            if (Focused)
                Theme.StrokeRound(g, pill, radius, Theme.Alpha(Theme.TextPrimary, 80), 1f);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Focus(); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>Win11 search field: inset well, magnifier glyph, placeholder.</summary>
    internal sealed class MSearchBox : Control
    {
        private readonly TextBox _inner;
        private bool _focused;
        private string _cue = "搜索";
        private bool _hover;

        public event EventHandler SearchTextChanged;

        public override string Text
        {
            get { return _inner.Text; }
            set { _inner.Text = value; }
        }

        public string CueText
        {
            get { return _cue; }
            set { _cue = value; ApplyCue(); }
        }

        public TextBox Inner { get { return _inner; } }

        public MSearchBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Height = Theme.S(Theme.CtrlHeight);
            BackColor = Theme.Background;
            Cursor = Cursors.IBeam;

            _inner = new TextBox();
            _inner.BorderStyle = BorderStyle.None;
            _inner.Font = Theme.Body;
            _inner.ForeColor = Theme.TextPrimary;
            _inner.BackColor = Theme.Layer;
            _inner.TextChanged += delegate
            {
                EventHandler h = SearchTextChanged;
                if (h != null) h(this, EventArgs.Empty);
            };
            _inner.GotFocus += delegate { _focused = true; Invalidate(); };
            _inner.LostFocus += delegate { _focused = false; Invalidate(); };
            _inner.HandleCreated += delegate { ApplyCue(); };
            Controls.Add(_inner);
        }

        public void ApplyPalette()
        {
            BackColor = Theme.Background;
            _inner.BackColor = Theme.Layer;
            _inner.ForeColor = Theme.TextPrimary;
            ApplyCue();
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyCue();
        }

        private void ApplyCue()
        {
            if (_inner != null && _inner.IsHandleCreated && !string.IsNullOrEmpty(_cue))
                Native.SetCueBanner(_inner, _cue);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_inner == null) return;
            int left = Theme.S(30);
            _inner.SetBounds(left, (Height - _inner.PreferredHeight) / 2,
                Math.Max(Theme.S(20), Width - left - Theme.S(10)), _inner.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            using (SolidBrush b = new SolidBrush(Theme.Background))
                g.FillRectangle(b, ClientRectangle);

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRound(g, r, Theme.SF(Theme.RadiusControl), Theme.Layer);
            Color stroke = _focused ? Theme.Accent : (_hover ? Theme.StrokeStrong : Theme.Stroke);
            Theme.StrokeRound(g, r, Theme.SF(Theme.RadiusControl), stroke, 1f);

            using (Font f = new Font("Segoe MDL2 Assets", 9.5f, FontStyle.Regular, GraphicsUnit.Point))
            {
                TextRenderer.DrawText(g, "\uE721", f,
                    new Rectangle(Theme.S(9), 0, Theme.S(18), Height),
                    _focused ? Theme.TextSecondary : Theme.TextTertiary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { _inner.Focus(); base.OnClick(e); }
    }

    /// <summary>Drop-down that opens a themed menu instead of a native combo box.</summary>
    internal sealed class MComboBox : Control
    {
        private readonly List<string> _items = new List<string>();
        private int _selectedIndex = -1;
        private bool _hover;
        private string _displayText;

        public event EventHandler SelectedIndexChanged;

        public IList<string> Items { get { return _items; } }

        /// <summary>
        /// Text shown when the field is a picker rather than a real drop-down
        /// (the action slot opens its own dialog).
        /// </summary>
        public string DisplayText
        {
            get { return _displayText; }
            set { _displayText = value; Invalidate(); }
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                int index = value;
                if (index < -1) index = -1;
                if (_items.Count == 0) index = -1;
                else if (index >= _items.Count) index = _items.Count - 1;
                if (index == _selectedIndex) return;
                _selectedIndex = index;
                Invalidate();
                EventHandler h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public MComboBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            Height = Theme.S(Theme.CtrlHeight);
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            BackColor = Theme.Background;
            TabStop = true;
        }

        public void ApplyPalette()
        {
            BackColor = Theme.Background;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            using (SolidBrush b = new SolidBrush(Theme.Background))
                g.FillRectangle(b, ClientRectangle);

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRound(g, r, Theme.SF(Theme.RadiusControl),
                _hover ? Theme.SurfaceHover : Theme.Surface);
            Theme.StrokeRound(g, r, Theme.SF(Theme.RadiusControl),
                Focused ? Theme.Accent : Theme.Stroke, 1f);

            string text = _displayText;
            if (text == null)
                text = _selectedIndex >= 0 && _selectedIndex < _items.Count
                    ? _items[_selectedIndex] : "";
            TextRenderer.DrawText(g, text, Font,
                new Rectangle(Theme.S(10), 0, Width - Theme.S(34), Height),
                Enabled ? Theme.TextPrimary : Theme.TextDisabled,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            using (Pen pen = new Pen(Theme.TextSecondary, Theme.SF(1.3f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                float x = Width - Theme.SF(18f);
                float y = Height / 2f - Theme.SF(2f);
                g.DrawLine(pen, x, y, x + Theme.SF(4f), y + Theme.SF(4f));
                g.DrawLine(pen, x + Theme.SF(4f), y + Theme.SF(4f), x + Theme.SF(8f), y);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            Focus();
            base.OnClick(e);
            if (!Enabled || _items.Count == 0) return;
            ShowMenu();
        }

        private void ShowMenu()
        {
            // Same themed popup as the ellipsis menu: a ContextMenuStrip here was
            // the last piece of the old native look.
            MPopupMenu menu = new MPopupMenu();
            for (int i = 0; i < _items.Count; i++)
            {
                int index = i;
                menu.AddItem(_items[i], "", i == _selectedIndex,
                    delegate { SelectedIndex = index; });
            }
            menu.ShowBelow(this, false);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || e.KeyCode == Keys.Down)
            {
                if (!Enabled || _items.Count == 0) return;
                ShowMenu();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>Segmented control for switching sub-sections without scrolling.</summary>
    internal sealed class MSegmented : Control
    {
        private readonly List<string> _items = new List<string>();
        private int _selected;
        private int _hover = -1;
        private bool _hideFocusRing;

        public event EventHandler SelectedIndexChanged;

        public IList<string> Items { get { return _items; } }

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                int index = value;
                if (index < 0) index = 0;
                if (_items.Count > 0 && index >= _items.Count) index = _items.Count - 1;
                if (index == _selected) return;
                _selected = index;
                Invalidate();
                EventHandler h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public MSegmented()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Height = Theme.S(34);
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            TabStop = true;
            BackColor = Theme.Background;
        }

        public void ApplyPalette() { BackColor = Theme.Background; Invalidate(); }

        private Rectangle ItemBounds(int index)
        {
            if (_items.Count == 0) return Rectangle.Empty;
            int w = (Width - 2) / _items.Count;
            return new Rectangle(1 + index * w, 1, w, Height - 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            using (SolidBrush b = new SolidBrush(Theme.Background))
                g.FillRectangle(b, ClientRectangle);

            // Control.Enabled only gates input; owner-drawn controls must also
            // change what they paint, otherwise a disabled segment still looks
            // live and the user cannot tell why clicks do nothing.
            bool enabled = Enabled;
            RectangleF outer = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRound(g, outer, Theme.SF(Theme.RadiusChip), Theme.Layer);
            Theme.StrokeRound(g, outer, Theme.SF(Theme.RadiusChip), Theme.Stroke, 1f);

            for (int i = 0; i < _items.Count; i++)
            {
                Rectangle bounds = ItemBounds(i);
                RectangleF pill = new RectangleF(bounds.X + Theme.SF(1f), bounds.Y + Theme.SF(1f),
                    bounds.Width - Theme.SF(2f), bounds.Height - Theme.SF(2f));
                bool selected = i == _selected;
                if (enabled && selected)
                {
                    Theme.FillRound(g, pill, Theme.SF(Theme.RadiusControl), Theme.Surface);
                    Theme.StrokeRound(g, pill, Theme.SF(Theme.RadiusControl), Theme.Stroke, 1f);
                }
                else if (enabled && i == _hover)
                {
                    Theme.FillRound(g, pill, Theme.SF(Theme.RadiusControl), Theme.SurfaceHover);
                }
                TextRenderer.DrawText(g, _items[i], enabled && selected ? Theme.BodyStrong : Theme.Body, bounds,
                    !enabled ? Theme.TextDisabled
                        : selected ? Theme.TextPrimary : Theme.TextSecondary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }

            if (Focused && !_hideFocusRing)
                Theme.StrokeRound(g, outer, Theme.SF(Theme.RadiusChip), Theme.Alpha(Theme.TextPrimary, 80), 1f);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!Enabled)
            {
                if (_hover != -1) { _hover = -1; Invalidate(); }
                return;
            }
            int index = -1;
            for (int i = 0; i < _items.Count; i++)
                if (ItemBounds(i).Contains(e.Location)) { index = i; break; }
            if (index != _hover) { _hover = index; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            _hideFocusRing = true; // 鼠标点选不画外圈焦点框，避免抢内容视线
            Focus();
            for (int i = 0; i < _items.Count; i++)
                if (ItemBounds(i).Contains(e.Location)) { SelectedIndex = i; break; }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Enabled && _items.Count > 0)
            {
                int target = _selected;
                if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Up) target--;
                else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Down) target++;
                else if (e.KeyCode == Keys.Home) target = 0;
                else if (e.KeyCode == Keys.End) target = _items.Count - 1;

                if (target != _selected)
                {
                    _hideFocusRing = false;
                    SelectedIndex = target;
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { _hideFocusRing = false; Invalidate(); base.OnLostFocus(e); }

        protected override void OnEnabledChanged(EventArgs e)
        {
            _hover = -1;
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
            base.OnEnabledChanged(e);
        }
    }

    /// <summary>Captures a single key press for the mapping recorder.</summary>
    internal sealed class MKeyCapture : Control
    {
        private Keys _key = Keys.None;
        private bool _hover;
        private bool _capsBefore;

        public event EventHandler KeyChanged;

        public Keys KeyCode
        {
            get { return _key; }
            set
            {
                _key = value;
                Invalidate();
                EventHandler h = KeyChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public MKeyCapture()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            Height = Theme.S(36);
            Font = Theme.BodyStrong;
            Cursor = Cursors.Hand;
            BackColor = Theme.Background;
            TabStop = true;
        }

        public void ApplyPalette() { BackColor = Theme.Background; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            using (SolidBrush b = new SolidBrush(Theme.Background))
                g.FillRectangle(b, ClientRectangle);

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRound(g, r, Theme.SF(Theme.RadiusControl),
                _hover || Focused ? Theme.Layer : Theme.Surface);
            Color stroke = Focused ? Theme.Accent : (_hover ? Theme.StrokeStrong : Theme.Stroke);
            Theme.StrokeRound(g, r, Theme.SF(Theme.RadiusControl), stroke, 1f);

            string text = _key == Keys.None ? "点击后按一个键" : KeyText.FormatKey(_key);
            TextRenderer.DrawText(g, text, _key == Keys.None ? Theme.Body : Theme.BodyStrong,
                ClientRectangle, _key == Keys.None ? Theme.TextTertiary : Theme.TextPrimary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void OnEnter(EventArgs e)
        {
            _capsBefore = Control.IsKeyLocked(Keys.CapsLock);
            base.OnEnter(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (KeyText.IsModifier(e.KeyCode) || e.KeyCode == Keys.CapsLock) return;
            KeyCode = e.KeyCode & Keys.KeyCode;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.CapsLock)
            {
                bool now = Control.IsKeyLocked(Keys.CapsLock);
                if (now != _capsBefore) Native.ToggleCapsLock();
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
            base.OnKeyUp(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            // Tab, Escape and the four arrows are dialog navigation keys by
            // default; in the capture box they are the keys the user wants to
            // bind, so they must arrive as input instead of moving focus out.
            if (key == Keys.Tab || key == Keys.Escape || IsNavigationKey(key)) return true;
            return base.IsInputKey(keyData);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // The dialog still cancels on Escape and navigates with the arrows
            // before the key ever reaches OnKeyDown, so take those keys here.
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Escape || IsNavigationKey(key))
            {
                KeyCode = key;
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static bool IsNavigationKey(Keys key)
        {
            return key == Keys.Up || key == Keys.Down || key == Keys.Left || key == Keys.Right;
        }

        /// <summary>
        /// Self-test hook: replays a navigation key through the same path as the
        /// message loop, so the Esc/arrow capture can be tested without a window.
        /// </summary>
        internal bool ProcessNavigationForTest(Keys key)
        {
            Message msg = new Message();
            return ProcessCmdKey(ref msg, key);
        }

        /// <summary>Self-test hook: exposes the input-key decision for dialogs.</summary>
        internal bool IsInputKeyForTest(Keys key)
        {
            return IsInputKey(key);
        }

        /// <summary>
        /// True when a capture box (or a combo field containing one) anywhere under
        /// <paramref name="root"/> has focus, so a dialog can leave Escape/arrows to it.
        /// </summary>
        internal static bool AnyFocused(Control root)
        {
            if (root == null) return false;
            foreach (Control child in root.Controls)
            {
                if ((child is MKeyCapture || child is MKeyComboField) && child.ContainsFocus) return true;
                if (AnyFocused(child)) return true;
            }
            return false;
        }

        protected override void OnKeyPress(KeyPressEventArgs e) { e.Handled = true; base.OnKeyPress(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>Rounded surface used for grouped settings.</summary>
    internal sealed class MCard : Panel
    {
        private readonly List<int> _separators = new List<int>();

        public MCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        /// <summary>
        /// Records a horizontal divider drawn during the card's own paint pass.
        /// Keeping it out of the child collection means no transparent label can
        /// ever paint over it.
        /// </summary>
        public void AddSeparator(int y)
        {
            _separators.Add(y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            using (SolidBrush b = new SolidBrush(Theme.Background))
                g.FillRectangle(b, ClientRectangle);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float radius = Theme.SF(Theme.RadiusCard);
            Theme.FillRound(g, r, radius, Theme.Surface);
            Theme.StrokeRound(g, r, radius, Theme.Stroke, 1f);

            if (_separators.Count > 0)
            {
                using (SolidBrush line = new SolidBrush(Theme.Stroke))
                {
                    for (int i = 0; i < _separators.Count; i++)
                    {
                        int y = _separators[i];
                        if (y <= 0 || y >= Height - 1) continue;
                        g.FillRectangle(line, Theme.S(18), y,
                            Math.Max(0, Width - Theme.S(36)), 1);
                    }
                }
            }
            base.OnPaint(e);
        }
    }

    internal enum MInfoSeverity
    {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>Inline status bar shown at the bottom of the content area.</summary>
    internal sealed class MInfoBar : Control
    {
        private string _message = "";
        private MInfoSeverity _severity = MInfoSeverity.Info;

        public string Message
        {
            get { return _message; }
            set { _message = value ?? ""; Invalidate(); }
        }

        public MInfoSeverity Severity
        {
            get { return _severity; }
            set { _severity = value; Invalidate(); }
        }

        public MInfoBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.Caption;
            Height = Theme.S(34);
            Visible = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (string.IsNullOrEmpty(_message)) return;
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            Color fore = _severity == MInfoSeverity.Error ? Theme.Danger
                : _severity == MInfoSeverity.Warning ? Theme.Warning
                : _severity == MInfoSeverity.Success ? Theme.Success : Theme.TextSecondary;
            Color fill = _severity == MInfoSeverity.Error
                ? (Theme.IsDark ? Theme.DangerSoft : Color.FromArgb(253, 240, 238))
                : _severity == MInfoSeverity.Warning
                    ? Theme.Blend(Theme.Warning, Theme.Background, Theme.IsDark ? 0.80f : 0.88f)
                : _severity == MInfoSeverity.Success
                    ? (Theme.IsDark ? Theme.Blend(Theme.Success, Theme.Background, 0.82f)
                                    : Color.FromArgb(240, 250, 240))
                    : Theme.Layer;

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRound(g, r, Theme.SF(Theme.RadiusChip), fill);
            Theme.StrokeRound(g, r, Theme.SF(Theme.RadiusChip),
                Theme.Alpha(fore, Theme.IsDark ? 70 : 46), 1f);

            using (SolidBrush dot = new SolidBrush(fore))
                g.FillEllipse(dot, Theme.SF(12f), (Height - Theme.SF(6f)) / 2f, Theme.SF(6f), Theme.SF(6f));

            TextRenderer.DrawText(g, _message, Font,
                new Rectangle(Theme.S(26), 0, Width - Theme.S(34), Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Themed menu painting so context menus match the app.</summary>
    internal sealed class MMenuColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
        public override Color MenuItemSelected { get { return Theme.SurfaceHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.SurfaceHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.SurfaceHover; } }
        public override Color MenuItemBorder { get { return Theme.SurfaceHover; } }
        public override Color MenuBorder { get { return Theme.Stroke; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
        public override Color SeparatorDark { get { return Theme.Stroke; } }
        public override Color SeparatorLight { get { return Theme.Stroke; } }
    }

    internal sealed class MMenuRenderer : ToolStripProfessionalRenderer
    {
        public MMenuRenderer() : base(new MMenuColorTable()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.TextPrimary : Theme.TextDisabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush b = new SolidBrush(Theme.Surface))
                e.Graphics.FillRectangle(b, e.AffectedBounds);
        }
    }
}
