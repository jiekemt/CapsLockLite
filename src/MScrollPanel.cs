using System;
using System.Drawing;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// Scroll host for plugin-provided views. Scrolling and the thumb are painted
    /// inside this control's own canvas (never a child window), so the plugin
    /// settings page matches the rest of the application instead of showing a
    /// native scrollbar.
    /// </summary>
    internal sealed class MScrollPanel : Control
    {
        private Control _view;
        private int _scroll;
        private bool _barHover;
        private bool _barDrag;
        private int _dragGrab;

        public MScrollPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable, true);
            BackColor = Theme.Background;
            TabStop = true;
        }

        public Control View { get { return _view; } }

        /// <summary>Replaces the hosted view (null clears it).</summary>
        public void SetView(Control view)
        {
            if (_view != null)
            {
                _view.SizeChanged -= ViewSizeChanged;
                Controls.Remove(_view);
                _view = null;
            }

            _view = view;
            _scroll = 0;

            if (_view != null)
            {
                _view.SizeChanged += ViewSizeChanged;
                Controls.Add(_view);
                AttachWheel(_view);
            }

            LayoutView();
            Invalidate();
            if (_view != null) Focus();
        }

        /// <summary>
        /// The hosted view covers this control, so wheel messages would otherwise
        /// stop at the plugin's own controls. Forward them all to the host.
        /// </summary>
        private void AttachWheel(Control root)
        {
            root.MouseWheel += ViewMouseWheel;
            foreach (Control child in root.Controls) AttachWheel(child);
        }

        private void ViewMouseWheel(object sender, MouseEventArgs e)
        {
            OnMouseWheel(e);
        }

        public void ApplyPalette()
        {
            BackColor = Theme.Background;
            Invalidate();
        }

        private void ViewSizeChanged(object sender, EventArgs e)
        {
            LayoutView();
            Invalidate();
        }

        private int GutterWidth { get { return Theme.S(14); } }

        private int ContentHeight { get { return _view == null ? 0 : _view.Height; } }

        private int MaxScroll { get { return Math.Max(0, ContentHeight - Height); } }

        private bool Overflows { get { return MaxScroll > 0; } }

        private void Clamp()
        {
            if (_scroll < 0) _scroll = 0;
            if (_scroll > MaxScroll) _scroll = MaxScroll;
        }

        private void LayoutView()
        {
            if (_view == null) return;

            int width = Math.Max(Theme.S(120), Width - (Overflows ? GutterWidth : 0));
            if (_view.Width != width) _view.Width = width;

            Clamp();
            _view.Location = new Point(0, -_scroll);
        }

        private Rectangle Thumb
        {
            get
            {
                if (!Overflows) return Rectangle.Empty;

                int trackTop = Theme.S(4);
                int trackHeight = Math.Max(1, Height - Theme.S(8));
                int thumb = Math.Max(Theme.S(28),
                    (int)Math.Round((double)trackHeight * Height / ContentHeight));
                if (thumb > trackHeight) thumb = trackHeight;

                int travel = Math.Max(0, trackHeight - thumb);
                int offset = MaxScroll <= 0 ? 0 : (int)Math.Round((double)travel * _scroll / MaxScroll);
                return new Rectangle(Width - Theme.S(10), trackTop + offset, Theme.S(6), thumb);
            }
        }

        private bool InBarZone(Point p)
        {
            return Overflows && p.X >= Width - GutterWidth;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            using (SolidBrush back = new SolidBrush(Theme.Background))
                g.FillRectangle(back, ClientRectangle);

            if (!Overflows) return;

            Rectangle thumb = Thumb;
            if (thumb.Height <= 0) return;

            Color color = _barDrag || _barHover ? Theme.ScrollThumbHover : Theme.ScrollThumb;
            Theme.FillRound(g, new RectangleF(thumb.X, thumb.Y, thumb.Width, thumb.Height),
                thumb.Width / 2f, color);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutView();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_barDrag)
            {
                Rectangle thumb = Thumb;
                int trackTop = Theme.S(4);
                int trackHeight = Math.Max(1, Height - Theme.S(8));
                int travel = Math.Max(1, trackHeight - thumb.Height);
                int y = e.Y - _dragGrab - trackTop;
                int value = (int)Math.Round((double)y * MaxScroll / travel);
                int next = Math.Max(0, Math.Min(MaxScroll, value));
                if (next != _scroll)
                {
                    _scroll = next;
                    LayoutView();
                    Invalidate();
                }
                return;
            }

            bool hover = InBarZone(e.Location);
            if (hover != _barHover)
            {
                _barHover = hover;
                Invalidate();
            }
            Cursor = hover ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_barHover && !_barDrag)
            {
                _barHover = false;
                Invalidate();
            }
            Cursor = Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!InBarZone(e.Location) || !Overflows) return;

            Rectangle thumb = Thumb;
            if (thumb.Contains(e.Location))
            {
                _barDrag = true;
                _dragGrab = e.Y - thumb.Y;
            }
            else
            {
                int trackTop = Theme.S(4);
                int trackHeight = Math.Max(1, Height - Theme.S(8));
                int travel = Math.Max(1, trackHeight - thumb.Height);
                int y = e.Y - trackTop - thumb.Height / 2;
                _scroll = Math.Max(0, Math.Min(MaxScroll,
                    (int)Math.Round((double)y * MaxScroll / travel)));
                LayoutView();
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_barDrag)
            {
                _barDrag = false;
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            HandledMouseEventArgs handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;

            if (!Overflows) return;

            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines <= 0) lines = 3;

            _scroll -= (e.Delta > 0 ? 1 : -1) * lines * Theme.S(40);
            Clamp();
            LayoutView();
            Invalidate();
            base.OnMouseWheel(e);
        }
    }
}
