using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CapsLockLite
{
    internal static class ConfigStore
    {
        private static string _configDirectory;

        public static string ConfigDirectory
        {
            get
            {
                if (_configDirectory == null)
                    _configDirectory = ResolveConfigDirectory();
                return _configDirectory;
            }
        }

        /// <summary>
        /// Test-only override used by --selftest. It keeps the self-test away
        /// from the real %APPDATA% folder and from the user's plugins.
        /// </summary>
        internal static void UseTestDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory)) return;
            _configDirectory = Path.GetFullPath(directory);
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDirectory, "settings.json"); }
        }

        /// <summary>
        /// True when the config/plugin directory is not writable by a broad
        /// principal such as Everyone, Authenticated Users, Users or
        /// Interactive. Plugin loading is refused when this is false: otherwise
        /// another local user could plant plugin source.
        /// </summary>
        public static bool IsConfigDirectoryPrivate
        {
            get { return IsDirectoryPrivate(ConfigDirectory); }
        }

        public static string LogPath
        {
            get { return Path.Combine(ConfigDirectory, "error.log"); }
        }

        public static string DisplayConfigPath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(appData)
                    && ConfigPath.StartsWith(appData, StringComparison.OrdinalIgnoreCase))
                    return "%APPDATA%" + ConfigPath.Substring(appData.Length);
                return ConfigPath;
            }
        }

        private static string ResolveConfigDirectory()
        {
            // Keep all writable state inside a per-user directory. The old
            // CAPSLOCKLITE_DATA_DIR override and the "next to the exe" fallback
            // both allowed another user (or a redirect) to plant plugin source
            // that this process would later execute. Portable deployments should
            // use a per-user folder, not a shared one.
            string fallback = null;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
            {
                string candidate = Path.Combine(appData, "CapsLockLite");
                if (CanWrite(candidate))
                {
                    if (fallback == null) fallback = candidate;
                    if (IsDirectoryPrivate(candidate)) return candidate;
                }
            }

            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(local))
            {
                string candidate = Path.Combine(local, "CapsLockLite");
                if (CanWrite(candidate))
                {
                    if (fallback == null) fallback = candidate;
                    if (IsDirectoryPrivate(candidate)) return candidate;
                }
            }

            // Last resort: a per-user temp directory. It is still private to the
            // current user on a normal Windows installation.
            string temp = Path.Combine(Path.GetTempPath(), "CapsLockLite");
            if (CanWrite(temp))
            {
                if (fallback == null) fallback = temp;
                if (IsDirectoryPrivate(temp)) return temp;
            }

            if (fallback != null) return fallback;

            // Let the first real write report the failure instead of silently
            // using a path we know is unusable.
            return Path.Combine(appData ?? "", "CapsLockLite");
        }

        private static bool IsDirectoryPrivate(string directory)
        {
            try
            {
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return false;
                DirectoryInfo info = new DirectoryInfo(directory);
                DirectorySecurity security = info.GetAccessControl();
                AuthorizationRuleCollection rules = security.GetAccessRules(
                    true, true, typeof(SecurityIdentifier));

                const FileSystemRights writeMask =
                    FileSystemRights.WriteData | FileSystemRights.AppendData |
                    FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes |
                    FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles |
                    FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

                foreach (AuthorizationRule raw in rules)
                {
                    FileSystemAccessRule rule = raw as FileSystemAccessRule;
                    if (rule == null) continue;
                    if (rule.AccessControlType != AccessControlType.Allow) continue;
                    if ((rule.FileSystemRights & writeMask) == 0) continue;

                    string sid = rule.IdentityReference == null ? "" : rule.IdentityReference.Value;
                    if (sid == "S-1-1-0"          // Everyone
                        || sid == "S-1-5-4"       // INTERACTIVE
                        || sid == "S-1-5-7"       // ANONYMOUS LOGON
                        || sid == "S-1-5-11"      // Authenticated Users
                        || sid == "S-1-5-32-545"  // BUILTIN\Users
                        || sid == "S-1-5-32-546") // BUILTIN\Guests
                        return false;
                }
                return true;
            }
            catch
            {
                // If the ACL cannot be read, fail closed: the caller will still
                // use the directory for settings, but plugins will stay disabled.
                return false;
            }
        }

        private static bool CanWrite(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string probe = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "test");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        public static AppSettings LoadOrCreate()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    AppSettings fresh = CreateDefault();
                    Save(fresh);
                    return fresh;
                }

                string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                AppSettings settings = _serializer.Deserialize<AppSettings>(json);
                if (settings == null)
                    throw new InvalidDataException("配置文件内容为空。");
                Normalize(settings);
                return settings;
            }
            catch (Exception ex)
            {
                Log("加载配置失败：" + ex);
                TryBackupBrokenConfig();
                AppSettings fallback = CreateDefault();
                try { Save(fallback); }
                catch { }
                return fallback;
            }
        }

        public static AppSettings CreateDefault()
        {
            AppSettings s = new AppSettings();
            s.Mappings = ActionCatalog.CreateDefaultMappings();
            return s;
        }

        public static void Save(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            Normalize(settings);
            Directory.CreateDirectory(ConfigDirectory);
            string json = JsonPretty.Format(_serializer.Serialize(settings));
            string temp = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                if (File.Exists(ConfigPath))
                {
                    string backup = ConfigPath + ".bak";
                    try
                    {
                        File.Replace(temp, ConfigPath, backup, true);
                    }
                    catch (Exception)
                    {
                        // Some file systems do not support Replace; fall back to
                        // a copy while still keeping the old file until the new
                        // content has been written successfully.
                        File.Copy(temp, ConfigPath, true);
                        TryDelete(temp);
                    }
                }
                else
                {
                    File.Move(temp, ConfigPath);
                }
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        public static void Export(AppSettings settings, string path)
        {
            Normalize(settings);
            string json = JsonPretty.Format(_serializer.Serialize(settings));
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        public static AppSettings Import(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            AppSettings settings = _serializer.Deserialize<AppSettings>(json);
            if (settings == null) throw new InvalidDataException("不是有效的配置文件。");
            Normalize(settings);
            return settings;
        }

        public static string ValidateJson(string json)
        {
            try
            {
                AppSettings settings = _serializer.Deserialize<AppSettings>(json);
                if (settings == null) return "配置内容为空。";
                Normalize(settings);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        /// <summary>
        /// v2: "打开说明文档" (`system.help`) became "打开主界面" (`system.show`), and
        /// the default Caps+F1 follows it. Only the untouched default binding is
        /// rewritten, so a shortcut the user changed keeps whatever it has.
        /// </summary>
        private static void MigrateToVersion2(AppSettings settings)
        {
            if (settings.Mappings == null) return;
            for (int i = 0; i < settings.Mappings.Count; i++)
            {
                KeyMapping mapping = settings.Mappings[i];
                if (mapping == null) continue;
                if (!mapping.Caps || mapping.Ctrl || mapping.Alt || mapping.Shift || mapping.Win) continue;
                if (mapping.Key != (int)Keys.F1) continue;
                if (!string.Equals(mapping.Action, "system.help", StringComparison.Ordinal)) continue;
                mapping.Action = "system.show";
            }
        }

        public static void Normalize(AppSettings settings)
        {
            if (settings.ConfigVersion < 2) MigrateToVersion2(settings);
            settings.ConfigVersion = 6;
            if (settings.TapAction == null) settings.TapAction = "caps";
            if (settings.TapAction != "caps" && settings.TapAction != "esc"
                && settings.TapAction != "none" && settings.TapAction != "ime")
                settings.TapAction = "caps";
            if (settings.InputBackend == null) settings.InputBackend = InputPipeline.BackendAuto;
            if (settings.InputBackend != InputPipeline.BackendAuto
                && settings.InputBackend != InputPipeline.BackendSystem
                && settings.InputBackend != InputPipeline.BackendDriver)
                settings.InputBackend = InputPipeline.BackendAuto;
            if (settings.LongPressMs < 150) settings.LongPressMs = 150;
            if (settings.LongPressMs > 2000) settings.LongPressMs = 2000;
            if (settings.WheelStep < 40) settings.WheelStep = 40;
            if (settings.WheelStep > 1200) settings.WheelStep = 1200;
            if (settings.Mappings == null) settings.Mappings = new List<KeyMapping>();
            if (settings.PluginOrder == null) settings.PluginOrder = new List<string>();
            // The order list is a hint, not data: drop blanks and duplicates so a
            // hand-edited file cannot make the plugin page loop or mis-sort.
            List<string> order = new List<string>();
            HashSet<string> orderSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in settings.PluginOrder)
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (orderSeen.Add(name)) order.Add(name);
            }
            settings.PluginOrder = order;
            // A remembered window size outside any plausible desktop is treated as
            // "not set" so a config copied from a bigger machine cannot open the
            // window somewhere unreachable.
            if (settings.WindowWidth < 600 || settings.WindowWidth > 8000) settings.WindowWidth = 0;
            if (settings.WindowHeight < 400 || settings.WindowHeight > 8000) settings.WindowHeight = 0;

            settings.Mappings = NormalizeMappings(settings.Mappings);

            // v5: only the Caps layer exists. A mapping saved as a system-wide
            // hotkey would silently stop firing once registration is gone, so fold
            // it into the Caps layer instead of losing the shortcut.
            foreach (KeyMapping m in settings.Mappings)
            {
                if (m != null && !m.Caps) m.Caps = true;
            }

            NormalizePresets(settings);
            NormalizeMacros(settings);
        }

        /// <summary>
        /// Repairs one shortcut list. Shared by the global list and by every
        /// preset: ids, retired action ids, parameters, notes and the Caps-layer
        /// invariant are the same rules wherever a list comes from.
        /// </summary>
        public static List<KeyMapping> NormalizeMappings(List<KeyMapping> source)
        {
            List<KeyMapping> result = new List<KeyMapping>();
            if (source == null) return result;

            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count; i++)
            {
                KeyMapping m = source[i];
                if (m == null) continue;
                if (string.IsNullOrEmpty(m.Id) || ids.Contains(m.Id))
                    m.Id = Guid.NewGuid().ToString("N");
                ids.Add(m.Id);
                // A mapping without a main key can never fire, but dropping it here
                // used to delete hand-edited entries without a word. Keep it; the
                // editor is the place that refuses to save one without a key.
                // 1.14.1: single-key native actions were folded into input.keys.
                ActionCatalog.MigrateKeySendAction(m);
                ActionDef def = ActionCatalog.Get(m.Action);
                if (!string.Equals(def.Id, m.Action, StringComparison.Ordinal))
                {
                    m.Action = ActionCatalog.None;
                    def = ActionCatalog.Get(m.Action);
                }
                // A shortcut pointing at a plugin that is not installed keeps its
                // id, parameter and trigger mode. Normalising only live actions
                // means a temporarily missing plugin is not silent data loss.
                if (!def.Missing)
                {
                    m.Param = ActionCatalog.NormalizeParam(m.Action, m.Param);
                    // A wheel notch has no key-up, so a hand-edited "hold" on a
                    // wheel trigger can never repeat; the editor already locks
                    // this, normalize external files the same way.
                    if (!ActionCatalog.IsRepeatable(m.Action) || MouseKeys.IsWheel((Keys)m.Key))
                        m.TriggerMode = "once";
                    else if (m.TriggerMode != "hold" && m.TriggerMode != "once")
                        m.TriggerMode = "hold";
                }
                m.Note = m.Note == null ? "" : m.Note.Trim();
                if (m.Note.Length > 40) m.Note = m.Note.Substring(0, 40);
                // Only actions whose display text would otherwise be a raw key
                // combination (or mouse coordinates) keep a note; everything else
                // shows its own action name.
                if (!ActionCatalog.SupportsNote(m.Action)) m.Note = "";
                result.Add(m);
            }
            return result;
        }

        /// <summary>
        /// Presets are user data like macros: repair shapes here once instead of
        /// in every reader. One program can only own one preset, so later
        /// duplicates are detached (their shortcuts stay) rather than dropped.
        /// </summary>
        private static void NormalizePresets(AppSettings settings)
        {
            settings.Presets = NormalizePresetList(settings.Presets);
        }

        /// <summary>Repairs a preset list: ids, names, processes and shortcut lists.</summary>
        public static List<ShortcutPreset> NormalizePresetList(List<ShortcutPreset> source)
        {
            List<ShortcutPreset> result = new List<ShortcutPreset>();
            if (source == null) return result;

            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> processes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count && result.Count < 50; i++)
            {
                ShortcutPreset preset = source[i];
                if (preset == null) continue;
                if (string.IsNullOrEmpty(preset.Id) || ids.Contains(preset.Id))
                    preset.Id = Guid.NewGuid().ToString("N");
                ids.Add(preset.Id);

                preset.Name = preset.Name == null ? "" : preset.Name.Trim();
                if (preset.Name.Length > 60) preset.Name = preset.Name.Substring(0, 60);
                preset.Process = NormalizeProcessName(preset.Process);
                if (preset.Name.Length == 0)
                {
                    preset.Name = preset.Process.Length > 0 ? preset.Process : "未命名预设";
                }
                // A hand-edited file can bind two presets to one program. Keep the
                // first match and leave the other one unbound so its shortcuts are
                // not silently thrown away.
                if (preset.Process.Length > 0 && !processes.Add(preset.Process))
                    preset.Process = "";

                preset.Mappings = NormalizeMappings(preset.Mappings);
                result.Add(preset);
            }
            return result;
        }

        /// <summary>
        /// Reduces a path or a file name to the bare executable name used for
        /// matching, so "C:\...\Code.exe" and "code" are the same preset target.
        /// </summary>
        public static string NormalizeProcessName(string value)
        {
            string name = value == null ? "" : value.Trim().Trim('"');
            if (name.Length == 0) return "";
            try
            {
                name = System.IO.Path.GetFileName(name);
            }
            catch { }
            if (name == null) return "";
            name = name.Trim().Trim('"');
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name.Trim();
        }

        /// <summary>
        /// Macro data comes from two places this app does not fully control: the
        /// config file (hand edit, older version) and imported .clmacro files.
        /// Everything downstream assumes unique ids, clamped numbers and a step
        /// list that is never null, so repair it here once instead of in every
        /// caller.
        /// </summary>
        private static void NormalizeMacros(AppSettings settings)
        {
            settings.Macros = NormalizeMacroList(settings.Macros);
        }

        /// <summary>Repairs a macro list: nulls, duplicate ids, ranges, name lengths.</summary>
        public static List<Macro> NormalizeMacroList(List<Macro> source)
        {
            List<Macro> result = new List<Macro>();
            if (source == null) return result;

            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Count && result.Count < 200; i++)
            {
                Macro macro = source[i];
                if (macro == null) continue;
                if (string.IsNullOrEmpty(macro.Id) || ids.Contains(macro.Id))
                    macro.Id = Guid.NewGuid().ToString("N");
                ids.Add(macro.Id);
                macro.Name = macro.Name == null ? "" : macro.Name.Trim();
                if (macro.Name.Length == 0) macro.Name = "未命名宏";
                if (macro.Name.Length > 60) macro.Name = macro.Name.Substring(0, 60);
                if (macro.Note == null) macro.Note = "";
                if (macro.Steps == null) macro.Steps = new List<MacroStep>();

                List<MacroStep> steps = new List<MacroStep>();
                for (int s = 0; s < macro.Steps.Count && steps.Count < 500; s++)
                {
                    MacroStep step = macro.Steps[s];
                    if (step == null) continue;
                    if (string.IsNullOrEmpty(step.Action)) step.Action = ActionCatalog.None;
                    // Macro files are portable, so they can arrive with retired ids.
                    ActionCatalog.MigrateKeySendStep(step);
                    if (step.Param == null) step.Param = "";
                    // The step name is the only free text here, so cap it before it
                    // reaches the editor row and the config file.
                    step.Note = step.Note == null ? "" : step.Note.Trim();
                    if (step.Note.Length > 60) step.Note = step.Note.Substring(0, 60);
                    if (step.DelayMs < 0) step.DelayMs = 0;
                    if (step.DelayMs > 60000) step.DelayMs = 60000;
                    if (step.Count < 1) step.Count = 1;
                    if (step.Count > 999) step.Count = 999;
                    steps.Add(step);
                }
                macro.Steps = steps;
                result.Add(macro);
            }
            return result;
        }

        /// <summary>
        /// A macro writes one line per step and one per click, so the log grows with
        /// normal use. Cap it: when the file passes <see cref="MaxLogBytes"/> it is
        /// moved aside to error.1.log (the previous one is replaced) and a fresh file
        /// starts. One rotation is enough to keep the current run and the one before.
        /// </summary>
        private const long MaxLogBytes = 1024 * 1024;

        /// <summary>
        /// Lines waiting to be written. A whole macro is a handful of lines, so this
        /// is only ever large when something is spamming (a failing hook, say).
        /// </summary>
        private const int MaxQueuedLines = 4000;

        private static readonly object _logGate = new object();
        private static readonly Queue<string> _logQueue = new Queue<string>();
        private static Thread _logThread;
        private static int _logDropped;
        private static bool _logWriting;

        /// <summary>
        /// Queues a log line. This must stay cheap: it is called from the input and hook
        /// threads, i.e. inside a macro's timing. Writing used to happen right here, and
        /// a log that had grown to ~2 MB (antivirus re-scanning it on every append) added
        /// enough delay between the modifier press and the click to make Ctrl+click land
        /// as a plain click. The line is stamped now; the disk sees it later.
        /// </summary>
        public static void Log(string message)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine;
                lock (_logGate)
                {
                    if (_logQueue.Count >= MaxQueuedLines)
                    {
                        _logDropped++;
                        return;
                    }
                    _logQueue.Enqueue(line);
                    Monitor.Pulse(_logGate);
                    if (_logThread != null) return;
                    _logThread = new Thread(LogWriterLoop);
                    _logThread.IsBackground = true;
                    _logThread.Name = "CapsLockLite.Log";
                    _logThread.Start();
                }
            }
            catch { }
        }

        /// <summary>Writes what is queued, then returns. Used when the app is closing.</summary>
        public static void FlushLog(int timeoutMs)
        {
            long limit = Environment.TickCount + (timeoutMs < 1 ? 1 : timeoutMs);
            try
            {
                while (unchecked(Environment.TickCount - limit) < 0)
                {
                    lock (_logGate)
                    {
                        if (_logQueue.Count == 0 && !_logWriting) return;
                    }
                    Thread.Sleep(5);
                }
            }
            catch { }
        }

        private static void LogWriterLoop()
        {
            StringBuilder batch = new StringBuilder();
            while (true)
            {
                try
                {
                    lock (_logGate)
                    {
                        while (_logQueue.Count == 0) Monitor.Wait(_logGate, 5000);
                        while (_logQueue.Count > 0) batch.Append(_logQueue.Dequeue());
                        if (_logDropped > 0)
                        {
                            batch.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                                .Append("  （日志过多，丢弃 ").Append(_logDropped).Append(" 行）")
                                .Append(Environment.NewLine);
                            _logDropped = 0;
                        }
                        _logWriting = true;
                    }
                    if (batch.Length == 0)
                    {
                        lock (_logGate) _logWriting = false;
                        continue;
                    }
                    WriteBatch(batch.ToString());
                    batch.Length = 0;
                    lock (_logGate) _logWriting = false;
                }
                catch
                {
                    lock (_logGate) _logWriting = false;
                    Thread.Sleep(200);
                }
            }
        }

        private static void WriteBatch(string text)
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                RotateLogIfNeeded();
                File.AppendAllText(LogPath, text, Encoding.UTF8);
            }
            catch { }
        }

        private static void RotateLogIfNeeded()
        {
            try
            {
                FileInfo file = new FileInfo(LogPath);
                if (!file.Exists || file.Length < MaxLogBytes) return;
                string backup = LogPath + ".1";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(LogPath, backup);
            }
            catch { }
        }

        private static void TryBackupBrokenConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return;
                string backup = ConfigPath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(ConfigPath, backup, true);
            }
            catch { }
        }

        public static string EnsureDirectory()
        {
            Directory.CreateDirectory(ConfigDirectory);
            return ConfigDirectory;
        }
    }
}
