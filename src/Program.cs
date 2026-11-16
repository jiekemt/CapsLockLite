using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CapsLockLite
{
    internal static class Program
    {
        private const string MutexName = "Local\\CapsLockLite.SingleInstance";
        private static Mutex _instanceMutex;

        [STAThread]
        private static void Main(string[] args)
        {
            if (HasArg(args, "--selftest"))
            {
                SelfTest.Run();
                return;
            }
            EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += ApplicationThreadException;
            AppDomain.CurrentDomain.UnhandledException += DomainUnhandledException;

            // A restart spawned from inside the app carries the old pid so the
            // new process does not show a window for the instance that is still
            // shutting down.
            int waitPid = ParseInt(GetArgValue(args, "--restart-wait="));
            if (waitPid > 0) WaitForProcessExit(waitPid, 5000);

            bool created;
            Mutex mutex = new Mutex(true, MutexName, out created);
            _instanceMutex = mutex;
            using (mutex)
            {
                if (!created)
                {
                    ActivateRunningInstance();
                    return;
                }

                bool minimized = HasArg(args, "--minimized");
                string tab = GetArgValue(args, "--tab=");
                // Developer screenshot modes are compiled out of release builds.
                // In particular there is no --demo=import path in the release
                // binary that could compile and run a plugin from the command line.
#if DEBUG
                string demo = GetArgValue(args, "--demo=");
#else
                string demo = "";
#endif
                Application.Run(new MainForm(minimized, tab, demo));
            }
        }

        private static bool HasArg(string[] args, string name)
        {
            foreach (string arg in args)
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static int ParseInt(string text)
        {
            int value;
            if (int.TryParse(text, out value)) return value;
            return 0;
        }

        private static void WaitForProcessExit(int pid, int timeoutMs)
        {
            try
            {
                using (Process old = Process.GetProcessById(pid))
                    old.WaitForExit(timeoutMs);
            }
            catch (Exception)
            {
                // Already gone (or not ours): the mutex below decides.
            }
        }

        /// <summary>
        /// Drops the single-instance mutex so a freshly spawned copy can start,
        /// then starts that copy with the current pid attached.
        /// </summary>
        internal static void RestartApplication()
        {
            string exe = Application.ExecutablePath;
            try
            {
                Mutex mutex = _instanceMutex;
                _instanceMutex = null;
                if (mutex != null)
                {
                    try { mutex.ReleaseMutex(); }
                    catch (Exception) { }
                    mutex.Dispose();
                }

                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = exe;
                info.Arguments = "--restart-wait=" + Process.GetCurrentProcess().Id.ToString();
                info.UseShellExecute = false;
                info.WorkingDirectory = Path.GetDirectoryName(exe);
                Process.Start(info);
            }
            catch (Exception ex)
            {
                ConfigStore.Log("自动重启失败：" + ex.Message);
            }
        }


        private static string GetArgValue(string[] args, string prefix)
        {
            foreach (string arg in args)
                if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return arg.Substring(prefix.Length);
            return "";
        }

        private static void EnableDpiAwareness()
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 10)
                    Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
                else
                    Native.SetProcessDPIAware();
            }
            catch
            {
                try { Native.SetProcessDPIAware(); }
                catch { }
            }
        }

        private static void ActivateRunningInstance()
        {
            IntPtr hwnd = Native.FindWindow(null, "CapsLock Lite");
            if (hwnd != IntPtr.Zero)
            {
                // Let the running instance show itself; see Native.WM_ACTIVATE_INSTANCE.
                if (Native.WM_ACTIVATE_INSTANCE != 0)
                {
                    Native.PostMessage(hwnd, Native.WM_ACTIVATE_INSTANCE, IntPtr.Zero, IntPtr.Zero);
                }
                else
                {
                    Native.ShowWindowAsync(hwnd, Native.SW_SHOW);
                }
                Native.SetForegroundWindow(hwnd);
            }
            else
            {
                MDialog.Tell(null, "CapsLock Lite",
                    "CapsLock Lite 已经在运行，请在系统托盘中找到它的图标。", MDialogKind.Info);
            }
        }

        private static void ApplicationThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ConfigStore.Log("未处理的界面异常：" + e.Exception);
            MessageBox.Show("程序遇到一个错误，但仍在运行。\r\n\r\n" + e.Exception.Message,
                "CapsLock Lite", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static void DomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ConfigStore.Log("未处理的异常：" + e.ExceptionObject);
        }
    }
}
