using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CapsLockLite;

namespace CapsLockLite.Samples.Pro
{
    /// <summary>
    /// 完整版截图插件：全屏、活动窗口、自定义范围（带实时预览与框选）。
    ///
    /// 范围截图完全由插件自带的全屏覆盖窗体实现，不依赖宿主提供任何 UI 接口，
    /// 因此不需要修改插件契约。
    /// </summary>
    public sealed class ScreenshotProPlugin : IPlugin
    {
        public string Id { get { return "com.capslocklite.screenshot.pro"; } }
        public string Name { get { return "截图（完整版）"; } }
        public string Version { get { return "1.2.0"; } }
        public string Author { get { return "CapsLock Lite 示例"; } }
        public string Description { get { return "全屏、活动窗口、自定义范围截图；范围截图带暗化预览、放大镜与选区调整。"; } }

        public IPluginAction[] CreateActions()
        {
            return new IPluginAction[]
            {
                new FullScreenAction(),
                new ActiveWindowAction(),
                new RegionAction()
            };
        }
    }

    internal sealed class FullScreenAction : IPluginAction
    {
        public string Id { get { return "fullscreen"; } }
        public string Category { get { return "截图"; } }
        public string Name { get { return "全屏截图"; } }
        public string Description { get { return "截取所有显示器的可见内容；保存文件夹留空时存到图片库下的 CapsLockLite。"; } }
        public ActionParamKind ParamKind { get { return ActionParamKind.Folder; } }
        public string DefaultParam { get { return string.Empty; } }
        public bool Repeatable { get { return false; } }

        public void Execute(PluginContext context)
        {
            if (Shot.Busy)
            {
                context.Report("正在选择截图区域，请先完成或取消。");
                return;
            }

            Shot.ReleaseKeys();
            using (Bitmap shot = Shot.CaptureVirtualScreen())
            {
                if (shot == null)
                {
                    context.Error("无法捕获屏幕。");
                    return;
                }
                Shot.Publish(context, Shot.Save(shot, "全屏", context.Param));
            }
        }
    }

    internal sealed class ActiveWindowAction : IPluginAction
    {
        public string Id { get { return "active-window"; } }
        public string Category { get { return "截图"; } }
        public string Name { get { return "活动窗口截图"; } }
        public string Description { get { return "截取当前前台窗口；保存文件夹留空时存到图片库下的 CapsLockLite。"; } }
        public ActionParamKind ParamKind { get { return ActionParamKind.Folder; } }
        public string DefaultParam { get { return string.Empty; } }
        public bool Repeatable { get { return false; } }

        public void Execute(PluginContext context)
        {
            if (Shot.Busy)
            {
                context.Report("正在选择截图区域，请先完成或取消。");
                return;
            }

            Shot.ReleaseKeys();
            using (Bitmap shot = Shot.CaptureActiveWindow())
            {
                if (shot == null)
                {
                    context.Error("没有可截取的前台窗口。");
                    return;
                }
                Shot.Publish(context, Shot.Save(shot, "窗口", context.Param));
            }
        }
    }

    internal sealed class RegionAction : IPluginAction
    {
        public string Id { get { return "region"; } }
        public string Category { get { return "截图"; } }
        public string Name { get { return "自定义范围截图"; } }
        public string Description { get { return "先冻结屏幕，再拖拽框选；选区实时预览，可移动/缩放，Enter 保存、Esc 取消。"; } }
        public ActionParamKind ParamKind { get { return ActionParamKind.Folder; } }
        public string DefaultParam { get { return string.Empty; } }
        public bool Repeatable { get { return false; } }

        public void Execute(PluginContext context)
        {
            if (!RegionSelectorForm.TryBegin())
            {
                context.Report("已经有一个范围选择窗口在等待操作。");
                return;
            }

            try
            {
                // Give the chord keys a moment to be released, then freeze the
                // screen *before* showing the overlay so the overlay itself can
                // never end up in the capture.
                Shot.ReleaseKeys();
                Shot.Busy = true;

                Rectangle bounds = SystemInformation.VirtualScreen;
                using (Bitmap screen = Shot.CaptureVirtualScreen())
                {
                    if (screen == null)
                    {
                        context.Error("无法捕获屏幕。");
                        return;
                    }

                    using (RegionSelectorForm selector = new RegionSelectorForm(screen, bounds))
                    {
                        if (selector.ShowDialog() != DialogResult.OK || selector.SelectionImage == null)
                        {
                            context.Report("已取消区域截图。");
                            return;
                        }

                        using (Bitmap selection = selector.SelectionImage)
                        {
                            context.Log("区域截图：画布 " + bounds.Width + "x" + bounds.Height
                                + "，选区 " + selection.Width + "x" + selection.Height);
                            Shot.Publish(context, Shot.Save(selection, "区域", context.Param));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                context.Error("区域截图失败：" + ex.Message);
                context.Log(ex.ToString());
            }
            finally
            {
                Shot.Busy = false;
                RegionSelectorForm.End();
            }
        }
    }

    /// <summary>Screen capture and file helpers shared by the three actions.</summary>
    internal static class Shot
    {
        /// <summary>True while the region selector is open; other shots wait.</summary>
        public static bool Busy { get; set; }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public static void ReleaseKeys()
        {
            System.Threading.Thread.Sleep(240);
        }

        public static Bitmap CaptureVirtualScreen()
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            if (bounds.Width <= 0 || bounds.Height <= 0) return null;

            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size,
                        CopyPixelOperation.SourceCopy);
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                return null;
            }
        }

        public static Bitmap CaptureActiveWindow()
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            RECT rect;
            if (!GetWindowRect(hwnd, out rect)) return null;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return null;

            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            bool printed = false;
            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    IntPtr hdc = graphics.GetHdc();
                    try { printed = PrintWindow(hwnd, hdc, 2); }
                    finally { graphics.ReleaseHdc(hdc); }

                    if (!printed)
                    {
                        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size,
                            CopyPixelOperation.SourceCopy);
                    }
                }
                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                return null;
            }
        }

        public static string Save(Bitmap bitmap, string prefix, string folderParam)
        {
            string folder = ResolveFolder(folderParam);
            Directory.CreateDirectory(folder);

            string name = prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png";
            string path = Path.Combine(folder, name);
            bitmap.Save(path, ImageFormat.Png);
            return path;
        }

        public static void Publish(PluginContext context, string path)
        {
            try { Clipboard.SetText(path); } catch { }
            context.Report("截图已保存：" + path);
            context.Log("saved " + path);
        }

        private static string ResolveFolder(string folderParam)
        {
            string folder = folderParam == null ? string.Empty : folderParam.Trim().Trim('"');
            if (folder.Length == 0)
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CapsLockLite");
            }
            return Environment.ExpandEnvironmentVariables(folder);
        }
    }

    /// <summary>
    /// Full-screen overlay that freezes the desktop and lets the user pick a
    /// rectangle. The captured frame is used for both the dimmed background and
    /// the live preview inside the selection, so what you see is what gets saved.
    /// </summary>
    internal sealed class RegionSelectorForm : Form
    {
        private const int GripSize = 7;
        private const int MinSize = 4;

        private static bool _open;

        private static readonly Font HintFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
        private static readonly Font InfoFont = new Font("Consolas", 9f, FontStyle.Bold);

        private readonly Bitmap _screen;
        private readonly Rectangle _virtualBounds;

        private Rectangle _selection;
        private bool _hasSelection;
        private bool _dragging;
        private int _mode;
        private Point _origin;
        private Rectangle _startRect;
        private Point _cursor;
        private bool _cursorInside;

        public Bitmap SelectionImage { get; private set; }

        public static bool TryBegin()
        {
            if (_open) return false;
            _open = true;
            return true;
        }

        public static void End()
        {
            _open = false;
        }

        public RegionSelectorForm(Bitmap screen, Rectangle virtualBounds)
        {
            _screen = screen;
            _virtualBounds = virtualBounds;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = virtualBounds;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Color.Black;
            Cursor = Cursors.Cross;
            Text = "选择截图区域";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint, true);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            SetForegroundWindow(Handle);
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hwnd);

        // ---- painting ------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            g.DrawImage(_screen, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));

            using (SolidBrush dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                g.FillRectangle(dim, ClientRectangle);

            if (_hasSelection && _selection.Width >= MinSize && _selection.Height >= MinSize)
            {
                Rectangle selection = _selection;

                // The selection is redrawn at full brightness: this is the live
                // preview of exactly what will be saved.
                g.DrawImage(_screen, selection, selection, GraphicsUnit.Pixel);

                using (Pen border = new Pen(Color.FromArgb(0, 174, 255), 2f))
                    g.DrawRectangle(border, selection.Left, selection.Top,
                        selection.Width, selection.Height);

                DrawHandles(g, selection);
                DrawSizeBadge(g, selection);
            }

            if (_cursorInside) DrawMagnifier(g, _cursor);
            DrawHint(g);
        }

        private void DrawHandles(Graphics g, Rectangle selection)
        {
            using (SolidBrush fill = new SolidBrush(Color.White))
            using (Pen edge = new Pen(Color.FromArgb(0, 120, 190)))
            {
                Point[] corners = new Point[]
                {
                    new Point(selection.Left, selection.Top),
                    new Point(selection.Right, selection.Top),
                    new Point(selection.Left, selection.Bottom),
                    new Point(selection.Right, selection.Bottom)
                };
                for (int i = 0; i < corners.Length; i++)
                {
                    Rectangle box = new Rectangle(corners[i].X - GripSize / 2,
                        corners[i].Y - GripSize / 2, GripSize, GripSize);
                    g.FillRectangle(fill, box);
                    g.DrawRectangle(edge, box);
                }
            }
        }

        private void DrawSizeBadge(Graphics g, Rectangle selection)
        {
            string text = selection.Width + " × " + selection.Height;
            Size size = TextRenderer.MeasureText(text, InfoFont);
            int width = size.Width + 14;
            int height = size.Height + 6;

            int x = selection.Left;
            int y = selection.Top - height - 6;
            if (y < 4) y = selection.Top + 6;
            if (x + width > ClientSize.Width) x = ClientSize.Width - width - 4;
            if (x < 4) x = 4;

            Rectangle box = new Rectangle(x, y, width, height);
            using (SolidBrush back = new SolidBrush(Color.FromArgb(215, 0, 0, 0)))
            using (GraphicsPath path = Rounded(box, 4f))
                g.FillPath(back, path);

            TextRenderer.DrawText(g, text, InfoFont, box, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
        }

        private void DrawMagnifier(Graphics g, Point cursor)
        {
            const int zoom = 8;
            const int boxSize = 128;
            int sample = boxSize / zoom;

            Rectangle source = new Rectangle(cursor.X - sample / 2, cursor.Y - sample / 2,
                sample, sample);
            source.Intersect(new Rectangle(0, 0, _screen.Width, _screen.Height));
            if (source.Width <= 0 || source.Height <= 0) return;

            int x = cursor.X + 20;
            int y = cursor.Y + 20;
            if (x + boxSize > ClientSize.Width) x = cursor.X - boxSize - 20;
            if (y + boxSize > ClientSize.Height) y = cursor.Y - boxSize - 20;
            if (x < 0) x = 0;
            if (y < 0) y = 0;

            Rectangle target = new Rectangle(x, y, boxSize, boxSize);
            g.DrawImage(_screen, target, source, GraphicsUnit.Pixel);

            using (Pen frame = new Pen(Color.White, 1f))
                g.DrawRectangle(frame, target);

            int centerX = x + boxSize / 2;
            int centerY = y + boxSize / 2;
            using (Pen cross = new Pen(Color.FromArgb(255, 96, 96), 1f))
            {
                g.DrawLine(cross, centerX - 7, centerY, centerX + 7, centerY);
                g.DrawLine(cross, centerX, centerY - 7, centerX, centerY + 7);
            }

            string info = cursor.X + ", " + cursor.Y;
            Size text = TextRenderer.MeasureText(info, InfoFont);
            Rectangle label = new Rectangle(x, y + boxSize + 4, text.Width + 10, text.Height + 4);
            using (SolidBrush back = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                g.FillRectangle(back, label);
            TextRenderer.DrawText(g, info, InfoFont, label, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
        }

        private void DrawHint(Graphics g)
        {
            string hint = "拖动框选 · 方向键微调 · Enter 保存 · Esc 取消";
            Size size = TextRenderer.MeasureText(hint, HintFont);
            int width = size.Width + 24;
            int height = size.Height + 14;

            // Centre on the monitor under the cursor, not on the whole virtual
            // desktop: with staggered monitors the virtual centre can land in the
            // dead space between screens and the hint becomes invisible.
            Rectangle monitor = MonitorUnderCursor();

            int x = monitor.Left + (monitor.Width - width) / 2;
            int y = monitor.Top + 18;
            if (y + height > ClientSize.Height) y = ClientSize.Height - height - 18;
            if (x < 0) x = 0;

            Rectangle box = new Rectangle(x, y, width, height);

            using (SolidBrush back = new SolidBrush(Color.FromArgb(205, 0, 0, 0)))
            using (GraphicsPath path = Rounded(box, 6f))
                g.FillPath(back, path);

            TextRenderer.DrawText(g, hint, HintFont, box, Color.FromArgb(240, 240, 240),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
        }

        private Rectangle MonitorUnderCursor()
        {
            Screen screen;
            try { screen = Screen.FromPoint(Cursor.Position); }
            catch { screen = Screen.PrimaryScreen; }

            Rectangle local = new Rectangle(
                screen.Bounds.X - _virtualBounds.X,
                screen.Bounds.Y - _virtualBounds.Y,
                screen.Bounds.Width,
                screen.Bounds.Height);
            local.Intersect(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
            return local;
        }

        private static GraphicsPath Rounded(Rectangle bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            path.AddArc(bounds.X, bounds.Y, d, d, 180f, 90f);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270f, 90f);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0f, 90f);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        // ---- interaction ---------------------------------------------------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Right)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            int handle = HitHandle(e.Location);
            if (handle != 0)
            {
                _mode = handle;
                _startRect = _selection;
            }
            else if (_hasSelection && _selection.Contains(e.Location))
            {
                _mode = 2;
                _startRect = _selection;
            }
            else
            {
                _mode = 1;
                _startRect = Rectangle.Empty;
                _hasSelection = true;
                _selection = new Rectangle(e.Location, Size.Empty);
            }

            _origin = e.Location;
            _dragging = true;
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _cursor = e.Location;
            _cursorInside = true;

            if (_dragging)
            {
                switch (_mode)
                {
                    case 1:
                        _selection = Normalize(_origin, e.Location);
                        break;
                    case 2:
                        MoveSelection(e.Location);
                        break;
                    default:
                        ResizeSelection(e.Location);
                        break;
                }
                _selection = ClampToClient(_selection);
            }
            else if (_hasSelection && _selection.Contains(e.Location))
            {
                Cursor = Cursors.SizeAll;
            }
            else if (HitHandle(e.Location) != 0)
            {
                Cursor = Cursors.Cross;
            }
            else
            {
                Cursor = Cursors.Cross;
            }

            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging) return;

            _dragging = false;
            Capture = false;
            _mode = 0;

            if (_selection.Width < MinSize || _selection.Height < MinSize)
            {
                _hasSelection = false;
                _selection = Rectangle.Empty;
            }
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _cursorInside = false;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && _hasSelection && _selection.Contains(e.Location))
                Accept();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                    DialogResult = DialogResult.Cancel;
                    Close();
                    return true;

                case Keys.Enter:
                case Keys.Space:
                    Accept();
                    return true;

                case Keys.Control | Keys.A:
                    _hasSelection = true;
                    _selection = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
                    Invalidate();
                    return true;

                case Keys.Left:
                    Nudge(-1, 0);
                    return true;

                case Keys.Right:
                    Nudge(1, 0);
                    return true;

                case Keys.Up:
                    Nudge(0, -1);
                    return true;

                case Keys.Down:
                    Nudge(0, 1);
                    return true;

                case Keys.Shift | Keys.Left:
                    Nudge(-10, 0);
                    return true;

                case Keys.Shift | Keys.Right:
                    Nudge(10, 0);
                    return true;

                case Keys.Shift | Keys.Up:
                    Nudge(0, -10);
                    return true;

                case Keys.Shift | Keys.Down:
                    Nudge(0, 10);
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Nudge(int dx, int dy)
        {
            if (!_hasSelection) return;
            Rectangle moved = _selection;
            moved.Offset(dx, dy);
            _selection = ClampToClient(moved);
            Invalidate();
        }

        private void MoveSelection(Point cursor)
        {
            int dx = cursor.X - _origin.X;
            int dy = cursor.Y - _origin.Y;
            Rectangle moved = _startRect;
            moved.Offset(dx, dy);
            _selection = ClampToClient(moved);
        }

        private void ResizeSelection(Point cursor)
        {
            Rectangle start = _startRect;
            switch (_mode)
            {
                case 3:
                    _selection = Normalize(new Point(start.Right, start.Bottom), cursor);
                    break;
                case 4:
                    _selection = Normalize(new Point(start.Left, start.Bottom), cursor);
                    break;
                case 5:
                    _selection = Normalize(new Point(start.Right, start.Top), cursor);
                    break;
                case 6:
                    _selection = Normalize(new Point(start.Left, start.Top), cursor);
                    break;
            }
        }

        private int HitHandle(Point point)
        {
            if (!_hasSelection) return 0;
            Rectangle s = _selection;

            if (Near(point, s.Left, s.Top)) return 3;
            if (Near(point, s.Right, s.Top)) return 4;
            if (Near(point, s.Left, s.Bottom)) return 5;
            if (Near(point, s.Right, s.Bottom)) return 6;
            return 0;
        }

        private static bool Near(Point point, int x, int y)
        {
            return Math.Abs(point.X - x) <= GripSize && Math.Abs(point.Y - y) <= GripSize;
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            int left = Math.Min(a.X, b.X);
            int top = Math.Min(a.Y, b.Y);
            int right = Math.Max(a.X, b.X);
            int bottom = Math.Max(a.Y, b.Y);
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private Rectangle ClampToClient(Rectangle rect)
        {
            int left = Math.Max(0, rect.Left);
            int top = Math.Max(0, rect.Top);
            int right = Math.Min(ClientSize.Width, rect.Right);
            int bottom = Math.Min(ClientSize.Height, rect.Bottom);
            if (right < left) right = left;
            if (bottom < top) bottom = top;
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        private void Accept()
        {
            if (!_hasSelection) return;

            Rectangle selection = ClampToClient(_selection);
            if (selection.Width < 1 || selection.Height < 1) return;

            Bitmap result = new Bitmap(selection.Width, selection.Height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.DrawImage(_screen,
                    new Rectangle(0, 0, selection.Width, selection.Height),
                    selection, GraphicsUnit.Pixel);
            }

            SelectionImage = result;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (SelectionImage != null)
                {
                    SelectionImage.Dispose();
                    SelectionImage = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
