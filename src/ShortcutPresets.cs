using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CapsLockLite
{
    /// <summary>
    /// Resolves the program that owns the foreground window. Called from the
    /// keyboard hook, so the process lookup is cached per window handle: the same
    /// window always belongs to the same process, and a switch to another window
    /// (even inside one app) triggers a fresh lookup.
    /// </summary>
    internal static class ForegroundApp
    {
        private static IntPtr _lastWindow = IntPtr.Zero;
        private static string _lastName = "";

        /// <summary>Executable name without extension; empty when it cannot be read.</summary>
        public static string CurrentProcessName()
        {
            IntPtr window = Native.GetForegroundWindow();
            if (window == IntPtr.Zero) return "";
            if (window == _lastWindow) return _lastName;

            string name = ReadProcessName(window);
            _lastWindow = window;
            _lastName = name;
            return name;
        }

        public static string ProcessNameForWindow(IntPtr window)
        {
            if (window == IntPtr.Zero) return "";
            return ReadProcessName(window);
        }

        private static string ReadProcessName(IntPtr window)
        {
            try
            {
                uint pid;
                Native.GetWindowThreadProcessId(window, out pid);
                if (pid == 0) return "";
                using (Process process = Process.GetProcessById((int)pid))
                {
                    return ConfigStore.NormalizeProcessName(process.ProcessName);
                }
            }
            catch
            {
                // The window can belong to a process that already exited, or to a
                // process we are not allowed to open. Both mean "no match".
                return "";
            }
        }
    }

    /// <summary>
    /// Chooses which shortcut list the engine should use. The global list is the
    /// fallback; a preset replaces it only while its program owns the foreground
    /// window and only when it has been bound to one.
    /// </summary>
    internal static class ShortcutSets
    {
        public static List<KeyMapping> Active(AppSettings settings)
        {
            if (settings == null) return new List<KeyMapping>();
            return ForProcess(settings, ForegroundApp.CurrentProcessName());
        }

        public static List<KeyMapping> ForProcess(AppSettings settings, string process)
        {
            if (settings == null) return new List<KeyMapping>();
            if (settings.Mappings == null) settings.Mappings = new List<KeyMapping>();
            if (string.IsNullOrEmpty(process)) return settings.Mappings;

            List<ShortcutPreset> presets = settings.Presets;
            if (presets != null)
            {
                // Normalize both sides so "code.exe", "Code.exe" and a stored full
                // path all compare equal; the hook passes a bare name, tests and
                // imported files may not.
                string wanted = ConfigStore.NormalizeProcessName(process);
                if (wanted.Length == 0) return settings.Mappings;
                for (int i = 0; i < presets.Count; i++)
                {
                    ShortcutPreset preset = presets[i];
                    if (preset == null || preset.Mappings == null) continue;
                    if (string.Equals(ConfigStore.NormalizeProcessName(preset.Process), wanted,
                        StringComparison.OrdinalIgnoreCase))
                        return preset.Mappings;
                }
            }
            return settings.Mappings;
        }

        /// <summary>
        /// Every shortcut list, global first. Used for conflict reporting, macro
        /// bound counts and anything else that must see presets as well.
        /// </summary>
        public static List<List<KeyMapping>> All(AppSettings settings)
        {
            List<List<KeyMapping>> lists = new List<List<KeyMapping>>();
            if (settings == null) return lists;
            lists.Add(settings.Mappings ?? new List<KeyMapping>());
            if (settings.Presets != null)
            {
                for (int i = 0; i < settings.Presets.Count; i++)
                {
                    ShortcutPreset preset = settings.Presets[i];
                    if (preset == null) continue;
                    if (preset.Mappings == null) preset.Mappings = new List<KeyMapping>();
                    lists.Add(preset.Mappings);
                }
            }
            return lists;
        }

        public static ShortcutPreset FindByProcess(AppSettings settings, string process)
        {
            if (settings == null || settings.Presets == null) return null;
            string wanted = ConfigStore.NormalizeProcessName(process);
            if (wanted.Length == 0) return null;
            for (int i = 0; i < settings.Presets.Count; i++)
            {
                ShortcutPreset preset = settings.Presets[i];
                if (preset == null) continue;
                if (string.Equals(ConfigStore.NormalizeProcessName(preset.Process), wanted,
                    StringComparison.OrdinalIgnoreCase))
                    return preset;
            }
            return null;
        }

        public static ShortcutPreset FindById(AppSettings settings, string id)
        {
            if (settings == null || settings.Presets == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < settings.Presets.Count; i++)
            {
                ShortcutPreset preset = settings.Presets[i];
                if (preset != null && string.Equals(preset.Id, id, StringComparison.Ordinal)) return preset;
            }
            return null;
        }
    }
}
