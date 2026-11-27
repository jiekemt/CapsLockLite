using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace CapsLockLite
{
    internal sealed class ActionDef
    {
        public string Id { get; private set; }
        public string Category { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public ActionParamKind ParamKind { get; private set; }
        public string DefaultParam { get; private set; }
        public bool Repeatable { get; private set; }

        /// <summary>True for actions contributed by a plugin at runtime.</summary>
        public bool IsPlugin { get; private set; }

        /// <summary>
        /// True when the action id names a plugin action that is not loaded right
        /// now. The definition is a placeholder so existing shortcuts keep their
        /// id and parameter instead of being reset to "none".
        /// </summary>
        public bool Missing { get; private set; }

        /// <summary>True for actions that come from a user macro.</summary>
        public bool IsMacro { get; private set; }

        public string PluginId { get; private set; }
        public IPluginAction PluginAction { get; private set; }

        /// <summary>Values of a drop-down parameter, in the order the plugin listed them.</summary>
        public string[] ChoiceValues { get; private set; }

        /// <summary>Human labels of a drop-down parameter, parallel to <see cref="ChoiceValues"/>.</summary>
        public string[] ChoiceLabels { get; private set; }

        public int ChoiceCount
        {
            get { return ChoiceValues == null ? 0 : ChoiceValues.Length; }
        }

        public ActionDef(string id, string category, string name, string description,
            ActionParamKind paramKind, string defaultParam, bool repeatable)
        {
            Id = id;
            Category = category;
            Name = name;
            Description = description;
            ParamKind = paramKind;
            DefaultParam = defaultParam;
            Repeatable = repeatable;
            ChoiceValues = new string[0];
            ChoiceLabels = new string[0];
        }

        public static ActionDef ForPlugin(string pluginId, IPluginAction action, string fullId)
        {
            ActionDef def = new ActionDef(fullId,
                string.IsNullOrEmpty(action.Category) ? "插件" : action.Category,
                string.IsNullOrEmpty(action.Name) ? (action.Id ?? fullId) : action.Name,
                action.Description == null ? "" : action.Description,
                action.ParamKind,
                action.DefaultParam == null ? "" : action.DefaultParam,
                action.Repeatable);
            def.IsPlugin = true;
            def.PluginId = pluginId;
            def.PluginAction = action;
            if (action.ParamKind == ActionParamKind.Select)
                def.UseChoices(action as IPluginParamChoices);
            return def;
        }

        /// <summary>
        /// Reads the drop-down items of a Select action. "value|label" splits into the
        /// value the plugin receives and the label the user sees; a bare item uses the
        /// same text for both. Blank items are dropped, so a sloppy plugin cannot add
        /// an empty row to the editor.
        /// </summary>
        private void UseChoices(IPluginParamChoices source)
        {
            string[] raw = null;
            try
            {
                raw = source == null ? null : source.ParamChoices;
            }
            catch
            {
                raw = null;
            }
            List<string> values = new List<string>();
            List<string> labels = new List<string>();
            if (raw != null)
            {
                for (int i = 0; i < raw.Length; i++)
                {
                    string item = raw[i];
                    if (item == null) continue;
                    string value = item;
                    string label = item;
                    int bar = item.IndexOf('|');
                    if (bar >= 0)
                    {
                        value = item.Substring(0, bar);
                        label = item.Substring(bar + 1);
                    }
                    value = value.Trim();
                    label = label.Trim();
                    if (value.Length == 0) continue;
                    if (label.Length == 0) label = value;
                    values.Add(value);
                    labels.Add(label);
                }
            }
            ChoiceValues = values.ToArray();
            ChoiceLabels = labels.ToArray();
        }

        /// <summary>Label shown for a stored drop-down value (falls back to the value).</summary>
        public string ChoiceLabelOf(string value)
        {
            if (value == null) return "";
            for (int i = 0; i < ChoiceCount; i++)
                if (string.Equals(ChoiceValues[i], value, StringComparison.Ordinal))
                    return ChoiceLabels[i];
            return value;
        }

        /// <summary>Index of a stored value in the drop-down, or -1.</summary>
        public int ChoiceIndexOf(string value)
        {
            if (value == null) return -1;
            for (int i = 0; i < ChoiceCount; i++)
                if (string.Equals(ChoiceValues[i], value, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        public static ActionDef ForMissing(string fullId)
        {
            ActionDef def = new ActionDef(fullId, "插件", "插件动作未加载",
                "提供该动作的插件未安装或加载失败。", ActionParamKind.None, "", false);
            def.IsPlugin = true;
            def.Missing = true;
            return def;
        }

        public static ActionDef ForMacro(string fullId, string name, int stepCount)
        {
            ActionDef def = new ActionDef(fullId, "宏动作", name,
                "宏动作：" + Math.Max(0, stepCount) + " 个动作。", ActionParamKind.None, "", false);
            def.IsMacro = true;
            return def;
        }

        public static ActionDef ForMissingMacro(string fullId)
        {
            ActionDef def = new ActionDef(fullId, "宏动作", "宏动作不存在",
                "这个宏已被删除，或来自其他设备的配置。", ActionParamKind.None, "", false);
            def.IsMacro = true;
            def.Missing = true;
            return def;
        }

        public override string ToString()
        {
            return Category + " · " + Name;
        }
    }

    internal static class ActionCatalog
    {
        public const string None = "none";

        /// <summary>Every plugin action id starts with this prefix.</summary>
        public const string PluginIdPrefix = "plugin.";

        /// <summary>Every macro action id starts with this prefix.</summary>
        public const string MacroIdPrefix = "macro.";

        private static readonly List<ActionDef> _actions = BuildActions();
        private static readonly Dictionary<string, ActionDef> _byId = BuildIndex();

        private static readonly Dictionary<string, ActionDef> _missing =
            new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase);

        public static bool IsPluginActionId(string id)
        {
            return !string.IsNullOrEmpty(id)
                && id.StartsWith(PluginIdPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsMacroActionId(string id)
        {
            return !string.IsNullOrEmpty(id)
                && id.StartsWith(MacroIdPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool Contains(string id)
        {
            return !string.IsNullOrEmpty(id) && _byId.ContainsKey(id);
        }

        /// <summary>
        /// Adds the actions of one plugin under "plugin.&lt;pluginId&gt;.&lt;actionId&gt;".
        /// Collisions are reported into <paramref name="errors"/> and skipped.
        /// </summary>
        public static IList<ActionDef> RegisterPluginActions(string pluginId,
            IList<IPluginAction> actions, IList<string> errors)
        {
            List<ActionDef> registered = new List<ActionDef>();
            if (actions == null) return registered;

            for (int i = 0; i < actions.Count; i++)
            {
                IPluginAction action = actions[i];
                if (action == null)
                {
                    if (errors != null) errors.Add("插件包含空动作。");
                    continue;
                }
                string fullId = PluginIdPrefix + pluginId + "." + action.Id;
                if (Contains(fullId))
                {
                    if (errors != null) errors.Add("动作 ID 已被占用：" + fullId);
                    continue;
                }
                ActionDef def = ActionDef.ForPlugin(pluginId, action, fullId);
                _actions.Add(def);
                _byId[fullId] = def;
                _missing.Remove(fullId);
                registered.Add(def);
            }
            return registered;
        }

        /// <summary>Removes every plugin action; used before a reload.</summary>
        public static void UnregisterPluginActions()
        {
            for (int i = _actions.Count - 1; i >= 0; i--)
            {
                if (_actions[i].IsPlugin) _actions.RemoveAt(i);
            }
            List<string> stale = new List<string>();
            foreach (KeyValuePair<string, ActionDef> pair in _byId)
            {
                if (pair.Value.IsPlugin) stale.Add(pair.Key);
            }
            for (int i = 0; i < stale.Count; i++) _byId.Remove(stale[i]);
            _missing.Clear();
        }

        /// <summary>
        /// Re-exposes the user's macros under "macro.&lt;id&gt;" so they show up in
        /// the action picker and can be bound or nested like any other action.
        /// </summary>
        public static void RegisterMacros(IList<Macro> macros)
        {
            UnregisterMacroActions();
            if (macros == null) return;
            for (int i = 0; i < macros.Count; i++)
            {
                Macro macro = macros[i];
                if (macro == null || string.IsNullOrEmpty(macro.Id)) continue;
                string fullId = MacroIdPrefix + macro.Id;
                if (_byId.ContainsKey(fullId)) continue;
                ActionDef def = ActionDef.ForMacro(fullId, macro.Name, macro.StepCount);
                _actions.Add(def);
                _byId[fullId] = def;
                _missing.Remove(fullId);
            }
        }

        private static void UnregisterMacroActions()
        {
            for (int i = _actions.Count - 1; i >= 0; i--)
            {
                if (_actions[i].IsMacro) _actions.RemoveAt(i);
            }
            List<string> stale = new List<string>();
            foreach (KeyValuePair<string, ActionDef> pair in _byId)
            {
                if (pair.Value.IsMacro) stale.Add(pair.Key);
            }
            for (int i = 0; i < stale.Count; i++) _byId.Remove(stale[i]);
        }

        public static IList<ActionDef> All
        {
            get { return _actions; }
        }

        public static IList<string> Categories
        {
            get
            {
                List<string> list = new List<string>();
                foreach (ActionDef d in _actions)
                {
                    if (!list.Contains(d.Category)) list.Add(d.Category);
                }
                return list;
            }
        }

        public static ActionDef Get(string id)
        {
            ActionDef def;
            if (id != null && _byId.TryGetValue(id, out def)) return def;

            // A plugin action that is not installed right now still has to keep a
            // stable definition, otherwise loading a config would rewrite it to
            // "none" and silently destroy the user's shortcut.
            if (IsPluginActionId(id))
            {
                if (!_missing.TryGetValue(id, out def))
                {
                    def = ActionDef.ForMissing(id);
                    _missing[id] = def;
                }
                return def;
            }
            if (IsMacroActionId(id))
            {
                if (!_missing.TryGetValue(id, out def))
                {
                    def = ActionDef.ForMissingMacro(id);
                    _missing[id] = def;
                }
                return def;
            }
            return _byId[None];
        }

        public static bool IsRepeatable(string id)
        {
            return Get(id).Repeatable;
        }

        /// <summary>
        /// Native actions that were only a single key send are gone: they are now
        /// expressed with input.keys so users get one mechanism, one editor and the
        /// physical-hold behaviour for free. This table migrates old configs and
        /// imported macro files (the left side is the retired action id).
        /// </summary>
        private static readonly Dictionary<string, KeySendPreset> KeySendReplacements =
            new Dictionary<string, KeySendPreset>(StringComparer.Ordinal)
            {
                { "move.up", new KeySendPreset("Up", "上移") },
                { "move.down", new KeySendPreset("Down", "下移") },
                { "move.left", new KeySendPreset("Left", "左移") },
                { "move.right", new KeySendPreset("Right", "右移") },
                { "move.line.start", new KeySendPreset("Home", "移到行首") },
                { "move.line.end", new KeySendPreset("End", "移到行尾") },
                { "move.page.start", new KeySendPreset("Ctrl + Home", "移到页首") },
                { "move.page.end", new KeySendPreset("Ctrl + End", "移到页尾") },
                { "move.page.up", new KeySendPreset("PageUp", "上翻一页") },
                { "move.page.down", new KeySendPreset("PageDown", "下翻一页") },
                { "select.line.start", new KeySendPreset("Shift + Home", "选中到行首") },
                { "select.line.end", new KeySendPreset("Shift + End", "选中到行尾") },
                { "select.page.start", new KeySendPreset("Ctrl + Shift + Home", "选中到页首") },
                { "select.page.end", new KeySendPreset("Ctrl + Shift + End", "选中到页尾") },
                { "delete.back", new KeySendPreset("Backspace", "向后删除") },
                { "delete.forward", new KeySendPreset("Delete", "向前删除") },
                { "delete.word.back", new KeySendPreset("Ctrl + Backspace", "向后删除单词") },
                { "delete.word.forward", new KeySendPreset("Ctrl + Delete", "向前删除单词") },
                { "edit.enter", new KeySendPreset("Enter", "回车") },
                { "edit.undo", new KeySendPreset("Ctrl + Z", "撤销") },
                { "edit.redo", new KeySendPreset("Ctrl + Y", "重做") },
                { "edit.tab.next", new KeySendPreset("Ctrl + Tab", "下一个标签页") },
                { "edit.tab.prev", new KeySendPreset("Ctrl + Shift + Tab", "上一个标签页") },
                { "clipboard.copy", new KeySendPreset("Ctrl + C", "复制") },
                { "clipboard.cut", new KeySendPreset("Ctrl + X", "剪切") },
                { "clipboard.paste", new KeySendPreset("Ctrl + V", "粘贴") },
                // 1.14.4: these two did more than one key or drove the app itself;
                // they are retired because a plain key send covers the common case.
                { "edit.insert.line.below", new KeySendPreset("Enter", "回车") },
                { "system.reload", new KeySendPreset("F5", "刷新") }
            };

        public static bool TryGetKeySendReplacement(string action, out string param)
        {
            param = null;
            KeySendPreset preset;
            if (string.IsNullOrEmpty(action) || !KeySendReplacements.TryGetValue(action, out preset))
                return false;
            param = preset.Combo;
            return true;
        }

        internal static bool TryGetKeySendPreset(string action, out KeySendPreset preset)
        {
            preset = null;
            if (string.IsNullOrEmpty(action)) return false;
            return KeySendReplacements.TryGetValue(action, out preset);
        }

        /// <summary>Rewrites a mapping that still points at a retired key-send action.</summary>
        public static bool MigrateKeySendAction(KeyMapping mapping)
        {
            KeySendPreset preset;
            if (mapping == null || !TryGetKeySendPreset(mapping.Action, out preset)) return false;
            mapping.Action = "input.keys";
            mapping.Param = preset.Combo;
            if (string.IsNullOrEmpty(mapping.Note)) mapping.Note = preset.Note;
            return true;
        }

        /// <summary>Same migration for macro steps (macro files outlive the app version).</summary>
        internal static bool MigrateKeySendStep(MacroStep step)
        {
            string param;
            if (step == null || !TryGetKeySendReplacement(step.Action, out param)) return false;
            step.Action = "input.keys";
            step.Param = param;
            return true;
        }

        public static string Describe(string id, string param)
        {
            ActionDef def = Get(id);
            if (def.Id == None) return "不执行任何操作";
            if (def.Missing) return def.IsMacro ? "宏动作不存在" : "插件动作未加载：" + Shorten(id, 40);
            int count = ParseCount(param, 1);
            string name = def.Name;
            switch (def.ParamKind)
            {
                case ActionParamKind.Count:
                    if (count > 1) return name + " ×" + count;
                    return name;
                case ActionParamKind.Path:
                    if (!string.IsNullOrEmpty(param)) return name + "：" + Shorten(param, 28);
                    return name;
                case ActionParamKind.Folder:
                    if (!string.IsNullOrEmpty(param)) return name + "：" + Shorten(param, 28);
                    return name;
                case ActionParamKind.Url:
                    if (!string.IsNullOrEmpty(param)) return name + "：" + Shorten(param, 34);
                    return name;
                case ActionParamKind.Text:
                    if (!string.IsNullOrEmpty(param)) return name + "：" + Shorten(param.Replace("\r", " ").Replace("\n", " "), 24);
                    return name;
                case ActionParamKind.KeyCombo:
                    if (string.IsNullOrEmpty(param)) return name;
                    // Send-keys rows show the combination itself; the action name
                    // adds nothing the user needs to read on every row.
                    if (def.Id == "input.keys") return param;
                    return name + "：" + param;
                case ActionParamKind.MouseClick:
                    MouseClickPoint point = MouseClickPoint.Parse(param);
                    string held = point.ModifierText;
                    return name + "：" + (held.Length == 0 ? "" : held + " + ")
                        + point.ButtonName + " (" + point.X + ", " + point.Y + ")";
                case ActionParamKind.MousePoint:
                    MouseClickPoint target = MouseClickPoint.Parse(param);
                    return name + "：(" + target.X + ", " + target.Y + ")";
                case ActionParamKind.Select:
                    if (string.IsNullOrEmpty(param)) return name;
                    return name + "：" + Shorten(def.ChoiceLabelOf(param), 20);
            }
            return name;
        }

        /// <summary>
        /// True for actions whose row would otherwise read as a raw input
        /// (a key combination or a pair of coordinates) rather than a name.
        /// Those are the only ones that accept a user note.
        /// </summary>
        public static bool SupportsNote(string action)
        {
            return action == "input.keys" || action == "mouse.click";
        }

        /// <summary>
        /// Row text for a mapping: rows with a user note show it first. Key-send
        /// rows then fall back to the combination (never the generic action name);
        /// other actions keep their normal description.
        /// </summary>
        public static string Describe(KeyMapping mapping)
        {
            if (mapping == null) return "";
            bool noted = !string.IsNullOrEmpty(mapping.Note) && SupportsNote(mapping.Action);
            if (noted) return mapping.Note;
            if (mapping.Action == "input.keys")
            {
                if (!string.IsNullOrEmpty(mapping.Param)) return mapping.Param;
                return "发送按键";
            }
            return Describe(mapping.Action, mapping.Param);
        }

        public static string DefaultParamFor(ActionDef def)
        {
            if (def == null) return "";
            return def.DefaultParam ?? "";
        }

        public static string NormalizeParam(string action, string param)
        {
            ActionDef def = Get(action);
            switch (def.ParamKind)
            {
                case ActionParamKind.Count:
                    return ParseCount(param, 1).ToString();
                case ActionParamKind.None:
                    return "";
                case ActionParamKind.MouseClick:
                    return MouseClickPoint.Format(MouseClickPoint.Parse(param));
                case ActionParamKind.MousePoint:
                    {
                        MouseClickPoint point = MouseClickPoint.Parse(param);
                        return point.X + ";" + point.Y;
                    }
                case ActionParamKind.Select:
                    {
                        // A stored value that the plugin no longer offers is kept as
                        // is: the user may be looking at a temporarily unloaded
                        // plugin, and rewriting the value would silently change what
                        // the shortcut does once the plugin comes back. A brand new
                        // row (empty) starts on the first choice.
                        if (!string.IsNullOrEmpty(param)) return param;
                        return def.ChoiceCount > 0 ? def.ChoiceValues[0] : "";
                    }
                default:
                    return param ?? "";
            }
        }

        public static int ParseCount(string param, int fallback)
        {
            int n;
            if (int.TryParse(param, out n))
            {
                if (n < 1) return 1;
                if (n > 100) return 100;
                return n;
            }
            return fallback;
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (text.Length <= max) return text;
            return text.Substring(0, max - 1) + "…";
        }

        private static Dictionary<string, ActionDef> BuildIndex()
        {
            Dictionary<string, ActionDef> map = new Dictionary<string, ActionDef>(StringComparer.OrdinalIgnoreCase);
            foreach (ActionDef d in _actions) map[d.Id] = d;
            return map;
        }

        private static List<ActionDef> BuildActions()
        {
            List<ActionDef> a = new List<ActionDef>();
            const string cursor = "光标移动";
            const string select = "选择文字";
            const string del = "删除与编辑";
            const string win = "窗口";
            const string sys = "系统与媒体";
            const string launch = "启动与输入";
            const string mouse = "鼠标";

            // 常用类目放最前：启动与输入 → 鼠标 → 其他，其余按使用频率排列。
            a.Add(new ActionDef("launch.program", launch, "运行程序或打开文件", "使用系统默认方式运行程序、文件或快捷方式。", ActionParamKind.Path, "", false));
            a.Add(new ActionDef("launch.folder", launch, "打开文件夹", "在资源管理器中打开指定文件夹。", ActionParamKind.Path, "", false));
            a.Add(new ActionDef("launch.url", launch, "打开网址", "使用默认浏览器打开网址。", ActionParamKind.Url, "https://", false));
            a.Add(new ActionDef("input.text", launch, "输入文本", "向当前窗口输入一段文本（支持中文）。", ActionParamKind.Text, "", false));
            a.Add(new ActionDef("input.keys", launch, "发送按键", "向当前窗口发送自定义组合键，也可以是鼠标左键/右键/中键或滚轮上下。点按键框后按一个主键，修饰键由上方开关决定。", ActionParamKind.KeyCombo, "", true));

            a.Add(new ActionDef("mouse.move", mouse, "鼠标移动",
            "把光标移动到指定坐标，只移动、不点击。需要“悬停才展开菜单”的界面，用它加一步，再给下一步设置等待时间来充当悬停时长。",
            ActionParamKind.MousePoint, "0;0", false));
        a.Add(new ActionDef("mouse.click", mouse, "鼠标点击",
                "在指定坐标点击一次。点「抓取坐标」后窗口会隐藏，按下 CapsLock 记录鼠标位置（Esc 取消）。",
                ActionParamKind.MouseClick, "L;0;0", false));

            a.Add(new ActionDef(None, "其他", "不执行操作", "拦截该组合键，但不执行任何动作。", ActionParamKind.None, "", false));

            a.Add(new ActionDef("move.word.left", cursor, "按单词左移", "光标向左移动指定单词数。", ActionParamKind.Count, "1", true));
            a.Add(new ActionDef("move.word.right", cursor, "按单词右移", "光标向右移动指定单词数。", ActionParamKind.Count, "1", true));

            a.Add(new ActionDef("select.up", select, "向上选中", "按住 Shift 向上选中指定次数。", ActionParamKind.Count, "1", true));
            a.Add(new ActionDef("select.down", select, "向下选中", "按住 Shift 向下选中指定次数。", ActionParamKind.Count, "1", true));
            a.Add(new ActionDef("select.left", select, "向左选中", "按住 Shift 向左选中指定次数。", ActionParamKind.Count, "1", true));
            a.Add(new ActionDef("select.right", select, "向右选中", "按住 Shift 向右选中指定次数。", ActionParamKind.Count, "1", true));
            a.Add(new ActionDef("select.word.left", select, "向左选中单词", "按单词向左扩展选区。", ActionParamKind.Count, "1", true));
            a.Add(new ActionDef("select.word.right", select, "向右选中单词", "按单词向右扩展选区。", ActionParamKind.Count, "1", true));
            a.Add(new ActionDef("select.current.word", select, "选中当前单词", "快速选中光标所在的单词。", ActionParamKind.None, "", false));
            a.Add(new ActionDef("select.current.line", select, "选中当前行", "选中光标所在的整行。", ActionParamKind.None, "", false));

            a.Add(new ActionDef("delete.line", del, "清空当前行", "选中当前行内容并删除。", ActionParamKind.None, "", false));
            a.Add(new ActionDef("delete.all", del, "删除全部内容", "全选后删除。", ActionParamKind.None, "", false));
            a.Add(new ActionDef("delete.to.line.start", del, "删除到行首", "删除光标左侧至行首的内容。", ActionParamKind.None, "", true));
            a.Add(new ActionDef("delete.to.line.end", del, "删除到行尾", "删除光标右侧至行尾的内容。", ActionParamKind.None, "", true));
            a.Add(new ActionDef("delete.to.page.start", del, "删除到页首", "删除光标左侧至文档开头的内容。", ActionParamKind.None, "", false));
            a.Add(new ActionDef("delete.to.page.end", del, "删除到页尾", "删除光标右侧至文档末尾的内容。", ActionParamKind.None, "", false));

            a.Add(new ActionDef("window.topmost", win, "窗口置顶", "切换当前窗口的置顶状态。", ActionParamKind.None, "", false));
            a.Add(new ActionDef("window.transparency", win, "窗口半透明", "切换当前窗口的半透明状态。", ActionParamKind.None, "", false));
            a.Add(new ActionDef("window.minimize", win, "最小化窗口", "最小化当前窗口。", ActionParamKind.None, "", false));
            a.Add(new ActionDef("window.maximize", win, "最大化/还原窗口", "在当前窗口的最大化和还原之间切换。", ActionParamKind.None, "", false));
            a.Add(new ActionDef("window.close", win, "关闭窗口", "向当前窗口发送关闭消息。", ActionParamKind.None, "", false));
            a.Add(new ActionDef("window.snap.left", win, "窗口贴左", "将当前窗口放到屏幕左半边。", ActionParamKind.None, "", false));
            a.Add(new ActionDef("window.snap.right", win, "窗口贴右", "将当前窗口放到屏幕右半边。", ActionParamKind.None, "", false));

            a.Add(new ActionDef("system.calc", sys, "打开计算器", "打开 Windows 系统计算器。", ActionParamKind.None, "", false));
            a.Add(new ActionDef("system.show", sys, "打开主界面", "显示并激活 CapsLock Lite 主窗口。", ActionParamKind.None, "", false));
            a.Add(new ActionDef("system.translate", sys, "翻译选中文字", "复制选中文字并用浏览器打开必应翻译。", ActionParamKind.None, "", false));
            a.Add(new ActionDef("system.escape", sys, "转义选中文字", "复制选中文字并生成 JavaScript 转义字符串。", ActionParamKind.None, "", false));
            a.Add(new ActionDef("system.toggle.caps", sys, "切换大小写", "切换 Caps Lock 状态。", ActionParamKind.None, "", false));
            a.Add(new ActionDef("system.media.next", sys, "下一曲", "发送媒体下一曲按键。", ActionParamKind.None, "", true));
            a.Add(new ActionDef("system.media.prev", sys, "上一曲", "发送媒体上一曲按键。", ActionParamKind.None, "", true));
            a.Add(new ActionDef("system.media.play", sys, "播放/暂停", "发送媒体播放/暂停按键。", ActionParamKind.None, "", true));
            a.Add(new ActionDef("system.volume.up", sys, "音量增加", "发送音量增加按键。", ActionParamKind.None, "", true));
            a.Add(new ActionDef("system.volume.down", sys, "音量减小", "发送音量减小按键。", ActionParamKind.None, "", true));
            a.Add(new ActionDef("system.volume.mute", sys, "静音", "发送静音按键。", ActionParamKind.None, "", false));

            return a;
        }

        public static List<KeyMapping> CreateDefaultMappings()
        {
            List<KeyMapping> list = new List<KeyMapping>();
            int index = 0;

            Add(list, ref index, true, false, false, false, false, Keys.E, "input.keys", "Up", "上移", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.D, "input.keys", "Down", "下移", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.S, "input.keys", "Left", "左移", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.F, "input.keys", "Right", "右移", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.P, "input.keys", "Home", "移到行首", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.OemSemicolon, "input.keys", "End", "移到行尾", "once");
            Add(list, ref index, true, false, true, false, false, Keys.P, "input.keys", "Ctrl + Home", "移到页首", "hold");
            Add(list, ref index, true, false, true, false, false, Keys.OemSemicolon, "input.keys", "Ctrl + End", "移到页尾", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.I, "select.up", "1", "", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.K, "select.down", "1", "", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.J, "select.left", "1", "", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.L, "select.right", "1", "", "hold");
            Add(list, ref index, true, false, true, false, false, Keys.I, "select.up", "3", "", "hold");
            Add(list, ref index, true, false, true, false, false, Keys.K, "select.down", "3", "", "hold");
            Add(list, ref index, true, false, true, false, false, Keys.J, "select.left", "5", "", "hold");
            Add(list, ref index, true, false, true, false, false, Keys.L, "select.right", "5", "", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.H, "select.word.left", "1", "", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.M, "select.word.right", "1", "", "hold");
            Add(list, ref index, true, false, true, false, false, Keys.H, "select.word.left", "3", "", "hold");
            Add(list, ref index, true, false, true, false, false, Keys.M, "select.word.right", "3", "", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.N, "select.current.word", "", "", "once");
            Add(list, ref index, true, false, true, false, false, Keys.N, "select.current.line", "", "", "once");
            Add(list, ref index, true, false, false, false, false, Keys.U, "input.keys", "Shift + Home", "选中到行首", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.O, "input.keys", "Shift + End", "选中到行尾", "once");
            Add(list, ref index, true, false, true, false, false, Keys.U, "input.keys", "Ctrl + Shift + Home", "选中到页首", "hold");
            Add(list, ref index, true, false, true, false, false, Keys.O, "input.keys", "Ctrl + Shift + End", "选中到页尾", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.W, "input.keys", "Backspace", "向后删除", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.R, "input.keys", "Delete", "向前删除", "hold");
            Add(list, ref index, true, false, true, false, false, Keys.W, "input.keys", "Ctrl + Backspace", "向前删除单词", "hold");
            Add(list, ref index, true, false, true, false, false, Keys.R, "input.keys", "Ctrl + Delete", "向后删除单词", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.OemOpenBrackets, "delete.to.line.start", "", "", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.OemQuestion, "delete.to.line.end", "", "", "hold");
            Add(list, ref index, true, false, true, false, false, Keys.OemOpenBrackets, "delete.to.page.start", "", "", "once");
            Add(list, ref index, true, false, true, false, false, Keys.OemQuestion, "delete.to.page.end", "", "", "once");
            Add(list, ref index, true, false, false, false, false, Keys.Back, "delete.line", "", "", "once");
            Add(list, ref index, true, false, true, false, false, Keys.Back, "delete.all", "", "", "once");
            Add(list, ref index, true, false, false, false, false, Keys.Space, "input.keys", "Space", "空格", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.Return, "input.keys", "Enter", "回车", "once");
            Add(list, ref index, true, false, false, false, false, Keys.X, "input.keys", "Ctrl + X", "剪切", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.C, "input.keys", "Ctrl + C", "复制", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.V, "input.keys", "Ctrl + V", "粘贴", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.F1, "system.show", "", "", "once");
            Add(list, ref index, true, false, false, false, false, Keys.F2, "system.calc", "", "", "once");
            Add(list, ref index, true, false, false, false, false, Keys.F3, "system.translate", "", "", "once");
            Add(list, ref index, true, false, false, false, false, Keys.F4, "window.transparency", "", "", "once");
            Add(list, ref index, true, false, false, false, false, Keys.F5, "input.keys", "F5", "刷新", "once");
            Add(list, ref index, true, false, false, false, false, Keys.F6, "window.topmost", "", "", "once");
            Add(list, ref index, true, false, false, false, false, Keys.F8, "system.escape", "", "", "once");
            Add(list, ref index, true, false, true, false, false, Keys.S, "move.word.left", "1", "", "hold");
            Add(list, ref index, true, false, true, false, false, Keys.F, "move.word.right", "1", "", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.F9, "system.volume.mute", "", "", "once");
            Add(list, ref index, true, false, false, false, false, Keys.F10, "system.volume.down", "", "", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.F11, "system.volume.up", "", "", "hold");
            Add(list, ref index, true, false, true, false, false, Keys.Space, "input.keys", "Space", "空格", "hold");
            Add(list, ref index, true, false, true, false, false, Keys.Return, "input.keys", "Enter", "回车", "hold");
            Add(list, ref index, true, false, false, false, false, Keys.G, "input.keys", "Enter", "回车", "hold");
            Add(list, ref index, true, false, true, false, false, Keys.G, "input.keys", "Enter", "回车", "hold");

            return list;
        }

        private static void Add(List<KeyMapping> list, ref int index, bool caps, bool ctrl, bool alt, bool shift, bool win, Keys key, string action, string param, string note)
        {
            Add(list, ref index, caps, ctrl, alt, shift, win, key, action, param, note, null);
        }

        private static void Add(List<KeyMapping> list, ref int index, bool caps, bool ctrl, bool alt, bool shift, bool win, Keys key, string action, string param, string note, string triggerMode)
        {
            index++;
            KeyMapping m = new KeyMapping();
            m.Id = "default-" + index.ToString("00");
            m.Caps = caps;
            m.Ctrl = ctrl;
            m.Alt = alt;
            m.Shift = shift;
            m.Win = win;
            m.KeyCode = key;
            m.Action = action;
            m.Param = param;
            m.Note = note ?? "";
            m.TriggerMode = string.IsNullOrEmpty(triggerMode) ? "once" : triggerMode;
            m.Enabled = true;
            list.Add(m);
        }

        private static void Add(List<KeyMapping> list, ref int index, bool caps, bool ctrl, bool alt, bool shift, bool win, Keys key, string action, string param)
        {
            Add(list, ref index, caps, ctrl, alt, shift, win, key, action, param, null);
        }

        /// <summary>Name a retired key-send action deserves in the list.</summary>
        internal static string DefaultNoteFor(string retiredAction)
        {
            KeySendPreset preset;
            return TryGetKeySendPreset(retiredAction, out preset) ? preset.Note : "";
        }
    }

    internal sealed class KeySendPreset
    {
        public readonly string Combo;
        public readonly string Note;

        public KeySendPreset(string combo, string note)
        {
            Combo = combo;
            Note = note;
        }
    }
}
