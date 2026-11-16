using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace CapsLockLite
{
    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "CapsLockLite";
        private const string StartupFile = "CapsLock Lite.cmd";
        private static readonly UIntPtr HKeyCurrentUser = new UIntPtr(0x80000001);
        private const uint KeySetValue = 0x0002;
        private const uint KeyQueryValue = 0x0001;
        private const uint RegSz = 1;

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegOpenKeyEx(UIntPtr hKey, string subKey, uint options, uint samDesired, out IntPtr result);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegSetValueEx(IntPtr hKey, string valueName, uint reserved, uint type, byte[] data, uint dataSize);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegQueryValueEx(IntPtr hKey, string valueName, IntPtr reserved, out uint type, byte[] data, ref uint dataSize);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegCloseKey(IntPtr hKey);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int RegDeleteValue(IntPtr hKey, string valueName);

        public static string LastMethod { get; private set; }
        public static string LastError { get; private set; }

        public static bool Apply(bool enabled, bool startMinimized)
        {
            LastMethod = "";
            LastError = "";
            if (!enabled)
            {
                TryRegistryDelete();
                TryStartupDelete();
                LastMethod = "removed";
                return true;
            }

            string command = CommandLine(startMinimized);
            if (TryRegistrySet(command))
            {
                TryStartupDelete();
                LastMethod = "registry";
                return true;
            }
            if (TryStartupSet(startMinimized))
            {
                LastMethod = "startup-folder";
                return true;
            }
            if (string.IsNullOrEmpty(LastError))
                LastError = "无法写入注册表或启动文件夹。";
            return false;
        }

        public static bool IsRegistered()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key != null && key.GetValue(ValueName) != null) return true;
                }
            }
            catch { }
            if (TryRegistryNativeExists()) return true;
            return File.Exists(StartupFilePath);
        }

        public static string CurrentCommand()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return "";
                    object value = key.GetValue(ValueName);
                    return value == null ? "" : value.ToString();
                }
            }
            catch
            {
                return "";
            }
        }

        public static string StartupFilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupFile);
            }
        }

        private static string CommandLine(bool startMinimized)
        {
            string command = "\"" + Application.ExecutablePath + "\"";
            if (startMinimized) command += " --minimized";
            return command;
        }

        private static bool TryRegistrySet(string command)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) throw new InvalidOperationException("Run 注册表项不存在。");
                    key.SetValue(ValueName, command, RegistryValueKind.String);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                ConfigStore.Log("注册表方式设置开机启动失败：" + ex.Message);
            }

            if (TryRegistryNativeSet(command))
            {
                LastMethod = "registry-native";
                return true;
            }

            // A restricted .NET process may be denied the Registry API while a
            // separate reg.exe process is still allowed to write HKCU.
            string data = command.Replace("\"", "\\\"");
            string arguments = "add \"HKCU\\" + RunKey + "\" /v " + ValueName +
                " /t REG_SZ /d \"" + data + "\" /f";
            if (RunProcess("reg.exe", arguments, 5000))
            {
                LastMethod = "registry-reg";
                return true;
            }
            return false;
        }

        private static void TryRegistryDelete()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key != null && key.GetValue(ValueName) != null)
                        key.DeleteValue(ValueName, false);
                    return;
                }
            }
            catch { }

            TryRegistryNativeDelete();
            RunProcess("reg.exe", "delete \"HKCU\\" + RunKey + "\" /v " + ValueName + " /f", 3000);
        }

        private static bool TryRegistryNativeSet(string command)
        {
            IntPtr key;
            int result = RegOpenKeyEx(HKeyCurrentUser, RunKey, 0, KeySetValue, out key);
            if (result != 0)
            {
                LastError = "注册表打开失败，Win32 错误 " + result;
                return false;
            }
            try
            {
                byte[] bytes = Encoding.Unicode.GetBytes(command + "\0");
                result = RegSetValueEx(key, ValueName, 0, RegSz, bytes, (uint)bytes.Length);
                if (result != 0)
                {
                    LastError = "注册表写入失败，Win32 错误 " + result;
                    return false;
                }
                return true;
            }
            finally
            {
                RegCloseKey(key);
            }
        }

        private static void TryRegistryNativeDelete()
        {
            IntPtr key;
            if (RegOpenKeyEx(HKeyCurrentUser, RunKey, 0, KeySetValue, out key) == 0)
            {
                try { RegDeleteValue(key, ValueName); }
                finally { RegCloseKey(key); }
            }
        }

        private static bool TryRegistryNativeExists()
        {
            IntPtr key;
            if (RegOpenKeyEx(HKeyCurrentUser, RunKey, 0, KeyQueryValue, out key) != 0) return false;
            try
            {
                uint type;
                uint size = 2048;
                byte[] data = new byte[size];
                return RegQueryValueEx(key, ValueName, IntPtr.Zero, out type, data, ref size) == 0;
            }
            finally
            {
                RegCloseKey(key);
            }
        }

        private static bool TryStartupSet(bool startMinimized)
        {
            try
            {
                string path = StartupFilePath;
                string command = "start \"\" \"" + Application.ExecutablePath + "\"";
                if (startMinimized) command += " --minimized";
                string content = "@echo off\r\n" + command + "\r\n";
                File.WriteAllText(path, content, Encoding.Default);
                LastMethod = "startup-folder";
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                ConfigStore.Log("启动文件夹方式设置开机启动失败：" + ex.Message);
                return false;
            }
        }

        private static void TryStartupDelete()
        {
            try
            {
                string path = StartupFilePath;
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        private static bool RunProcess(string fileName, string arguments, int timeoutMs)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = fileName;
                info.Arguments = arguments;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WindowStyle = ProcessWindowStyle.Hidden;
                using (Process process = Process.Start(info))
                {
                    if (process == null) return false;
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch { }
                        return false;
                    }
                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                ConfigStore.Log("启动辅助进程失败：" + fileName + " " + ex.Message);
                return false;
            }
        }

    }
}
