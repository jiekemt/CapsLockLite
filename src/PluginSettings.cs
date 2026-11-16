using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CapsLockLite
{
    /// <summary>
    /// Per-plugin key/value storage backing <see cref="IPluginSettingsContext"/>.
    /// One JSON file per plugin, next to its source
    /// ("plugins\&lt;id&gt;\settings.json"), so a broken file only loses that
    /// plugin's settings and a backup of the folder keeps them together.
    /// </summary>
    internal sealed class PluginSettingsStore
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        private readonly string _pluginId;
        private readonly string _path;
        private Dictionary<string, string> _values;
        private bool _dirty;

        public PluginSettingsStore(string pluginId)
        {
            _pluginId = pluginId == null ? "" : pluginId;
            // Live next to the plugin source: plugins\<id>\settings.json
            _path = Path.Combine(PluginRuntime.DataDirectoryOf(_pluginId), "settings.json");
        }

        public string FilePath { get { return _path; } }

        /// <summary>True when a value changed since the last save.</summary>
        public bool HasChanges { get { return _dirty; } }

        public string Get(string key, string fallback)
        {
            if (string.IsNullOrEmpty(key)) return fallback == null ? "" : fallback;
            EnsureLoaded();
            string value;
            if (_values.TryGetValue(key, out value)) return value;
            return fallback == null ? "" : fallback;
        }

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            EnsureLoaded();
            _values[key] = value == null ? "" : value;
            _dirty = true;
        }

        public bool Save(out string error)
        {
            error = null;
            EnsureLoaded();
            try
            {
                string directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                string json = JsonPretty.Format(Serializer.Serialize(_values));
                File.WriteAllText(_path, json, new UTF8Encoding(false));
                _dirty = false;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void EnsureLoaded()
        {
            if (_values != null) return;

            _values = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(_path)) return;
                string json = File.ReadAllText(_path, Encoding.UTF8);
                if (json.Trim().Length == 0) return;

                Dictionary<string, string> loaded =
                    Serializer.Deserialize<Dictionary<string, string>>(json);
                if (loaded == null) return;

                foreach (KeyValuePair<string, string> pair in loaded)
                    _values[pair.Key] = pair.Value == null ? "" : pair.Value;
            }
            catch (Exception ex)
            {
                ConfigStore.Log("读取插件设置失败：" + _path + " - " + ex.Message);
            }
        }
    }

    /// <summary>Host implementation of the settings-page context handed to plugins.</summary>
    internal sealed class PluginSettingsContext : IPluginSettingsContext
    {
        private readonly string _pluginId;
        private readonly PluginSettingsStore _store;
        private readonly ISynchronizeInvoke _synchronizer;
        private readonly Action<string> _report;
        private readonly Action<string> _error;

        public PluginSettingsContext(string pluginId, PluginSettingsStore store,
            ISynchronizeInvoke synchronizer, Action<string> report, Action<string> error)
        {
            _pluginId = pluginId == null ? "" : pluginId;
            _store = store;
            _synchronizer = synchronizer;
            _report = report;
            _error = error;
        }

        public string StorePath { get { return _store.FilePath; } }

        /// <summary>True when the page changed something and it is not saved yet.</summary>
        public bool HasChanges { get { return _store.HasChanges; } }

        public string Get(string key, string defaultValue)
        {
            return _store.Get(key, defaultValue);
        }

        public void Set(string key, string value)
        {
            _store.Set(key, value);
        }

        public bool Save()
        {
            string error;
            if (_store.Save(out error)) return true;
            Error("保存插件设置失败：" + error);
            return false;
        }

        public void Report(string message)
        {
            Post(_report, message);
        }

        public void Error(string message)
        {
            Post(_error, string.IsNullOrEmpty(message) ? "插件设置出错。" : message);
        }

        public void Log(string message)
        {
            ConfigStore.Log("插件设置[" + _pluginId + "] " + message);
        }

        public bool DarkMode { get { return Theme.IsDark; } }

        public Color AccentColor { get { return Theme.Accent; } }

        public int Dpi { get { return Theme.Dpi; } }

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
