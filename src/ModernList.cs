using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>One row of an <see cref="MListView"/>. Rows paint themselves.</summary>
    internal abstract class MListItem
    {
        public abstract void Paint(Graphics g, Rectangle bounds, bool selected, bool hovered, bool enabled);

        /// <summary>Toggle hotspot inside the row, or Rectangle.Empty when absent.</summary>
        public virtual Rectangle ToggleBounds(Rectangle bounds)
        {
            return Rectangle.Empty;
        }

        public virtual string Key
        {
            get { return null; }
        }
    }

    /// <summary>
    /// Owner-drawn, virtualised list with a scrollbar painted *inside this
    /// control's own surface*. No child scrollbar window is created, which keeps
    /// composition and screenshots reliable.
    ///
    /// Multi-select (Ctrl / Shift / Ctrl+A) and drag reordering are opt-in:
    /// pickers and dialogs keep the plain single-selection behaviour.
    /// </summary>
    internal sealed class MListView : Control
    {
        private readonly List<MListItem> _items = new List<MListItem>();
        private readonly MListSelection _selection = new MListSelection();
        private readonly List<float> _offsets = new List<float>();
        private readonly List<float> _targets = new List<float>();
        private readonly Timer _anim;
        private int _rowHeight = Theme.S(Theme.RowHeight);
        private int _headerHeight;
        private int _scroll;
        private int _hover = -1;
        private bool _barHover;
        private bool _barDrag;
        private int _dragGrab;
        private string _emptyText = "没有内容";
        private const int HeaderGap = 4;

        // Drag-reorder state. _pressed covers the whole gesture; _dragging only
        // starts once the pointer has left the click slop, so a plain click still
        // behaves like a click.
        private const int DragThreshold = 4;
        private bool _multiSelect;
        private bool _allowReorder;
        private bool _pressed;
        // Which row asked for a toggle (-1 when the request did not come from a
        // single row). Lets the owner flip one switch without touching the rest
        // of a multi-row selection.
        private int _toggleIndex = -1;
        private int _pressIndex = -1;
        private Point _pressPoint;
        private bool _pressOnToggle;
        private bool _pressCtrl;
        private bool _pressShift;
        private bool _dragging;
        private int _dragFrom;
        private int _dragCount;
        private int _dragSlot;
        private float _dragTop;
        private float _dragTarget;
        private int _grabWithin;
        private Point _pointer;

        // Self-test can drive modifiers and the animation without a message loop.
        private bool _inSimulation;
        private Keys _simulatedModifiers = Keys.None;

        private const int AnimInterval = 15;

        public event EventHandler SelectionChanged;
        public event EventHandler ItemActivated;
        public event EventHandler ItemToggleRequested;
        public event EventHandler ItemDeleteRequested;
        /// <summary>Right click on a row: the owner shows the row's context menu.</summary>
        public event EventHandler ItemContextRequested;
        /// <summary>Right click on the empty area below the rows / header: the owner shows the page menu.</summary>
        public event EventHandler EmptyContextRequested;
        /// <summary>Drag settled and the rows really moved; the owner persists the order.</summary>
        public event EventHandler<MListReorderEventArgs> ItemsReordered;

        /// <summary>Paints the sticky header; set by the owner.</summary>
        public Action<Graphics, Rectangle> HeaderPainter { get; set; }

        public MListView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Surface;
            TabStop = true;
            _anim = new Timer();
            _anim.Interval = AnimInterval;
            _anim.Tick += OnAnimationTick;
        }

        public IList<MListItem> Items { get { return _items; } }

        public int RowHeight
        {
            get { return _rowHeight; }
            set { _rowHeight = Math.Max(Theme.S(24), value); Invalidate(); }
        }

        public int HeaderHeight
        {
            get { return _headerHeight; }
            set { _headerHeight = Math.Max(0, value); Clamp(); Invalidate(); }
        }

        public string EmptyText
        {
            get { return _emptyText; }
            set { _emptyText = value ?? ""; Invalidate(); }
        }

        /// <summary>Ctrl / Shift / Ctrl+A selection. Off by default for pickers.</summary>
        public bool MultiSelect
        {
            get { return _multiSelect; }
            set
            {
                _multiSelect = value;
                if (!value && _selection.SelectedCount > 1)
                {
                    int lead = _selection.Lead;
                    _selection.SelectOnly(lead);
                    RaiseSelection();
                }
            }
        }

        /// <summary>Drag rows (or a contiguous selected block) to reorder them.</summary>
        public bool AllowReorder
        {
            get { return _allowReorder; }
            set
            {
                _allowReorder = value;
                if (!value && _dragging) CancelDrag();
            }
        }

        /// <summary>True while a reorder gesture is in flight (self-test / owner hints).</summary>
        public bool Dragging
        {
            get { return _dragging; }
        }

        public int SelectedIndex
        {
            get { return _selection.Lead; }
            set
            {
                int index = ClampIndex(value);
                if (index < 0) _selection.Clear();
                else _selection.SelectOnly(index);
                EnsureVisible(_selection.Lead);
                RaiseSelection();
            }
        }

        public MListItem SelectedItem
        {
            get
            {
                int lead = _selection.Lead;
                if (lead < 0 || lead >= _items.Count) return null;
                return _items[lead];
            }
        }

        public IList<MListItem> SelectedItems
        {
            get
            {
                List<MListItem> list = new List<MListItem>();
                List<int> indices = _selection.SelectedIndices();
                for (int i = 0; i < indices.Count; i++)
                {
                    int index = indices[i];
                    if (index >= 0 && index < _items.Count) list.Add(_items[index]);
                }
                return list;
            }
        }

        public IList<int> SelectedIndices { get { return _selection.SelectedIndices(); } }

        public int SelectedCount { get { return _selection.SelectedCount; } }

        /// <summary>
        /// Row whose switch was clicked (or the lead row for Space), or -1 when
        /// the toggle request is not tied to one row.
        /// </summary>
        public int ToggleHotspotIndex { get { return _toggleIndex; } }

        public bool IsSelected(int index) { return _selection.IsSelected(index); }

        public void SelectAll()
        {
            if (!_multiSelect) return;
            _selection.SelectAll();
            RaiseSelection();
        }

        public void ClearSelection()
        {
            _selection.Clear();
            RaiseSelection();
        }

        /// <summary>
        /// Re-applies a selection after a refresh rebuilt the rows. Owners keep
        /// the selected identities (mapping id / plugin path) outside the list.
        /// </summary>
        public void ApplySelection(IList<int> indices, int lead)
        {
            _selection.SetSelection(indices, lead);
            EnsureVisible(_selection.Lead);
            RaiseSelection();
        }

        public void SetItems(IEnumerable<MListItem> items)
        {
            StopDrag(true);
            _items.Clear();
            if (items != null) _items.AddRange(items);
            _selection.Reset(_items.Count);
            _offsets.Clear();
            _targets.Clear();
            EnsureOffsetStorage();
            _hover = -1;
            Clamp();
            Invalidate();
            RaiseSelection();
        }

        public void ApplyPalette()
        {
            BackColor = Theme.Surface;
            Invalidate();
        }

        public void ScrollToTop()
        {
            _scroll = 0;
            Invalidate();
        }

        // ---- geometry ------------------------------------------------------
        /// <summary>
        /// First pixel row available to list content: below the sticky header plus a
        /// small safety gap, so a partially scrolled row never touches the titles.
        /// </summary>
        private int RowTop { get { return _headerHeight + Theme.S(HeaderGap); } }

        private int ViewportHeight
        {
            get { return Math.Max(0, Height - RowTop); }
        }

        private int ContentHeight
        {
            get { return _items.Count * _rowHeight; }
        }

        private int MaxScroll
        {
            get { return Math.Max(0, ContentHeight - ViewportHeight); }
        }

        private bool Overflows
        {
            get { return MaxScroll > 0; }
        }

        private void Clamp()
        {
            if (_scroll < 0) _scroll = 0;
            if (_scroll > MaxScroll) _scroll = MaxScroll;
        }

        private int ClampIndex(int index)
        {
            if (_items.Count == 0) return -1;
            if (index < 0) return -1;
            if (index >= _items.Count) return _items.Count - 1;
            return index;
        }

        public void EnsureVisible(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            // Before the first layout the viewport height is zero, so "scroll this
            // row into view" would scroll it one full row past the top and hide
            // row 0. Wait until there is a viewport; resize clamps afterwards.
            if (ViewportHeight <= 0) return;
            int top = index * _rowHeight;
            int bottom = top + _rowHeight;
            if (top < _scroll) _scroll = top;
            else if (bottom > _scroll + ViewportHeight) _scroll = bottom - ViewportHeight;
            Clamp();
        }

        /// <summary>Rows move while a block is being dragged, so the scrollbar follows.</summary>
        private void EnsureVisibleWithOffsets(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            if (ViewportHeight <= 0) return;
            int top = index * _rowHeight + (int)Math.Round(OffsetAt(index));
            int bottom = top + _rowHeight;
            if (top < _scroll) _scroll = top;
            else if (bottom > _scroll + ViewportHeight) _scroll = bottom - ViewportHeight;
            Clamp();
        }

        private float OffsetAt(int index)
        {
            if (index < 0 || index >= _offsets.Count) return 0f;
            return _offsets[index];
        }

        // ---- scrollbar geometry -------------------------------------------
        private int BarTrackWidth { get { return Theme.S(_barHover || _barDrag ? 8 : 5); } }

        private Rectangle BarThumb
        {
            get
            {
                if (!Overflows) return Rectangle.Empty;
                int trackTop = RowTop + Theme.S(4);
                int trackHeight = Math.Max(1, Height - trackTop - Theme.S(4));
                int total = ContentHeight;
                int thumb = Math.Max(Theme.S(28), (int)Math.Round((double)trackHeight * ViewportHeight / total));
                if (thumb > trackHeight) thumb = trackHeight;
                int travel = Math.Max(0, trackHeight - thumb);
                int offset = MaxScroll <= 0 ? 0 : (int)Math.Round((double)travel * _scroll / MaxScroll);
                return new Rectangle(Width - Theme.S(10), trackTop + offset, Theme.S(6), thumb);
            }
        }

        private bool InBarZone(Point p)
        {
            if (!Overflows) return false;
            return p.X >= Width - Theme.S(14);
        }

        // ---- painting ------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);

            using (SolidBrush b = new SolidBrush(Theme.Surface))
                g.FillRectangle(b, ClientRectangle);

            if (_items.Count == 0)
            {
                TextRenderer.DrawText(g, _emptyText, Theme.Body,
                    new Rectangle(0, RowTop, Width, Math.Max(1, ViewportHeight)),
                    Theme.TextTertiary,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPadding |
                    TextFormatFlags.PreserveGraphicsClipping);
                PaintHeader(g);
                PaintScrollBar(g);
                return;
            }

            int maxOffset = 0;
            for (int i = 0; i < _offsets.Count; i++)
            {
                int magnitude = Math.Abs((int)Math.Round(_offsets[i]));
                if (magnitude > maxOffset) maxOffset = magnitude;
            }
            if (_dragging) maxOffset = Math.Max(maxOffset, _rowHeight * _dragCount);

            int first = Math.Max(0, (_scroll - _rowHeight - maxOffset) / _rowHeight);
            int last = Math.Min(_items.Count - 1,
                (_scroll + ViewportHeight + _rowHeight + maxOffset) / _rowHeight);

            // Rows are clipped to the viewport. Text inside them also passes
            // PreserveGraphicsClipping, because TextRenderer ignores the clip by
            // default and would otherwise paint a half-scrolled row over the header.
            Region original = g.Clip;
            g.SetClip(new Rectangle(0, RowTop, Width, ViewportHeight));

            for (int i = first; i <= last; i++)
            {
                if (_dragging && i >= _dragFrom && i < _dragFrom + _dragCount) continue;
                PaintRow(g, i);
            }

            if (_dragging)
            {
                PaintDropMarker(g);
                for (int i = Math.Max(first, _dragFrom); i <= Math.Min(last, _dragFrom + _dragCount - 1); i++)
                    PaintRow(g, i);
                PaintDragOutline(g);
            }

            g.Clip = original;

            // Header is painted last and is fully opaque, so a partially visible
            // row can never bleed over the column titles.
            PaintHeader(g);
            PaintScrollBar(g);
        }

        /// <summary>
        /// Vertical offset of the floating block, clamped to the rows that can
        /// actually be dropped into. Without this the block can be dragged past
        /// the top of the viewport and gets half clipped by the row clip, which
        /// reads as a rendering glitch rather than as a gesture.
        /// </summary>
        private int DragTopVisual()
        {
            float top = _dragTop;
            float max = Math.Max(0f, (_items.Count - _dragCount) * (float)_rowHeight);
            if (top < 0f) top = 0f;
            if (top > max) top = max;
            return (int)Math.Round(top);
        }

        private void PaintRow(Graphics g, int index)
        {
            if (index < 0 || index >= _items.Count) return;
            bool dragging = _dragging && index >= _dragFrom && index < _dragFrom + _dragCount;

            int y;
            if (dragging)
            {
                y = RowTop + DragTopVisual() - _scroll + (index - _dragFrom) * _rowHeight;
            }
            else
            {
                y = RowTop + index * _rowHeight - _scroll + (int)Math.Round(OffsetAt(index));
                Rectangle probe = new Rectangle(0, y, Width, _rowHeight);
                // A row peeking out by less than ~40% of its height reads as leftover
                // debris rather than as scrollable content, so skip those slivers.
                if (probe.Y < RowTop && probe.Bottom - RowTop < _rowHeight * 2 / 5) return;
            }

            Rectangle row = new Rectangle(0, y, Width, _rowHeight);
            if (row.Bottom <= RowTop || row.Y >= Height) return;

            bool selected = _selection.IsSelected(index);
            bool hovered = !_dragging && index == _hover;
            _items[index].Paint(g, row, selected, hovered, true);
        }

        private void PaintDropMarker(Graphics g)
        {
            int to = MListReorder.TargetStart(_dragFrom, _dragCount, _dragSlot);
            if (to == _dragFrom) return;
            int y = RowTop + to * _rowHeight - _scroll;
            if (y < RowTop - 1 || y > Height) return;
            using (SolidBrush b = new SolidBrush(Theme.Accent))
                g.FillRectangle(b, Theme.S(8), y - 1, Math.Max(1, Width - Theme.S(20)), Math.Max(2, Theme.S(2)));
        }

        private void PaintDragOutline(Graphics g)
        {
            int y = RowTop + DragTopVisual() - _scroll;
            Rectangle rect = new Rectangle(Theme.S(4), y + 1,
                Math.Max(1, Width - Theme.S(8)), Math.Max(1, _dragCount * _rowHeight - 2));
            float weight = Math.Max(1f, Theme.SF(1.5f));
            using (Pen pen = new Pen(Theme.Accent, weight))
            {
                pen.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset;
                g.DrawRectangle(pen, rect);
            }
        }

        private void PaintHeader(Graphics g)
        {
            if (_headerHeight <= 0 || HeaderPainter == null) return;
            HeaderPainter(g, new Rectangle(0, 0, Width, _headerHeight));
            using (SolidBrush line = new SolidBrush(Theme.Stroke))
                g.FillRectangle(line, 0, _headerHeight - 1, Width, 1);
        }

        private void PaintScrollBar(Graphics g)
        {
            if (!Overflows) return;
            Rectangle thumb = BarThumb;
            if (thumb.Height <= 0) return;
            Color color = _barDrag || _barHover ? Theme.ScrollThumbHover : Theme.ScrollThumb;
            Theme.FillRound(g, new RectangleF(thumb.X, thumb.Y, thumb.Width, thumb.Height),
                thumb.Width / 2f, color);
        }

        // ---- animation -----------------------------------------------------
        private void EnsureOffsetStorage()
        {
            while (_offsets.Count < _items.Count) _offsets.Add(0f);
            while (_offsets.Count > _items.Count) _offsets.RemoveAt(_offsets.Count - 1);
            while (_targets.Count < _items.Count) _targets.Add(0f);
            while (_targets.Count > _items.Count) _targets.RemoveAt(_targets.Count - 1);
        }

        private void ClearOffsets()
        {
            for (int i = 0; i < _offsets.Count; i++) _offsets[i] = 0f;
            for (int i = 0; i < _targets.Count; i++) _targets[i] = 0f;
        }

        private bool AnimationSettled()
        {
            for (int i = 0; i < _offsets.Count; i++)
            {
                if (Math.Abs(_offsets[i]) > 0.5f) return false;
                if (_targets[i] != 0f) return false;
            }
            return true;
        }

        private void StartAnimation()
        {
            EnsureOffsetStorage();
            if (!_anim.Enabled) _anim.Start();
        }

        private void StopAnimationWhenSettled()
        {
            if (!_dragging && AnimationSettled())
            {
                ClearOffsets();
                _anim.Stop();
            }
        }

        private void OnAnimationTick(object sender, EventArgs e)
        {
            // A drag lives only as long as the left button is down. A lost
            // mouse-up (pointer released outside the window, capture stolen by
            // another app) would otherwise leave the block glued to the cursor.
            if (_dragging && !_inSimulation && (Control.MouseButtons & MouseButtons.Left) == 0)
            {
                CommitDrag();
                return;
            }

            bool changed = false;

            if (_dragging)
            {
                float delta = _dragTarget - _dragTop;
                if (Math.Abs(delta) < 0.75f)
                {
                    if (_dragTop != _dragTarget) { _dragTop = _dragTarget; changed = true; }
                }
                else
                {
                    _dragTop += delta * 0.35f;
                    changed = true;
                }
                AutoScrollForDrag();
                UpdateDragTargets();
            }

            for (int i = 0; i < _offsets.Count; i++)
            {
                if (_dragging && i >= _dragFrom && i < _dragFrom + _dragCount) continue;
                float target = i < _targets.Count ? _targets[i] : 0f;
                float current = _offsets[i];
                if (Math.Abs(target - current) < 0.5f)
                {
                    if (current != target) { _offsets[i] = target; changed = true; }
                }
                else
                {
                    _offsets[i] = current + (target - current) * 0.35f;
                    changed = true;
                }
            }

            if (changed) Invalidate();

            if (!_dragging && AnimationSettled())
            {
                _anim.Stop();
            }
        }

        private void AutoScrollForDrag()
        {
            if (!Overflows) return;
            int edge = _rowHeight;
            int step = Theme.S(6);
            if (_pointer.Y < RowTop + edge) _scroll -= step;
            else if (_pointer.Y > Height - edge) _scroll += step;
            else return;
            Clamp();
            Invalidate();
        }

        /// <summary>Slot the floating block would drop into right now.</summary>
        private void UpdateDragTargets()
        {
            EnsureOffsetStorage();
            _dragTarget = _pointer.Y - RowTop + _scroll - _grabWithin;

            int slot = (int)Math.Round(_dragTarget / (float)_rowHeight);
            // A slot that lands inside the block snaps to the nearer edge, so the
            // rows never shuffle under the pointer while it sits on the block.
            if (slot > _dragFrom && slot < _dragFrom + _dragCount)
                slot = (slot - _dragFrom) * 2 <= _dragCount ? _dragFrom : _dragFrom + _dragCount;
            if (slot < 0) slot = 0;
            if (slot > _items.Count) slot = _items.Count;

            if (slot != _dragSlot)
            {
                _dragSlot = slot;
                RecomputeTargets();
                Invalidate();
            }
        }

        private void RecomputeTargets()
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (i >= _dragFrom && i < _dragFrom + _dragCount) { _targets[i] = 0f; continue; }
                int moved = MListReorder.MoveIndex(i, _dragFrom, _dragCount, _dragSlot);
                _targets[i] = (moved - i) * _rowHeight;
            }
        }

        // ---- interaction ---------------------------------------------------
        private Keys PressedModifiers
        {
            get { return _inSimulation ? _simulatedModifiers : Control.ModifierKeys; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_barDrag)
            {
                Rectangle thumb = BarThumb;
                int trackTop = RowTop + Theme.S(4);
                int trackHeight = Math.Max(1, Height - trackTop - Theme.S(4));
                int travel = Math.Max(1, trackHeight - thumb.Height);
                int y = e.Y - _dragGrab - trackTop;
                int value = (int)Math.Round((double)y * MaxScroll / travel);
                int next = Math.Max(0, Math.Min(MaxScroll, value));
                if (next != _scroll) { _scroll = next; Invalidate(); }
                return;
            }

            if (_dragging)
            {
                _pointer = e.Location;
                StartAnimation();
                return;
            }

            if (_pressed && _pressIndex >= 0 && !_pressOnToggle && _allowReorder
                && Math.Abs(e.Y - _pressPoint.Y) >= DragThreshold)
            {
                StartDrag();
                return;
            }

            bool barHover = InBarZone(e.Location);
            if (barHover != _barHover) { _barHover = barHover; Invalidate(); }

            int index = IndexAt(e.Location);
            if (index != _hover) { _hover = index; Invalidate(); }
            Cursor = barHover ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Invalidate(); }
            if (_barHover && !_barDrag) { _barHover = false; Invalidate(); }
            if (!_dragging) Cursor = Cursors.Default;
        }

        private int IndexAt(Point p)
        {
            if (p.Y < RowTop) return -1;
            if (p.X >= Width - Theme.S(14) && Overflows) return -1;
            int index = (p.Y - RowTop + _scroll) / _rowHeight;
            if (index < 0 || index >= _items.Count) return -1;
            return index;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            // Right click is a menu gesture, never an action: it may select the
            // row under the pointer but must not flip its switch or start a drag.
            if (e.Button == MouseButtons.Right)
            {
                int target = IndexAt(e.Location);
                if (target < 0)
                {
                    EventHandler empty = EmptyContextRequested;
                    if (empty != null) empty(this, EventArgs.Empty);
                    return;
                }
                if (_multiSelect && _selection.IsSelected(target))
                {
                    // Keep the block: the menu acts on every selected row.
                    _selection.SetLead(target);
                    RaiseSelection();
                }
                else
                {
                    SelectedIndex = target;
                }
                EventHandler context = ItemContextRequested;
                if (context != null) context(this, EventArgs.Empty);
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (InBarZone(e.Location) && Overflows)
            {
                Rectangle thumb = BarThumb;
                if (thumb.Contains(e.Location))
                {
                    _barDrag = true;
                    _dragGrab = e.Y - thumb.Y;
                }
                else
                {
                    int trackTop = RowTop + Theme.S(4);
                    int trackHeight = Math.Max(1, Height - trackTop - Theme.S(4));
                    int travel = Math.Max(1, trackHeight - thumb.Height);
                    int y = e.Y - trackTop - thumb.Height / 2;
                    int value = (int)Math.Round((double)y * MaxScroll / travel);
                    _scroll = Math.Max(0, Math.Min(MaxScroll, value));
                    Invalidate();
                }
                return;
            }

            int index = IndexAt(e.Location);
            if (index < 0)
            {
                Keys modifiers = PressedModifiers;
                if ((modifiers & (Keys.Control | Keys.Shift)) == Keys.None) ClearSelection();
                return;
            }

            _pressIndex = index;
            _pressPoint = e.Location;
            _pressed = true;
            _pressCtrl = (PressedModifiers & Keys.Control) != 0;
            _pressShift = (PressedModifiers & Keys.Shift) != 0;
            _pressOnToggle = false;

            Rectangle row = RowBounds(index);
            Rectangle toggle = _items[index].ToggleBounds(row);
            if (!toggle.IsEmpty && toggle.Contains(e.Location))
            {
                // A switch belongs to its own row: keep any larger selection but
                // make this row the lead so the owner toggles the right mapping.
                _pressOnToggle = true;
                if (!_selection.IsSelected(index)) _selection.SelectOnly(index);
                else _selection.SetLead(index);
                RaiseSelection();
                _toggleIndex = index;
                EventHandler t = ItemToggleRequested;
                if (t != null) t(this, EventArgs.Empty);
                _toggleIndex = -1;
                _pressIndex = -1;
                _pressed = false;
                return;
            }

            if (!_multiSelect)
            {
                if (_selection.Lead != index || _selection.SelectedCount != 1)
                    _selection.SelectOnly(index);
                else
                    _selection.SetLead(index);
                RaiseSelection();
                return;
            }

            if (_pressCtrl)
            {
                _selection.Toggle(index);
                RaiseSelection();
            }
            else if (_pressShift)
            {
                _selection.SelectRange(index);
                RaiseSelection();
            }
            else
            {
                if (!_selection.IsSelected(index))
                {
                    _selection.SelectOnly(index);
                    RaiseSelection();
                }
                else
                {
                    // Plain press on a selected row: keep the block so a drag can
                    // pick it up; a press-and-release collapses on mouse-up.
                    _selection.SetLead(index);
                    RaiseSelection();
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_barDrag) { _barDrag = false; Invalidate(); return; }
            if (e.Button != MouseButtons.Left) return;

            if (_dragging)
            {
                // The release point is the drop point: a gesture that ends before
                // the animation tick has to settle its slot from here.
                _pointer = e.Location;
                CommitDrag();
                return;
            }
            if (Capture) Capture = false;

            int pressIndex = _pressIndex;
            bool pressOnToggle = _pressOnToggle;
            bool collapse = _pressed && !_pressCtrl && !_pressShift && !pressOnToggle
                && pressIndex >= 0 && _selection.SelectedCount > 1 && _selection.IsSelected(pressIndex);

            _pressed = false;
            _pressIndex = -1;
            _pressOnToggle = false;

            if (collapse)
            {
                _selection.SelectOnly(pressIndex);
                RaiseSelection();
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            // Only the left button edits. A right double-click is two menu
            // gestures, and it must never open the editor behind the menu.
            if (e.Button != MouseButtons.Left) return;
            int index = IndexAt(e.Location);
            if (index < 0) return;
            SelectedIndex = index;
            EventHandler h = ItemActivated;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            if (_dragging) CommitDrag();
            _pressed = false;
            _pressIndex = -1;
            Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            // Capture can vanish without a mouse-up (another window steals it,
            // the user alt-tabs). The gesture must not outlive the button.
            if (_dragging && !Capture) CommitDrag();
        }

        // ---- drag-reorder --------------------------------------------------
        private Rectangle RowBounds(int index)
        {
            int y = RowTop + index * _rowHeight - _scroll + (int)Math.Round(OffsetAt(index));
            return new Rectangle(0, y, Width, _rowHeight);
        }

        private void StartDrag()
        {
            if (!_allowReorder || _items.Count == 0) return;

            List<int> selected = _selection.SelectedIndices();
            bool blockOk = selected.Count > 0
                && selected[0] <= _pressIndex
                && _pressIndex <= selected[selected.Count - 1]
                && _selection.IsContiguous();
            if (!blockOk)
            {
                _selection.SelectOnly(_pressIndex);
                RaiseSelection();
                selected = _selection.SelectedIndices();
            }

            EnsureOffsetStorage();
            _dragging = true;
            _dragFrom = selected[0];
            _dragCount = selected.Count;

            int pressContentY = _pressPoint.Y - RowTop + _scroll;
            _grabWithin = pressContentY - _dragFrom * _rowHeight;
            int blockHeight = _dragCount * _rowHeight;
            if (_grabWithin < 0) _grabWithin = 0;
            if (_grabWithin > blockHeight - 1) _grabWithin = blockHeight - 1;

            _pointer = _pressPoint;
            _dragTop = _dragFrom * _rowHeight;
            _dragTarget = _dragTop;
            _dragSlot = _dragFrom;
            RecomputeTargets();
            Cursor = Cursors.SizeAll;
            // Keep receiving moves and the button-up even if the pointer leaves
            // the control; the gesture ends the moment the left button is up.
            Capture = true;
            StartAnimation();
            Invalidate();
        }

        private void CommitDrag()
        {
            if (!_dragging) return;
            // A quick flick can end before the first animation tick; settle the
            // slot from the latest pointer position so the drop still lands.
            UpdateDragTargets();
            int from = _dragFrom;
            int count = _dragCount;
            int slot = _dragSlot;

            _dragging = false;
            _pressed = false;
            _pressIndex = -1;
            _pressOnToggle = false;
            Capture = false;
            Cursor = Cursors.Default;
            _dragFrom = -1;
            _dragCount = 0;
            _dragSlot = 0;
            ClearOffsets();
            _anim.Stop();

            int to = MListReorder.TargetStart(from, count, slot);
            if (to >= 0 && to != from && from >= 0 && from + count <= _items.Count)
            {
                MListReorder.Move(_items, from, count, slot);
                List<int> range = new List<int>(count);
                for (int i = 0; i < count; i++) range.Add(to + i);
                _selection.SetSelection(range, to);
                EnsureVisible(to);
                Invalidate();
                RaiseSelection();
                EventHandler<MListReorderEventArgs> h = ItemsReordered;
                if (h != null) h(this, new MListReorderEventArgs(from, count, to));
            }
            Invalidate();
        }

        /// <summary>Aborts a drag; the block glides back instead of jumping.</summary>
        private void CancelDrag()
        {
            if (!_dragging) return;
            _dragging = false;
            _pressed = false;
            _pressIndex = -1;
            _pressOnToggle = false;
            Capture = false;
            Cursor = Cursors.Default;
            _dragFrom = -1;
            _dragCount = 0;
            _dragSlot = 0;
            for (int i = 0; i < _targets.Count; i++) _targets[i] = 0f;
            StartAnimation();
            Invalidate();
        }

        private void StopDrag(bool immediate)
        {
            if (!_dragging) return;
            _dragging = false;
            Cursor = Cursors.Default;
            _dragFrom = -1;
            _dragCount = 0;
            _dragSlot = 0;
            ClearOffsets();
            if (immediate) _anim.Stop();
        }

        /// <summary>Self-test hook: jump the animation to its end state.</summary>
        public void FinishAnimation()
        {
            for (int i = 0; i < _offsets.Count; i++) _offsets[i] = 0f;
            for (int i = 0; i < _targets.Count; i++) _targets[i] = 0f;
            _anim.Stop();
            Invalidate();
        }

        /// <summary>Self-test hook: replays a mouse-down with an explicit button.</summary>
        public void SimulateMouseDown(MouseButtons button, Point location)
        {
            SimulateMouseDown(button, location, Keys.None);
        }

        /// <summary>Self-test hook: mouse-down with simulated modifier keys.</summary>
        public void SimulateMouseDown(MouseButtons button, Point location, Keys modifiers)
        {
            _inSimulation = true;
            _simulatedModifiers = modifiers;
            try
            {
                OnMouseDown(new MouseEventArgs(button, 1, location.X, location.Y, 0));
            }
            finally
            {
                _inSimulation = false;
                _simulatedModifiers = Keys.None;
            }
        }

        /// <summary>Self-test hook: mouse-move without a real cursor.</summary>
        public void SimulateMouseMove(Point location)
        {
            OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, location.X, location.Y, 0));
        }

        /// <summary>Self-test hook: mouse-up without a real cursor.</summary>
        public void SimulateMouseUp(Point location)
        {
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, location.X, location.Y, 0));
        }

        /// <summary>Self-test hook: replays a double-click with an explicit button.</summary>
        public void SimulateDoubleClick(MouseButtons button, Point location)
        {
            OnMouseDoubleClick(new MouseEventArgs(button, 2, location.X, location.Y, 0));
        }

        /// <summary>Self-test hook: replays a key press with explicit modifiers.</summary>
        public void SimulateKeyDown(Keys keyCode, bool control, bool shift)
        {
            OnKeyDown(new KeyEventArgs(keyCode | (control ? Keys.Control : Keys.None) | (shift ? Keys.Shift : Keys.None)));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            HandledMouseEventArgs handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
            if (!Overflows) return;
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines <= 0) lines = 3;
            int delta = lines * _rowHeight;
            _scroll += e.Delta > 0 ? -delta : delta;
            Clamp();
            Invalidate();
            base.OnMouseWheel(e);
        }

        private void MoveLead(int target, bool extend, bool keepOthers)
        {
            _selection.MoveLead(target, extend, keepOthers);
            EnsureVisibleWithOffsets(_selection.Lead);
            RaiseSelection();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (_items.Count == 0) return;

            if (e.Control && e.KeyCode == Keys.A && _multiSelect)
            {
                _selection.SelectAll();
                EnsureVisibleWithOffsets(_selection.Lead);
                RaiseSelection();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Escape && _dragging)
            {
                CancelDrag();
                e.Handled = true;
                return;
            }

            int lead = _selection.Lead;
            int page = Math.Max(1, ViewportHeight / _rowHeight - 1);
            bool extend = e.Shift && _multiSelect;
            bool keepOthers = e.Control && _multiSelect;

            switch (e.KeyCode)
            {
                case Keys.Up:
                    MoveLead(lead <= 0 ? 0 : lead - 1, extend, keepOthers);
                    e.Handled = true;
                    break;
                case Keys.Down:
                    MoveLead(lead < 0 ? 0 : Math.Min(_items.Count - 1, lead + 1), extend, keepOthers);
                    e.Handled = true;
                    break;
                case Keys.PageUp:
                    MoveLead(Math.Max(0, (lead < 0 ? 0 : lead) - page), extend, keepOthers);
                    e.Handled = true;
                    break;
                case Keys.PageDown:
                    MoveLead(lead < 0 ? 0 : Math.Min(_items.Count - 1, lead + page), extend, keepOthers);
                    e.Handled = true;
                    break;
                case Keys.Home:
                    MoveLead(0, extend, keepOthers);
                    e.Handled = true;
                    break;
                case Keys.End:
                    MoveLead(_items.Count - 1, extend, keepOthers);
                    e.Handled = true;
                    break;
                case Keys.Enter:
                    {
                        EventHandler h = ItemActivated;
                        if (h != null) h(this, EventArgs.Empty);
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        break;
                    }
                case Keys.Space:
                    {
                        _toggleIndex = _selection.Lead;
                        EventHandler h = ItemToggleRequested;
                        if (h != null) h(this, EventArgs.Empty);
                        _toggleIndex = -1;
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        break;
                    }
                case Keys.Delete:
                    {
                        EventHandler h = ItemDeleteRequested;
                        if (h != null) h(this, EventArgs.Empty);
                        e.Handled = true;
                        break;
                    }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Clamp();
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

        private void RaiseSelection()
        {
            Invalidate();
            EventHandler h = SelectionChanged;
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _anim != null)
            {
                _anim.Stop();
                _anim.Tick -= OnAnimationTick;
                _anim.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
