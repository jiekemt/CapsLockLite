using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>Load state of one plugin file, as shown on the plugins page.</summary>
    internal sealed class PluginStatus
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public int ActionCount { get; set; }
        public IList<ActionDef> Actions { get; set; }

        /// <summary>The plugin's own folder ("plugins\&lt;id&gt;").</summary>
        public string DataDirectory { get; set; }

        /// <summary>The plugin instance; lets the host ask for its settings page.</summary>
        public IPlugin Instance { get; set; }

        /// <summary>True when the plugin source matches a hash in the trust store.</summary>
        public bool Trusted { get; set; }

        /// <summary>SHA-256 of the source file when it was scanned.</summary>
        public string Hash { get; set; }

        /// <summary>True when the plugin provides its own settings page.</summary>
        public bool HasSettings { get { return Instance is IPluginWithSettings; } }

        /// <summary>Null when the plugin loaded, otherwise the reason it did not.</summary>
        public string Error { get; set; }

        public bool IsLoaded { get { return string.IsNullOrEmpty(Error); } }

        public bool FileExists
        {
            get
            {
                try { return !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath); }
                catch { return false; }
            }
        }

        public PluginStatus()
        {
            FilePath = "";
            FileName = "";
            Id = "";
            Name = "";
            Version = "";
            Author = "";
            Description = "";
            DataDirectory = "";
            Hash = "";
            Actions = new List<ActionDef>();
        }
    }

    /// <summary>
    /// One plugin source that passed compile-only inspection and is waiting for
    /// the user's trust decision. No plugin code has been instantiated at this
    /// point.
    /// </summary>
    internal sealed class PluginImportResult
    {
        public string DisplayName { get; set; }
        public string FilePath { get; set; }
        public string StagedPath { get; set; }
        public string Hash { get; set; }
        public string PluginName { get; set; }
        public int ActionCount { get; set; }

        /// <summary>True when the source was staged; it is not trusted yet.</summary>
        public bool Imported { get; set; }

        public bool Trusted { get; set; }
        public string Error { get; set; }

        public PluginImportResult()
        {
            DisplayName = "";
            FilePath = "";
            StagedPath = "";
            Hash = "";
            PluginName = "";
            Error = "";
        }
    }

    /// <summary>
    /// Loads plugin source files, compiles them against this executable and
    /// registers their actions in <see cref="ActionCatalog"/>.
    ///
    /// Security model:
    /// - plugin execution is gated only by the trust store, not by a global switch;
    /// - a source file is skipped unless its SHA-256 is present in
    ///   plugins\trusted.txt, so a dropped file is never compiled or executed;
    /// - import only compiles and inspects metadata, it does not instantiate the
    ///   plugin before the user explicitly trusts the file;
    /// - plugin actions run on a dedicated STA thread, not on the UI/hook thread.
    /// </summary>
    internal static class PluginRuntime
    {
        /// <summary>Contract version. Bumped only on breaking API changes.</summary>
        public const int ApiVersion = 1;

        private static readonly Regex PluginIdPattern = new Regex(
            @"^[a-z0-9][a-z0-9_-]*(\.[a-z0-9][a-z0-9_-]*)+$", RegexOptions.CultureInvariant);

        private static readonly Regex ActionIdPattern = new Regex(
            @"^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant);

        private const int MaxArchiveEntries = 500;
        private const int MaxPluginSourceBytes = 512 * 1024;
        private const int MaxArchiveTotalBytes = 16 * 1024 * 1024;
        private const int CompileTimeoutMs = 30000;

        private sealed class RegisteredAction
        {
            public string PluginId;
            public IPluginAction Action;

            public RegisteredAction(string pluginId, IPluginAction action)
            {
                PluginId = pluginId;
                Action = action;
            }
        }

        private static readonly Dictionary<string, RegisteredAction> _byActionId =
            new Dictionary<string, RegisteredAction>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<PluginStatus> _plugins = new List<PluginStatus>();
        private static readonly HashSet<string> _trustedHashes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static readonly object _sync = new object();
        private static bool _initialized;
        private static PluginDispatcher _dispatcher;

        public static IList<PluginStatus> Plugins { get { return _plugins; } }

        public static string PluginDirectory
        {
            get { return Path.Combine(ConfigStore.ConfigDirectory, "plugins"); }
        }

        public static string PendingDirectory
        {
            get { return Path.Combine(PluginDirectory, ".pending"); }
        }

        private static string TrustStorePath
        {
            get { return Path.Combine(PluginDirectory, "trusted.txt"); }
        }

        public static bool IsValidPluginId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.Length <= 64 && PluginIdPattern.IsMatch(id);
        }

        public static bool IsValidPluginActionId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.Length <= 48 && ActionIdPattern.IsMatch(id);
        }

        /// <summary>Initializes the plugin subsystem and loads trusted plugins.</summary>
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveHostAssembly;
            Reload();
        }

        /// <summary>Releases plugin instances and the dedicated plugin thread.</summary>
        public static void Shutdown()
        {
            DisposePlugins();
            ActionCatalog.UnregisterPluginActions();
            lock (_sync)
            {
                _byActionId.Clear();
                _plugins.Clear();
            }

            PluginDispatcher dispatcher = _dispatcher;
            _dispatcher = null;
            if (dispatcher != null)
            {
                try { dispatcher.Dispose(); }
                catch { }
            }
        }

        /// <summary>Recompiles every trusted plugin file and rebuilds the registry.</summary>
        public static void Reload()
        {
            DisposePlugins();
            ActionCatalog.UnregisterPluginActions();
            lock (_sync)
            {
                _byActionId.Clear();
                _plugins.Clear();
            }


            try
            {
                Directory.CreateDirectory(PluginDirectory);
                LoadTrustStore();
                CleanBuildDirectory();
                CleanImportDirectory();
            }
            catch (Exception ex)
            {
                ConfigStore.Log("创建插件目录失败：" + ex.Message);
                return;
            }

            if (!ConfigStore.IsConfigDirectoryPrivate)
            {
                PluginStatus blocked = new PluginStatus();
                blocked.Name = "插件目录不安全";
                blocked.FilePath = PluginDirectory;
                blocked.Trusted = false;
                blocked.Error = "配置目录可被其他用户写入，已拒绝加载插件。请修复目录权限后重试。";
                _plugins.Add(blocked);
                ConfigStore.Log("插件目录权限过宽，已拒绝加载：" + PluginDirectory);
                return;
            }

            List<string> files = FindPluginFiles(PluginDirectory);
            HashSet<string> pluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> actionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < files.Count; i++)
            {
                string file = files[i];
                if (!File.Exists(file)) continue;

                try
                {
                    FileInfo info = new FileInfo(file);
                    if (info.Length > MaxPluginSourceBytes)
                    {
                        PluginStatus tooLarge = UntrustedStatus(file);
                        tooLarge.Error = "插件源文件超过 " + (MaxPluginSourceBytes / 1024) + " KB，已跳过。";
                        _plugins.Add(tooLarge);
                        continue;
                    }
                }
                catch { }

                string hash;
                try
                {
                    hash = ComputeHash(file);
                }
                catch (Exception ex)
                {
                    PluginStatus bad = UntrustedStatus(file);
                    bad.Error = "读取插件失败：" + Describe(ex);
                    _plugins.Add(bad);
                    continue;
                }

                if (!_trustedHashes.Contains(hash))
                {
                    PluginStatus untrusted = UntrustedStatus(file);
                    untrusted.Hash = hash;
                    untrusted.Error = "插件未信任：请先阅读源码，再点击“信任”。";
                    _plugins.Add(untrusted);
                    continue;
                }

                PluginStatus status;
                try
                {
                    status = LoadFile(file, hash, pluginIds, actionIds, true);
                }
                catch (Exception ex)
                {
                    status = UntrustedStatus(file);
                    status.Trusted = true;
                    status.Hash = hash;
                    status.Error = "插件加载失败：" + Describe(ex);
                }
                _plugins.Add(status);
                if (!status.IsLoaded)
                {
                    ConfigStore.Log("插件加载失败：" + status.FilePath + " - " + status.Error);
                }
            }

            CleanEmptyPendingFolders();
        }

        /// <summary>
        /// Queues a plugin action on the dedicated STA thread. Returns false when
        /// the thread could not be created; the caller should report that.
        /// </summary>
        public static bool Dispatch(KeyMapping mapping, IAppHost host)
        {
            if (mapping == null) return false;
            PluginDispatcher dispatcher = EnsureDispatcher();
            if (dispatcher == null)
            {
                ReportStatus(host, "插件执行线程不可用，动作未执行。", true);
                return false;
            }
            bool posted = dispatcher.Post(delegate { Execute(mapping, host); });
            if (!posted)
                ReportStatus(host, "插件动作队列已满或执行线程已停止，动作未执行。", true);
            return posted;
        }

        /// <summary>Runs a plugin action directly. Used by tests and the dispatcher.</summary>
        public static void Execute(KeyMapping mapping, IAppHost host)
        {
            if (mapping == null) return;

            RegisteredAction registered;
            lock (_sync)
            {
                if (!_byActionId.TryGetValue(mapping.Action, out registered))
                    registered = null;
            }
            if (registered == null)
            {
                ReportStatus(host, "插件动作未加载：" + mapping.Action, true);
                return;
            }

            ISynchronizeInvoke synchronizer = host as ISynchronizeInvoke;
            PluginContext context = new PluginContext(
                registered.PluginId, mapping.Action, mapping.Param, synchronizer,
                delegate(string message) { ReportStatus(host, message, false); },
                delegate(string message) { ReportStatus(host, message, true); });

            try
            {
                registered.Action.Execute(context);
            }
            catch (Exception ex)
            {
                ConfigStore.Log("插件动作执行失败：" + mapping.Action + " - " + ex);
                ReportStatus(host, "插件动作失败：" + ex.Message, true);
            }
        }

        /// <summary>Used by the self-test: compile and register one trusted file.</summary>
        internal static PluginStatus LoadForTest(string path)
        {
            string hash = ComputeHash(path);
            _trustedHashes.Add(hash);
            return LoadFile(path, hash,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(StringComparer.OrdinalIgnoreCase), true);
        }

        /// <summary>
        /// Imports plugin files dropped on the window or picked in the dialog.
        /// It compiles each candidate and inspects the type metadata, but it does
        /// NOT instantiate the plugin or create its final data folder. The caller
        /// must show a trust prompt and then call <see cref="TrustImports"/> or
        /// <see cref="DiscardImports"/>.
        /// </summary>
        public static IList<PluginImportResult> ImportPaths(string[] paths)
        {
            List<PluginImportResult> results = new List<PluginImportResult>();
            string importDirectory = Path.Combine(PluginDirectory, ".import");

            if (!ConfigStore.IsConfigDirectoryPrivate)
            {
                results.Add(ImportFailed("插件目录", "配置目录可被其他用户写入，已拒绝导入。请修复目录权限。"));
                return results;
            }

            try
            {
                Directory.CreateDirectory(PluginDirectory);
                Directory.CreateDirectory(importDirectory);
                if (paths != null)
                {
                    for (int i = 0; i < paths.Length; i++)
                    {
                        string path = paths[i];
                        if (string.IsNullOrEmpty(path) || !File.Exists(path))
                        {
                            results.Add(ImportFailed(Path.GetFileName(path), "文件不存在。"));
                            continue;
                        }

                        string extension = Path.GetExtension(path).ToLowerInvariant();
                        if (extension == ".cs")
                        {
                            ImportFile(path, importDirectory, results);
                        }
                        else if (extension == ".zip")
                        {
                            ImportArchive(path, importDirectory, results);
                        }
                        else
                        {
                            results.Add(ImportFailed(Path.GetFileName(path), "只支持 .cs 或 .zip 文件。"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                results.Add(ImportFailed("导入", "读取失败：" + Describe(ex)));
            }
            finally
            {
                try { if (Directory.Exists(importDirectory)) Directory.Delete(importDirectory, true); }
                catch { }
            }
            return results;
        }

        /// <summary>Adds the staged hashes to the trust store and loads them.</summary>
        public static bool TrustImports(IList<PluginImportResult> results)
        {
            if (!ConfigStore.IsConfigDirectoryPrivate) return false;
            bool changed = false;
            if (results != null)
            {
                for (int i = 0; i < results.Count; i++)
                {
                    PluginImportResult result = results[i];
                    if (result == null || !result.Imported || string.IsNullOrEmpty(result.Hash)) continue;
                    if (_trustedHashes.Add(result.Hash)) changed = true;
                    result.Trusted = true;
                }
            }
            if (changed && !SaveTrustStore()) return false;
            Reload();
            return true;
        }

        /// <summary>Deletes staged files when the user declines the trust prompt.</summary>
        public static void DiscardImports(IList<PluginImportResult> results)
        {
            if (results == null) return;
            for (int i = 0; i < results.Count; i++)
            {
                PluginImportResult result = results[i];
                if (result == null || string.IsNullOrEmpty(result.StagedPath)) continue;
                if (!IsUnderPluginRoot(result.StagedPath)) continue;
                TryDelete(result.StagedPath);
            }
            CleanEmptyPendingFolders();
        }

        /// <summary>
        /// Trusts one source file by hash. The file must live inside the plugin
        /// folder; this is the explicit user decision that allows it to run.
        /// </summary>
        public static bool TrustFile(string path)
        {
            if (!ConfigStore.IsConfigDirectoryPrivate) return false;
            string full;
            if (!TryResolvePluginFile(path, out full)) return false;
            string hash;
            try { hash = ComputeHash(full); }
            catch (Exception ex)
            {
                ConfigStore.Log("计算插件哈希失败：" + full + " - " + ex.Message);
                return false;
            }

            _trustedHashes.Add(hash);
            if (!SaveTrustStore()) return false;
            Reload();
            return true;
        }

        /// <summary>
        /// Forgets trust for the given SHA-256 hashes. Callers that are about to
        /// delete the files must compute the hashes first.
        /// </summary>
        public static bool UntrustHashes(IEnumerable<string> hashes)
        {
            if (hashes == null) return true;
            bool changed = false;
            foreach (string raw in hashes)
            {
                string hash = (raw ?? "").Trim();
                if (hash.Length == 0) continue;
                if (_trustedHashes.Remove(hash)) changed = true;
            }
            if (!changed) return true;
            if (!SaveTrustStore()) return false;
            Reload();
            return true;
        }

        /// <summary>Removes the trust record for one existing plugin source file.</summary>
        public static bool UntrustFile(string path)
        {
            string full;
            if (!TryResolvePluginFile(path, out full)) return false;
            if (!File.Exists(full)) return false;
            string hash;
            try { hash = ComputeHash(full); }
            catch (Exception ex)
            {
                ConfigStore.Log("计算插件哈希失败：" + full + " - " + ex.Message);
                return false;
            }
            return UntrustHashes(new string[] { hash });
        }

        /// <summary>SHA-256 of a file, lowercase hex, no separators.</summary>
        public static string ComputeHash(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(stream);
                StringBuilder text = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++) text.Append(digest[i].ToString("x2"));
                return text.ToString();
            }
        }

        private static PluginDispatcher EnsureDispatcher()
        {
            lock (_sync)
            {
                if (_dispatcher != null) return _dispatcher;
                try
                {
                    _dispatcher = new PluginDispatcher();
                }
                catch (Exception ex)
                {
                    ConfigStore.Log("创建插件执行线程失败：" + ex);
                    return null;
                }
                return _dispatcher;
            }
        }

        private static void ReportStatus(IAppHost host, string message, bool error)
        {
            if (host == null || string.IsNullOrEmpty(message)) return;
            ISynchronizeInvoke synchronizer = host as ISynchronizeInvoke;
            if (synchronizer != null && synchronizer.InvokeRequired)
            {
                try { synchronizer.BeginInvoke(new Action<string, bool>(host.ShowStatus), new object[] { message, error }); }
                catch { }
                return;
            }
            host.ShowStatus(message, error);
        }

        private static void DisposePlugins()
        {
            PluginStatus[] snapshot;
            lock (_sync) { snapshot = _plugins.ToArray(); }
            for (int i = 0; i < snapshot.Length; i++)
            {
                PluginStatus status = snapshot[i];
                if (status == null || status.Instance == null) continue;
                try
                {
                    IPluginLifecycle lifecycle = status.Instance as IPluginLifecycle;
                    if (lifecycle != null) lifecycle.Shutdown();
                }
                catch (Exception ex)
                {
                    ConfigStore.Log("插件关闭失败：" + status.Id + " - " + ex.Message);
                }
                try
                {
                    IDisposable disposable = status.Instance as IDisposable;
                    if (disposable != null) disposable.Dispose();
                }
                catch (Exception ex)
                {
                    ConfigStore.Log("插件释放失败：" + status.Id + " - " + ex.Message);
                }
            }
        }

        private static PluginStatus UntrustedStatus(string file)
        {
            PluginStatus status = new PluginStatus();
            status.FilePath = file;
            status.FileName = Path.GetFileName(file);
            status.Name = Path.GetFileNameWithoutExtension(file);
            status.DataDirectory = "";
            status.Trusted = false;
            return status;
        }

        private static void LoadTrustStore()
        {
            _trustedHashes.Clear();
            try
            {
                if (!File.Exists(TrustStorePath)) return;
                string[] lines = File.ReadAllLines(TrustStorePath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = (lines[i] ?? "").Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    if (IsHexHash(line)) _trustedHashes.Add(line);
                    else ConfigStore.Log("忽略无效的插件信任条目：" + line);
                }
            }
            catch (Exception ex)
            {
                ConfigStore.Log("读取插件信任库失败：" + ex.Message);
            }
        }

        private static bool SaveTrustStore()
        {
            try
            {
                Directory.CreateDirectory(PluginDirectory);
                List<string> hashes = new List<string>(_trustedHashes);
                hashes.Sort(StringComparer.OrdinalIgnoreCase);
                StringBuilder text = new StringBuilder();
                text.AppendLine("# CapsLockLite trusted plugin SHA-256 (one per line)");
                for (int i = 0; i < hashes.Count; i++) text.AppendLine(hashes[i]);

                string temp = TrustStorePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temp, text.ToString(), new UTF8Encoding(false));
                    if (File.Exists(TrustStorePath))
                    {
                        try { File.Replace(temp, TrustStorePath, null); }
                        catch (Exception)
                        {
                            File.Copy(temp, TrustStorePath, true);
                            TryDelete(temp);
                        }
                    }
                    else
                    {
                        File.Move(temp, TrustStorePath);
                    }
                    return true;
                }
                finally
                {
                    TryDelete(temp);
                }
            }
            catch (Exception ex)
            {
                ConfigStore.Log("保存插件信任库失败：" + ex.Message);
                return false;
            }
        }

        private static bool IsHexHash(string text)
        {
            if (text == null || text.Length != 64) return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        private static bool IsUnderPluginRoot(string path)
        {
            try
            {
                string root = Path.GetFullPath(PluginDirectory).TrimEnd('\\');
                string full = Path.GetFullPath(path).TrimEnd('\\');
                return full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static bool TryResolvePluginFile(string path, out string full)
        {
            full = "";
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                full = Path.GetFullPath(path);
                if (!IsUnderPluginRoot(full)) return false;
                if (!File.Exists(full)) return false;
                FileAttributes attributes = File.GetAttributes(full);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static PluginImportResult ImportFailed(string name, string error)
        {
            PluginImportResult result = new PluginImportResult();
            result.DisplayName = string.IsNullOrEmpty(name) ? "插件文件" : name;
            result.Error = error;
            return result;
        }

        private static void ImportFile(string sourcePath, string importDirectory,
            List<PluginImportResult> results)
        {
            string display = Path.GetFileName(sourcePath);
            try
            {
                FileInfo info = new FileInfo(sourcePath);
                if (info.Length > MaxPluginSourceBytes)
                {
                    results.Add(ImportFailed(display, "源文件超过 " + (MaxPluginSourceBytes / 1024) + " KB，已跳过。"));
                    return;
                }
            }
            catch (Exception ex)
            {
                results.Add(ImportFailed(display, "读取失败：" + Describe(ex)));
                return;
            }

            string temp = Path.Combine(importDirectory,
                Guid.NewGuid().ToString("N") + "-" + SanitizeFileName(display));
            try
            {
                File.Copy(sourcePath, temp, true);
            }
            catch (Exception ex)
            {
                results.Add(ImportFailed(display, "读取失败：" + Describe(ex)));
                return;
            }
            InspectAndStage(temp, display, results);
        }

        private static void ImportArchive(string zipPath, string importDirectory,
            List<PluginImportResult> results)
        {
            string archiveName = Path.GetFileName(zipPath);
            int found = 0;
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    int scanned = 0;
                    int index = 0;
                    long total = 0;
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (++scanned > MaxArchiveEntries) break;
                        if (!entry.Name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                        if (entry.FullName.IndexOf("__MACOSX", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                        found++;
                        index++;
                        string display = entry.Name;
                        if (entry.Length > MaxPluginSourceBytes)
                        {
                            results.Add(ImportFailed(display, "源文件超过 " + (MaxPluginSourceBytes / 1024) + " KB，已跳过。"));
                            continue;
                        }
                        if (total + entry.Length > MaxArchiveTotalBytes)
                        {
                            results.Add(ImportFailed(archiveName, "压缩包解压后超过 " + (MaxArchiveTotalBytes / 1024 / 1024) + " MB，已停止。"));
                            break;
                        }
                        total += entry.Length;

                        string temp = Path.Combine(importDirectory,
                            index.ToString() + "-" + SanitizeFileName(display));
                        try
                        {
                            using (Stream input = entry.Open())
                            using (FileStream output = File.Create(temp))
                            {
                                input.CopyTo(output);
                            }
                        }
                        catch (Exception ex)
                        {
                            results.Add(ImportFailed(display, "无法从压缩包解出：" + Describe(ex)));
                            continue;
                        }

                        InspectAndStage(temp, display, results);
                    }
                }

                if (found == 0)
                    results.Add(ImportFailed(archiveName, "压缩包里没有找到 .cs 文件。"));
            }
            catch (Exception ex)
            {
                results.Add(ImportFailed(archiveName, "无法读取压缩包：" + Describe(ex)));
            }
        }

        /// <summary>
        /// Compiles a candidate and checks that it contains exactly one public
        /// IPlugin type. It deliberately does not call Activator.CreateInstance,
        /// Initialize or CreateActions: those are only reached after the user has
        /// trusted the file.
        /// </summary>
        private static void InspectAndStage(string tempPath, string displayName,
            List<PluginImportResult> results)
        {
            string compileError;
            Assembly assembly = CompileFile(tempPath, out compileError);
            if (assembly == null)
            {
                results.Add(ImportFailed(displayName, compileError));
                return;
            }

            try
            {
                Type pluginType = FindPluginType(assembly);
                if (pluginType == null)
                {
                    results.Add(ImportFailed(displayName, "未找到 public 的 CapsLockLite.IPlugin 实现。"));
                    return;
                }
            }
            catch (Exception ex)
            {
                results.Add(ImportFailed(displayName, Describe(ex)));
                return;
            }

            string hash;
            try { hash = ComputeHash(tempPath); }
            catch (Exception ex)
            {
                results.Add(ImportFailed(displayName, "读取失败：" + Describe(ex)));
                return;
            }

            try
            {
                string pending = Path.Combine(PendingDirectory, hash);
                Directory.CreateDirectory(pending);
                string staged = Path.Combine(pending, SanitizeFileName(displayName));
                File.Copy(tempPath, staged, true);

                PluginImportResult result = new PluginImportResult();
                result.DisplayName = displayName;
                result.FilePath = staged;
                result.StagedPath = staged;
                result.Hash = hash;
                result.PluginName = Path.GetFileNameWithoutExtension(displayName);
                result.Imported = true;
                results.Add(result);
            }
            catch (Exception ex)
            {
                results.Add(ImportFailed(displayName, "写入待启用目录失败：" + Describe(ex)));
            }
        }

        private static string SanitizeFileName(string name)
        {
            string file = Path.GetFileName(name == null ? "" : name);
            if (string.IsNullOrEmpty(file)) file = "plugin.cs";
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
                file = file.Replace(invalid[i], '_');
            if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) file += ".cs";
            return file;
        }

        private static PluginStatus FindLoaded(string filePath)
        {
            for (int i = 0; i < _plugins.Count; i++)
            {
                if (string.Equals(_plugins[i].FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                    return _plugins[i];
            }
            return null;
        }

        private static PluginStatus FindById(string pluginId)
        {
            for (int i = 0; i < _plugins.Count; i++)
            {
                if (string.Equals(_plugins[i].Id, pluginId, StringComparison.OrdinalIgnoreCase))
                    return _plugins[i];
            }
            return null;
        }

        /// <summary>Conventional folder for a plugin id ("plugins\&lt;id&gt;").</summary>
        internal static string FolderOfId(string pluginId)
        {
            return Path.Combine(PluginDirectory, SanitizeFolderName(pluginId));
        }

        /// <summary>
        /// The folder a loaded plugin actually lives in; falls back to the
        /// conventional one for ids that are not loaded right now.
        /// </summary>
        internal static string DataDirectoryOf(string pluginId)
        {
            PluginStatus loaded = FindById(pluginId);
            if (loaded != null && !string.IsNullOrEmpty(loaded.DataDirectory))
                return loaded.DataDirectory;
            return FolderOfId(pluginId);
        }

        private static void MigrateIntoFolder(PluginStatus status, string pluginFolder, bool move)
        {
            if (!move) return;

            string source = status.FilePath;
            try
            {
                string target = Path.Combine(pluginFolder, Path.GetFileName(source));
                if (string.Equals(target, source, StringComparison.OrdinalIgnoreCase)) return;

                Directory.CreateDirectory(pluginFolder);
                if (File.Exists(target)) File.Delete(target);
                File.Move(source, target);

                status.FilePath = target;
                status.FileName = Path.GetFileName(target);
                ConfigStore.Log("插件已移入独立目录：" + source + " → " + target);
            }
            catch (Exception ex)
            {
                ConfigStore.Log("插件移入独立目录失败：" + source + " - " + ex.Message);
            }
        }

        private static bool IsWorkingFile(string path)
        {
            string normalized = path == null ? "" : path.Replace('/', '\\');
            return normalized.IndexOf("\\.build\\", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("\\.import\\", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("\\settings\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsWorkingDirectory(string name)
        {
            return string.Equals(name, ".build", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, ".import", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "settings", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> FindPluginFiles(string root)
        {
            List<string> files = new List<string>();
            try
            {
                FileAttributes rootAttributes = File.GetAttributes(root);
                if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
                {
                    ConfigStore.Log("插件根目录是重解析点，已跳过：" + root);
                    return files;
                }
            }
            catch (Exception ex)
            {
                ConfigStore.Log("读取插件根目录失败：" + root + " - " + ex.Message);
                return files;
            }

            Stack<string> pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                try
                {
                    string[] found = Directory.GetFiles(directory, "*.cs");
                    for (int i = 0; i < found.Length; i++)
                    {
                        if (IsWorkingFile(found[i])) continue;
                        try
                        {
                            FileAttributes attributes = File.GetAttributes(found[i]);
                            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        }
                        catch { continue; }
                        files.Add(found[i]);
                    }

                    string[] children = Directory.GetDirectories(directory);
                    for (int i = 0; i < children.Length; i++)
                    {
                        string name = Path.GetFileName(children[i]);
                        if (IsWorkingDirectory(name)) continue;
                        try
                        {
                            FileAttributes attributes = File.GetAttributes(children[i]);
                            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                        }
                        catch { continue; }
                        pending.Push(children[i]);
                    }
                }
                catch (Exception ex)
                {
                    ConfigStore.Log("读取插件目录失败：" + directory + " - " + ex.Message);
                }
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
            return files;
        }

        private static string SanitizeFolderName(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return "plugin";

            StringBuilder builder = new StringBuilder(pluginId.Length);
            for (int i = 0; i < pluginId.Length; i++)
            {
                char c = pluginId[i];
                if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_') builder.Append(c);
                else builder.Append('_');
            }
            return builder.ToString();
        }

        private static PluginStatus LoadFile(string file, string hash,
            HashSet<string> usedPluginIds, HashSet<string> usedActionIds, bool register)
        {
            PluginStatus status = new PluginStatus();
            status.FilePath = file;
            status.FileName = Path.GetFileName(file);
            status.Name = Path.GetFileNameWithoutExtension(file);
            status.Hash = hash;
            status.Trusted = false;

            if (register && !_trustedHashes.Contains(hash))
            {
                status.Error = "插件未信任。";
                return status;
            }
            status.Trusted = true;

            string compileError;
            Assembly assembly = CompileFile(file, out compileError);
            if (assembly == null)
            {
                status.Error = string.IsNullOrEmpty(compileError) ? "编译失败。" : compileError;
                return status;
            }

            try
            {
                Type pluginType = FindPluginType(assembly);
                if (pluginType == null)
                {
                    status.Error = "未找到 public 的 CapsLockLite.IPlugin 实现。";
                    return status;
                }

                IPlugin plugin = (IPlugin)Activator.CreateInstance(pluginType);
                status.Instance = plugin;
                status.Id = plugin.Id == null ? "" : plugin.Id;
                status.Name = string.IsNullOrEmpty(plugin.Name) ? status.Id : plugin.Name;
                status.Version = plugin.Version == null ? "" : plugin.Version;
                status.Author = plugin.Author == null ? "" : plugin.Author;
                status.Description = plugin.Description == null ? "" : plugin.Description;

                if (!IsValidPluginId(status.Id))
                {
                    status.Error = "插件 Id 不合法：" + status.Id
                        + "（要求小写、点分，例如 com.example.screenshot）";
                    return status;
                }
                if (usedPluginIds.Contains(status.Id))
                {
                    status.Error = "插件 Id 与另一个插件重复：" + status.Id;
                    return status;
                }

                string normalized = file.Replace('/', '\\');
                string currentDirectory = Path.GetDirectoryName(file) ?? "";
                string ownFolder = FolderOfId(status.Id);
                bool inPluginsRoot = string.Equals(
                    currentDirectory.TrimEnd('\\'), PluginDirectory.TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
                bool alreadyInOwnFolder = string.Equals(
                    currentDirectory.TrimEnd('\\'), ownFolder.TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase);
                bool inPending = normalized.IndexOf("\\.pending\\", StringComparison.OrdinalIgnoreCase) >= 0;

                // A file in the plugins root or in .pending is moved into its own
                // folder after a successful load. A file already in a custom
                // subfolder keeps its location.
                bool useOwnFolder = !alreadyInOwnFolder && (inPluginsRoot || inPending);
                if (string.IsNullOrEmpty(currentDirectory)) useOwnFolder = true;
                string pluginFolder = useOwnFolder ? ownFolder : currentDirectory;

                try { Directory.CreateDirectory(pluginFolder); }
                catch { }
                status.DataDirectory = pluginFolder;

                IPluginHosted hosted = plugin as IPluginHosted;
                if (hosted != null)
                {
                    // Called before CreateActions so a plugin can read its own
                    // settings from DataDirectory at startup.
                    hosted.Initialize(new PluginHostInfo(status.Id, file, pluginFolder,
                        PluginDirectory, Theme.Dpi, Theme.IsDark, Theme.Accent));
                }

                IPluginAction[] actions = plugin.CreateActions();
                if (actions == null) actions = new IPluginAction[0];
                if (actions.Length == 0)
                {
                    status.Error = "插件没有提供任何动作。";
                    return status;
                }

                List<IPluginAction> valid = new List<IPluginAction>();
                List<string> newActionIds = new List<string>();
                HashSet<string> localIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < actions.Length; i++)
                {
                    IPluginAction action = actions[i];
                    if (action == null)
                    {
                        status.Error = "CreateActions 返回了 null 动作。";
                        return status;
                    }
                    string localId = action.Id == null ? "" : action.Id;
                    if (!IsValidPluginActionId(localId))
                    {
                        status.Error = "动作 Id 不合法：" + localId + "（要求小写，可含点、下划线、连字符）";
                        return status;
                    }
                    if (!localIds.Add(localId))
                    {
                        status.Error = "插件内动作 Id 重复：" + localId;
                        return status;
                    }
                    if (string.IsNullOrEmpty(action.Name))
                    {
                        status.Error = "动作 " + localId + " 的 Name 不能为空。";
                        return status;
                    }
                    string fullId = ActionCatalog.PluginIdPrefix + status.Id + "." + localId;
                    if (fullId.Length > 120)
                    {
                        status.Error = "动作 Id 过长：" + fullId;
                        return status;
                    }
                    if (usedActionIds.Contains(fullId) || ActionCatalog.Contains(fullId))
                    {
                        status.Error = "动作 Id 与其他动作冲突：" + fullId;
                        return status;
                    }
                    valid.Add(action);
                    newActionIds.Add(fullId);
                }

                List<ActionDef> defs = new List<ActionDef>();
                if (register)
                {
                    List<string> registrationErrors = new List<string>();
                    IList<ActionDef> registered = ActionCatalog.RegisterPluginActions(
                        status.Id, valid, registrationErrors);
                    if (registrationErrors.Count > 0)
                    {
                        status.Error = string.Join("；", registrationErrors.ToArray());
                        return status;
                    }
                    for (int i = 0; i < registered.Count; i++) defs.Add(registered[i]);
                }

                status.Actions = defs;
                status.ActionCount = defs.Count;
                usedPluginIds.Add(status.Id);
                for (int i = 0; i < newActionIds.Count; i++) usedActionIds.Add(newActionIds[i]);
                if (register)
                {
                    for (int i = 0; i < defs.Count; i++)
                    {
                        _byActionId[defs[i].Id] = new RegisteredAction(status.Id, defs[i].PluginAction);
                    }

                    // Keep the source next to its data: plugins\<id>\<file>.cs
                    MigrateIntoFolder(status, pluginFolder, inPluginsRoot || inPending);
                }
                return status;
            }
            catch (TargetInvocationException ex)
            {
                status.Error = "插件初始化失败：" + Describe(ex.InnerException ?? ex);
                return status;
            }
            catch (Exception ex)
            {
                status.Error = "插件加载失败：" + Describe(ex);
                return status;
            }
        }

        private static string Describe(Exception ex)
        {
            if (ex == null) return "";
            Exception inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;
            return inner.GetType().Name + ": " + inner.Message;
        }

        private static Type FindPluginType(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }
            if (types == null) return null;

            Type found = null;
            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type == null || !type.IsPublic || type.IsAbstract || type.IsInterface) continue;
                if (!typeof(IPlugin).IsAssignableFrom(type)) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null) continue;
                if (found != null)
                    throw new InvalidOperationException("一个插件文件只能包含一个 public 的 IPlugin 实现。");
                found = type;
            }
            return found;
        }

        private static Assembly CompileFile(string file, out string error)
        {
            error = null;
            string buildDirectory = Path.Combine(PluginDirectory, ".build");
            string token = Guid.NewGuid().ToString("N");
            string output = Path.Combine(buildDirectory, "plugin-" + token + ".dll");

            try
            {
                Directory.CreateDirectory(buildDirectory);

                string compiler = FindCompiler();
                if (compiler == null)
                {
                    error = "找不到 C# 编译器 csc.exe，需要 .NET Framework 4.x。";
                    return null;
                }

                string framework = RuntimeEnvironment.GetRuntimeDirectory();
                string arguments = "/nologo /target:library /langversion:5 /optimize+ /debug-"
                    + " /out:" + Quote(output)
                    + " /r:" + Quote(Path.Combine(framework, "System.dll"))
                    + " /r:" + Quote(Path.Combine(framework, "System.Core.dll"))
                    + " /r:" + Quote(Path.Combine(framework, "System.Drawing.dll"))
                    + " /r:" + Quote(Path.Combine(framework, "System.Windows.Forms.dll"))
                    // The contract lives in this executable; compiling against the
                    // running host turns API drift into a compile error.
                    + " /r:" + Quote(typeof(PluginRuntime).Assembly.Location)
                    + " " + Quote(file);

                ProcessStartInfo info = new ProcessStartInfo();
                // Run csc directly, without cmd.exe. That removes the shell from
                // the trust boundary: percent signs and other cmd metacharacters
                // in a plugin file name are just characters in an argument.
                info.FileName = compiler;
                info.Arguments = arguments;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.WindowStyle = ProcessWindowStyle.Hidden;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;

                // Give the compiler a scratch directory we know is writable, so a
                // locked-down or sandboxed TEMP cannot break plugin loading.
                try
                {
                    info.EnvironmentVariables["TEMP"] = buildDirectory;
                    info.EnvironmentVariables["TMP"] = buildDirectory;
                }
                catch { }

                StringBuilder diagnostics = new StringBuilder();
                object diagnosticsGate = new object();
                DataReceivedEventHandler collect = delegate(object sender, DataReceivedEventArgs args)
                {
                    if (args == null || args.Data == null) return;
                    lock (diagnosticsGate) diagnostics.AppendLine(args.Data);
                };

                using (Process process = new Process())
                {
                    process.StartInfo = info;
                    process.OutputDataReceived += collect;
                    process.ErrorDataReceived += collect;
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(CompileTimeoutMs))
                    {
                        try { process.Kill(); }
                        catch { }
                        try { process.WaitForExit(2000); }
                        catch { }
                        error = "编译器超时（" + (CompileTimeoutMs / 1000) + " 秒），已终止。";
                        return null;
                    }
                    // Flush the asynchronous output handlers.
                    process.WaitForExit();

                    int exitCode = process.ExitCode;
                    string text;
                    lock (diagnosticsGate) text = diagnostics.ToString();

                    if (exitCode != 0 || !File.Exists(output))
                    {
                        error = string.IsNullOrEmpty(text)
                            ? "编译失败，退出码 " + exitCode + "。"
                            : text.Trim();
                        return null;
                    }

                    // Load from bytes so the temporary dll can be deleted right
                    // away; the assembly stays usable for the process lifetime.
                    return Assembly.Load(File.ReadAllBytes(output));
                }
            }
            catch (Exception ex)
            {
                error = "编译插件失败：" + Describe(ex);
                return null;
            }
            finally
            {
                TryDelete(output);
            }
        }

        private static string FindCompiler()
        {
            // Deliberately ignore a CSC environment override: plugin compilation
            // should always use the framework compiler that ships with Windows.
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] candidates = new string[]
            {
                Path.Combine(windows, @"Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
                Path.Combine(windows, @"Microsoft.NET\Framework\v4.0.30319\csc.exe")
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i])) return candidates[i];
            }
            return null;
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }

        private static string ReadText(string path)
        {
            try
            {
                if (!File.Exists(path)) return "";
                // csc writes diagnostics in the console code page, not UTF-8.
                return File.ReadAllText(path, Encoding.Default);
            }
            catch
            {
                return "";
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        private static void CleanBuildDirectory()
        {
            string buildDirectory = Path.Combine(PluginDirectory, ".build");
            try
            {
                if (!Directory.Exists(buildDirectory)) return;
                string[] leftovers = Directory.GetFiles(buildDirectory);
                for (int i = 0; i < leftovers.Length; i++) TryDelete(leftovers[i]);
            }
            catch { }
        }

        /// <summary>Removes extraction leftovers from an interrupted import.</summary>
        private static void CleanImportDirectory()
        {
            string importDirectory = Path.Combine(PluginDirectory, ".import");
            try
            {
                if (Directory.Exists(importDirectory))
                    Directory.Delete(importDirectory, true);
            }
            catch { }
        }

        private static void CleanEmptyPendingFolders()
        {
            try
            {
                if (!Directory.Exists(PendingDirectory)) return;
                string[] directories = Directory.GetDirectories(PendingDirectory);
                for (int i = 0; i < directories.Length; i++)
                {
                    try
                    {
                        if (Directory.GetFiles(directories[i]).Length == 0
                            && Directory.GetDirectories(directories[i]).Length == 0)
                            Directory.Delete(directories[i]);
                    }
                    catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Plugins are compiled in memory and reference this executable by name;
        /// resolve that reference to the already loaded host assembly.
        /// </summary>
        private static Assembly ResolveHostAssembly(object sender, ResolveEventArgs args)
        {
            Assembly host = typeof(PluginRuntime).Assembly;
            AssemblyName requested;
            try { requested = new AssemblyName(args.Name); }
            catch { return null; }
            if (string.Equals(requested.Name, host.GetName().Name, StringComparison.OrdinalIgnoreCase))
                return host;
            return null;
        }
    }
}
