using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CapsLockLite
{
    /// <summary>
    /// On-disk shape of a macro file. One file may hold several macros so a user
    /// can export a whole set (for example "work" and "home" variants) at once.
    /// The file contains no code, only action ids, parameters, delays and counts.
    /// </summary>
    internal sealed class MacroFile
    {
        public int Version { get; set; }
        public List<Macro> Macros { get; set; }

        public MacroFile()
        {
            Version = 1;
            Macros = new List<Macro>();
        }
    }

    internal static class MacroFileStore
    {
        public const string Extension = ".clmacro";
        public const string Filter = "宏文件 (*.clmacro)|*.clmacro|所有文件 (*.*)|*.*";

        private static readonly JavaScriptSerializer _serializer = new JavaScriptSerializer();

        public static void Save(string path, IList<Macro> macros)
        {
            MacroFile file = new MacroFile();
            if (macros != null)
            {
                for (int i = 0; i < macros.Count; i++)
                {
                    if (macros[i] != null) file.Macros.Add(macros[i].Clone());
                }
            }
            string json = JsonPretty.Format(_serializer.Serialize(file));
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        public static List<Macro> Load(string path)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            MacroFile file = _serializer.Deserialize<MacroFile>(json);
            if (file == null) throw new InvalidDataException("宏文件内容为空。");
            List<Macro> list = file.Macros == null ? new List<Macro>() : file.Macros;
            return ConfigStore.NormalizeMacroList(list);
        }
    }

    /// <summary>Helpers shared by the macro page, the editor and the runner.</summary>
    internal static class MacroLibrary
    {
        public const int MaxNesting = 8;

        public static Macro FindById(IList<Macro> macros, string id)
        {
            if (macros == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < macros.Count; i++)
            {
                Macro m = macros[i];
                if (m != null && string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) return m;
            }
            return null;
        }

        public static Macro FindByActionId(IList<Macro> macros, string actionId)
        {
            if (!ActionCatalog.IsMacroActionId(actionId)) return null;
            return FindById(macros, actionId.Substring(ActionCatalog.MacroIdPrefix.Length));
        }

        public static int BoundCount(IList<KeyMapping> mappings, Macro macro)
        {
            if (mappings == null || macro == null) return 0;
            string id = macro.ActionId;
            int count = 0;
            for (int i = 0; i < mappings.Count; i++)
            {
                KeyMapping m = mappings[i];
                if (m != null && string.Equals(m.Action, id, StringComparison.OrdinalIgnoreCase)) count++;
            }
            return count;
        }

        public static string UniqueName(IList<Macro> macros, string wanted)
        {
            string baseName = string.IsNullOrEmpty(wanted) ? "新建宏" : wanted.Trim();
            if (baseName.Length > 60) baseName = baseName.Substring(0, 60);
            if (!NameExists(macros, baseName, null)) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                string candidate = baseName + " " + i;
                if (!NameExists(macros, candidate, null)) return candidate;
            }
            return baseName + " " + Guid.NewGuid().ToString("N").Substring(0, 4);
        }

        /// <summary>
        /// Rewrites <paramref name="steps"/> in the order the editor list shows them.
        /// Steps the list does not know about keep their old relative order at the end,
        /// so a reorder can never drop data.
        /// </summary>
        public static List<MacroStep> ApplyStepOrder(IList<MacroStep> steps, IList<MacroStep> ordered)
        {
            List<MacroStep> result = new List<MacroStep>();
            if (ordered != null)
            {
                for (int i = 0; i < ordered.Count; i++)
                {
                    MacroStep step = ordered[i];
                    if (step == null || result.Contains(step)) continue;
                    result.Add(step);
                }
            }
            if (steps != null)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    if (steps[i] != null && !result.Contains(steps[i])) result.Add(steps[i]);
                }
            }
            return result;
        }

        public static bool NameExists(IList<Macro> macros, string name, string exceptId)
        {
            if (macros == null || string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < macros.Count; i++)
            {
                Macro m = macros[i];
                if (m == null) continue;
                if (exceptId != null && string.Equals(m.Id, exceptId, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(m.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// Rebuilds the "macro.&lt;id&gt;" actions so the picker, the mapping list
        /// and the editor all see the macros that exist right now.
        /// </summary>
        public static void RefreshCatalog(IList<Macro> macros)
        {
            ActionCatalog.RegisterMacros(macros);
        }

        /// <summary>
        /// Walks the macro graph without running anything. Returns a user facing
        /// error, or null when the macro can run. Missing nested macros are not an
        /// error here: the editor may reference a macro the user is about to create.
        /// </summary>
        public static string ValidateNesting(IList<Macro> macros, Macro root)
        {
            if (root == null) return "宏不存在。";
            List<string> stack = new List<string>();
            return Walk(macros, root, stack);
        }

        private static string Walk(IList<Macro> macros, Macro macro, List<string> stack)
        {
            if (macro == null) return null;
            if (stack.Count >= MaxNesting)
                return "嵌套层数超过 " + MaxNesting + " 层，请精简宏结构。";
            for (int i = 0; i < stack.Count; i++)
            {
                if (string.Equals(stack[i], macro.Id, StringComparison.OrdinalIgnoreCase))
                    return "检测到循环引用：" + macro.Name + " 间接引用了自己。";
            }
            stack.Add(macro.Id);
            if (macro.Steps != null)
            {
                for (int i = 0; i < macro.Steps.Count; i++)
                {
                    MacroStep step = macro.Steps[i];
                    if (step == null || !ActionCatalog.IsMacroActionId(step.Action)) continue;
                    Macro child = FindByActionId(macros, step.Action);
                    string error = Walk(macros, child, stack);
                    if (error != null) return error;
                }
            }
            stack.RemoveAt(stack.Count - 1);
            return null;
        }

        /// <summary>Copy that never shares step objects or the step list.</summary>
        public static Macro Duplicate(Macro macro)
        {
            return macro == null ? null : macro.Clone();
        }
    }
}
