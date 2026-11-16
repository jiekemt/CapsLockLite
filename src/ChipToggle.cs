using System;
using System.Drawing;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>Checkable pill used for choosing modifier keys.</summary>
    internal sealed class MChipToggle : Control
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

        public MChipToggle()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            Font = Theme.Body;
            Height = Theme.S(30);
            Cursor = Cursors.Hand;
            BackColor = Color.Transparent;
            TabStop = true;
        }

        public void FitToText()
        {
            Size size = Theme.Measure(Text, Font);
            Width = size.Width + Theme.S(38);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float radius = Theme.SF(Theme.RadiusChip);

            Color fill = _checked ? Theme.AccentSoft : (_hover ? Theme.SurfaceHover : Theme.Surface);
            Color stroke = _checked ? Theme.Accent : (_hover ? Theme.StrokeStrong : Theme.Stroke);
            Theme.FillRound(g, r, radius, fill);
            Theme.StrokeRound(g, r, radius, stroke, 1f);

            float box = Theme.SF(13f);
            float bx = Theme.SF(9f);
            float by = (Height - box) / 2f;
            if (_checked)
            {
                Theme.FillRound(g, new RectangleF(bx, by, box, box), Theme.SF(3f), Theme.Accent);
                using (Pen pen = new Pen(Theme.OnAccent, Theme.SF(1.6f)))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLines(pen, new PointF[]
                    {
                        new PointF(bx + Theme.SF(2.8f), by + Theme.SF(6.6f)),
                        new PointF(bx + Theme.SF(5.4f), by + Theme.SF(9.2f)),
                        new PointF(bx + Theme.SF(9.8f), by + Theme.SF(3.8f))
                    });
                }
            }
            else
            {
                Theme.StrokeRound(g, new RectangleF(bx, by, box, box), Theme.SF(3f), Theme.StrokeStrong, 1f);
            }

            TextRenderer.DrawText(g, Text, _checked ? Theme.BodyStrong : Theme.Body,
                new Rectangle(Theme.S(28), 0, Width - Theme.S(32), Height),
                _checked ? Theme.TextPrimary : Theme.TextSecondary,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            if (Focused)
                Theme.StrokeRound(g, new RectangleF(1.5f, 1.5f, Width - 3f, Height - 3f),
                    radius, Theme.Alpha(Theme.Accent, 140), 1f);
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
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }
}
