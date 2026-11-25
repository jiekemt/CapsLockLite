using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// One lifted card, as the source draws it: the source outline plus the action
    /// line exactly where and how big the source paints it. Passing the whole card
    /// (instead of only the action text) is what lets the surface that shows it —
    /// which may not be the source at all — hide the key name and nothing else.
    /// All coordinates are screen space; each surface converts on arrival.
    /// </summary>
    internal sealed class DragGhostCard
    {
        /// <summary>Source outline in screen coordinates.</summary>
        public PointF[] Shape;

        /// <summary>Action line to show, already fitted by the source ("" = none).</summary>
        public string Action = "";

        /// <summary>Where the source draws that line, in screen coordinates.</summary>
        public Rectangle Text;

        /// <summary>Pixel size of the source's action font.</summary>
        public float FontSize;
    }

    /// <summary>
    /// The shared visual language of a shortcut surface. Every rule here was
    /// lifted verbatim from <c>MKeyboardView.DrawKey</c> so the mouse card and the
    /// board cannot drift apart: same face / edge / text colours per state, same
    /// disabled hatch, same count badge formula and font.
    /// </summary>
    internal static class KeyVisual
    {
        /// <summary>Key face fill for the current state (hover applied last).</summary>
        public static Color FaceColor(bool modifierOn, bool enabledBound, bool disabledOnly,
            bool usedNeutral, bool hovered)
        {
            Color faceColor = Theme.IsDark ? Theme.SurfaceAlt : Theme.Surface;
            if (modifierOn)
            {
                // A selected modifier keeps the normal cap; the mask carries the state.
            }
            else if (enabledBound)
            {
                faceColor = Theme.IsDark
                    ? Theme.Blend(Theme.Accent, Theme.SurfaceAlt, 0.72f)
                    : Theme.AccentSoft;
            }
            else if (disabledOnly)
            {
                faceColor = Theme.SurfaceAlt;
            }
            else if (usedNeutral)
            {
                faceColor = Theme.IsDark
                    ? Theme.Blend(Theme.AccentSoft, Theme.SurfaceAlt, 0.80f)
                    : Theme.Blend(Theme.AccentSoft, Theme.Surface, 0.55f);
            }
            if (hovered)
                faceColor = Theme.Blend(faceColor, Theme.IsDark ? Color.White : Color.Black, 0.06f);
            return faceColor;
        }

        /// <summary>Border colour of a key face for the current state.</summary>
        public static Color EdgeColor(bool modifierOn, bool enabledBound, bool disabledOnly,
            bool usedNeutral)
        {
            if (modifierOn) return Theme.Accent;
            if (enabledBound) return Theme.Alpha(Theme.Accent, 190);
            if (disabledOnly) return Theme.StrokeStrong;
            if (usedNeutral) return Theme.Alpha(Theme.Accent, 90);
            return Theme.StrokeStrong;
        }

        /// <summary>
        /// Text colour of a key label: accent for an exact-chord hit, secondary for
        /// a disabled-only key, otherwise the normal primary/secondary per theme.
        /// </summary>
        public static Color TextColor(bool modifierOn, bool enabledBound, bool disabledOnly,
            bool usedNeutral)
        {
            if (modifierOn) return Theme.IsDark ? Theme.Accent : Theme.AccentPressed;
            if (enabledBound) return Theme.Accent;
            if (disabledOnly) return Theme.TextSecondary;
            return Theme.IsDark ? Theme.TextPrimary : Theme.TextSecondary;
        }

        /// <summary>Grey hatch for "the shortcuts exist but all are disabled".</summary>
        public static void Hatch(Graphics g, GraphicsPath clip, float unit)
        {
            RectangleF bounds = clip.GetBounds();
            Region previous = g.Clip;
            try
            {
                g.SetClip(clip, CombineMode.Intersect);
                using (Pen pen = new Pen(Theme.Alpha(Theme.TextDisabled, 120), 1f))
                {
                    float step = Math.Max(4f, unit * 0.20f);
                    for (float x = bounds.Left - bounds.Height; x < bounds.Right + bounds.Height; x += step)
                        g.DrawLine(pen, x, bounds.Top, x + bounds.Height, bounds.Bottom);
                }
            }
            finally
            {
                g.Clip = previous;
                previous.Dispose();
            }
        }

        /// <summary>Pixel font for a count badge, sized from the key unit.</summary>
        public static Font CreateBadgeFont(float unit)
        {
            return new Font(Theme.Body.FontFamily, Math.Max(5.5f, unit * 0.155f),
                FontStyle.Bold, GraphicsUnit.Pixel);
        }

        /// <summary>
        /// Count badge: a true circle in the bottom-right corner. Two-digit counts
        /// grow the circle, and the digit is drawn with the pixel font so it stays
        /// optically centred at any DPI.
        /// </summary>
        public static void DrawBadge(Graphics g, RectangleF face, int count, float unit, Font font)
        {
            if (font == null || count <= 0 || unit < 34f) return;

            string text = count.ToString();
            float textWidth = Theme.Measure(text, font).Width;
            float diameter = Math.Max(9f, Math.Max(unit * 0.26f, textWidth + 4f));
            float inset = Math.Max(1f, unit * 0.04f);
            RectangleF circle = new RectangleF(face.Right - diameter - inset,
                face.Bottom - diameter - inset, diameter, diameter);
            if (circle.X < face.X + 1f || circle.Y < face.Y + 1f) return;

            Theme.FillRound(g, circle, diameter / 2f, Theme.Accent);
            TextRenderer.DrawText(g, text, font, Rectangle.Round(circle), Theme.OnAccent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        // ---- label and action rows (the exact rules of MKeyboardView) ---------
        public const TextFormatFlags LabelFlags =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        public const TextFormatFlags LineFlags =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top |
            TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping |
            TextFormatFlags.NoPrefix;

        public const TextFormatFlags ActionFlags =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping |
            TextFormatFlags.NoPrefix;

        /// <summary>
        /// The key-name row of a cap, with the same fit ladder the board uses:
        /// full label face, then the smaller faces, then a two-line split. A cap
        /// without an action line (and the neutral overview) only ever draws this.
        /// </summary>
        public static void DrawLabel(Graphics g, string label, RectangleF face, Color color,
            Font labelFont, Font smallFont, Font tinyFont)
        {
            if (string.IsNullOrEmpty(label)) return;
            int width = Math.Max(1, (int)face.Width + 2);
            Rectangle bounds = new Rectangle((int)face.X - 1, (int)face.Y, width,
                Math.Max(1, (int)face.Height));
            KeyCap cap = new KeyCap(Keys.None, label, 0f, 0f, 1f);
            KeyLabelPlan plan = KeyLabel.Plan(cap, width, face.Height, labelFont, smallFont, tinyFont);

            if (plan.Kind == KeyLabelKind.TwoLines)
            {
                int firstHeight = KeyActionText.LineHeight(plan.FirstFont);
                int secondHeight = KeyActionText.LineHeight(plan.SecondFont);
                int total = firstHeight + secondHeight;
                float padY = Math.Max(1f, (face.Height - total) / 2f);
                int y = (int)Math.Round(face.Y + padY);
                Rectangle top = new Rectangle((int)face.X - 1, y, width, firstHeight);
                Rectangle bottom = new Rectangle((int)face.X - 1, y + firstHeight, width, secondHeight);
                TextRenderer.DrawText(g, plan.First, plan.FirstFont, top, color, LineFlags);
                TextRenderer.DrawText(g, plan.Second, plan.SecondFont, bottom, color, LineFlags);
                return;
            }
            TextRenderer.DrawText(g, label, plan.FirstFont, bounds, color, LabelFlags);
        }

        /// <summary>
        /// Where the action line of a face lands. The rows are centred as one block
        /// and the action row follows them, so hiding the name rows keeps the action
        /// line exactly where it was — both the normal paint and the drag card use
        /// this one rule, which is what makes a lifted key look like the same key.
        /// </summary>
        public static Rectangle ActionRowRect(RectangleF face, KeyActionLayout layout)
        {
            if (layout == null || !layout.HasAction) return Rectangle.Empty;
            int height = Math.Max(1, (int)face.Height);
            int total = layout.Height;
            int y = (int)Math.Round(face.Y + (height - total) / 2f);
            if (y < face.Y + 1f) y = (int)face.Y + 1;
            if (y + total > face.Y + height) y = (int)face.Y + height - total;
            if (layout.RowHeight != null)
            {
                for (int i = 0; i < layout.RowHeight.Length; i++) y += layout.RowHeight[i];
            }
            return new Rectangle((int)face.X + 1, y, Math.Max(1, (int)face.Width - 2),
                Math.Max(1, layout.ActionHeight));
        }

        /// <summary>
        /// Key-name rows plus the action row, centred as one block. The name is
        /// already fitted by the plan, so no GDI ellipsis cuts a glyph.
        /// </summary>
        public static void DrawActionLayout(Graphics g, RectangleF face, KeyActionLayout layout,
            Color color, bool hideSymbol)
        {
            if (layout == null) return;
            int width = Math.Max(1, (int)face.Width + 2);
            int height = Math.Max(1, (int)face.Height);
            int total = layout.Height;
            int y = (int)Math.Round(face.Y + (height - total) / 2f);
            if (y < face.Y + 1f) y = (int)face.Y + 1;
            if (y + total > face.Y + height) y = (int)face.Y + height - total;

            for (int i = 0; i < layout.RowText.Length; i++)
            {
                Rectangle row = new Rectangle((int)face.X - 1, y, width, layout.RowHeight[i]);
                if (!hideSymbol)
                {
                    TextRenderer.DrawText(g, layout.RowText[i], layout.RowFont[i], row, color,
                        LineFlags);
                }
                y += layout.RowHeight[i];
            }

            Rectangle action = ActionRowRect(face, layout);
            TextRenderer.DrawText(g, layout.ActionText, layout.ActionFont, action,
                Theme.Alpha(color, 205), ActionFlags);
        }

        /// <summary>
        /// Lifted card for a drag. Whether the shape is the source cap's face or a
        /// mouse zone's polygon, both surfaces draw it with this one routine:
        /// shadow, card face, accent border and the action line only (the name is
        /// hidden while dragging, exactly like the board's own ghost).
        ///
        /// The action line is drawn into <paramref name="textRect"/>, the rectangle
        /// the source uses, so the name keeps its place and its size while the
        /// element is lifted instead of being re-centred on the card.
        /// </summary>
        public static void DrawGhost(Graphics g, PointF[] shape, string action,
            Rectangle textRect, float fontSize)
        {
            DrawGhostCore(g, shape, action, textRect, fontSize);
        }

        /// <summary>Bounding box of a card outline, the size the shape really covers.</summary>
        public static RectangleF GhostBounds(PointF[] shape)
        {
            if (shape == null || shape.Length == 0) return RectangleF.Empty;
            float minX = shape[0].X, maxX = shape[0].X, minY = shape[0].Y, maxY = shape[0].Y;
            for (int i = 1; i < shape.Length; i++)
            {
                if (shape[i].X < minX) minX = shape[i].X;
                if (shape[i].X > maxX) maxX = shape[i].X;
                if (shape[i].Y < minY) minY = shape[i].Y;
                if (shape[i].Y > maxY) maxY = shape[i].Y;
            }
            return new RectangleF(minX, minY, Math.Max(1f, maxX - minX),
                Math.Max(1f, maxY - minY));
        }

        private static void DrawGhostCore(Graphics g, PointF[] shape, string action,
            Rectangle textRect, float fontSize)
        {
            RectangleF bounds = GhostBounds(shape);
            bool round = shape.Length == 4 && IsAxisAlignedRect(shape);
            float radius = Math.Min(Theme.SF(4f), Math.Max(2f, bounds.Height * 0.18f));

            PointF[] shadow = new PointF[shape.Length];
            for (int i = 0; i < shape.Length; i++)
                shadow[i] = new PointF(shape[i].X + 1f, shape[i].Y + 3f);

            if (round)
            {
                Theme.FillRound(g, new RectangleF(bounds.X + 1f, bounds.Y + 3f, bounds.Width, bounds.Height),
                    radius, Theme.Alpha(Color.Black, Theme.IsDark ? 90 : 30));
                Theme.FillRound(g, bounds, radius, Theme.IsDark ? Theme.SurfaceAlt : Theme.Surface);
                Theme.StrokeRound(g, new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f,
                    Math.Max(0.5f, bounds.Width - 1f), Math.Max(0.5f, bounds.Height - 1f)),
                    radius, Theme.Accent, 2f);
            }
            else
            {
                using (SolidBrush shadowBrush = new SolidBrush(Theme.Alpha(Color.Black, Theme.IsDark ? 90 : 30)))
                    g.FillPolygon(shadowBrush, shadow);
                using (SolidBrush face = new SolidBrush(Theme.IsDark ? Theme.SurfaceAlt : Theme.Surface))
                    g.FillPolygon(face, shape);
                using (Pen pen = new Pen(Theme.Accent, 2f))
                {
                    pen.LineJoin = LineJoin.Round;
                    g.DrawPolygon(pen, shape);
                }
            }

            if (string.IsNullOrEmpty(action)) return;
            // Same family and pixel size the source used, rebuilt here so the card
            // never holds a font owned by the other surface.
            float pixels = fontSize > 1f ? fontSize : Math.Max(7.5f, bounds.Height * 0.16f);
            using (Font font = new Font(Theme.Body.FontFamily, pixels, FontStyle.Regular,
                GraphicsUnit.Pixel))
            {
                Rectangle text = textRect.Width > 0 && textRect.Height > 0
                    ? textRect
                    : new Rectangle((int)bounds.X + Theme.S(2),
                        (int)Math.Round(bounds.Y + (bounds.Height
                            - KeyActionText.LineHeight(font)) / 2f),
                        Math.Max(1, (int)bounds.Width - Theme.S(4)),
                        Math.Max(1, KeyActionText.LineHeight(font)));
                TextRenderer.DrawText(g, action, font, text, Theme.Alpha(Theme.Accent, 205),
                    ActionFlags);
            }
        }

        private static bool IsAxisAlignedRect(PointF[] points)
        {
            for (int i = 0; i < points.Length; i++)
            {
                PointF a = points[i];
                PointF b = points[(i + 1) % points.Length];
                if (Math.Abs(a.X - b.X) > 0.01f && Math.Abs(a.Y - b.Y) > 0.01f) return false;
            }
            return true;
        }
    }
}
