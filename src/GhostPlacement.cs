using System;
using System.Drawing;

namespace CapsLockLite
{
    /// <summary>
    /// Where a lifted card sits while it is dragged. Both surfaces clamp with this
    /// one rule, so a card never slides out of the surface painting it and never
    /// arrives at the other surface cut in half at an edge.
    /// </summary>
    internal static class GhostPlacement
    {
        /// <summary>Distance kept between the card and the surface edge.</summary>
        public static float Margin { get { return Theme.SF(2f); } }

        /// <summary>
        /// Top-left for a card of <paramref name="card"/> size inside a surface of
        /// <paramref name="pane"/> size. The card stops at the edge instead of
        /// leaving the surface; one bigger than the surface pins to the margin so
        /// only one side is ever clipped.
        /// </summary>
        public static PointF Clamp(PointF topLeft, SizeF card, Size pane)
        {
            float margin = Margin;
            float maxX = Math.Max(margin, pane.Width - card.Width - margin);
            float maxY = Math.Max(margin, pane.Height - card.Height - margin);
            return new PointF(
                Math.Min(Math.Max(topLeft.X, margin), maxX),
                Math.Min(Math.Max(topLeft.Y, margin), maxY));
        }

        /// <summary>Offset that moves a card rectangle back inside the surface.</summary>
        public static Point ClampOffset(RectangleF card, Size pane)
        {
            PointF inside = Clamp(card.Location, card.Size, pane);
            return new Point((int)Math.Round(inside.X - card.X),
                (int)Math.Round(inside.Y - card.Y));
        }
    }
}
