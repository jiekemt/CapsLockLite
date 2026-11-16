using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// Kind of parameter an action needs. The shortcut editor renders a matching
    /// input control, so a plugin never has to draw its own configuration UI.
    /// </summary>
    public enum ActionParamKind
    {
        None = 0,
        Count = 1,
        Path = 2,
        Url = 3,
        Text = 4,
        KeyCombo = 5,
        Folder = 6,
        MouseClick = 7
    }

    /// <summary>
    /// Metadata plus action factory for one plugin. A plugin file must contain
    /// exactly one public implementation of this interface.
    /// </summary>
    public interface IPlugin
    {
        /// <summary>Global plugin id, lowercase dot-separated, e.g. "com.example.screenshot".</summary>
        string Id { get; }

        /// <summary>Display name shown in the plugins page and in status messages.</summary>
        string Name { get; }

        /// <summary>Plugin version, free-form (semantic versioning recommended).</summary>
        string Version { get; }

        /// <summary>Author name, may be empty.</summary>
        string Author { get; }

        /// <summary>One-line description shown on the plugins page.</summary>
        string Description { get; }

        /// <summary>
        /// Creates the actions this plugin contributes. Called once per load;
        /// the returned instances are reused for every trigger.
        /// </summary>
        IPluginAction[] CreateActions();
    }

    /// <summary>One executable action contributed by a plugin.</summary>
    public interface IPluginAction
    {
        /// <summary>
        /// Plugin-local action id, lowercase, e.g. "fullscreen". The host stores
        /// it as "plugin.&lt;plugin id&gt;.&lt;action id&gt;" so it is globally unique.
        /// </summary>
        string Id { get; }

        /// <summary>Grouping label in the action picker, e.g. "截图".</summary>
        string Category { get; }

        /// <summary>Human readable action name shown in the shortcut list.</summary>
        string Name { get; }

        /// <summary>One-line explanation shown in the editor.</summary>
        string Description { get; }

        /// <summary>Which parameter input the editor should show.</summary>
        ActionParamKind ParamKind { get; }

        /// <summary>Initial parameter value when the action is picked.</summary>
        string DefaultParam { get; }

        /// <summary>True when the action may be triggered repeatedly while held.</summary>
        bool Repeatable { get; }

        /// <summary>
        /// Runs the action on the host's dedicated plugin STA thread (with a
        /// message loop); keep it under ~200 ms and report problems through
        /// <paramref name="context"/> instead of throwing.
        /// </summary>
        void Execute(PluginContext context);
    }

    /// <summary>
    /// Optional companion interface for plugins that need a dedicated settings
    /// page. Implement it **in addition to** <see cref="IPlugin"/>; plugins that
    /// do not implement it keep working unchanged.
    /// </summary>
    public interface IPluginWithSettings
    {
        /// <summary>
        /// Creates the settings page descriptor. Called when the user opens the
        /// page; returning null means "this plugin has no settings".
        /// </summary>
        IPluginSettingsPage CreateSettingsPage();
    }

    /// <summary>One plugin-owned settings page, shown inside the plugins area.</summary>
    public interface IPluginSettingsPage
    {
        /// <summary>Short page title, e.g. "准心". Falls back to the plugin name.</summary>
        string Title { get; }

        /// <summary>
        /// Builds the page content. Called once per open; the host disposes the
        /// returned control when the user leaves the page. The host controls the
        /// width, the view must set its own Height.
        /// </summary>
        Control CreateView(IPluginSettingsContext context);
    }

    /// <summary>
    /// Services a settings page gets from the host: per-plugin persistent storage
    /// (a JSON file next to the plugins), status reporting and theme hints.
    /// </summary>
    public interface IPluginSettingsContext
    {
        /// <summary>Reads a stored value; returns the fallback when the key is absent.</summary>
        string Get(string key, string defaultValue);

        /// <summary>Stores a value in memory. Call <see cref="Save"/> to persist it.</summary>
        void Set(string key, string value);

        /// <summary>Writes the values to disk. Returns false when the write failed.</summary>
        bool Save();

        /// <summary>Shows an informational message in the main window status bar.</summary>
        void Report(string message);

        /// <summary>Shows an error message in the main window status bar.</summary>
        void Error(string message);

        /// <summary>Appends a line to the application error log.</summary>
        void Log(string message);

        /// <summary>True when the host is in dark mode; use it to match the palette.</summary>
        bool DarkMode { get; }

        /// <summary>The host accent colour.</summary>
        Color AccentColor { get; }

        /// <summary>Current host DPI (96 = 100%); use it to scale your layout.</summary>
        int Dpi { get; }
    }

    /// <summary>
    /// Optional: called once right after the plugin instance is created and
    /// before <see cref="IPlugin.CreateActions"/>. Gives the plugin its own data
    /// folder so files it writes live next to its source.
    /// </summary>
    public interface IPluginHosted
    {
        void Initialize(PluginHostInfo host);
    }

    /// <summary>
    /// Optional lifecycle hook. The host calls <see cref="Shutdown"/> before a
    /// plugin is reloaded, disabled or uninstalled. It is best-effort: code that a
    /// plugin started outside this call may still run in the process until the
    /// application exits, which is why unloading a loaded plugin asks the user to
    /// restart.
    /// </summary>
    public interface IPluginLifecycle
    {
        void Shutdown();
    }

    /// <summary>Host facts handed to <see cref="IPluginHosted.Initialize"/>.</summary>
    public sealed class PluginHostInfo
    {
        /// <summary>Global plugin id, e.g. "com.example.screenshot".</summary>
        public string PluginId { get; private set; }

        /// <summary>Full path of the plugin source file.</summary>
        public string PluginFile { get; private set; }

        /// <summary>
        /// The plugin's own folder ("plugins\&lt;plugin id&gt;"). Always exists;
        /// put your own settings files here.
        /// </summary>
        public string DataDirectory { get; private set; }

        /// <summary>The plugins root folder.</summary>
        public string PluginsRoot { get; private set; }

        /// <summary>Current host DPI (96 = 100%).</summary>
        public int Dpi { get; private set; }

        /// <summary>True when the host is in dark mode.</summary>
        public bool DarkMode { get; private set; }

        /// <summary>The host accent colour.</summary>
        public Color AccentColor { get; private set; }

        internal PluginHostInfo(string pluginId, string pluginFile, string dataDirectory,
            string pluginsRoot, int dpi, bool darkMode, Color accentColor)
        {
            PluginId = pluginId ?? "";
            PluginFile = pluginFile ?? "";
            DataDirectory = dataDirectory ?? "";
            PluginsRoot = pluginsRoot ?? "";
            Dpi = dpi;
            DarkMode = darkMode;
            AccentColor = accentColor;
        }
    }

    /// <summary>
    /// Per-invocation context passed to <see cref="IPluginAction.Execute"/>.
    /// Reporting is thread-safe: calls from background threads are marshalled
    /// back to the UI thread.
    /// </summary>
    public sealed class PluginContext
    {
        private readonly ISynchronizeInvoke _synchronizer;
        private readonly Action<string> _report;
        private readonly Action<string> _error;

        /// <summary>Id of the plugin that owns the running action.</summary>
        public string PluginId { get; private set; }

        /// <summary>Full action id ("plugin.&lt;plugin id&gt;.&lt;action id&gt;").</summary>
        public string ActionId { get; private set; }

        /// <summary>Configured parameter text, never null (may be empty).</summary>
        public string Param { get; private set; }

        internal PluginContext(string pluginId, string actionId, string param,
            ISynchronizeInvoke synchronizer, Action<string> report, Action<string> error)
        {
            PluginId = pluginId ?? "";
            ActionId = actionId ?? "";
            Param = param ?? "";
            _synchronizer = synchronizer;
            _report = report;
            _error = error;
        }

        /// <summary>Shows an informational message in the main window status bar.</summary>
        public void Report(string message)
        {
            Post(_report, message);
        }

        /// <summary>Shows an error message; prefix is added by the host.</summary>
        public void Error(string message)
        {
            Post(_error, string.IsNullOrEmpty(message) ? "插件动作执行失败。" : message);
        }

        /// <summary>Appends a line to the application error log.</summary>
        public void Log(string message)
        {
            ConfigStore.Log("插件[" + PluginId + "] " + message);
        }

        private void Post(Action<string> target, string message)
        {
            if (target == null || string.IsNullOrEmpty(message)) return;
            if (_synchronizer != null && _synchronizer.InvokeRequired)
            {
                try { _synchronizer.BeginInvoke(target, new object[] { message }); }
                catch { }
                return;
            }
            target(message);
        }
    }
}
