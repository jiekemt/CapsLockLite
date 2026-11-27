using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CapsLockLite
{
    internal sealed class KeyMapping
    {
        public string Id { get; set; }
        public bool Caps { get; set; }
        public bool Ctrl { get; set; }
        public bool Alt { get; set; }
        public bool Shift { get; set; }
        public bool Win { get; set; }
        public int Key { get; set; }
        public string Action { get; set; }
        public string Param { get; set; }

        /// <summary>
        /// Optional human name shown instead of the raw key combination in the
        /// shortcut list (for example "复制" instead of "Ctrl + C"). Only used
        /// by key-send actions.
        /// </summary>
        public string Note { get; set; }

        public string TriggerMode { get; set; }
        public bool Enabled { get; set; }

        public KeyMapping()
        {
            Id = Guid.NewGuid().ToString("N");
            Caps = true;
            Key = (int)Keys.None;
            Action = ActionCatalog.None;
            Param = "";
            Note = "";
            Enabled = true;
        }

        [ScriptIgnore]
        public Keys KeyCode
        {
            get { return (Keys)Key; }
            set { Key = (int)value; }
        }

        [ScriptIgnore]
        public string TriggerText
        {
            get { return KeyText.FormatTrigger(this); }
        }

        [ScriptIgnore]
        public string ActionText
        {
            get { return ActionCatalog.Describe(this); }
        }

        public KeyMapping Clone()
        {
            KeyMapping m = new KeyMapping();
            m.CopyFrom(this);
            return m;
        }

        /// <summary>
        /// Copies every editable field from another mapping. Used when the editor
        /// returns a candidate: copying field by field is how new fields (a note,
        /// a restore flag) silently stopped being saved before.
        /// </summary>
        public void CopyFrom(KeyMapping other)
        {
            if (other == null) return;
            Id = other.Id;
            Caps = other.Caps;
            Ctrl = other.Ctrl;
            Alt = other.Alt;
            Shift = other.Shift;
            Win = other.Win;
            Key = other.Key;
            Action = other.Action;
            Param = other.Param;
            Note = other.Note;
            TriggerMode = other.TriggerMode;
            Enabled = other.Enabled;
        }
    }

    /// <summary>
    /// One step of a macro. A step names another action (built-in, plugin or
    /// macro) plus its parameter, how long to wait after it runs, and how many
    /// times to repeat it before moving on.
    /// </summary>
    internal sealed class MacroStep
    {
        public string Action { get; set; }
        public string Param { get; set; }

        /// <summary>
        /// User-chosen name for this step, shown in the 动作 column instead of the
        /// action description. The 参数 column keeps showing the real parameter.
        /// </summary>
        public string Note { get; set; }

        /// <summary>Wait after this step finishes, in milliseconds (0..60000).</summary>
        public int DelayMs { get; set; }

        /// <summary>Times to run this step before the next one (1..999).</summary>
        public int Count { get; set; }

        public MacroStep()
        {
            Action = ActionCatalog.None;
            Param = "";
            Note = "";
            DelayMs = 50;
            Count = 1;
        }

        [ScriptIgnore]
        public string ActionText
        {
            get { return ActionCatalog.Describe(Action, Param); }
        }

        public MacroStep Clone()
        {
            MacroStep step = new MacroStep();
            step.Action = Action;
            step.Param = Param;
            step.Note = Note;
            step.DelayMs = DelayMs;
            step.Count = Count;
            return step;
        }
    }

    /// <summary>
    /// A named list of steps that can be bound like any other action and can be
    /// nested inside another macro through the "macro.&lt;id&gt;" action id.
    /// </summary>
    internal sealed class Macro
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Note { get; set; }

        /// <summary>
        /// The id it had in the exported file. Only used while importing so the
        /// user can be told which macros were renumbered; never persisted.
        /// </summary>
        [ScriptIgnore]
        public string ImportedFrom { get; set; }

        public List<MacroStep> Steps { get; set; }

        /// <summary>
        /// Put the cursor back where it was before the macro started. Off by default
        /// because most macros move the pointer on purpose.
        /// </summary>
        public bool RestoreCursor { get; set; }

        public Macro()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "新建宏";
            Note = "";
            Steps = new List<MacroStep>();
        }

        [ScriptIgnore]
        public string ActionId
        {
            get { return ActionCatalog.MacroIdPrefix + Id; }
        }

        [ScriptIgnore]
        public int StepCount
        {
            get { return Steps == null ? 0 : Steps.Count; }
        }

        public Macro Clone()
        {
            Macro copy = new Macro();
            copy.Id = Id;
            copy.Name = Name;
            copy.Note = Note;
            copy.RestoreCursor = RestoreCursor;
            copy.Steps = new List<MacroStep>();
            if (Steps != null)
            {
                for (int i = 0; i < Steps.Count; i++)
                {
                    if (Steps[i] != null) copy.Steps.Add(Steps[i].Clone());
                }
            }
            return copy;
        }
    }

    internal sealed class AppSettings
    {
        public int ConfigVersion { get; set; }
        public bool Enabled { get; set; }
        public bool RunAtStartup { get; set; }
        public bool StartMinimized { get; set; }
        public bool DarkMode { get; set; }

        public string TapAction { get; set; }
        public int LongPressMs { get; set; }

        /// <summary>
        /// Wheel units needed for one wheel-shortcut action. Windows counts one
        /// standard notch as 120, so 120 means "one notch, one action"; a wheel or
        /// trackpad that sends smaller steps can be set lower to fire per notch.
        /// </summary>
        public int WheelStep { get; set; }

        /// <summary>
        /// Where synthetic input goes: "auto" (driver when available), "system"
        /// (SendInput) or "driver" (Interception). See InputPipeline.
        /// </summary>
        public string InputBackend { get; set; }
        public List<KeyMapping> Mappings { get; set; }

        /// <summary>Plugin file names in the order the user arranged them. Unknown names
        /// are ignored and new plugins fall in after the known ones, so this list
        /// never blocks a plugin from loading.</summary>
        public List<string> PluginOrder { get; set; }

        /// <summary>User macros. Each one is also exposed as a bindable action.</summary>
        public List<Macro> Macros { get; set; }

        /// <summary>
        /// Shortcut lists bound to a foreground program. The global list above
        /// stays the fallback whenever none of these matches.
        /// </summary>
        public List<ShortcutPreset> Presets { get; set; }

        /// <summary>Last window size the user left the app at (0 = never set).</summary>
        public int WindowWidth { get; set; }
        public int WindowHeight { get; set; }
        public bool WindowMaximized { get; set; }

        public AppSettings()
        {
            ConfigVersion = 6;
            Enabled = true;
            RunAtStartup = false;
            StartMinimized = false;
            DarkMode = false;
            TapAction = "caps";
            LongPressMs = 350;
            WheelStep = 120;
            InputBackend = "auto";
            Mappings = new List<KeyMapping>();
            PluginOrder = new List<string>();
            Macros = new List<Macro>();
            Presets = new List<ShortcutPreset>();
            WindowWidth = 0;
            WindowHeight = 0;
            WindowMaximized = false;
        }
    }

    /// <summary>
    /// A shortcut list bound to one program. While that program owns the
    /// foreground window its list replaces the global one; when it is empty (a
    /// freshly created preset) the global list stays in charge.
    /// </summary>
    internal sealed class ShortcutPreset
    {
        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>
        /// Executable file name without its extension, compared ignoring case.
        /// Empty means "not bound to a program yet".
        /// </summary>
        public string Process { get; set; }

        public List<KeyMapping> Mappings { get; set; }

        public ShortcutPreset()
        {
            Id = Guid.NewGuid().ToString("N");
            Name = "";
            Process = "";
            Mappings = new List<KeyMapping>();
        }

        [ScriptIgnore]
        public bool Bound
        {
            get { return !string.IsNullOrEmpty(Process); }
        }

        public ShortcutPreset Clone()
        {
            ShortcutPreset copy = new ShortcutPreset();
            copy.Id = Id;
            copy.Name = Name;
            copy.Process = Process;
            copy.Mappings = new List<KeyMapping>();
            if (Mappings != null)
            {
                for (int i = 0; i < Mappings.Count; i++)
                {
                    if (Mappings[i] != null) copy.Mappings.Add(Mappings[i].Clone());
                }
            }
            return copy;
        }
    }

    internal sealed class HotkeyRegistrationFailure
    {
        public KeyMapping Mapping { get; private set; }
        public string Reason { get; private set; }

        public HotkeyRegistrationFailure(KeyMapping mapping, string reason)
        {
            Mapping = mapping;
            Reason = reason;
        }
    }
}
