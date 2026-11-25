using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// The mouse card next to the shortcut list. Five hit zones sit on a
    /// straight-edged mouse outline and carry exactly the same states and
    /// interactions as a keycap: neutral / used / filtered / disabled / selected /
    /// hover, click to filter, double-click to edit, right-click menu and
    /// drag-and-drop in both directions.
    ///
    /// All state colours, the disabled hatch and the count badge come from
    /// <see cref="KeyVisual"/>, so the two surfaces cannot drift apart. Geometry is
    /// expressed in the 150 x 210 unit space of the mockup and scaled to the card.
    /// A zone is a cell: the visible key is the cell inset by the board's own key
    /// gap, with the board's corner radius and 3D depth, so a mouse button and a
    /// keycap are built by the same rules.
    /// </summary>
    internal sealed class MMousePad : Control
    {
        private const float BodyWidth = 150f;
        private const float BodyHeight = 210f;
        private const int DragThreshold = 4;
        private const int DoubleClickMs = 400;

        /// <summary>Left edge of the wheel column, in the 150 x 210 design space.</summary>
        private const float WheelLeft = 56f;
        /// <summary>Right edge of the wheel column.</summary>
        private const float WheelRight = 94f;
        /// <summary>Bottom edge the two buttons and the wheel column share.</summary>
        private const float ClusterBottom = 114f;

        private static readonly PointF[] BodyOutline =
        {
            new PointF(36f, 0f), new PointF(114f, 0f), new PointF(146f, 28f),
            new PointF(150f, 74f), new PointF(150f, 152f), new PointF(130f, 198f),
            new PointF(110f, 210f), new PointF(40f, 210f), new PointF(20f, 198f),
            new PointF(0f, 152f), new PointF(0f, 74f), new PointF(4f, 28f)
        };

        private static readonly Keys[] ZoneOrder =
        {
            (Keys)MouseKeys.Left, (Keys)MouseKeys.Right, (Keys)MouseKeys.Middle,
            (Keys)MouseKeys.WheelUp, (Keys)MouseKeys.WheelDown
        };

        private sealed class ZoneState
        {
            public int Count;
            public int ExactCount;
            public int ExactEnabled;
            public bool EnabledBound;
            public bool DisabledOnly;
            public string Action;
        }

        private readonly GraphicsPath _bodyPath = new GraphicsPath();
        private readonly GraphicsPath[] _facePaths =
        {
            new GraphicsPath(), new GraphicsPath(), new GraphicsPath(),
            new GraphicsPath(), new GraphicsPath()
        };
        private readonly GraphicsPath[] _shadowPaths =
        {
            new GraphicsPath(), new GraphicsPath(), new GraphicsPath(),
            new GraphicsPath(), new GraphicsPath()
        };

        private List<KeyMapping> _mappings = new List<KeyMapping>();
        private bool _filterCaps;
        private bool _filterCtrl;
        private bool _filterAlt;
        private bool _filterShift;
        private bool _filterWin;
        private Keys _selected = Keys.None;
        private Keys _hover = Keys.None;
        private Keys _dropTarget = Keys.None;
        private float _collapse;
        private int _expandedWidth;
        private bool _hoverChevron;
        private float _unit = 1f;
        private float _gap = 1f;
        private int _headerH = 26;
        private Font _badgeFont;
        private float _badgeFontUnit;
        private float _badgeUnit;
        private Font _labelFont;
        private Font _subFont;
        private Font _tinyFont;
        private Font[] _actionFonts;
        private int[] _actionLines;
        private float _textUnit;
        private readonly PointF[][] _facePoints = new PointF[ZoneOrder.Length][];
        private readonly RectangleF[] _faceBounds = new RectangleF[ZoneOrder.Length];
        private readonly float[] _faceDepth = new float[ZoneOrder.Length];
        private readonly float[] _radii = new float[ZoneOrder.Length];
        private Rectangle _chevronHit;
        private int _mouseCount;
        private int _geomWidth;
        private int _geomHeight;
        private int _geomLayoutWidth;
        private float _geomKeyUnit = -1f;

        // drag state (this card is the source)
        private bool _dragArmed;
        private bool _dragActive;
        private bool _chevronDown;
        private Keys _pressKey = Keys.None;
        private Keys _dragFromKey = Keys.None;
        private Keys _dragTarget = Keys.None;
        private Point _pressPoint;
        private PointF _dragPoint;
        /// <summary>Where inside the zone's face the drag was grabbed.</summary>
        private PointF _dragGrab;
        private Rectangle _dragDirty;
        private bool _dragDirtyValid;
        private Keys _lastClickKey = Keys.None;
        private int _lastClickTick;

        // drag state (the keyboard is the source and passes its card here)
        private bool _externalActive;
        private PointF[] _externalShape;
        private string _externalAction = "";
        private Rectangle _externalText = Rectangle.Empty;
        private float _externalFontSize;
        private Point _externalPoint;

        public event Action<Keys> ZoneClicked;
        public event Action<Keys> ZoneActivated;
        public event Action<Keys> ZoneContextRequested;
        public event Action<Keys, Keys> ZoneDropped;
        public Func<Point, Keys> ExternalDropTarget;
        public event Action<Keys> ExternalTargetChanged;
        /// <summary>Raised on every drag move: the lifted card in screen space, the pointer and whether it left the card.</summary>
        public event Action<DragGhostCard, Point, bool> DragPointerMoved;
        /// <summary>Raised when a drag ends, so the other surface can drop its card.</summary>
        public event Action DragFinished;
        /// <summary>Raised by the chevron: true = collapse. The page runs the animation.</summary>
        public event Action<bool> CollapseRequested;

        public MMousePad()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Background;
            TabStop = false;
            Height = Theme.S(300);
        }

        /// <summary>
        /// 0 = fully open, 1 = collapsed to the rail. Animated by the page the same
        /// way the board is: the card narrows while the mouse slides out to the right
        /// (it never squashes) and the rail chevron/title fade in.
        /// </summary>
        public float CollapseProgress
        {
            get { return _collapse; }
            set
            {
                float next = value < 0f ? 0f : (value > 1f ? 1f : value);
                if (Math.Abs(_collapse - next) < 0.0005f) return;
                _collapse = next;
                Invalidate();
            }
        }

        public bool Collapsed
        {
            get { return _collapse >= 0.999f; }
            set { CollapseProgress = value ? 1f : 0f; }
        }

        /// <summary>
        /// Width the card has when it is open. The body is laid out for this width and
        /// simply slides right as the card narrows, so collapsing never rescales it.
        /// 0 means "the current width".
        /// </summary>
        public int ExpandedWidth
        {
            get { return _expandedWidth; }
            set
            {
                if (_expandedWidth == value) return;
                _expandedWidth = value;
                Invalidate();
            }
        }

        public Keys SelectedKey
        {
            get { return _selected; }
            set
            {
                if (_selected == value) return;
                _selected = value;
                Invalidate();
            }
        }

        /// <summary>
        /// Device pixels per key unit of the board above. Badge size, hatch step and
        /// the zone label sizes all derive from it, so the mouse always paints on
        /// the same scale as the keycaps.
        /// </summary>
        public float BadgeUnit
        {
            get { return _badgeUnit; }
            set
            {
                if (Math.Abs(_badgeUnit - value) < 0.01f) return;
                _badgeUnit = value;
                Invalidate();
            }
        }

        public void SetDropTarget(Keys key)
        {
            if (_dropTarget == key) return;
            Keys before = _dropTarget;
            _dropTarget = key;
            InvalidateZones(before, key);
        }

        /// <summary>Repaints only the two zones a highlight moves between.</summary>
        private void InvalidateZones(Keys from, Keys to)
        {
            EnsureGeometry();
            Rectangle band = Rectangle.Empty;
            int first = IndexOfZone(from);
            if (first >= 0 && !_faceBounds[first].IsEmpty) band = Rectangle.Round(_faceBounds[first]);
            int second = IndexOfZone(to);
            if (second >= 0 && !_faceBounds[second].IsEmpty)
            {
                Rectangle next = Rectangle.Round(_faceBounds[second]);
                band = band.IsEmpty ? next : Rectangle.Union(band, next);
            }
            if (band.Width > 0 && band.Height > 0)
            {
                band.Inflate(4, 8);
                Invalidate(band);
            }
        }

        /// <summary>
        /// Draws the lifted card while a keycap is dragged over this card. The page
        /// forwards the keyboard's drag so both directions show the same feedback.
        /// </summary>
        public void SetExternalDrag(DragGhostCard card, Point screenPoint, bool outside)
        {
            PointF[] shapeScreen = card == null ? null : card.Shape;
            // This card owns the feedback only while the pointer is inside it; past
            // the edge the surface under the pointer takes over.
            bool over = shapeScreen != null && shapeScreen.Length >= 3 && !screenPoint.IsEmpty;
            Point local = screenPoint.IsEmpty ? Point.Empty : PointToClient(screenPoint);
            bool active = over && ClientRectangle.Contains(local);
            if (!active)
            {
                // Nothing is painted here any more: repaint once to erase the card,
                // then stay silent. A drag elsewhere must not cost this card a
                // frame per mouse move.
                if (!_externalActive && _externalShape == null) return;
                ClearExternalDrag();
                return;
            }

            Rectangle before = _externalShape == null ? Rectangle.Empty : BandOf(_externalShape);
            _externalActive = true;
            _externalAction = card.Action ?? "";
            _externalFontSize = card.FontSize;
            _externalPoint = local;
            Point origin = PointToScreen(Point.Empty);
            _externalShape = new PointF[shapeScreen.Length];
            for (int i = 0; i < shapeScreen.Length; i++)
                _externalShape[i] = new PointF(shapeScreen[i].X - origin.X, shapeScreen[i].Y - origin.Y);
            // The action line travels with the card, in the same coordinates.
            _externalText = card.Text.Width > 0 && card.Text.Height > 0
                ? new Rectangle(card.Text.X - origin.X, card.Text.Y - origin.Y,
                    card.Text.Width, card.Text.Height)
                : Rectangle.Empty;

            // The card stays whole on the surface painting it: on the board the rule
            // is the same, so a card handed over arrives clamped at this edge instead
            // of half outside it.
            Point shift = GhostPlacement.ClampOffset(KeyVisual.GhostBounds(_externalShape),
                ClientSize);
            if (shift.X != 0 || shift.Y != 0)
            {
                for (int i = 0; i < _externalShape.Length; i++)
                    _externalShape[i] = new PointF(_externalShape[i].X + shift.X,
                        _externalShape[i].Y + shift.Y);
                if (_externalText.Width > 0) _externalText.Offset(shift.X, shift.Y);
            }

            Rectangle after = BandOf(_externalShape);
            Rectangle band = before.IsEmpty ? after : (after.IsEmpty ? before
                : Rectangle.Union(before, after));
            if (band.Width > 0 && band.Height > 0) Invalidate(band);
            else Invalidate();
        }

        /// <summary>Drops the card the keyboard handed over, at once.</summary>
        private void ClearExternalDrag()
        {
            if (!_externalActive && _externalShape == null) return;
            Rectangle band = _externalShape == null ? Rectangle.Empty : BandOf(_externalShape);
            _externalActive = false;
            _externalShape = null;
            _externalAction = "";
            _externalText = Rectangle.Empty;
            _externalFontSize = 0f;
            if (band.Width > 0 && band.Height > 0) Invalidate(band);
            else Invalidate();
        }

        public void SetMappings(IList<KeyMapping> mappings)
        {
            _mappings = mappings == null ? new List<KeyMapping>() : new List<KeyMapping>(mappings);
            _mouseCount = 0;
            foreach (KeyMapping mapping in _mappings)
            {
                if (mapping == null) continue;
                if (MouseKeys.IsMouseKey((Keys)mapping.Key)) _mouseCount++;
            }
            Invalidate();
        }

        public void SetFilter(bool caps, bool ctrl, bool alt, bool shift, bool win)
        {
            if (_filterCaps == caps && _filterCtrl == ctrl && _filterAlt == alt
                && _filterShift == shift && _filterWin == win) return;
            _filterCaps = caps;
            _filterCtrl = ctrl;
            _filterAlt = alt;
            _filterShift = shift;
            _filterWin = win;
            Invalidate();
        }

        private bool HasFilter
        {
            get
            {
                return KeyboardFilter.HasSelection(_filterCaps, _filterCtrl, _filterAlt,
                    _filterShift, _filterWin);
            }
        }

        private float KeyUnit
        {
            get { return _badgeUnit > 1f ? _badgeUnit : Math.Max(1f, Width / 4f); }
        }

        private static string ZoneName(Keys key)
        {
            if (key == (Keys)MouseKeys.Left) return "左键";
            if (key == (Keys)MouseKeys.Right) return "右键";
            if (key == (Keys)MouseKeys.Middle) return "中键";
            if (key == (Keys)MouseKeys.WheelUp) return "上滑";
            if (key == (Keys)MouseKeys.WheelDown) return "下滑";
            return KeyText.FormatKey(key);
        }

        // ---- geometry -------------------------------------------------------
        /// <summary>
        /// Builds the frame and the five keys on the board's own rules: the gap is
        /// <c>KeyGapUnit</c> of a key unit on every side of every cell, the corner
        /// radius is the cap radius formula, and the 3D depth is the cap depth. Only
        /// the cell shapes differ (the two buttons follow the mouse contour), so the
        /// spacing reads as one system with the board above.
        /// </summary>
        private void EnsureGeometry()
        {
            float keyUnit = KeyUnit;
            int layoutWidth = _expandedWidth > 1 ? _expandedWidth : Width;
            if (_geomWidth == Width && _geomHeight == Height && _geomLayoutWidth == layoutWidth
                && Math.Abs(_geomKeyUnit - keyUnit) < 0.01f) return;
            _geomWidth = Width;
            _geomHeight = Height;
            _geomLayoutWidth = layoutWidth;
            _geomKeyUnit = keyUnit;

            int pad = Theme.S(12);
            _headerH = Math.Max(Theme.S(26), Theme.LineHeight(Theme.BodyStrong) + Theme.S(8));
            int areaW = Math.Max(1, layoutWidth - pad * 2);
            int areaH = Math.Max(1, Height - pad * 2 - _headerH);

            _unit = Math.Max(0.2f, Math.Min(areaW / BodyWidth, areaH / BodyHeight));
            // The body keeps the open-card layout and stays anchored to the card's
            // leading edge: as the card narrows it is clipped by the far edge instead
            // of being rescaled, which is the horizontal form of the board being
            // revealed inside a shorter host.
            float ox = pad + (areaW - BodyWidth * _unit) / 2f;
            float oy = pad + _headerH + (areaH - BodyHeight * _unit) / 2f;

            // The same gap the board puts on each side of a cap.
            _gap = Math.Max(1f, keyUnit * KeyGapUnit);
            float g = _gap;

            PointF[] body = ScalePoints(BodyOutline, ox, oy);
            BuildRounded(_bodyPath, body, CornerRadius(BodyHeight * _unit));

            // Every zone is a cell like a cap cell on the board: the cell boundary is
            // the frame contour (or the seam between two zones) and the visible key is
            // the cell inset by the board's key gap. The frame keeps two gaps, i.e.
            // exactly the distance between two neighbouring keycaps, so the margin and
            // the seams read as one spacing rule.
            PointF[] inner = Inset(body, g * 2f);
            float xLeft = ox + WheelLeft * _unit;
            float xRight = ox + WheelRight * _unit;
            float bottom = oy + ClusterBottom * _unit;

            PointF[] left = ClipHalf(ClipHalf(inner, false, xLeft - g, true), true, bottom - g, true);
            PointF[] right = ClipHalf(ClipHalf(inner, false, xRight + g, false), true, bottom - g, true);

            BuildZone(0, left);
            BuildZone(1, right);

            // The wheel column keeps three stacked cells with no gap between them:
            // the seam comes from the same gap inset as everywhere else, so the
            // vertical rhythm is the board's, not a hand-picked offset. Its cells
            // start one gap inside the frame, which puts the visible key two gaps
            // away from the frame like the two buttons.
            float cellHeight = (bottom - oy - g) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float y = oy + g + i * cellHeight + g;
                float height = cellHeight - g * 2f;
                if (height < 1f) height = 1f;
                RectangleF rect = new RectangleF(xLeft + g, y, xRight - xLeft - g * 2f, height);
                PointF[] cell =
                {
                    new PointF(rect.Left, rect.Top), new PointF(rect.Right, rect.Top),
                    new PointF(rect.Right, rect.Bottom), new PointF(rect.Left, rect.Bottom)
                };
                // ZoneOrder is left, right, middle, wheelUp, wheelDown while the
                // wheel rows are up, middle, down.
                BuildZone(WheelZone[i], cell);
            }

            _chevronHit = new Rectangle(
                Collapsed ? (Width - Theme.S(24)) / 2 : Math.Max(0, Width - Theme.S(30)),
                Theme.S(8), Theme.S(24), Theme.S(24));
        }

        /// <summary>Wheel rows in drawing order (up, middle, down) as ZoneOrder indexes.</summary>
        private static readonly int[] WheelZone = { 3, 2, 4 };

        /// <summary>
        /// Turns one cell polygon into a key: the visible face is the cell inset by
        /// the key gap and rounded like a cap, the 3D bottom is the same face pushed
        /// down by the cap depth.
        /// </summary>
        private void BuildZone(int index, PointF[] cell)
        {
            if (cell == null || cell.Length < 3) return;
            RectangleF bounds = BoundsOf(cell);
            float height = Math.Max(1f, bounds.Height);
            float depth = Math.Min(height * 0.10f, Math.Max(1.5f, KeyUnit * 0.06f));

            // Top face: same polygon with the bottom edge raised by the depth, so the
            // depth band sits outside the face and never on top of it.
            PointF[] top = new PointF[cell.Length];
            float cut = bounds.Bottom - depth;
            for (int i = 0; i < cell.Length; i++)
                top[i] = new PointF(cell[i].X, Math.Min(cell[i].Y, cut));

            BuildRounded(_facePaths[index], top, CornerRadius(height));
            _facePoints[index] = top;
            _faceBounds[index] = BoundsOf(top);
            _faceDepth[index] = depth;
            _radii[index] = CornerRadius(height);

            using (GraphicsPath shadow = (GraphicsPath)_facePaths[index].Clone())
            {
                using (Matrix move = new Matrix())
                {
                    move.Translate(0f, depth);
                    shadow.Transform(move);
                }
                _shadowPaths[index].Reset();
                _shadowPaths[index].AddPath(shadow, false);
            }
        }

        /// <summary>Key gap as a fraction of a key unit, exactly the board's value.</summary>
        private const float KeyGapUnit = 0.03f;

        /// <summary>Corner radius of a face, exactly the board's cap formula.</summary>
        private static float CornerRadius(float height)
        {
            return Math.Min(Theme.SF(4f), Math.Max(2f, height * 0.18f));
        }

        /// <summary>
        /// Rounded polygon: every corner is replaced by a quadratic arc of at most
        /// <paramref name="radius"/>, so a contour-following button gets the same
        /// rounded corners as a square cap.
        /// </summary>
        private static void BuildRounded(GraphicsPath path, PointF[] points, float radius)
        {
            path.Reset();
            if (points == null || points.Length < 3) return;

            List<PointF> clean = new List<PointF>(points.Length);
            for (int i = 0; i < points.Length; i++)
            {
                PointF current = points[i];
                if (clean.Count > 0)
                {
                    PointF last = clean[clean.Count - 1];
                    if (Math.Abs(last.X - current.X) < 0.01f
                        && Math.Abs(last.Y - current.Y) < 0.01f) continue;
                }
                clean.Add(current);
            }
            while (clean.Count > 2)
            {
                PointF first = clean[0];
                PointF last = clean[clean.Count - 1];
                if (Math.Abs(first.X - last.X) < 0.01f
                    && Math.Abs(first.Y - last.Y) < 0.01f) clean.RemoveAt(clean.Count - 1);
                else break;
            }

            int n = clean.Count;
            if (n < 3)
            {
                if (clean.Count > 0) path.AddPolygon(clean.ToArray());
                return;
            }
            if (radius < 0.5f)
            {
                path.AddPolygon(clean.ToArray());
                return;
            }

            PointF[] from = new PointF[n];
            PointF[] to = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                PointF previous = clean[(i + n - 1) % n];
                PointF current = clean[i];
                PointF next = clean[(i + 1) % n];
                float inLength = Dist(previous, current);
                float outLength = Dist(current, next);
                if (inLength < 0.01f || outLength < 0.01f)
                {
                    from[i] = current;
                    to[i] = current;
                    continue;
                }
                float r = Math.Min(radius, Math.Min(inLength, outLength) * 0.5f);
                from[i] = Lerp(current, previous, r / inLength);
                to[i] = Lerp(current, next, r / outLength);
            }

            path.StartFigure();
            path.AddLine(to[n - 1], from[0]);
            for (int i = 0; i < n; i++)
            {
                PointF corner = clean[i];
                path.AddBezier(from[i], corner, corner, to[i]);
                path.AddLine(to[i], from[(i + 1) % n]);
            }
            path.CloseFigure();
        }

        /// <summary>Moves every edge of a convex polygon inward by <paramref name="distance"/>.</summary>
        private static PointF[] Inset(PointF[] polygon, float distance)
        {
            int n = polygon.Length;
            if (n < 3 || distance <= 0f) return (PointF[])polygon.Clone();

            double area = 0.0;
            for (int i = 0; i < n; i++)
            {
                PointF a = polygon[i];
                PointF b = polygon[(i + 1) % n];
                area += (double)a.X * b.Y - (double)b.X * a.Y;
            }
            float sign = area >= 0 ? 1f : -1f;

            PointF[] result = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                PointF previous = polygon[(i + n - 1) % n];
                PointF current = polygon[i];
                PointF next = polygon[(i + 1) % n];
                PointF first = InwardNormal(previous, current, sign);
                PointF second = InwardNormal(current, next, sign);
                float mx = first.X + second.X;
                float my = first.Y + second.Y;
                float length = (float)Math.Sqrt(mx * mx + my * my);
                if (length < 0.0001f)
                {
                    result[i] = current;
                    continue;
                }
                mx /= length;
                my /= length;
                float cosine = first.X * mx + first.Y * my;
                if (cosine < 0.25f) cosine = 0.25f;
                float scale = distance / cosine;
                result[i] = new PointF(current.X + mx * scale, current.Y + my * scale);
            }
            return result;
        }

        private static PointF InwardNormal(PointF from, PointF to, float sign)
        {
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.0001f) return new PointF(0f, 0f);
            dx /= length;
            dy /= length;
            return new PointF(-dy * sign, dx * sign);
        }

        private static float Dist(PointF a, PointF b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static PointF Lerp(PointF a, PointF b, float t)
        {
            return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        /// <summary>Sutherland–Hodgman clip against one axis-aligned half-plane.</summary>
        private static PointF[] ClipHalf(PointF[] polygon, bool horizontal, float value, bool keepLow)
        {
            if (polygon == null || polygon.Length < 3) return polygon;
            List<PointF> output = new List<PointF>(polygon.Length + 2);
            for (int i = 0; i < polygon.Length; i++)
            {
                PointF current = polygon[i];
                PointF next = polygon[(i + 1) % polygon.Length];
                float currentValue = horizontal ? current.Y : current.X;
                float nextValue = horizontal ? next.Y : next.X;
                bool currentIn = keepLow ? currentValue <= value : currentValue >= value;
                bool nextIn = keepLow ? nextValue <= value : nextValue >= value;
                if (currentIn) output.Add(current);
                if (currentIn != nextIn)
                {
                    float delta = nextValue - currentValue;
                    float t = Math.Abs(delta) < 0.0001f ? 0f : (value - currentValue) / delta;
                    output.Add(new PointF(current.X + (next.X - current.X) * t,
                        current.Y + (next.Y - current.Y) * t));
                }
            }
            return output.Count >= 3 ? output.ToArray() : polygon;
        }

        private PointF[] ScalePoints(PointF[] points, float ox, float oy)
        {
            PointF[] scaled = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++)
                scaled[i] = new PointF(ox + points[i].X * _unit, oy + points[i].Y * _unit);
            return scaled;
        }

        private static RectangleF BoundsOf(PointF[] points)
        {
            if (points == null || points.Length == 0) return RectangleF.Empty;
            float minX = points[0].X, maxX = points[0].X, minY = points[0].Y, maxY = points[0].Y;
            for (int i = 1; i < points.Length; i++)
            {
                if (points[i].X < minX) minX = points[i].X;
                if (points[i].X > maxX) maxX = points[i].X;
                if (points[i].Y < minY) minY = points[i].Y;
                if (points[i].Y > maxY) maxY = points[i].Y;
            }
            return new RectangleF(minX, minY, Math.Max(1f, maxX - minX), Math.Max(1f, maxY - minY));
        }

        private static PointF[] Translate(PointF[] points, float dx, float dy)
        {
            PointF[] moved = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++)
                moved[i] = new PointF(points[i].X + dx, points[i].Y + dy);
            return moved;
        }

        private static Rectangle BandOf(PointF[] points)
        {
            RectangleF bounds = BoundsOf(points);
            if (bounds.Width <= 0f || bounds.Height <= 0f) return Rectangle.Empty;
            bounds.Inflate(4f, 6f);
            return Rectangle.Round(bounds);
        }

        private static int IndexOfZone(Keys key)
        {
            for (int i = 0; i < ZoneOrder.Length; i++)
                if (ZoneOrder[i] == key) return i;
            return -1;
        }

        /// <summary>Self-test hook: hit test in control coordinates.</summary>
        internal Keys HitTestForTest(Point point)
        {
            EnsureGeometry();
            return HitTest(point);
        }

        /// <summary>Self-test hook: device bounds of one zone.</summary>
        internal RectangleF ZoneBoundsForTest(Keys key)
        {
            EnsureGeometry();
            int index = IndexOfZone(key);
            return index < 0 ? RectangleF.Empty : _faceBounds[index];
        }

        /// <summary>Self-test hook: the gap the card puts on each side of a zone.</summary>
        internal float GapForTest
        {
            get { EnsureGeometry(); return _gap; }
        }

        /// <summary>Self-test hook: 3D depth of one zone.</summary>
        internal float DepthForTest(Keys key)
        {
            EnsureGeometry();
            int index = IndexOfZone(key);
            return index < 0 ? 0f : _faceDepth[index];
        }

        /// <summary>Self-test hook: bounds of the drawn frame outline.</summary>
        internal RectangleF FrameBoundsForTest()
        {
            EnsureGeometry();
            return _bodyPath.GetBounds();
        }

        /// <summary>
        /// Harness hook: where the lifted card sits for a pointer at
        /// <paramref name="pointer"/>, clamped the way it is painted.
        /// </summary>
        internal RectangleF GhostRectForTest(Point pointer)
        {
            EnsureGeometry();
            int index = IndexOfZone(_dragFromKey);
            if (index < 0 || _faceBounds[index].IsEmpty) return RectangleF.Empty;
            PointF topLeft = ClampedGhostTopLeft(new PointF(pointer.X, pointer.Y));
            return new RectangleF(topLeft.X, topLeft.Y, _faceBounds[index].Width,
                _faceBounds[index].Height);
        }

        /// <summary>Harness hook: the card the keyboard handed over, in client space.</summary>
        internal RectangleF ExternalGhostForTest()
        {
            return _externalShape == null ? RectangleF.Empty : KeyVisual.GhostBounds(_externalShape);
        }


        private Keys HitTest(Point point)
        {
            // Mid-collapse the card is only a slice of the open layout, so nothing
            // inside it should react to the pointer.
            if (_collapse > 0.01f || !ClientRectangle.Contains(point)) return Keys.None;
            for (int i = 0; i < ZoneOrder.Length; i++)
            {
                if (_facePaths[i] == null) continue;
                if (_facePaths[i].IsVisible(point)) return ZoneOrder[i];
            }
            return Keys.None;
        }

        internal Keys ZoneAtScreen(Point screen)
        {
            if (!Visible) return Keys.None;
            EnsureGeometry();
            return HitTest(PointToClient(screen));
        }

        // ---- paint -----------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            EnsureGeometry();

            RectangleF card = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            // The card frame uses the cap radius, not the panel radius: on this page
            // the board is square-edged and only the keys are rounded, so the mouse
            // panel follows the keys instead of introducing a third rounding.
            float cardRadius = CornerRadius(Height);
            Theme.FillRound(g, card, cardRadius, Theme.Surface);
            Theme.StrokeRound(g, card, cardRadius, Theme.Stroke, 1f);

            int pad = Theme.S(12);
            float t = _collapse;

            if (t >= 0.999f)
            {
                DrawChevron(g, Width / 2f, 90f);
                DrawRailTitle(g, 1f);
                return;
            }

            // The header fades towards the card surface while the card narrows, so it
            // never lingers on top of the rail.
            float headerFade = Math.Min(1f, t * 2.2f);
            TextRenderer.DrawText(g, "鼠标", Theme.BodyStrong,
                new Rectangle(pad, pad - Theme.S(2), Theme.S(80), _headerH),
                Theme.Blend(Theme.TextPrimary, Theme.Surface, headerFade),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, _mouseCount + " 条", Theme.Caption,
                new Rectangle(pad, pad - Theme.S(2),
                    Math.Max(Theme.S(20), Width - pad * 2 - Theme.S(26)), _headerH),
                Theme.Blend(Theme.TextTertiary, Theme.Surface, headerFade),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            DrawBody(g);

            // The rail is an opaque strip: the mouse slides underneath it as the card
            // narrows, so the last frame is exactly the collapsed rail (no jump).
            if (t > 0.001f)
            {
                RectangleF strip = new RectangleF(-cardRadius, 0.5f,
                    Theme.S(30) + cardRadius, Height - 1f);
                using (GraphicsPath path = Theme.Rounded(strip, cardRadius))
                {
                    using (SolidBrush b = new SolidBrush(Theme.Surface)) g.FillPath(b, path);
                    using (Pen pen = new Pen(Theme.Blend(Theme.Surface, Theme.Stroke,
                        0.4f + 0.6f * t), 1f)) g.DrawPath(pen, path);
                }
            }

            if (_dragActive)
            {
                // Outside the card the target surface draws the lifted shape; a
                // clipped half shape at the edge would only look broken.
                if (ClientRectangle.Contains(Point.Round(_dragPoint)))
                    DrawOwnGhost(g);
            }
            else if (_externalActive && _externalShape != null)
            {
                KeyVisual.DrawGhost(g, _externalShape, _externalAction, _externalText,
                    _externalFontSize);
            }

            // One chevron for both states: it walks from the card's top-right corner
            // to the middle of the rail and flips direction on the way. The rail part
            // of the card is fixed at 30px, so the chevron and the title stay there
            // while the rest of the card is still closing.
            float railWidth = Math.Min(Width, Theme.S(30));
            float openX = Width - pad - Theme.S(7);
            DrawChevron(g, openX + (railWidth / 2f - openX) * t, -90f + 180f * t);
            if (t > 0.001f) DrawRailTitle(g, t);
        }

        private void DrawChevron(Graphics g, float cx, float rotation)
        {
            int pad = Theme.S(12);
            float cy = pad + _headerH / 2f;
            float half = Theme.SF(4f);
            float rise = Theme.SF(2.4f);

            GraphicsState state = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(rotation);
            Color color = _hoverChevron ? Theme.TextPrimary : Theme.TextSecondary;
            using (Pen pen = new Pen(color, Theme.SF(1.5f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, -half, -rise, 0f, rise);
                g.DrawLine(pen, 0f, rise, half, -rise);
            }
            g.Restore(state);
        }

        /// <summary>
        /// Title of the collapsed rail. A rotated label cannot go through
        /// TextRenderer (GDI ignores the Graphics transform), so the rail stacks the
        /// two characters instead of painting a rotated string in the wrong place.
        /// <paramref name="strength"/> fades it in while the card collapses.
        /// </summary>
        private void DrawRailTitle(Graphics g, float strength)
        {
            int pad = Theme.S(12);
            int line = Math.Max(1, Theme.LineHeight(Theme.Caption));
            int top = (int)(pad + _headerH / 2f) + Theme.S(20);
            int width = Math.Min(Width, Theme.S(30));
            string title = "鼠标";
            Color color = Theme.Blend(Theme.Surface, Theme.TextTertiary, strength);
            for (int i = 0; i < title.Length; i++)
            {
                TextRenderer.DrawText(g, title.Substring(i, 1), Theme.Caption,
                    new Rectangle(0, top + i * line, width, line), color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }

        private void DrawBody(Graphics g)
        {
            using (SolidBrush b = new SolidBrush(Theme.Layer)) g.FillPath(b, _bodyPath);
            using (Pen pen = new Pen(Theme.StrokeStrong, Math.Max(1f, _unit * 1.1f)))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawPath(pen, _bodyPath);
            }

            EnsureTextFonts();
            bool filtered = HasFilter;

            ZoneState[] states = new ZoneState[ZoneOrder.Length];
            Color[] fills = new Color[ZoneOrder.Length];
            Color[] edges = new Color[ZoneOrder.Length];
            Color[] labels = new Color[ZoneOrder.Length];

            for (int i = 0; i < ZoneOrder.Length; i++)
            {
                Keys key = ZoneOrder[i];
                ZoneState state = ComputeState(key);
                bool usedNeutral = !filtered && state.Count > 0;
                bool hovered = key == _hover || key == _dropTarget || key == _dragTarget;
                states[i] = state;
                fills[i] = KeyVisual.FaceColor(false, state.EnabledBound, state.DisabledOnly,
                    usedNeutral, hovered);
                edges[i] = KeyVisual.EdgeColor(false, state.EnabledBound, state.DisabledOnly,
                    usedNeutral);
                labels[i] = KeyVisual.TextColor(false, state.EnabledBound, state.DisabledOnly,
                    usedNeutral);
            }

            // Every 3D bottom first, then every face: a depth band can never paint
            // over the neighbouring key.
            for (int i = 0; i < ZoneOrder.Length; i++)
            {
                Color shadow = Theme.IsDark
                    ? Theme.Blend(fills[i], Color.Black, 0.55f)
                    : Theme.Blend(fills[i], Theme.StrokeStrong, 0.55f);
                using (SolidBrush b = new SolidBrush(shadow)) g.FillPath(b, _shadowPaths[i]);
            }

            for (int i = 0; i < ZoneOrder.Length; i++)
            {
                Keys key = ZoneOrder[i];
                ZoneState state = states[i];
                RectangleF bounds = _faceBounds[i];

                using (SolidBrush b = new SolidBrush(fills[i])) g.FillPath(b, _facePaths[i]);
                if (state.DisabledOnly)
                    KeyVisual.Hatch(g, _facePaths[i], KeyUnit);
                using (Pen pen = new Pen(edges[i], 1f))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPath(pen, _facePaths[i]);
                }
                DrawZoneText(g, key, bounds, state, labels[i]);
                if (!filtered && state.Count > 0)
                {
                    EnsureBadgeFont(KeyUnit);
                    KeyVisual.DrawBadge(g, bounds, state.Count, KeyUnit, _badgeFont);
                }
                if (key == _dropTarget || key == _dragTarget)
                {
                    using (Pen pen = new Pen(Theme.Accent, 2f))
                    {
                        pen.LineJoin = LineJoin.Round;
                        g.DrawPath(pen, _facePaths[i]);
                    }
                }
                if (key == _selected)
                {
                    using (Pen pen = new Pen(Theme.TextPrimary, 1.6f))
                    {
                        pen.LineJoin = LineJoin.Round;
                        g.DrawPath(pen, _facePaths[i]);
                    }
                }
                // The dragged key stays visible but dimmed, exactly like the board's
                // source cap: the veil goes over its label and selection ring too.
                if (_dragActive && key == _dragFromKey)
                {
                    using (SolidBrush dim = new SolidBrush(Theme.Alpha(Theme.Background, 170)))
                        g.FillPath(dim, _facePaths[i]);
                }
            }
        }

        /// <summary>
        /// Zone labels use the board's own fit ladder (KeyLabel.Plan +
        /// KeyActionText.Layout): the same pixel font, the same shrinking steps and
        /// the same "name above, action below" block. The action row only exists
        /// inside a chord; the neutral overview keeps the name alone.
        /// </summary>
        private void EnsureTextFonts()
        {
            float unit = KeyUnit;
            if (_labelFont != null && Math.Abs(_textUnit - unit) < 0.2f) return;
            _textUnit = unit;
            if (_labelFont != null) _labelFont.Dispose();
            if (_subFont != null) _subFont.Dispose();
            if (_tinyFont != null) _tinyFont.Dispose();
            if (_actionFonts != null)
            {
                for (int i = 0; i < _actionFonts.Length; i++) _actionFonts[i].Dispose();
                _actionFonts = null;
                _actionLines = null;
            }
            KeyLabel.CreateFonts(unit, out _labelFont, out _subFont, out _tinyFont);
            KeyActionText.BuildLadder(unit, out _actionFonts, out _actionLines);
        }

        private void DrawZoneText(Graphics g, Keys key, RectangleF bounds, ZoneState state, Color color)
        {
            KeyActionLayout layout = ZoneLayout(key);
            if (layout != null && layout.HasAction)
            {
                KeyVisual.DrawActionLayout(g, bounds, layout, color, false);
                return;
            }
            KeyVisual.DrawLabel(g, ZoneName(key), bounds, color, _labelFont, _subFont, _tinyFont);
        }

        /// <summary>
        /// The visible face of one zone as a flattened polygon, in client space. Used
        /// for the lifted card so the ghost keeps the rounded outline of the key it
        /// came from (a hard polygon would look like a different shape).
        /// </summary>
        private PointF[] FlattenedFace(int index)
        {
            if (index < 0 || _facePoints[index] == null) return null;
            using (GraphicsPath path = new GraphicsPath())
            {
                BuildRounded(path, _facePoints[index], _radii[index]);
                path.Flatten();
                return path.PathPoints;
            }
        }

        /// <summary>
        /// The action layout a zone paints: the same fitted text and font the card
        /// uses while the zone sits still, so the lifted card can show the action
        /// line in exactly the same place and size.
        /// </summary>
        private KeyActionLayout ZoneLayout(Keys key)
        {
            int index = IndexOfZone(key);
            if (index < 0 || _faceBounds[index].IsEmpty) return null;
            ZoneState state = ComputeState(key);
            string action = HasFilter ? state.Action : null;
            if (string.IsNullOrEmpty(action)) return null;
            EnsureTextFonts();
            RectangleF bounds = _faceBounds[index];
            KeyCap cap = new KeyCap(key, ZoneName(key), 0f, 0f, 1f);
            return KeyActionText.Layout(cap, Math.Max(1, (int)bounds.Width + 2), bounds.Height,
                KeyUnit, _labelFont, _subFont, _tinyFont, _actionFonts, _actionLines, action);
        }

        /// <summary>
        /// The lifted card of one zone placed with its face at
        /// <paramref name="topLeft"/> (client space): the zone's rounded outline plus
        /// its action line where the zone paints it. Only the zone name is dropped.
        /// </summary>
        private DragGhostCard GhostCardAt(int index, PointF topLeft)
        {
            PointF[] shape = FlattenedFace(index);
            if (shape == null) return null;
            RectangleF bounds = _faceBounds[index];
            float dx = topLeft.X - bounds.X;
            float dy = topLeft.Y - bounds.Y;

            DragGhostCard card = new DragGhostCard();
            card.Shape = Translate(shape, dx, dy);
            KeyActionLayout layout = ZoneLayout(ZoneOrder[index]);
            if (layout != null && layout.HasAction)
            {
                card.Action = layout.ActionText ?? "";
                card.FontSize = layout.ActionFont == null ? 0f : layout.ActionFont.Size;
                card.Text = KeyVisual.ActionRowRect(new RectangleF(topLeft.X, topLeft.Y,
                    bounds.Width, bounds.Height), layout);
            }
            return card;
        }

        /// <summary>
        /// Top-left the card has while it follows the pointer: the point the user
        /// grabbed stays under the cursor, exactly like a dragged keycap. Centring the
        /// card instead would put the cursor on top of its own action line. This is
        /// the raw position; the card is clamped where it is painted.
        /// </summary>
        private PointF GhostTopLeft(PointF pointer)
        {
            return new PointF(pointer.X - _dragGrab.X, pointer.Y - _dragGrab.Y);
        }

        /// <summary>
        /// Where this card paints the lifted zone: clamped to the card's edge, the
        /// same clamp the board applies to its caps, so a zone never slides half out
        /// of its own surface.
        /// </summary>
        private PointF ClampedGhostTopLeft(PointF pointer)
        {
            int index = IndexOfZone(_dragFromKey);
            if (index < 0 || _faceBounds[index].IsEmpty) return GhostTopLeft(pointer);
            return GhostPlacement.Clamp(GhostTopLeft(pointer), _faceBounds[index].Size,
                ClientSize);
        }

        /// <summary>
        /// The lifted card is the zone itself: same outline, same shadow, accent
        /// border, and only the action line (the zone name is hidden while
        /// dragging, exactly like the board hides the key symbol).
        /// </summary>
        private void DrawOwnGhost(Graphics g)
        {
            DragGhostCard card = GhostCardAt(IndexOfZone(_dragFromKey),
                ClampedGhostTopLeft(_dragPoint));
            if (card == null) return;
            KeyVisual.DrawGhost(g, card.Shape, card.Action, card.Text, card.FontSize);
        }

        /// <summary>
        /// The same card in screen coordinates, so the surface under the pointer can
        /// paint it at the identical place and size.
        /// </summary>
        private DragGhostCard GhostCardOnScreen(Keys key, Point screenPoint)
        {
            DragGhostCard card = GhostCardAt(IndexOfZone(key),
                GhostTopLeft(PointToClient(screenPoint)));
            if (card == null) return null;
            Point origin = PointToScreen(Point.Empty);
            for (int i = 0; i < card.Shape.Length; i++)
                card.Shape[i] = new PointF(card.Shape[i].X + origin.X, card.Shape[i].Y + origin.Y);
            if (card.Text.Width > 0 && card.Text.Height > 0)
                card.Text = new Rectangle(card.Text.X + origin.X, card.Text.Y + origin.Y,
                    card.Text.Width, card.Text.Height);
            return card;
        }

        private void EnsureBadgeFont(float unit)
        {
            if (_badgeFont != null && Math.Abs(_badgeFontUnit - unit) < 0.01f) return;
            if (_badgeFont != null) _badgeFont.Dispose();
            _badgeFont = KeyVisual.CreateBadgeFont(unit);
            _badgeFontUnit = unit;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_badgeFont != null) { _badgeFont.Dispose(); _badgeFont = null; }
                if (_labelFont != null) { _labelFont.Dispose(); _labelFont = null; }
                if (_subFont != null) { _subFont.Dispose(); _subFont = null; }
                if (_tinyFont != null) { _tinyFont.Dispose(); _tinyFont = null; }
                if (_actionFonts != null)
                {
                    for (int i = 0; i < _actionFonts.Length; i++) _actionFonts[i].Dispose();
                    _actionFonts = null;
                }
                for (int i = 0; i < _facePaths.Length; i++)
                {
                    _facePaths[i].Dispose();
                    _shadowPaths[i].Dispose();
                }
                _bodyPath.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---- state -----------------------------------------------------------
        private ZoneState ComputeState(Keys key)
        {
            ZoneState state = new ZoneState();
            bool filtered = HasFilter;
            foreach (KeyMapping mapping in _mappings)
            {
                if (mapping == null) continue;
                if (((Keys)mapping.Key & Keys.KeyCode) != (key & Keys.KeyCode)) continue;
                state.Count++;
                if (filtered)
                {
                    if (!KeyboardFilter.Exact(mapping, _filterCaps, _filterCtrl, _filterAlt,
                        _filterShift, _filterWin)) continue;
                    // Disabled rows still count: a chord whose only shortcut is
                    // turned off must hatch, exactly like its keycap.
                    state.ExactCount++;
                    if (!mapping.Enabled) continue;
                    state.ExactEnabled++;
                    if (state.Action == null) state.Action = Describe(mapping);
                }
                else if (mapping.Enabled && state.Action == null)
                {
                    state.Action = Describe(mapping);
                }
            }
            if (filtered && state.ExactCount == 0 && state.Count > 0)
            {
                state.Action = null;
                return state;
            }
            state.EnabledBound = filtered && state.ExactEnabled > 0;
            state.DisabledOnly = filtered && state.ExactCount > 0 && state.ExactEnabled == 0;
            return state;
        }

        private static string Describe(KeyMapping mapping)
        {
            string note = mapping.Note == null ? "" : mapping.Note.Trim();
            if (note.Length > 0 && ActionCatalog.SupportsNote(mapping.Action)) return note;
            return mapping.ActionText;
        }

        // ---- input -----------------------------------------------------------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            EnsureGeometry();
            _dragArmed = false;
            _dragActive = false;
            if (e.Button == MouseButtons.Right) return;

            _chevronDown = _chevronHit.Contains(e.Location);
            if (_chevronDown) return;

            Keys key = HitTest(e.Location);
            if (key == Keys.None) return;
            _pressKey = key;
            _pressPoint = e.Location;
            _dragPoint = new PointF(e.Location.X, e.Location.Y);
            // Every zone lifts inside a chord, including one that carries nothing:
            // a button with no shortcut still has to answer the press like a keycap
            // instead of sitting there dead. Dropping it moves nothing, exactly like
            // an empty key on the board.
            if (HasFilter)
            {
                _dragArmed = true;
                _dragActive = false;
                _dragFromKey = key;
                _dragTarget = Keys.None;
                // Keep the grab point: the card follows the pointer with the same
                // offset the zone had under the cursor when the drag started.
                int grabIndex = IndexOfZone(key);
                RectangleF grabFace = grabIndex < 0 ? RectangleF.Empty : _faceBounds[grabIndex];
                _dragGrab = new PointF(e.Location.X - grabFace.X, e.Location.Y - grabFace.Y);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_dragArmed)
            {
                if (!_dragActive)
                {
                    if (Math.Abs(e.X - _pressPoint.X) < DragThreshold
                        && Math.Abs(e.Y - _pressPoint.Y) < DragThreshold) return;
                    _dragActive = true;
                    _dragDirtyValid = false;
                    Capture = true;
                    Cursor = Cursors.SizeAll;
                    _hover = Keys.None;
                }
                _dragPoint = new PointF(e.X, e.Y);

                // Inside the card a drop lands on another zone (the board behaves the
                // same way with its own caps); outside it the pointer's surface is
                // asked for a target.
                bool inside = ClientRectangle.Contains(e.Location);
                Keys target = Keys.None;
                if (!inside && ExternalDropTarget != null)
                    target = ExternalDropTarget(PointToScreen(e.Location));
                else if (inside)
                    target = InternalDropTarget(e.Location);
                if (target != _dragTarget)
                {
                    _dragTarget = target;
                    if (ExternalTargetChanged != null) ExternalTargetChanged(target);
                }
                if (DragPointerMoved != null)
                {
                    Point screen = PointToScreen(e.Location);
                    DragPointerMoved(GhostCardOnScreen(_dragFromKey, screen), screen, !inside);
                }
                // Repaint only the band the source, the target and the lifted card
                // occupy; the board's drag does exactly this.
                InvalidateDragBand();
                return;
            }

            Keys hit = HitTest(e.Location);
            bool overChevron = _chevronHit.Contains(e.Location);
            if (hit != _hover || overChevron != _hoverChevron)
            {
                _hover = hit;
                _hoverChevron = overChevron;
                Cursor = (hit != Keys.None || overChevron) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        /// <summary>The other zone under the pointer that can receive the drop.</summary>
        private Keys InternalDropTarget(Point point)
        {
            Keys hit = HitTest(point);
            if (hit == Keys.None || hit == _dragFromKey) return Keys.None;
            return hit;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragActive) return;
            if (_hover != Keys.None || _hoverChevron)
            {
                _hover = Keys.None;
                _hoverChevron = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (e.Button == MouseButtons.Right)
            {
                EnsureGeometry();
                Keys contextKey = HitTest(e.Location);
                if (contextKey != Keys.None && ZoneContextRequested != null)
                    ZoneContextRequested(contextKey);
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (_chevronDown)
            {
                _chevronDown = false;
                if (_chevronHit.Contains(e.Location))
                {
                    // The page owns the animation, so ask for the state instead of
                    // snapping the card to it.
                    bool collapse = !Collapsed;
                    if (CollapseRequested != null) CollapseRequested(collapse);
                    else Collapsed = collapse;
                }
                return;
            }

            Keys pressed = _pressKey;
            bool moved = _dragActive;
            Keys target = _dragTarget;
            Keys from = _dragFromKey;
            _pressKey = Keys.None;

            if (moved)
            {
                EndDrag();
                if (target != Keys.None && ZoneDropped != null) ZoneDropped(from, target);
                _lastClickKey = Keys.None;
                _lastClickTick = 0;
                return;
            }

            EndDrag();
            if (pressed == Keys.None) return;
            if (HitTest(e.Location) != pressed) return;
            ApplyZoneClick(pressed);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!_dragActive || Capture) return;
            Keys target = _dragTarget;
            Keys from = _dragFromKey;
            EndDrag();
            if (target != Keys.None && ZoneDropped != null) ZoneDropped(from, target);
            _lastClickKey = Keys.None;
            _lastClickTick = 0;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button != MouseButtons.Left) return;
            Keys key = HitTest(e.Location);
            if (key != Keys.None && ZoneActivated != null) ZoneActivated(key);
        }

        private void EndDrag()
        {
            Rectangle band = _dragDirtyValid ? _dragDirty : Rectangle.Empty;
            bool wasActive = _dragActive;
            _dragArmed = false;
            _dragActive = false;
            _dragFromKey = Keys.None;
            _dragTarget = Keys.None;
            _dragDirtyValid = false;
            if (Capture) Capture = false;
            Cursor = Cursors.Default;
            if (wasActive && ExternalTargetChanged != null) ExternalTargetChanged(Keys.None);
            if (wasActive && DragFinished != null) DragFinished();
            if (wasActive)
            {
                if (band.Width > 0 && band.Height > 0)
                {
                    band.Inflate(6, 8);
                    Invalidate(band);
                }
                else
                {
                    Invalidate();
                }
            }
        }

        /// <summary>Repaints only the band the drag visuals occupy (old ∪ new).</summary>
        private void InvalidateDragBand()
        {
            if (!_dragActive)
            {
                Invalidate();
                return;
            }
            Rectangle now = DragBand();
            Rectangle band = _dragDirtyValid ? Rectangle.Union(_dragDirty, now) : now;
            band.Inflate(6, 8);
            if (band.Width > 0 && band.Height > 0) Invalidate(band);
            _dragDirty = now;
            _dragDirtyValid = true;
        }

        /// <summary>Source zone, drop target zone and the lifted card, in client space.</summary>
        private Rectangle DragBand()
        {
            Rectangle band = Rectangle.Empty;
            int from = IndexOfZone(_dragFromKey);
            if (from >= 0 && !_faceBounds[from].IsEmpty)
                band = Rectangle.Round(_faceBounds[from]);
            int to = IndexOfZone(_dragTarget);
            if (to >= 0 && !_faceBounds[to].IsEmpty)
            {
                Rectangle target = Rectangle.Round(_faceBounds[to]);
                band = band.IsEmpty ? target : Rectangle.Union(band, target);
            }
            if (_dragActive)
            {
                Rectangle ghost = GhostBand();
                if (!ghost.IsEmpty)
                    band = band.IsEmpty ? ghost : Rectangle.Union(band, ghost);
            }
            return band;
        }

        /// <summary>
        /// Band of the lifted card this card is painting right now: its own while a
        /// zone is dragged, the handed-over one while a keycap is dragged over it.
        /// </summary>
        private Rectangle GhostBand()
        {
            int from = _dragActive ? IndexOfZone(_dragFromKey) : -1;
            if (from >= 0 && !_faceBounds[from].IsEmpty)
            {
                RectangleF face = _faceBounds[from];
                PointF topLeft = ClampedGhostTopLeft(_dragPoint);
                RectangleF moved = new RectangleF(topLeft.X, topLeft.Y, face.Width, face.Height);
                moved.Inflate(4f, 8f);
                return Rectangle.Round(moved);
            }
            return _externalShape == null ? Rectangle.Empty : BandOf(_externalShape);
        }

        private void ApplyZoneClick(Keys key)
        {
            int tick = Environment.TickCount;
            bool chord = HasFilter;
            bool second = chord && key == _lastClickKey && _lastClickTick != 0
                && tick - _lastClickTick >= 0 && tick - _lastClickTick <= DoubleClickMs;

            _lastClickKey = chord ? key : Keys.None;
            _lastClickTick = chord ? tick : 0;

            if (second)
            {
                if (_selected != key)
                {
                    _selected = key;
                    Invalidate();
                    if (ZoneClicked != null) ZoneClicked(key);
                }
                return;
            }
            if (ZoneClicked != null) ZoneClicked(key);
        }
    }
}
