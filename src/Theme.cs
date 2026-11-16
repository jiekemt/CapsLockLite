using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CapsLockLite
{
    /// <summary>
    /// Fluent / Windows 11 design tokens: one static palette, one metric scale.
    /// Everything visual is expressed here so a theme switch is a palette swap.
    /// </summary>
    internal static class Theme
    {
        public static bool IsDark { get; private set; }

        private static float _dpi = 1f;
        private static int _dpiValue = 96;

        /// <summary>Current scale in dots per inch (96 = 100%).</summary>
        public static int Dpi { get { return _dpiValue; } }

        // ---- palette -------------------------------------------------------
        public static Color Background;
        public static Color Surface;        // cards, inputs
        public static Color SurfaceAlt;     // selected nav, chips
        public static Color SurfaceHover;
        public static Color SurfacePressed;
        public static Color Layer;          // inset wells
        public static Color Stroke;
        public static Color StrokeStrong;
        public static Color TextPrimary;
        public static Color TextSecondary;
        public static Color TextTertiary;
        public static Color TextDisabled;
        public static Color Accent;
        public static Color AccentHover;
        public static Color AccentPressed;
        public static Color AccentSoft;
        public static Color OnAccent;
        public static Color Success;
        public static Color Warning;
        public static Color Danger;
        public static Color DangerSoft;
        public static Color ScrollThumb;
        public static Color ScrollThumbHover;
        public static Color Shadow;
        public static Color NavHover;
        public static Color TitleBarText;

        // ---- type ----------------------------------------------------------
        public static readonly Font Display = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Title = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Subtitle = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Body = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font BodyStrong = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Caption = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font CaptionStrong = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font KeyCap = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
        public static readonly Font Mono = new Font("Consolas", 9.5f, FontStyle.Regular, GraphicsUnit.Point);

        // ---- metrics (logical px; multiply with S) -------------------------
        public const int RadiusCard = 8;
        public const int RadiusControl = 4;
        public const int RadiusChip = 6;
        public const int PagePadding = 28;
        public const int RowHeight = 56;
        public const int CtrlHeight = 32;

        static Theme()
        {
            Apply(false);
        }

        /// <summary>Called once per process with the monitor DPI.</summary>
        public static void SetDpi(int dpi)
        {
            if (dpi <= 0) dpi = 96;
            _dpiValue = dpi;
            _dpi = dpi / 96f;
        }

        public static int S(int logical)
        {
            return (int)Math.Round(logical * _dpi);
        }

        public static float SF(float logical)
        {
            return logical * _dpi;
        }

        public static void Apply(bool dark)
        {
            IsDark = dark;
            Color accent = ReadSystemAccent();
            if (dark) ApplyDark(accent);
            else ApplyLight(accent);
        }

        private static void ApplyLight(Color accent)
        {
            Background = Color.FromArgb(243, 243, 243);
            Surface = Color.FromArgb(255, 255, 255);
            SurfaceAlt = Color.FromArgb(234, 234, 234);
            SurfaceHover = Color.FromArgb(248, 248, 248);
            SurfacePressed = Color.FromArgb(240, 240, 240);
            Layer = Color.FromArgb(246, 246, 246);
            Stroke = Color.FromArgb(229, 229, 229);
            StrokeStrong = Color.FromArgb(209, 209, 209);
            TextPrimary = Color.FromArgb(26, 26, 26);
            TextSecondary = Color.FromArgb(93, 93, 93);
            TextTertiary = Color.FromArgb(130, 130, 130);
            TextDisabled = Color.FromArgb(170, 170, 170);
            Accent = accent;
            AccentHover = Blend(accent, Color.Black, 0.10f);
            AccentPressed = Blend(accent, Color.Black, 0.22f);
            AccentSoft = Blend(accent, Color.White, 0.86f);
            OnAccent = Luminance(accent) > 0.62 ? Color.FromArgb(20, 20, 20) : Color.White;
            Success = Color.FromArgb(15, 123, 15);
            Warning = Color.FromArgb(157, 93, 0);
            Danger = Color.FromArgb(196, 43, 28);
            DangerSoft = Color.FromArgb(253, 240, 238);
            ScrollThumb = Color.FromArgb(150, 150, 150);
            ScrollThumbHover = Color.FromArgb(110, 110, 110);
            Shadow = Color.FromArgb(18, 0, 0, 0);
            NavHover = Color.FromArgb(238, 238, 238);
            TitleBarText = Color.FromArgb(26, 26, 26);
        }

        private static void ApplyDark(Color accent)
        {
            // Lift the accent so it stays readable on dark surfaces.
            accent = Blend(accent, Color.White, 0.28f);
            Background = Color.FromArgb(32, 32, 32);
            Surface = Color.FromArgb(43, 43, 43);
            SurfaceAlt = Color.FromArgb(58, 58, 58);
            SurfaceHover = Color.FromArgb(52, 52, 52);
            SurfacePressed = Color.FromArgb(60, 60, 60);
            Layer = Color.FromArgb(38, 38, 38);
            Stroke = Color.FromArgb(64, 64, 64);
            StrokeStrong = Color.FromArgb(82, 82, 82);
            TextPrimary = Color.FromArgb(255, 255, 255);
            TextSecondary = Color.FromArgb(200, 200, 200);
            TextTertiary = Color.FromArgb(158, 158, 158);
            TextDisabled = Color.FromArgb(120, 120, 120);
            Accent = accent;
            AccentHover = Blend(accent, Color.White, 0.12f);
            AccentPressed = Blend(accent, Color.White, 0.24f);
            AccentSoft = Blend(accent, Color.FromArgb(43, 43, 43), 0.78f);
            OnAccent = Luminance(accent) > 0.62 ? Color.FromArgb(20, 20, 20) : Color.White;
            Success = Color.FromArgb(108, 203, 95);
            Warning = Color.FromArgb(252, 225, 0);
            Danger = Color.FromArgb(255, 153, 164);
            DangerSoft = Color.FromArgb(66, 34, 34);
            ScrollThumb = Color.FromArgb(130, 130, 130);
            ScrollThumbHover = Color.FromArgb(170, 170, 170);
            Shadow = Color.FromArgb(40, 0, 0, 0);
            NavHover = Color.FromArgb(48, 48, 48);
            TitleBarText = Color.White;
        }

        /// <summary>Reads the Windows accent colour so the app follows the system.</summary>
        public static Color ReadSystemAccent()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\DWM", false))
                {
                    if (key != null)
                    {
                        object raw = key.GetValue("ColorizationColor");
                        if (raw is int)
                        {
                            int argb = (int)raw;
                            Color c = Color.FromArgb(255,
                                (argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF);
                            if (Luminance(c) > 0.06f) return c;
                        }
                    }
                }
            }
            catch { }
            return Color.FromArgb(0, 103, 192); // Windows 11 default blue
        }

        public static float Luminance(Color c)
        {
            return (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) / 255f;
        }

        public static Color Blend(Color from, Color to, float amount)
        {
            if (amount < 0f) amount = 0f;
            if (amount > 1f) amount = 1f;
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * amount),
                (int)(from.G + (to.G - from.G) * amount),
                (int)(from.B + (to.B - from.B) * amount));
        }

        public static Color Alpha(Color c, int alpha)
        {
            if (alpha < 0) alpha = 0;
            if (alpha > 255) alpha = 255;
            return Color.FromArgb(alpha, c);
        }

        // ---- drawing helpers ----------------------------------------------
        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float max = Math.Min(r.Width, r.Height) / 2f;
            float rad = Math.Max(1f, Math.Min(radius, Math.Max(1f, max)));
            float d = rad * 2f;
            if (d <= 0.5f)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180f, 90f);
            path.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
            path.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, RectangleF r, float radius, Color color)
        {
            if (r.Width <= 0f || r.Height <= 0f) return;
            using (GraphicsPath p = Rounded(r, radius))
            using (SolidBrush b = new SolidBrush(color))
                g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, RectangleF r, float radius, Color color, float width)
        {
            if (r.Width <= 0f || r.Height <= 0f) return;
            RectangleF rr = new RectangleF(r.X + width / 2f, r.Y + width / 2f,
                Math.Max(0.5f, r.Width - width), Math.Max(0.5f, r.Height - width));
            using (GraphicsPath p = Rounded(rr, radius))
            using (Pen pen = new Pen(color, width))
            {
                pen.Alignment = PenAlignment.Center;
                g.DrawPath(pen, p);
            }
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static Font FontFor(bool strong, int sizeKey)
        {
            if (sizeKey == 0) return strong ? BodyStrong : Body;
            if (sizeKey == 1) return strong ? CaptionStrong : Caption;
            return strong ? Subtitle : Body;
        }

        /// <summary>
        /// Adds a dialog title plus optional subtitle and returns the Y where the
        /// first content block should start. Stacking by measured height keeps the
        /// subtitle clear of a large title.
        /// </summary>
        public static int DialogHeader(Form form, string title, string subtitle)
        {
            int x = Theme.S(22);
            int width = Math.Max(Theme.S(120), form.ClientSize.Width - Theme.S(44));

            Label titleLabel = MakeLine(title, Theme.Display, Theme.TextPrimary, x, Theme.S(14), width);
            form.Controls.Add(titleLabel);

            int y = titleLabel.Bottom + Theme.S(1);
            if (!string.IsNullOrEmpty(subtitle))
            {
                Label subtitleLabel = MakeLine(subtitle, Theme.Caption, Theme.TextTertiary, x, y, width);
                form.Controls.Add(subtitleLabel);
                y = subtitleLabel.Bottom;
            }
            return y + Theme.S(10);
        }

        public static Label MakeLabel(string text, Font font, Color color, int x, int y, int w, int h)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = font;
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.Location = new Point(x, y);
            // Never hand back a box shorter than one rendered line: that is what
            // silently clipped the bottom of Chinese text before.
            label.Size = new Size(w, Math.Max(h, LineHeight(font)));
            label.AutoSize = false;
            label.UseMnemonic = false;
            return label;
        }

        /// <summary>Single-line text with ellipsis, vertically centred.</summary>
        public static void DrawText(Graphics g, string text, Font font, Rectangle bounds, Color color,
            TextFormatFlags extra)
        {
            if (string.IsNullOrEmpty(text) || bounds.Width <= 0) return;
            TextRenderer.DrawText(g, text, font, bounds, color,
                TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | extra);
        }

        /// <summary>Measures a single line the same way DrawText renders it.</summary>
        public static Size Measure(string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return Size.Empty;
            return TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding);
        }

        /// <summary>
        /// Height of one rendered line including the internal leading GDI adds.
        /// Undersized labels clip the bottom of CJK glyphs, so measure generously.
        /// </summary>
        public static int LineHeight(Font font)
        {
            Size size = TextRenderer.MeasureText("国Ag", font);
            return Math.Max(font.Height + 6, size.Height);
        }

        /// <summary>
        /// Height needed to show every line of <paramref name="text"/> within
        /// <paramref name="width"/> without clipping. Explicit line breaks are
        /// honoured and long lines wrap instead of being cut off.
        /// </summary>
        public static int BlockHeight(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) return LineHeight(font);
            Size size = TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            return Math.Max(LineHeight(font), size.Height + 4);
        }

        /// <summary>
        /// Label sized to fit its text vertically. Width stays authoritative, so a
        /// long paragraph wraps to as many lines as it needs.
        /// </summary>
        public static Label MakeBlock(string text, Font font, Color color, int x, int y, int width)
        {
            Label label = MakeLabel(text, font, color, x, y, width, BlockHeight(text, font, width));
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        /// <summary>Single-line label that never clips its descenders.</summary>
        public static Label MakeLine(string text, Font font, Color color, int x, int y, int width)
        {
            Label label = MakeLabel(text, font, color, x, y, width, LineHeight(font) + 2);
            label.AutoEllipsis = true;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }
    }
}
