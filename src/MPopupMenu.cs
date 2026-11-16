using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// Themed popup menu for the ellipsis button and the drop-downs. A
    /// ContextMenuStrip is a native control with square edges and its own
    /// spacing, which read as a different application next to the rounded cards,
    /// so this menu paints itself with the same palette, radius and type scale.
    /// </summary>
    internal sealed class MPopupMenu : Form
    {
        private sealed class Item
        {
            public string Text;
            public string Shortcut;
            public bool Separator;
            public bool Enabled;
            public bool Checked;
            public EventHandler Click;
        }

        private const int ItemHeight = 34;
        private const int SeparatorHeight = 9;
        private const int PadY = 6;
        private const int PadX = 14;

        private readonly List<Item> _items = new List<Item>();
        private int _hover = -1;
        private int _width = 180;

        public MPopupMenu()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Surface;
            Font = Theme.Body;
            DoubleBuffered = true;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ClassStyle |= 0x00020000;   // CS_DROPSHADOW
                return parameters;
            }
        }

        /// <summary>
        /// Cuts the window itself to the rounded card. Painting a rounded border
        /// is not enough: the window keeps its square corners, and the surface
        /// fill shows them as light right angles over whatever is behind the menu.
        /// </summary>
        private void UpdateWindowShape()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = Theme.Rounded(
                new RectangleF(0f, 0f, Width, Height), Theme.SF(Theme.RadiusCard)))
            {
                Region previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateWindowShape();
        }

        // ---- content ---------------------------------------------------------
        public void AddItem(string text, string shortcut, EventHandler click)
        {
            AddItem(text, shortcut, false, click);
        }

        public void AddItem(string text, string shortcut, bool selected, EventHandler click)
        {
            Item item = new Item();
            item.Text = text == null ? "" : text;
            item.Shortcut = shortcut == null ? "" : shortcut;
            item.Checked = selected;
            item.Enabled = true;
            item.Click = click;
            _items.Add(item);
        }

        /// <summary>
        /// Adds an item that may start greyed out. A disabled item stays visible
        /// (so the menu keeps its shape) but ignores clicks and keyboard.
        /// </summary>
        public void AddItem(string text, string shortcut, EventHandler click, bool enabled)
        {
            AddItem(text, shortcut, false, click);
            _items[_items.Count - 1].Enabled = enabled;
        }

        public void AddSeparator()
        {
            Item item = new Item();
            item.Separator = true;
            _items.Add(item);
        }

        // ---- geometry --------------------------------------------------------
        private void MeasureWidth()
        {
            int textWidth = 0;
            int shortcutWidth = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                Item item = _items[i];
                if (item.Separator) continue;
                Size size = Theme.Measure(item.Text, Theme.BodyStrong);
                if (size.Width > textWidth) textWidth = size.Width;
                if (item.Shortcut.Length > 0)
                {
                    Size key = Theme.Measure(item.Shortcut, Theme.Caption);
                    if (key.Width > shortcutWidth) shortcutWidth = key.Width;
                }
            }

            int width = textWidth + Theme.S(PadX) * 2 + Theme.S(10);
            if (shortcutWidth > 0) width += shortcutWidth + Theme.S(30);
            _width = Math.Max(Theme.S(168), Math.Min(Theme.S(380), width));
        }

        private int ContentHeight()
        {
            int height = Theme.S(PadY) * 2;
            for (int i = 0; i < _items.Count; i++)
                height += _items[i].Separator ? Theme.S(SeparatorHeight) : Theme.S(ItemHeight);
            return height;
        }

        private int ItemTop(int index)
        {
            int y = Theme.S(PadY);
            for (int i = 0; i < index; i++)
                y += _items[i].Separator ? Theme.S(SeparatorHeight) : Theme.S(ItemHeight);
            return y;
        }

        private int IndexAt(int y)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Separator) continue;
                int top = ItemTop(i);
                if (y >= top && y < top + Theme.S(ItemHeight)) return i;
            }
            return -1;
        }

        private int FirstSelectable()
        {
            for (int i = 0; i < _items.Count; i++)
                if (!_items[i].Separator && _items[i].Enabled) return i;
            return -1;
        }

        // ---- show ------------------------------------------------------------
        /// <summary>Opens the menu under a control, right- or left-aligned.</summary>
        public void ShowBelow(Control anchor, bool alignRight)
        {
            if (anchor == null) return;
            MeasureWidth();
            Point origin = anchor.PointToScreen(Point.Empty);
            int x = alignRight ? origin.X + anchor.Width - _width : origin.X;
            int y = origin.Y + anchor.Height + Theme.S(4);
            ShowAtPoint(x, y, anchor.FindForm(), Screen.FromControl(anchor));
        }

        /// <summary>Opens the menu at a screen position (context menu on a key).</summary>
        public void ShowAt(Point screen)
        {
            MeasureWidth();
            ShowAtPoint(screen.X, screen.Y, Form.ActiveForm, Screen.FromPoint(screen));
        }

        private void ShowAtPoint(int x, int y, Form owner, Screen screen)
        {
            if (_items.Count == 0) return;
            int height = ContentHeight();
            Rectangle area = screen.WorkingArea;
            if (x + _width > area.Right - 4) x = area.Right - _width - 4;
            if (y + height > area.Bottom - 4) y = area.Bottom - height - 4;
            if (x < area.Left + 4) x = area.Left + 4;
            if (y < area.Top + 4) y = area.Top + 4;

            SetBounds(x, y, _width, height);
            if (owner != null) Show(owner);
            else Show();
            // The menu can already be closed by the time Show() returns (a click
            // that opens it also dismisses it); touching the handle then would
            // recreate a handle on a disposed form and throw ObjectDisposedException.
            if (IsDisposed || !IsHandleCreated) return;
            Activate();
            _hover = FirstSelectable();
            Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetRoundedCorners(Handle, true);
        }

        // ---- painting ---------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);

            // The outer pixel is the border itself, so the rounded cut of the
            // window and the drawn edge are the same curve: no square sliver can
            // remain at a corner.
            using (SolidBrush border = new SolidBrush(Theme.Stroke))
                g.FillRectangle(border, ClientRectangle);
            Theme.FillRound(g, new RectangleF(1f, 1f,
                Math.Max(1f, Width - 2f), Math.Max(1f, Height - 2f)),
                Math.Max(1f, Theme.SF(Theme.RadiusCard) - 1f), Theme.Surface);

            for (int i = 0; i < _items.Count; i++)
            {
                Item item = _items[i];
                int top = ItemTop(i);

                if (item.Separator)
                {
                    using (SolidBrush line = new SolidBrush(Theme.Stroke))
                        g.FillRectangle(line, Theme.S(PadX), top + Theme.S(SeparatorHeight) / 2,
                            Math.Max(1, Width - Theme.S(PadX) * 2), 1);
                    continue;
                }

                Rectangle bounds = new Rectangle(Theme.S(6), top,
                    Math.Max(1, Width - Theme.S(12)), Theme.S(ItemHeight));
                if (i == _hover && item.Enabled)
                {
                    Theme.FillRound(g, bounds, Theme.SF(Theme.RadiusControl), Theme.SurfaceHover);
                }
                if (item.Checked)
                {
                    Theme.FillRound(g, new RectangleF(bounds.X + Theme.SF(4f),
                        bounds.Y + (bounds.Height - Theme.SF(14f)) / 2f,
                        Theme.SF(3f), Theme.SF(14f)), Theme.SF(1.5f), Theme.Accent);
                }

                int textLeft = bounds.X + Theme.S(PadX);
                int textRight = bounds.Right - Theme.S(PadX);
                int shortcutWidth = 0;
                if (item.Shortcut.Length > 0)
                {
                    shortcutWidth = Theme.Measure(item.Shortcut, Theme.Caption).Width + Theme.S(18);
                    TextRenderer.DrawText(g, item.Shortcut, Theme.Caption,
                        new Rectangle(textRight - shortcutWidth, bounds.Y, shortcutWidth, bounds.Height),
                        Theme.TextTertiary,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.Right |
                        TextFormatFlags.NoPadding);
                }

                int width = Math.Max(1, textRight - shortcutWidth - textLeft);
                TextRenderer.DrawText(g, item.Text, item.Checked ? Theme.BodyStrong : Theme.Body,
                    new Rectangle(textLeft, bounds.Y, width, bounds.Height),
                    item.Enabled ? Theme.TextPrimary : Theme.TextDisabled,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
        }

        // ---- interaction -------------------------------------------------------
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexAt(e.Y);
            if (index == _hover) return;
            _hover = index;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                Close();
                return;
            }
            int index = IndexAt(e.Y);
            if (index >= 0) Choose(index);
        }

        private void Choose(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            Item item = _items[index];
            if (item.Separator || !item.Enabled) return;

            EventHandler handler = item.Click;
            Close();
            if (handler != null) handler(this, EventArgs.Empty);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }
            if (keyData == Keys.Up)
            {
                MoveHover(-1);
                return true;
            }
            if (keyData == Keys.Down)
            {
                MoveHover(1);
                return true;
            }
            if (keyData == Keys.Enter || keyData == Keys.Space)
            {
                Choose(_hover);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void MoveHover(int direction)
        {
            if (_items.Count == 0) return;
            int index = _hover;
            for (int step = 0; step < _items.Count; step++)
            {
                index += direction;
                if (index < 0) index = _items.Count - 1;
                if (index >= _items.Count) index = 0;
                if (!_items[index].Separator && _items[index].Enabled)
                {
                    _hover = index;
                    Invalidate();
                    return;
                }
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            Dispose();
        }
    }
}
