using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CapsLockLite;

namespace CapsLockLite.Samples
{
    /// <summary>
    /// 示例插件：把全部屏幕或当前前台窗口保存为 PNG。
    ///
    /// 导入方式：主界面「插件 → 导入插件…」，选择本文件即可。
    /// 导入后立即可在「快捷键 → 新增/编辑 → 执行动作」里选到「截图 · 全屏截图」。
    /// </summary>
    public sealed class ScreenshotPlugin : IPlugin
    {
        public string Id { get { return "com.capslocklite.screenshot"; } }
        public string Name { get { return "截图"; } }
        public string Version { get { return "1.0.0"; } }
        public string Author { get { return "CapsLock Lite 示例"; } }
        public string Description { get { return "把全部屏幕或当前前台窗口保存为 PNG，并复制文件路径。"; } }

        public IPluginAction[] CreateActions()
        {
            return new IPluginAction[]
            {
                new FullScreenAction(),
                new ActiveWindowAction()
            };
        }
    }

    internal sealed class FullScreenAction : IPluginAction
    {
        public string Id { get { return "fullscreen"; } }
        public string Category { get { return "截图"; } }
        public string Name { get { return "全屏截图"; } }
        public string Description { get { return "截取所有屏幕的可见内容；保存文件夹留空时存到图片库下的 CapsLockLite。"; } }
        public ActionParamKind ParamKind { get { return ActionParamKind.Folder; } }
        public string DefaultParam { get { return string.Empty; } }
        public bool Repeatable { get { return false; } }

        public void Execute(PluginContext context)
        {
            Shot.ReleaseKeys();
            string path = Shot.CaptureScreen(SystemInformation.VirtualScreen, context.Param);
            Shot.Publish(context, path);
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
            Shot.ReleaseKeys();
            string path = Shot.CaptureWindow(context.Param);
            Shot.Publish(context, path);
        }
    }

    internal static class Shot
    {
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

        /// <summary>
        /// The chord keys are still physically down when Execute runs, so give them
        /// a moment to be released before the pixels are sampled.
        /// </summary>
        public static void ReleaseKeys()
        {
            System.Threading.Thread.Sleep(260);
        }

        public static string CaptureScreen(Rectangle bounds, string folderParam)
        {
            int width = Math.Max(1, bounds.Width);
            int height = Math.Max(1, bounds.Height);
            using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size,
                        CopyPixelOperation.SourceCopy);
                }
                return Save(bitmap, "全屏", folderParam);
            }
        }

        public static string CaptureWindow(string folderParam)
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) throw new InvalidOperationException("没有可截取的前台窗口。");

            RECT rect;
            if (!GetWindowRect(hwnd, out rect)) throw new InvalidOperationException("无法读取窗口位置。");

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) throw new InvalidOperationException("窗口尺寸无效。");

            using (Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    IntPtr hdc = graphics.GetHdc();
                    bool printed;
                    try { printed = PrintWindow(hwnd, hdc, 2); }
                    finally { graphics.ReleaseHdc(hdc); }

                    if (!printed)
                    {
                        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size,
                            CopyPixelOperation.SourceCopy);
                    }
                }
                return Save(bitmap, "窗口", folderParam);
            }
        }

        public static void Publish(PluginContext context, string path)
        {
            try { Clipboard.SetText(path); } catch { }
            context.Report("截图已保存：" + path);
            context.Log("saved " + path);
        }

        private static string Save(Bitmap bitmap, string prefix, string folderParam)
        {
            string folder = ResolveFolder(folderParam);
            Directory.CreateDirectory(folder);

            string name = prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png";
            string path = Path.Combine(folder, name);
            bitmap.Save(path, ImageFormat.Png);
            return path;
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
}
