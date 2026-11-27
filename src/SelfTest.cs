using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CapsLockLite
{
    internal static class SelfTest
    {
        private static readonly StringBuilder _log = new StringBuilder();
        private static int _failed;

        public static void Run()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.log");
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string testRoot = Path.Combine(baseDirectory,
                "selftest-data-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(testRoot);
            }
            catch
            {
                testRoot = Path.Combine(Path.GetTempPath(),
                    "CapsLockLite-selftest-" + Guid.NewGuid().ToString("N"));
                try { Directory.CreateDirectory(testRoot); }
                catch { }
            }
            try
            {
                ConfigStore.UseTestDirectory(testRoot);
                PluginRuntime.Initialize();

                _log.AppendLine("CapsLock Lite self-test");
                _log.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                _log.AppendLine("Executable: " + Application.ExecutablePath);
                _log.AppendLine("ConfigDir: " + ConfigStore.ConfigDirectory);
                _log.AppendLine();
                File.WriteAllText(path, _log.ToString(), new UTF8Encoding(false));

                Check("config directory is private", ConfigStore.IsConfigDirectoryPrivate, "");

                TestDefaults();
                TestRoundTrip();
                TestRepeat();
                TestCapsOnly();
                TestKeyText();
                TestMouseTriggers();
                TestMouseKeySend();
                TestMousePad();
                TestGhostPlacement();
                TestKeyboard();
                TestKeyboardLabels();
                TestKeyboardActions();
                TestPopupMenu();
                TestListInteraction();
                TestListSelection();
                TestMappingClipboard();
                TestTriggerChip();
                TestActionRowPlacement();
                TestReorderMath();
                TestMacros();
                TestKeySendMigration();
                TestMousePoint();
                TestInputs();
                TestMatching();
                TestConflicts();
                TestPresets();
                TestPlugins();
                TestPluginParamChoices();
                TestPluginTrust();
                TestPluginPackages();
                TestPluginSettings();
                TestHook();
                TestSyntheticLedger();
                TestDriverHoldRelease();
                TestRapidTaps();
                TestExtendedKeys();
                TestInputPipeline();
                TestDeliverySignal();
                TestSwallowedMouseRelease();
                TestPointerPin();
            }
            catch (Exception ex)
            {
                _failed++;
                _log.AppendLine("FATAL: " + ex);
            }
            finally
            {
                try { PluginRuntime.Shutdown(); }
                catch { }
                try { Directory.Delete(testRoot, true); }
                catch { }
            }

            _log.AppendLine();
            _log.AppendLine(_failed == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + _failed + ")");
            try { File.WriteAllText(path, _log.ToString(), new UTF8Encoding(false)); }
            catch { }
            // The log writer is asynchronous; give it a moment so a run does not lose
            // the last lines it produced.
            ConfigStore.FlushLog(2000);
            Environment.ExitCode = _failed == 0 ? 0 : 1;
        }

        private static void Check(string name, bool condition, string detail)
        {
            if (condition)
            {
                _log.AppendLine("PASS  " + name);
            }
            else
            {
                _failed++;
                _log.AppendLine("FAIL  " + name + "  " + (detail ?? ""));
            }
        }

        private static void TestDefaults()
        {
            List<KeyMapping> defaults = ActionCatalog.CreateDefaultMappings();
            Check("default mapping count", defaults.Count >= 55, "count=" + defaults.Count);

            HashSet<string> triggers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool unique = true;
            bool actionsValid = true;
            foreach (KeyMapping m in defaults)
            {
                string key = m.TriggerText;
                if (triggers.Contains(key)) unique = false;
                triggers.Add(key);
                if (ActionCatalog.Get(m.Action).Id != m.Action) actionsValid = false;
            }
            Check("default triggers are unique", unique, triggers.Count + " triggers");
            Check("default actions are valid", actionsValid, "");
            Check("default includes F2 calculator", ContainsAction(defaults, "system.calc"), "");
            Check("default F1 opens the main window",
                ContainsDefault(defaults, Keys.F1, false, "system.show", ""), "");
            Check("default Caps+E sends Up", ContainsDefault(defaults, Keys.E, false, "input.keys", "Up"), "");
        }

        private static bool ContainsAction(List<KeyMapping> list, string action)
        {
            foreach (KeyMapping m in list) if (m.Action == action) return true;
            return false;
        }

        private static bool ContainsDefault(List<KeyMapping> list, Keys key, bool alt, string action, string param)
        {
            foreach (KeyMapping m in list)
                if (m.KeyCode == key && m.Alt == alt && m.Action == action && m.Param == param) return true;
            return false;
        }

        private static void TestRoundTrip()
        {
            AppSettings original = ConfigStore.CreateDefault();
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-settings.json");
            ConfigStore.Export(original, path);
            AppSettings restored = ConfigStore.Import(path);
            Check("config export/import count", restored.Mappings.Count == original.Mappings.Count,
                restored.Mappings.Count + " vs " + original.Mappings.Count);
            Check("config export/import action", restored.Mappings.Count > 0 && restored.Mappings[0].Action == original.Mappings[0].Action, "");
            try { File.Delete(path); }
            catch { }

            // Short-press behaviour: the input-method tap is a valid setting, and an
            // unknown value still falls back to the caps toggle.
            AppSettings tap = ConfigStore.CreateDefault();
            tap.TapAction = "ime";
            ConfigStore.Normalize(tap);
            Check("tap action accepts the input method", tap.TapAction == "ime", tap.TapAction);
            tap.TapAction = "bogus";
            ConfigStore.Normalize(tap);
            Check("tap action falls back on unknown values", tap.TapAction == "caps", tap.TapAction);
        }

        /// <summary>
        /// The Caps layer is the only trigger the app accepts, and inside it an
        /// unbound key must stay silent instead of typing its normal character.
        /// </summary>
        private static void TestCapsOnly()
        {
            AppSettings settings = new AppSettings();
            settings.Mappings = new List<KeyMapping>();
            KeyMapping onlyE = new KeyMapping();
            onlyE.Caps = true;
            onlyE.KeyCode = Keys.E;
            onlyE.Action = "select.up";
            onlyE.Param = "1";
            settings.Mappings.Add(onlyE);

            using (HotkeyManager manager = new HotkeyManager(settings, IntPtr.Zero))
            {
                MethodInfo hook = typeof(HotkeyManager).GetMethod("OnHookKey", BindingFlags.Instance | BindingFlags.NonPublic);
                hook.Invoke(manager, new object[] { new KeyboardEventArgs(Keys.CapsLock, true, false, false, 0) });
                bool unbound = (bool)hook.Invoke(manager, new object[] { new KeyboardEventArgs(Keys.Z, true, false, false, 0) });
                Check("unbound key inside the Caps layer is swallowed", unbound, "");
                hook.Invoke(manager, new object[] { new KeyboardEventArgs(Keys.Z, false, true, false, 0) });
                hook.Invoke(manager, new object[] { new KeyboardEventArgs(Keys.CapsLock, false, true, false, 0) });
            }

            KeyMapping global = new KeyMapping();
            global.Caps = false;
            global.Ctrl = true;
            global.KeyCode = Keys.K;
            global.Action = "select.up";
            AppSettings normalize = new AppSettings();
            normalize.Mappings = new List<KeyMapping>();
            normalize.Mappings.Add(global);
            ConfigStore.Normalize(normalize);
            Check("legacy global hotkey folds into the Caps layer", global.Caps, "");
            Check("trigger validation requires the Caps layer",
                MappingEditorForm.TriggerValidationError(true) == null
                    && MappingEditorForm.TriggerValidationError(false) != null, "");
        }

        private static void TestRepeat()
        {
            AppSettings settings = new AppSettings();
            settings.Mappings = ActionCatalog.CreateDefaultMappings();
            ConfigStore.Normalize(settings);
            KeyMapping eMapping = null;
            foreach (KeyMapping m in settings.Mappings)
            {
                if (m.KeyCode == Keys.E && m.Caps && !m.Ctrl && !m.Alt) { eMapping = m; break; }
            }
            Check("repeat default follows action", eMapping != null && eMapping.TriggerMode == "hold", "");
            using (HotkeyManager manager = new HotkeyManager(settings, IntPtr.Zero))
            {
                int started = 0, repeated = 0, ended = 0;
                manager.HoldStarted += delegate { started++; };
                manager.HoldRepeated += delegate { repeated++; };
                manager.HoldEnded += delegate { ended++; };
                MethodInfo hook = typeof(HotkeyManager).GetMethod("OnHookKey", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo tick = typeof(HotkeyManager).GetMethod("RepeatTick", BindingFlags.Instance | BindingFlags.NonPublic);
                hook.Invoke(manager, new object[] { new KeyboardEventArgs(Keys.CapsLock, true, false, false, 0) });
                hook.Invoke(manager, new object[] { new KeyboardEventArgs(Keys.E, true, false, false, 0) });
                Check("repeat holds the key on first press", started == 1, started.ToString());
                tick.Invoke(manager, new object[] { null, EventArgs.Empty });
                Check("repeat keeps reporting while held", repeated == 1, repeated.ToString());
                hook.Invoke(manager, new object[] { new KeyboardEventArgs(Keys.E, false, true, false, 0) });
                tick.Invoke(manager, new object[] { null, EventArgs.Empty });
                Check("repeat releases once on release", ended == 1 && repeated == 1,
                    ended + "/" + repeated);
                hook.Invoke(manager, new object[] { new KeyboardEventArgs(Keys.CapsLock, false, true, false, 0) });
            }
            if (eMapping != null)
            {
                eMapping.TriggerMode = "once";
                using (HotkeyManager manager = new HotkeyManager(settings, IntPtr.Zero))
                {
                    int count = 0;
                    manager.ActionTriggered += delegate { count++; };
                    MethodInfo hook = typeof(HotkeyManager).GetMethod("OnHookKey", BindingFlags.Instance | BindingFlags.NonPublic);
                    MethodInfo tick = typeof(HotkeyManager).GetMethod("RepeatTick", BindingFlags.Instance | BindingFlags.NonPublic);
                    hook.Invoke(manager, new object[] { new KeyboardEventArgs(Keys.CapsLock, true, false, false, 0) });
                    hook.Invoke(manager, new object[] { new KeyboardEventArgs(Keys.E, true, false, false, 0) });
                    tick.Invoke(manager, new object[] { null, EventArgs.Empty });
                    Check("once mode does not repeat", count == 1, count.ToString());
                }
            }
            Check("input.keys is repeatable", ActionCatalog.IsRepeatable("input.keys"), "");
        }

        private static void CheckDefaults()
        {
            // 1.14.4: the shipped defaults come from the user's curated config.
            List<KeyMapping> list = ActionCatalog.CreateDefaultMappings();
            Check("default config has the curated size", list.Count == 57, list.Count.ToString());

            bool hasHold = false, hasOnce = false, hasCuratedEnter = false;
            bool retired = false;
            HashSet<string> chords = new HashSet<string>(StringComparer.Ordinal);
            bool duplicate = false;
            foreach (KeyMapping m in list)
            {
                if (m.TriggerMode == "hold") hasHold = true;
                if (m.TriggerMode == "once") hasOnce = true;
                if (m.Action == "edit.insert.line.below" || m.Action == "system.reload") retired = true;
                string chord = (m.Caps ? "C" : "-") + (m.Ctrl ? "c" : "-") + (m.Alt ? "a" : "-")
                    + (m.Shift ? "s" : "-") + (m.Win ? "w" : "-") + "-" + (int)m.KeyCode;
                if (!chords.Add(chord)) duplicate = true;
                if (m.Caps && !m.Ctrl && m.Alt && !m.Shift && !m.Win && m.KeyCode == Keys.Return
                    && m.Action == "input.keys" && m.Param == "Enter") hasCuratedEnter = true;
            }
            Check("defaults carry both trigger modes", hasHold && hasOnce, hasHold + "/" + hasOnce);
            Check("defaults have no duplicate chords", !duplicate, "");
            Check("defaults never use retired actions", !retired, "");
            Check("curated caps+alt+enter is a plain enter", hasCuratedEnter, "");
            Check("defaults keep their notes",
                list.Exists(delegate(KeyMapping m) { return m.KeyCode == Keys.W && !m.Alt && m.Note == "向后删除"; }), "");
        }

        private static void TestKeySendMigration()
        {            // 1.14.1 folded single-key native actions into input.keys; old configs
            // and imported macro files still carry the retired ids.
            KeyMapping mapping = new KeyMapping();
            mapping.Action = "clipboard.copy";
            mapping.Param = "";
            bool migrated = ActionCatalog.MigrateKeySendAction(mapping);
            Check("retired key action becomes input.keys", migrated && mapping.Action == "input.keys", mapping.Action);
            Check("retired key action keeps its combo", mapping.Param == "Ctrl + C", mapping.Param);

            MacroStep step = new MacroStep();
            step.Action = "edit.undo";
            step.Param = "";
            bool stepMigrated = ActionCatalog.MigrateKeySendStep(step);
            Check("retired macro step becomes input.keys", stepMigrated && step.Action == "input.keys", step.Action);
            Check("retired macro step keeps its combo", step.Param == "Ctrl + Z", step.Param);

            Check("retired action id is gone", ActionCatalog.Get("clipboard.copy").Id == ActionCatalog.None, "");
            bool retiredListed = false;
            foreach (ActionDef def in ActionCatalog.All)
            {
                if (def.Id == "delete.back" || def.Id == "clipboard.paste") { retiredListed = true; break; }
            }
            Check("retired actions are no longer listed", !retiredListed, "");

            KeyMapping live = new KeyMapping();
            live.Action = "select.up";
            live.Param = "2";
            Check("live actions are not migrated", !ActionCatalog.MigrateKeySendAction(live) && live.Action == "select.up", live.Action);

            // 1.14.4 retires two more actions; both have a plain-key replacement so
            // existing configs keep doing something recognisable.
            KeyMapping insertLine = new KeyMapping();
            insertLine.Action = "edit.insert.line.below";
            Check("retired insert-line becomes enter",
                ActionCatalog.MigrateKeySendAction(insertLine) && insertLine.Action == "input.keys" && insertLine.Param == "Enter" && insertLine.Note == "回车",
                insertLine.Action + "/" + insertLine.Param + "/" + insertLine.Note);
            KeyMapping reload = new KeyMapping();
            reload.Action = "system.reload";
            Check("retired reload becomes f5",
                ActionCatalog.MigrateKeySendAction(reload) && reload.Action == "input.keys" && reload.Param == "F5" && reload.Note == "刷新",
                reload.Action + "/" + reload.Param + "/" + reload.Note);
            Check("retired insert-line id is gone", ActionCatalog.Get("edit.insert.line.below").Id == ActionCatalog.None, "");
            Check("retired reload id is gone", ActionCatalog.Get("system.reload").Id == ActionCatalog.None, "");
            bool legacyListed = false;
            foreach (ActionDef def in ActionCatalog.All)
            {
                if (def.Id == "edit.insert.line.below" || def.Id == "system.reload") { legacyListed = true; break; }
            }
            Check("retired 1.14.4 actions are no longer listed", !legacyListed, "");

            CheckDefaults();

            // The four arrow actions are gone too: they are plain key sends now.
            KeyMapping arrow = new KeyMapping();
            arrow.Action = "move.up";
            arrow.Param = "1";
            Check("retired arrow becomes input.keys", ActionCatalog.MigrateKeySendAction(arrow) && arrow.Action == "input.keys", arrow.Action);
            Check("retired arrow gets its name", arrow.Param == "Up" && arrow.Note == "上移", arrow.Param + "/" + arrow.Note);
            Check("retired arrow id is gone", ActionCatalog.Get("move.up").Id == ActionCatalog.None, "");

            // The list shows the note when present, otherwise the combination
            // itself; the generic action name never appears on a key-send row.
            KeyMapping noted = new KeyMapping();
            noted.Action = "input.keys";
            noted.Param = "Ctrl + C";
            noted.Note = "复制";
            Check("key-send row prefers its note", ActionCatalog.Describe(noted) == "复制", ActionCatalog.Describe(noted));
            noted.Note = "";
            Check("key-send row falls back to the combo", ActionCatalog.Describe(noted) == "Ctrl + C", ActionCatalog.Describe(noted));
            noted.Param = "";
            Check("key-send row without anything says what it is", ActionCatalog.Describe(noted) == "发送按键", ActionCatalog.Describe(noted));

            // Mouse clicks accept a note too; other actions show their own name
            // and drop any stale note when the config is normalised.
            KeyMapping click = new KeyMapping();
            click.Action = "mouse.click";
            click.Param = "L;10;20";
            click.Note = "点开菜单";
            Check("mouse row prefers its note", ActionCatalog.Describe(click) == "点开菜单", ActionCatalog.Describe(click));
            click.Note = "";
            Check("mouse row falls back to its description",
                ActionCatalog.Describe(click).IndexOf("鼠标", StringComparison.Ordinal) >= 0,
                ActionCatalog.Describe(click));

            AppSettings scope = new AppSettings();
            KeyMapping keep = new KeyMapping();
            keep.Action = "mouse.click";
            keep.Param = "L;10;20";
            keep.Note = "  点开菜单  ";
            KeyMapping drop = new KeyMapping();
            drop.Action = "select.up";
            drop.Param = "2";
            drop.Note = "不该留下";
            scope.Mappings.Add(keep);
            scope.Mappings.Add(drop);
            ConfigStore.Normalize(scope);
            Check("normalise keeps notes only where they are shown",
                scope.Mappings.Count == 2
                && scope.Mappings[0].Note == "点开菜单"
                && scope.Mappings[1].Note == "",
                scope.Mappings.Count + ":" + (scope.Mappings.Count > 0 ? scope.Mappings[0].Note : "") + "/"
                + (scope.Mappings.Count > 1 ? scope.Mappings[1].Note : ""));

            // Saving an edit must copy the whole candidate: field-by-field copy
            // is how the note silently stopped being written back.
            KeyMapping edited = new KeyMapping();
            edited.Id = "same-id";
            edited.KeyCode = Keys.W;
            edited.Action = "input.keys";
            edited.Param = "Up";
            KeyMapping candidateRow = new KeyMapping();
            candidateRow.Id = "same-id";
            candidateRow.KeyCode = Keys.W;
            candidateRow.Action = "input.keys";
            candidateRow.Param = "Up";
            candidateRow.Note = "上移（已命名）";
            edited.CopyFrom(candidateRow);
            Check("copy-back keeps the note", edited.Note == "上移（已命名）", edited.Note);
        }

        private static void TestKeyText()
        {
            bool ctrl, alt, shift, win;
            Keys key;
            bool ok = KeyText.TryParseCombo("Ctrl + Shift + Left", out ctrl, out alt, out shift, out win, out key);
            Check("parse combo", ok && ctrl && shift && !alt && !win && key == Keys.Left, "");
            string text = KeyText.FormatTrigger(new KeyMapping { Caps = true, Alt = true, KeyCode = Keys.E });
            Check("format trigger", text == "Caps + Alt + E", text);

            Keys[] arrows = { Keys.Up, Keys.Down, Keys.Left, Keys.Right };
            bool arrowsRoundTrip = true;
            for (int i = 0; i < arrows.Length; i++)
            {
                Keys parsed;
                bool c3, a3, s3, w3;
                string combo = KeyText.FormatCombo(true, false, false, false, arrows[i]);
                bool okArrow = KeyText.TryParseCombo(combo, out c3, out a3, out s3, out w3, out parsed);
                if (!okArrow || parsed != arrows[i] || !c3) arrowsRoundTrip = false;
            }
            Check("arrow keys survive a combo round-trip", arrowsRoundTrip, "");
            Keys glyphKey;
            bool g2, g3, g4, g5;
            bool glyphOk = KeyText.TryParseCombo("Ctrl + ↑", out g2, out g3, out g4, out g5, out glyphKey);
            Check("arrow glyph parses as a send key", glyphOk && glyphKey == Keys.Up && g2, "Ctrl + ↑");

            bool numpadRoundTrip = true;
            string numpadDetail = "";
            for (int i = 0; i <= 9; i++)
            {
                Keys numpad = (Keys)((int)Keys.NumPad0 + i);
                Keys parsedNumpad;
                bool c6, a6, s6, w6;
                string combo = KeyText.FormatCombo(false, false, false, false, numpad);
                if (!KeyText.TryParseCombo(combo, out c6, out a6, out s6, out w6, out parsedNumpad)
                    || parsedNumpad != numpad)
                {
                    numpadRoundTrip = false;
                    numpadDetail = "failed at " + combo;
                    break;
                }
            }
            Check("numpad digits survive a combo round-trip", numpadRoundTrip, numpadDetail);

            Keys numpadAlt;
            bool n1, n2, n3, n4;
            bool numpadNames = KeyText.TryParseCombo("NumPad 8", out n1, out n2, out n3, out n4, out numpadAlt)
                && numpadAlt == Keys.NumPad8
                && KeyText.ParseKeyName("numpad8") == Keys.NumPad8
                && KeyText.ParseKeyName("Num 3") == Keys.NumPad3;
            Check("numpad names parse in every spelling", numpadNames, "");
        }

        /// <summary>
        /// Mouse triggers ride on KeyMapping.Key with pseudo codes outside the
        /// virtual-key range. The checks cover the key text, the configuration
        /// round-trip, the Caps-layer routing and the capture box.
        /// </summary>
        private static void TestMouseTriggers()
        {
            HashSet<Keys> codes = new HashSet<Keys>(MouseKeys.All);
            bool outsideKeyboardRange = true;
            for (int i = 0; i < MouseKeys.All.Length; i++)
            {
                if ((int)MouseKeys.All[i] <= 0xFF) outsideKeyboardRange = false;
                if (!MouseKeys.IsMouseKey(MouseKeys.All[i])) outsideKeyboardRange = false;
            }
            Check("mouse trigger codes are distinct pseudo keys",
                codes.Count == 5 && outsideKeyboardRange, "count=" + codes.Count);

            bool wheelOnly = MouseKeys.IsWheel((Keys)MouseKeys.WheelUp)
                && MouseKeys.IsWheel((Keys)MouseKeys.WheelDown)
                && !MouseKeys.IsWheel((Keys)MouseKeys.Left)
                && !MouseKeys.IsWheel((Keys)MouseKeys.Middle);
            Check("only the wheel codes are notches", wheelOnly, "");

            bool names = KeyText.FormatKey((Keys)MouseKeys.Left) == "鼠标左键"
                && KeyText.FormatKey((Keys)MouseKeys.Right) == "鼠标右键"
                && KeyText.FormatKey((Keys)MouseKeys.Middle) == "鼠标中键"
                && KeyText.FormatKey((Keys)MouseKeys.WheelUp) == "滚轮上滑"
                && KeyText.FormatKey((Keys)MouseKeys.WheelDown) == "滚轮下滑";
            Check("mouse trigger names", names, KeyText.FormatKey((Keys)MouseKeys.Left));

            KeyMapping mouseLeft = new KeyMapping();
            mouseLeft.Caps = true;
            mouseLeft.KeyCode = (Keys)MouseKeys.Left;
            mouseLeft.Action = "select.up";
            mouseLeft.Param = "1";
            Check("mouse trigger text", mouseLeft.TriggerText == "Caps + 鼠标左键",
                mouseLeft.TriggerText);

            // A wheel trigger has no key-up, so even a hand-edited "hold" has to
            // be normalized back to a single shot.
            AppSettings settings = new AppSettings();
            settings.Mappings = new List<KeyMapping>();
            KeyMapping wheelUp = new KeyMapping();
            wheelUp.Caps = true;
            wheelUp.KeyCode = (Keys)MouseKeys.WheelUp;
            wheelUp.Action = "select.up";
            wheelUp.Param = "1";
            wheelUp.TriggerMode = "hold";
            settings.Mappings.Add(wheelUp);
            settings.Mappings.Add(mouseLeft.Clone());
            ConfigStore.Normalize(settings);
            Check("wheel trigger is normalized to a single shot",
                settings.Mappings[0].TriggerMode == "once"
                    && settings.Mappings[0].Key == MouseKeys.WheelUp, settings.Mappings[0].TriggerMode);

            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-mouse.json");
            ConfigStore.Export(settings, path);
            AppSettings restored = ConfigStore.Import(path);
            Check("mouse trigger survives a config round-trip",
                restored.Mappings.Count == 2
                    && restored.Mappings[0].Key == MouseKeys.WheelUp
                    && restored.Mappings[1].Key == MouseKeys.Left
                    && restored.Mappings[1].TriggerText == "Caps + 鼠标左键",
                restored.Mappings.Count + " mappings");
            try { File.Delete(path); }
            catch { }

            // Caps layer routing: a bound mouse button fires and is swallowed, an
            // unbound one passes through, and nothing happens outside the layer.
            using (HotkeyManager manager = new HotkeyManager(settings, IntPtr.Zero))
            {
                int fired = 0;
                manager.ActionTriggered += delegate { fired++; };
                MethodInfo keyHook = typeof(HotkeyManager).GetMethod("OnHookKey",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo mouseHook = typeof(HotkeyManager).GetMethod("OnHookMouse",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                keyHook.Invoke(manager, new object[]
                    { new KeyboardEventArgs(Keys.CapsLock, true, false, false, 0) });
                bool down = (bool)mouseHook.Invoke(manager, new object[]
                    { new MouseButtonEventArgs((Keys)MouseKeys.Left, true, false, false, false, 0) });
                bool up = (bool)mouseHook.Invoke(manager, new object[]
                    { new MouseButtonEventArgs((Keys)MouseKeys.Left, false, true, false, false, 0) });
                bool unbound = (bool)mouseHook.Invoke(manager, new object[]
                    { new MouseButtonEventArgs((Keys)MouseKeys.Right, true, false, false, false, 0) });
                keyHook.Invoke(manager, new object[]
                    { new KeyboardEventArgs(Keys.CapsLock, false, true, false, 0) });
                bool outside = (bool)mouseHook.Invoke(manager, new object[]
                    { new MouseButtonEventArgs((Keys)MouseKeys.Left, true, false, false, false, 0) });
                Check("Caps + mouse button fires once and is swallowed",
                    fired == 1 && down && up, "fired=" + fired);
                Check("unbound mouse button passes through", !unbound, "");
                Check("mouse trigger outside the Caps layer passes through", !outside, "");
            }

            // The wheel accumulates small deltas into a whole 120 notch, and the
            // unbound direction keeps scrolling.
            AppSettings wheelSettings = new AppSettings();
            wheelSettings.Mappings = new List<KeyMapping>();
            wheelSettings.Mappings.Add(wheelUp.Clone());
            using (HotkeyManager manager = new HotkeyManager(wheelSettings, IntPtr.Zero))
            {
                int fired = 0;
                manager.ActionTriggered += delegate { fired++; };
                MethodInfo keyHook = typeof(HotkeyManager).GetMethod("OnHookKey",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo mouseHook = typeof(HotkeyManager).GetMethod("OnHookMouse",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                keyHook.Invoke(manager, new object[]
                    { new KeyboardEventArgs(Keys.CapsLock, true, false, false, 0) });
                bool half = (bool)mouseHook.Invoke(manager, new object[]
                    { new MouseButtonEventArgs((Keys)MouseKeys.WheelUp, true, false, true, false, 60) });
                int afterHalf = fired;
                bool whole = (bool)mouseHook.Invoke(manager, new object[]
                    { new MouseButtonEventArgs((Keys)MouseKeys.WheelUp, true, false, true, false, 60) });
                int afterWhole = fired;
                bool otherDirection = (bool)mouseHook.Invoke(manager, new object[]
                    { new MouseButtonEventArgs((Keys)MouseKeys.WheelDown, true, false, true, false, 120) });
                keyHook.Invoke(manager, new object[]
                    { new KeyboardEventArgs(Keys.CapsLock, false, true, false, 0) });
                Check("wheel fires once per 120 units",
                    half && whole && afterHalf == 0 && afterWhole == 1,
                    "afterHalf=" + afterHalf + " afterWhole=" + afterWhole);
                Check("unbound wheel direction passes through", !otherDirection, "");

                // The checks above ran after Caps went up; hold it again for the rest.
                keyHook.Invoke(manager, new object[]
                    { new KeyboardEventArgs(Keys.CapsLock, true, false, false, 0) });

                // A flicked wheel arrives as one message with several notches; every
                // notch is a real action, so none of them may be dropped.
                int beforeBurst = fired;
                mouseHook.Invoke(manager, new object[]
                    { new MouseButtonEventArgs((Keys)MouseKeys.WheelUp, true, false, true, false, 8 * 120) });
                Check("a coalesced burst fires every notch",
                    fired - beforeBurst == 8, (fired - beforeBurst) + " of 8");

                // Sensitivity: a 40-unit wheel fires per notch when told so.
                wheelSettings.WheelStep = 40;
                int beforeFine = fired;
                mouseHook.Invoke(manager, new object[]
                    { new MouseButtonEventArgs((Keys)MouseKeys.WheelUp, true, false, true, false, 40) });
                Check("the wheel step setting decides when it fires",
                    fired - beforeFine == 1, (fired - beforeFine) + " of 1");

                // And the default 120 needs three of those notches.
                wheelSettings.WheelStep = 120;
                int beforeDefault = fired;
                for (int i = 0; i < 2; i++)
                    mouseHook.Invoke(manager, new object[]
                        { new MouseButtonEventArgs((Keys)MouseKeys.WheelUp, true, false, true, false, 40) });
                int afterTwo = fired - beforeDefault;
                mouseHook.Invoke(manager, new object[]
                    { new MouseButtonEventArgs((Keys)MouseKeys.WheelUp, true, false, true, false, 40) });
                Check("one standard notch still needs 120 units",
                    afterTwo == 0 && fired - beforeDefault == 1,
                    "afterTwo=" + afterTwo + " total=" + (fired - beforeDefault));

                keyHook.Invoke(manager, new object[]
                    { new KeyboardEventArgs(Keys.CapsLock, false, true, false, 0) });
            }

            using (MKeyCapture capture = new MKeyCapture())
            {
                capture.AllowMouse = true;
                bool capturedRight = capture.CaptureMouseForTest(
                    new MouseButtonEventArgs((Keys)MouseKeys.Right, true, false, false, false, 0));
                Keys afterRight = capture.KeyCode;
                bool injected = capture.CaptureMouseForTest(
                    new MouseButtonEventArgs((Keys)MouseKeys.Middle, true, false, false, true, 0));
                bool wheelCaptured = capture.CaptureMouseForTest(
                    new MouseButtonEventArgs((Keys)MouseKeys.WheelDown, true, false, true, false, -120));
                Check("capture box records a mouse button",
                    capturedRight && afterRight == (Keys)MouseKeys.Right, afterRight.ToString());
                Check("capture box ignores injected and release events", !injected, "");
                Check("capture box records a wheel notch",
                    wheelCaptured && capture.KeyCode == (Keys)MouseKeys.WheelDown,
                    capture.KeyCode.ToString());

                // Recording a keyboard key must disarm the mouse recording, or the
                // next click on "保存" would be captured instead of saving.
                capture.CaptureKeyForTest(Keys.E);
                bool disarmed = !capture.MouseArmedForTest;
                bool keyboardKept = capture.KeyCode == Keys.E;
                Check("keyboard capture disarms the mouse recording",
                    disarmed && keyboardKept, capture.KeyCode.ToString());
            }

            using (MKeyCapture keyboardOnly = new MKeyCapture())
            {
                bool ignored = keyboardOnly.CaptureMouseForTest(
                    new MouseButtonEventArgs((Keys)MouseKeys.Left, true, false, false, false, 0));
                Check("capture box without mouse support stays keyboard-only",
                    !ignored && keyboardOnly.KeyCode == Keys.None, "");
            }
        }

        /// <summary>
        /// The five mouse keys are legal "发送按键" keys now: the label parses back,
        /// a combination with modifiers parses, the row shows the mouse key, and the
        /// editor's key box is allowed to record a button.
        /// </summary>
        private static void TestMouseKeySend()
        {
            bool roundTrip = true;
            string detail = "";
            for (int i = 0; i < MouseKeys.All.Length; i++)
            {
                string text = KeyText.FormatKey(MouseKeys.All[i]);
                if (KeyText.ParseKeyName(text) != MouseKeys.All[i])
                {
                    roundTrip = false;
                    detail = text;
                    break;
                }
            }
            Check("mouse key labels parse back as send keys", roundTrip, detail);

            bool aliases = KeyText.ParseKeyName("MouseLeft") == (Keys)MouseKeys.Left
                && KeyText.ParseKeyName("wheel down") == (Keys)MouseKeys.WheelDown
                && KeyText.ParseKeyName("鼠标右键") == (Keys)MouseKeys.Right;
            Check("mouse key aliases parse", aliases, "");

            bool ctrl, alt, shift, win;
            Keys key;
            bool combo = KeyText.TryParseCombo("Ctrl + 鼠标左键", out ctrl, out alt, out shift,
                out win, out key);
            Check("a send combo can be a mouse button",
                combo && ctrl && !alt && !shift && !win && key == (Keys)MouseKeys.Left,
                ctrl + " " + key);

            bool wheel = KeyText.TryParseCombo("Shift + 滚轮下滑", out ctrl, out alt, out shift,
                out win, out key);
            Check("a send combo can be a wheel notch",
                wheel && shift && key == (Keys)MouseKeys.WheelDown, shift + " " + key);

            Check("mouse send row shows the mouse key",
                ActionCatalog.Describe("input.keys", "鼠标中键") == "鼠标中键"
                    && ActionCatalog.NormalizeParam("input.keys", "鼠标中键") == "鼠标中键",
                ActionCatalog.Describe("input.keys", "鼠标中键"));

            using (MKeyComboField field = new MKeyComboField())
            {
                field.ComboText = "Ctrl + 鼠标中键";
                Check("combo field keeps a mouse key and records mouse keys",
                    field.KeyCode == (Keys)MouseKeys.Middle && field.RecordsMouse,
                    field.KeyCode + " / " + field.ComboText);
            }

            // The executor turns the pseudo code into a real mouse_event flag: the
            // button goes down/up and a notch carries its delta instead of a flag.
            bool flags = ActionExecutor.MouseButtonFlag(MouseKeys.All[0], false) == Native.MOUSEEVENTF_LEFTDOWN
                && ActionExecutor.MouseButtonFlag(MouseKeys.All[0], true) == Native.MOUSEEVENTF_LEFTUP
                && ActionExecutor.MouseButtonFlag(MouseKeys.All[1], false) == Native.MOUSEEVENTF_RIGHTDOWN
                && ActionExecutor.MouseButtonFlag(MouseKeys.All[1], true) == Native.MOUSEEVENTF_RIGHTUP
                && ActionExecutor.MouseButtonFlag(MouseKeys.All[2], false) == Native.MOUSEEVENTF_MIDDLEDOWN
                && ActionExecutor.MouseButtonFlag(MouseKeys.All[2], true) == Native.MOUSEEVENTF_MIDDLEUP
                && ActionExecutor.MouseButtonFlag(MouseKeys.All[3], false) == 0;
            Check("mouse send maps to mouse_event flags", flags, "");

            bool wheels = ActionExecutor.MouseWheelDelta(MouseKeys.All[3]) == 120
                && ActionExecutor.MouseWheelDelta(MouseKeys.All[4]) == -120
                && ActionExecutor.MouseWheelDelta(MouseKeys.All[0]) == 0;
            Check("mouse send maps a notch to its wheel delta", wheels, "");
        }

        /// <summary>
        /// The mouse card must hit-test the five zones and nothing else: the frame
        /// margin and the footer stay unclickable.
        /// </summary>
        private static void TestMousePad()
        {
            using (MMousePad pad = new MMousePad())
            {
                pad.SetBounds(0, 0, Theme.S(200), Theme.S(300));

                Keys[] zones =
                {
                    (Keys)MouseKeys.Left, (Keys)MouseKeys.Right, (Keys)MouseKeys.Middle,
                    (Keys)MouseKeys.WheelUp, (Keys)MouseKeys.WheelDown
                };
                bool hitAll = true;
                string detail = "";
                for (int i = 0; i < zones.Length; i++)
                {
                    RectangleF bounds = pad.ZoneBoundsForTest(zones[i]);
                    if (bounds.Width <= 0f || bounds.Height <= 0f)
                    {
                        hitAll = false;
                        detail = zones[i] + " has no bounds";
                        break;
                    }
                    Point center = new Point((int)(bounds.X + bounds.Width / 2f),
                        (int)(bounds.Y + bounds.Height / 2f));
                    Keys hit = pad.HitTestForTest(center);
                    if (hit != zones[i])
                    {
                        hitAll = false;
                        detail = zones[i] + " -> " + hit;
                        break;
                    }
                }
                Check("mouse card hit-tests all five zones", hitAll, detail);

                RectangleF left = pad.ZoneBoundsForTest((Keys)MouseKeys.Left);
                Keys margin = pad.HitTestForTest(new Point(
                    (int)left.X - Theme.S(1), (int)(left.Y + left.Height / 2f)));
                Check("mouse frame margin is not a hit zone", margin == Keys.None, margin.ToString());

                Keys footer = pad.HitTestForTest(new Point(
                    pad.Width / 2, pad.Height - Theme.S(4)));
                Check("mouse card footer is not a hit zone", footer == Keys.None, footer.ToString());

                // The five keys are keycaps: same gap on each side of a cell, same
                // 3D depth, so horizontal and vertical spacing match the board above.
                RectangleF leftZone = pad.ZoneBoundsForTest((Keys)MouseKeys.Left);
                RectangleF rightZone = pad.ZoneBoundsForTest((Keys)MouseKeys.Right);
                RectangleF wheel = pad.ZoneBoundsForTest((Keys)MouseKeys.Middle);
                float gap = pad.GapForTest;
                float horizontal = Math.Min(wheel.Left - leftZone.Right, rightZone.Left - wheel.Right);
                Check("mouse seams use the board's key gap",
                    Math.Abs(horizontal - gap * 2f) <= 1.5f,
                    horizontal.ToString("0.0") + " vs " + (gap * 2f).ToString("0.0"));

                RectangleF wheelUp = pad.ZoneBoundsForTest((Keys)MouseKeys.WheelUp);
                float vertical = wheel.Top - wheelUp.Bottom;
                float expected = gap * 2f + pad.DepthForTest((Keys)MouseKeys.WheelUp);
                Check("mouse rows use the board's gap plus 3D depth",
                    Math.Abs(vertical - expected) <= 1.5f,
                    vertical.ToString("0.0") + " vs " + expected.ToString("0.0"));

                // The frame keeps the same distance to a mouse key as two keyboard
                // keys keep to each other, so one spacing rule covers the whole card.
                RectangleF frame = pad.FrameBoundsForTest();
                float frameMargin = leftZone.Left - frame.Left;
                Check("mouse frame keeps the board's key gap",
                    Math.Abs(frameMargin - horizontal) <= 1.5f,
                    frameMargin.ToString("0.0") + " vs " + horizontal.ToString("0.0"));

                // Collapsing animates the card width; the drawing inside must keep its
                // size and its position relative to the leading edge (no rescaling).
                pad.ExpandedWidth = pad.Width;
                RectangleF openFrame = pad.FrameBoundsForTest();
                int openWidth = pad.Width;
                pad.SetBounds(0, 0, Math.Max(60, openWidth - 70), pad.Height);
                RectangleF narrowFrame = pad.FrameBoundsForTest();
                Check("collapsing the card does not rescale the mouse",
                    Math.Abs(narrowFrame.Width - openFrame.Width) <= 0.5f
                    && Math.Abs(narrowFrame.Height - openFrame.Height) <= 0.5f
                    && Math.Abs(narrowFrame.Left - openFrame.Left) <= 0.5f,
                    narrowFrame.Width.ToString("0.0") + "x" + narrowFrame.Height.ToString("0.0")
                    + " vs " + openFrame.Width.ToString("0.0") + "x"
                    + openFrame.Height.ToString("0.0"));
            }
        }

        private static void TestKeyboard()
        {
            List<KeyCap> caps = KeyboardLayout.Create();
            Check("keyboard layout is 104 keys", caps.Count == 104, "count=" + caps.Count);

            // The navigation cluster and the arrows must sit centred between the
            // main block and the numpad, not flush against either one.
            KeyCap prtSc = caps.Find(delegate(KeyCap c) { return c.Code == Keys.Snapshot; });
            KeyCap pause = caps.Find(delegate(KeyCap c) { return c.Code == Keys.Pause; });
            KeyCap numLock = caps.Find(delegate(KeyCap c) { return c.Code == Keys.NumLock; });
            float navLeftGap = prtSc == null ? -1f : prtSc.X - 15f;
            float navRightGap = (pause == null || numLock == null)
                ? -2f : numLock.X - (pause.X + pause.Width);
            Check("nav cluster is centred between the main block and the numpad",
                Math.Abs(navLeftGap - navRightGap) < 0.001f,
                navLeftGap.ToString("0.00") + " / " + navRightGap.ToString("0.00"));

            bool inside = true;
            bool noNone = true;
            bool labelled = true;
            for (int i = 0; i < caps.Count; i++)
            {
                KeyCap cap = caps[i];
                if (cap.X < 0f || cap.Y < 0f
                    || cap.X + cap.Width > KeyboardLayout.TotalWidth + 0.001f
                    || cap.Y + cap.Height > KeyboardLayout.TotalHeight + 0.001f)
                    inside = false;
                if (cap.Code == Keys.None) noNone = false;
                if (string.IsNullOrEmpty(cap.Label) && cap.Code != Keys.Space) labelled = false;
            }
            Check("keyboard keys stay inside the canvas", inside, "");
            Check("keyboard keys carry a real key code", noNone, "");
            Check("keyboard keys are labelled", labelled, "");

            bool overlapping = false;
            for (int i = 0; i < caps.Count && !overlapping; i++)
            {
                for (int j = i + 1; j < caps.Count; j++)
                {
                    if (Overlaps(caps[i], caps[j]))
                    {
                        overlapping = true;
                        break;
                    }
                }
            }
            Check("keyboard keys never overlap", !overlapping, "");

            Check("keyboard has the five modifier groups",
                CountModifier(caps, KeyModifier.Caps) == 1
                    && CountModifier(caps, KeyModifier.Ctrl) == 2
                    && CountModifier(caps, KeyModifier.Alt) == 2
                    && CountModifier(caps, KeyModifier.Shift) == 2
                    && CountModifier(caps, KeyModifier.Win) == 2,
                "");

            using (Font twoLineFont = KeyLabel.CreatePixelFont(11f))
            {
                int[] rows = MKeyboardView.TwoLineRowHeights(twoLineFont, twoLineFont);
                int needed = TextRenderer.MeasureText("Page", twoLineFont).Height;
                Check("two-line key labels leave room for descenders",
                    rows[0] >= needed && rows[1] >= needed,
                    rows[0] + "/" + rows[1] + " vs " + needed);
            }

            KeyMapping capsE = new KeyMapping();
            capsE.Caps = true;
            capsE.KeyCode = Keys.E;

            KeyMapping capsAltE = new KeyMapping();
            capsAltE.Caps = true;
            capsAltE.Alt = true;
            capsAltE.KeyCode = Keys.E;

            KeyMapping globalK = new KeyMapping();
            globalK.Caps = false;
            globalK.Ctrl = true;
            globalK.KeyCode = Keys.K;

            Check("preview filter: no selection marks nothing",
                !KeyboardFilter.Exact(capsE, false, false, false, false, false)
                    && !KeyboardFilter.Exact(capsAltE, false, false, false, false, false)
                    && !KeyboardFilter.Exact(globalK, false, false, false, false, false), "");
            Check("preview filter: Caps marks only plain Caps chords",
                KeyboardFilter.Exact(capsE, true, false, false, false, false)
                    && !KeyboardFilter.Exact(capsAltE, true, false, false, false, false), "");
            Check("preview filter: Caps does not mark Caps + Alt",
                !KeyboardFilter.Exact(capsAltE, true, false, false, false, false), "");
            Check("preview filter: Caps + Alt marks only Alt chords",
                !KeyboardFilter.Exact(capsE, true, false, true, false, false)
                    && KeyboardFilter.Exact(capsAltE, true, false, true, false, false), "");
            Check("preview filter: exact chord excludes global hotkeys",
                !KeyboardFilter.Exact(globalK, true, false, false, false, false)
                    && KeyboardFilter.Exact(globalK, false, true, false, false, false), "");
            Check("list filter: no selection keeps every row",
                KeyboardFilter.Matches(capsE, false, false, false, false, false)
                    && KeyboardFilter.Matches(globalK, false, false, false, false, false), "");

            KeyCap e = null;
            for (int i = 0; i < caps.Count; i++)
            {
                if (caps[i].Code == Keys.E) { e = caps[i]; break; }
            }
            Check("preview layout maps the E key", e != null && e.Covers(Keys.E), "");
        }

        private static bool Overlaps(KeyCap a, KeyCap b)
        {
            return a.X < b.X + b.Width - 0.001f
                && b.X < a.X + a.Width - 0.001f
                && a.Y < b.Y + b.Height - 0.001f
                && b.Y < a.Y + a.Height - 0.001f;
        }

        private static int CountModifier(List<KeyCap> caps, KeyModifier modifier)
        {
            int count = 0;
            for (int i = 0; i < caps.Count; i++)
                if (caps[i].Modifier == modifier) count++;
            return count;
        }

        private static void TestKeyboardLabels()
        {
            // Sweep the whole range rather than a few sample sizes: the label size
            // follows the key width, but GDI quantises fonts, so a boundary width
            // can still clip a name even when its neighbours are fine.
            List<string> failures = new List<string>();
            for (int width = 560; width <= 1600; width += 20)
            {
                List<string> bad = KeyboardLabelAudit.Truncated(width);
                for (int i = 0; i < bad.Count; i++)
                {
                    string entry = width.ToString() + ":" + bad[i];
                    if (!failures.Contains(entry)) failures.Add(entry);
                }
            }
            Check("key labels fit at every page width 560-1600",
                failures.Count == 0, string.Join(", ", failures.ToArray()));

            // Digit caps print the shift symbol beside the digit as one label
            // string. The old stack needed two line boxes on a 1u cap and its digit
            // row was clipped by the cap's lower edge at some key sizes.
            List<KeyCap> caps = KeyboardLayout.Create();
            float unit = Math.Max(4f, 892f - Theme.S(2) * 2) / KeyboardLayout.TotalWidth;
            float gap = Math.Max(1f, unit * 0.03f);
            Font label = null;
            Font small = null;
            Font tiny = null;
            KeyLabel.CreateFonts(unit, out label, out small, out tiny);
            try
            {
                List<string> wrong = new List<string>();
                int digitCaps = 0;
                for (int i = 0; i < caps.Count; i++)
                {
                    KeyCap cap = caps[i];
                    if (cap.Label.Length < 3 || cap.Label[1] != ' ') continue;
                    digitCaps++;

                    int width = (int)(cap.Width * unit - gap * 2f) + 2;
                    float faceHeight = cap.Height * unit - gap * 2f;
                    float depth = Math.Min(faceHeight * 0.10f, Math.Max(1.5f, unit * 0.06f));
                    KeyLabelPlan plan = KeyLabel.Plan(cap, width, faceHeight - depth,
                        label, small, tiny);
                    // One row in the largest face that fits: the pair must not drop
                    // to the stacked fallback on a 1u cap at the default width.
                    if (plan.Kind != KeyLabelKind.Full) wrong.Add(cap.Label + ":" + plan.Kind);
                }
                Check("digit caps keep symbol and digit on one row",
                    digitCaps == 13 && wrong.Count == 0,
                    "caps=" + digitCaps + " " + string.Join(", ", wrong.ToArray()));
            }
            finally
            {
                label.Dispose();
                small.Dispose();
                tiny.Dispose();
            }
        }

        private static void TestKeyboardActions()
        {
            // The default page is 892 px wide. A short name must reach every single
            // cap there, including the wide and the double-height numpad keys: the
            // action fit is per key, not per "key type that happens to be used".
            List<string> missing = KeyboardLabelAudit.ActionProblems(892, "上移", true);
            Check("every key can show a short action name at the default width",
                missing.Count == 0, string.Join(", ", missing.ToArray()));

            List<string> longNames = KeyboardLabelAudit.ActionProblems(
                892, "打开主界面并聚焦搜索框", false);
            Check("long action names stay inside every cap",
                longNames.Count == 0, string.Join(", ", longNames.ToArray()));

            // A font boundary must not let the name leave the cap at another width.
            List<string> failures = new List<string>();
            for (int width = 700; width <= 1400; width += 50)
            {
                List<string> bad = KeyboardLabelAudit.ActionProblems(width, "选中当前单词", false);
                for (int i = 0; i < bad.Count; i++)
                {
                    string entry = width.ToString() + ":" + bad[i];
                    if (!failures.Contains(entry)) failures.Add(entry);
                }
            }
            Check("action names fit at every page width 700-1400",
                failures.Count == 0, string.Join(", ", failures.ToArray()));

            using (Font font = KeyLabel.CreatePixelFont(7f))
            {
                string fitted = KeyActionText.Ellipsize("打开主界面并聚焦搜索框", font, 30);
                Check("ellipsis keeps the widest visible prefix",
                    fitted != null && fitted.EndsWith("…")
                        && Theme.Measure(fitted, font).Width <= 30, fitted);
            }

            // The audit above works in key units; this one runs the real control's
            // layout at real pixel sizes and checks the drawn rectangles.
            List<KeyMapping> mappings = ActionCatalog.CreateDefaultMappings();
            List<string> problems = new List<string>();
            int[] widths = { 700, 892, 1100, 1280 };
            using (MKeyboardView view = new MKeyboardView())
            {
                foreach (int width in widths)
                {
                    int height = (int)Math.Round(width
                        * (double)KeyboardLayout.TotalHeight / KeyboardLayout.TotalWidth);
                    view.Size = new Size(width, height);
                    view.SetMappings(mappings);
                    view.SetFilter(true, false, false, false, false);
                    List<string> found = view.LayoutProblems();
                    for (int i = 0; i < found.Count; i++)
                    {
                        string entry = width.ToString() + ":" + found[i];
                        if (!problems.Contains(entry)) problems.Add(entry);
                    }
                }
            }
            Check("board action names fit the real cap rectangles",
                problems.Count == 0, string.Join(", ", problems.ToArray()));

            using (MKeyboardView view = new MKeyboardView())
            {
                view.Size = new Size(892, 238);
                view.SetMappings(mappings);
                view.SetFilter(false, false, false, false, false);
                Check("neutral board marks used keys", view.MarkedKeyCount > 0,
                    view.MarkedKeyCount.ToString());
                Check("neutral board counts every chord on a key",
                    view.ActionCount(Keys.E) == CountKey(mappings, Keys.E),
                    view.ActionCount(Keys.E) + " vs " + CountKey(mappings, Keys.E));
                Check("neutral board shows no action names",
                    !view.HasActionOn(Keys.E), "");
                view.SetSelectedKey(Keys.E);
                Check("neutral key can be selected for the list",
                    view.SelectedKey == Keys.E, view.SelectedKey.ToString());
                view.SetFilter(true, false, false, false, false);
                Check("Caps marks a subset of the board", view.MarkedKeyCount > 0
                    && view.MarkedKeyCount < 104, view.MarkedKeyCount.ToString());
                Check("filtered board shows the action name", view.HasActionOn(Keys.E), "");
            }

            using (MKeyboardView view = new MKeyboardView())
            {
                view.Size = new Size(892, 238);
                KeyMapping enabled = new KeyMapping();
                enabled.Caps = true;
                enabled.KeyCode = Keys.Y;
                enabled.Action = "select.up";
                enabled.Param = "1";

                KeyMapping disabled = new KeyMapping();
                disabled.Caps = true;
                disabled.KeyCode = Keys.U;
                disabled.Action = "move.down";
                disabled.Param = "1";
                disabled.Enabled = false;

                view.SetMappings(new List<KeyMapping> { enabled, disabled });
                view.SetFilter(true, false, false, false, false);
                Check("disabled shortcut is counted as disabled",
                    view.DisabledKeyCount == 1, view.DisabledKeyCount.ToString());
                Check("disabled shortcut still names its action", view.HasActionOn(Keys.U), "");
                Check("enabled shortcut is not marked disabled",
                    view.HasActionOn(Keys.Y), "");

                view.SetFilter(false, false, false, false, false);
                Check("neutral count includes disabled shortcuts",
                    view.ActionCount(Keys.U) == 1, view.ActionCount(Keys.U).ToString());
                Check("neutral board hides disabled action names",
                    !view.HasActionOn(Keys.U), "");
            }

            // A double-click must never be swallowed: with no chord there is no
            // editor to open, so both clicks toggle (select, then deselect). With a
            // chord the second click keeps the key selected for the editor.
            using (MKeyboardView view = new MKeyboardView())
            {
                view.Size = new Size(892, 238);
                view.SetMappings(mappings);

                Keys neutralFirst = view.ClickKey(Keys.A, 1000);
                Keys neutralSecond = view.ClickKey(Keys.A, 1080);
                Check("neutral double-click selects then deselects",
                    neutralFirst == Keys.A && neutralSecond == Keys.None,
                    neutralFirst + " -> " + neutralSecond);

                view.SetFilter(true, false, false, false, false);
                Keys chordFirst = view.ClickKey(Keys.A, 20000);
                Keys chordSecond = view.ClickKey(Keys.A, 20080);
                Check("chord double-click keeps the key selected",
                    chordFirst == Keys.A && chordSecond == Keys.A,
                    chordFirst + " -> " + chordSecond);

                // Activation must not wait for the system window: only a quick pair
                // counts as a double-click, and a slower pair is two ordinary clicks.
                view.ClickKey(Keys.A, 40000);
                Check("quick second click counts as a double-click",
                    view.DoubleClickForTest(Keys.A, 40250), "");
                view.ClickKey(Keys.A, 50000);
                Check("slow second click does not count as a double-click",
                    !view.DoubleClickForTest(Keys.A, 50480), "");
                Keys slowFirst = view.ClickKey(Keys.A, 60000);
                Keys slowSecond = view.ClickKey(Keys.A, 60400);
                Check("chord clicks 400 ms apart toggle instead of activating",
                    slowFirst == Keys.A && slowSecond == Keys.None,
                    slowFirst + " -> " + slowSecond);

                // Activation must survive a stale selection: after a drag the first
                // click of the pair can toggle the cap off, and the double-click still
                // has to open the editor for that same cap.
                int activations = 0;
                view.KeyActivated += delegate { activations++; };
                view.SetFilter(false, true, false, false, false);
                Keys picked = view.ClickKey(Keys.B, 70000);
                Check("chord click selects the key", picked == Keys.B, "");
                Check("double-click opens the editor on the selected cap",
                    view.ActivateCapForTest(Keys.B) && view.SelectedKey == Keys.B && activations == 1,
                    "activations=" + activations);
                view.ClickKey(Keys.B, 80000);
                Check("double-click re-selects the cap after the first click cleared it",
                    view.ActivateCapForTest(Keys.B) && view.SelectedKey == Keys.B && activations == 2,
                    "activations=" + activations);
                view.SetFilter(false, false, false, false, false);
                Check("neutral overview never activates a cap",
                    !view.ActivateCapForTest(Keys.B) && activations == 2,
                    "activations=" + activations);

                // Pressing a used cap in a chord arms a drag and the click only lands
                // on release; that press must not erase the double-click pair, or the
                // editor never opens for a marked key.
                view.SetFilter(true, false, false, false, false);
                view.ClickKey(Keys.C, 90000);
                view.ArmDragForTest(Keys.C);
                Check("arming a drag keeps the double-click pair",
                    view.DoubleClickForTest(Keys.C, 90120), "");
                view.CancelDrag();
            }

            // Dragging a used key inside a chord reports the pair once, and a drop
            // on the source key itself is never a move.
            using (MKeyboardView view = new MKeyboardView())
            {
                view.Size = new Size(892, 238);
                Check("dragging a key reports the drop pair",
                    view.SimulateKeyDrag(Keys.A, Keys.B), "");
                Check("dropping on the source key is not a move",
                    !view.SimulateKeyDrag(Keys.A, Keys.A), "");

                // The drop used to leave the board in a state where the first click
                // of a pair toggled the selection off and the activation bailed out:
                // a quick second click after a drag must still open the editor.
                view.SetMappings(mappings);
                view.SetFilter(true, false, false, false, false);
                Keys afterDrag = view.ClickKey(Keys.B, 30000);
                Check("double-click still activates after a drag",
                    afterDrag == Keys.B && view.DoubleClickForTest(Keys.B, 30150), "");
            }
        }

        private static int CountKey(List<KeyMapping> mappings, Keys key)
        {
            int count = 0;
            foreach (KeyMapping mapping in mappings)
                if (mapping.KeyCode == key) count++;
            return count;
        }

        private sealed class StubRow : MListItem
        {
            public override void Paint(Graphics g, Rectangle bounds, bool selected,
                bool hovered, bool enabled)
            {
            }

            public override Rectangle ToggleBounds(Rectangle bounds)
            {
                return new Rectangle(bounds.Right - 40, bounds.Y + (bounds.Height - 20) / 2,
                    40, 20);
            }
        }

        private static void TestPopupMenu()
        {
            // The popup must be cut to its rounded card by the window region: a
            // rounded border alone leaves the surface visible in the corners.
            using (MPopupMenu menu = new MPopupMenu())
            {
                menu.AddItem("编辑…", "Enter", null);
                menu.AddSeparator();
                menu.AddItem("删除", "Del", null);
                menu.Size = new Size(200, 140);

                bool shaped = menu.Region != null
                    && !menu.Region.IsVisible(0f, 0f)
                    && !menu.Region.IsVisible(199f, 0f)
                    && !menu.Region.IsVisible(0f, 139f)
                    && menu.Region.IsVisible(100f, 70f);
                Check("popup menu is clipped to its rounded card", shaped,
                    menu.Region == null ? "no region" : "region set");
            }
        }

        private static void TestListInteraction()
        {
            using (MListView list = new MListView())
            {
                list.Size = new Size(400, 220);
                list.RowHeight = 30;
                list.SetItems(new List<MListItem> { new StubRow(), new StubRow() });

                int activated = 0;
                int contexts = 0;
                list.ItemActivated += delegate { activated++; };
                list.ItemContextRequested += delegate { contexts++; };

                Point row = new Point(50, 12);   // first row: RowTop is 4 px
                list.SimulateDoubleClick(MouseButtons.Left, row);
                bool leftEdits = activated == 1;
                list.SimulateDoubleClick(MouseButtons.Right, row);
                bool rightDoesNotEdit = activated == 1;
                Check("right double-click never opens the editor",
                    leftEdits && rightDoesNotEdit, "activated=" + activated);

                list.SimulateMouseDown(MouseButtons.Right, row);
                bool menuOnRight = contexts == 1 && list.SelectedIndex == 0;
                list.SimulateMouseDown(MouseButtons.Left, row);
                bool menuOnlyOnRight = contexts == 1;
                Check("right click on a row requests the row menu",
                    menuOnRight && menuOnlyOnRight, "contexts=" + contexts);

                Check("multi-select and reorder are opt-in",
                    !list.MultiSelect && !list.AllowReorder,
                    "multi=" + list.MultiSelect + " reorder=" + list.AllowReorder);
            }
        }

        /// <summary>
        /// Ctrl / Shift / Ctrl+A on the list, plus the rule that a plain press on
        /// an already selected row keeps the block until the mouse comes up.
        /// </summary>
        private static void TestListSelection()
        {
            using (MListView list = new MListView())
            {
                list.Size = new Size(400, 260);
                list.RowHeight = 30;
                list.MultiSelect = true;
                list.AllowReorder = true;
                List<MListItem> rows = new List<MListItem>();
                for (int i = 0; i < 5; i++) rows.Add(new StubRow());
                list.SetItems(rows);

                Point p0 = new Point(50, 4 + 15);
                Point p2 = new Point(50, 4 + 30 * 2 + 15);
                Point p4 = new Point(50, 4 + 30 * 4 + 15);
                Point p1 = new Point(50, 4 + 30 + 15);

                list.SimulateMouseDown(MouseButtons.Left, p0, Keys.Control);
                list.SimulateMouseUp(p0);
                bool one = list.SelectedCount == 1 && list.IsSelected(0);

                list.SimulateMouseDown(MouseButtons.Left, p2, Keys.Control);
                list.SimulateMouseUp(p2);
                bool two = list.SelectedCount == 2 && list.IsSelected(0) && list.IsSelected(2);

                list.SimulateMouseDown(MouseButtons.Left, p4, Keys.Shift);
                list.SimulateMouseUp(p4);
                bool range = list.SelectedCount == 3 && !list.IsSelected(0)
                    && list.IsSelected(2) && list.IsSelected(3) && list.IsSelected(4);

                list.SimulateKeyDown(Keys.A, true, false);
                bool all = list.SelectedCount == 5;

                list.SimulateMouseDown(MouseButtons.Left, p1, Keys.Control);
                list.SimulateMouseUp(p1);
                bool toggled = list.SelectedCount == 4 && !list.IsSelected(1);

                // Plain press on a row inside the block keeps the block until
                // release; release collapses to that row.
                list.SimulateKeyDown(Keys.A, true, false);
                list.SimulateMouseDown(MouseButtons.Left, p2);
                bool keptOnPress = list.SelectedCount == 5;
                list.SimulateMouseUp(p2);
                bool collapseOnRelease = list.SelectedCount == 1 && list.IsSelected(2);

                Check("ctrl/shift/ctrl+a multi-select on the list",
                    one && two && range && all && toggled && keptOnPress && collapseOnRelease,
                    "one=" + one + " two=" + two + " range=" + range + " all=" + all
                    + " toggle=" + toggled + " press=" + keptOnPress + " release=" + collapseOnRelease);

                // A switch belongs to its own row, even inside a block selection.
                list.SimulateKeyDown(Keys.A, true, false);
                int toggles = 0;
                int toggledIndex = -9;
                list.ItemToggleRequested += delegate
                {
                    toggles++;
                    toggledIndex = list.ToggleHotspotIndex;
                };
                list.SimulateMouseDown(MouseButtons.Left, new Point(380, 4 + 30 * 3 + 15));
                bool switchIndex = toggles == 1 && toggledIndex == 3 && list.SelectedCount == 5;

                // Space asks for the lead row, not the whole block.
                list.SimulateKeyDown(Keys.Space, false, false);
                bool spaceIndex = toggles == 2 && toggledIndex == list.SelectedIndex;
                Check("switch click and Space point at one row", switchIndex && spaceIndex,
                    "toggles=" + toggles + " index=" + toggledIndex
                    + " lead=" + list.SelectedIndex + " selected=" + list.SelectedCount);
            }
        }

        /// <summary>
        /// A lifted card is clamped to the surface that paints it: it stops at the
        /// edge instead of sliding half out, and a card handed over from the other
        /// surface is pulled back inside the one showing it.
        /// </summary>
        private static void TestGhostPlacement()
        {
            SizeF card = new SizeF(100f, 40f);
            Size pane = new Size(300, 200);

            PointF corner = GhostPlacement.Clamp(new PointF(-40f, 400f), card, pane);
            Check("ghost card stops at the surface edge",
                Math.Abs(corner.X - 2f) < 0.01f && Math.Abs(corner.Y - 158f) < 0.01f,
                corner.ToString());

            PointF inside = GhostPlacement.Clamp(new PointF(60f, 50f), card, pane);
            Check("ghost card keeps its place inside the surface",
                inside.X == 60f && inside.Y == 50f, inside.ToString());

            PointF big = GhostPlacement.Clamp(new PointF(40f, 40f), new SizeF(400f, 300f), pane);
            Check("ghost card bigger than the surface pins to the margin",
                Math.Abs(big.X - 2f) < 0.01f && Math.Abs(big.Y - 2f) < 0.01f, big.ToString());

            Point shift = GhostPlacement.ClampOffset(new RectangleF(-30f, -10f, 100f, 40f), pane);
            Check("handed card is pulled back inside the surface",
                shift.X == 32 && shift.Y == 12, shift.ToString());
        }

        /// <summary>
        /// A lifted card hides the key name but keeps the action line where the
        /// source paints it: the action row starts after the (invisible) name rows,
        /// both centred as one block, and it is the same rule the source uses.
        /// </summary>
        private static void TestActionRowPlacement()
        {
            KeyActionLayout layout = new KeyActionLayout();
            layout.RowText = new[] { "左键" };
            layout.RowFont = new[] { Theme.Body };
            layout.RowHeight = new[] { 20 };
            layout.ActionText = "复制";
            layout.ActionFont = Theme.Caption;
            layout.ActionHeight = 14;

            Rectangle face = new Rectangle(100, 100, 80, 200);
            Rectangle row = KeyVisual.ActionRowRect(face, layout);
            // 34 px of rows centred in 200 → top at 183, action below the name row.
            Check("action row keeps the hidden name row's space",
                row.Y == 203 && row.Height == 14 && row.X == 101 && row.Width == 78,
                row.ToString());
        }

        /// <summary>
        /// The trigger chip of a list row grows with its text. A chord that uses all
        /// five modifiers is wider than the old fixed 220 px column, which simply cut
        /// it in half; the chip now takes the room it needs and stops at the row edge.
        /// </summary>
        private static void TestTriggerChip()
        {
            string five = "Caps + Ctrl + Alt + Shift + Win + Num 5";
            int fiveNeeded = MappingListItem.ChipNeededWidth(five);
            Check("five-modifier trigger fits its chip",
                MappingListItem.TriggerChipWidth(five, Theme.S(700)) >= fiveNeeded,
                MappingListItem.TriggerChipWidth(five, Theme.S(700)) + " vs " + fiveNeeded);

            string longTrigger = five + " + " + new string('W', 40);
            int needed = MappingListItem.ChipNeededWidth(longTrigger);
            int roomy = MappingListItem.TriggerChipWidth(longTrigger, needed + Theme.S(100));
            Check("trigger chip grows with its text", roomy >= needed, roomy + " vs " + needed);

            int tight = MappingListItem.TriggerChipWidth(longTrigger, Theme.S(120));
            Check("trigger chip stops at the row edge", tight <= Math.Max(Theme.S(46), Theme.S(120)),
                tight.ToString());
        }

        /// <summary>
        /// The block reorder used by the list: a contiguous block of rows keeps
        /// its internal order, and rows around it shift by the block size.
        /// </summary>
        private static void TestReorderMath()        {            List<string> items = new List<string> { "a", "b", "c", "d", "e" };
            MListReorder.Move(items, 1, 2, 4);
            bool down = string.Join("", items.ToArray()) == "adbce";

            List<string> back = new List<string> { "a", "d", "b", "c", "e" };
            MListReorder.Move(back, 2, 2, 0);
            bool up = string.Join("", back.ToArray()) == "bcade";

            bool slots = MListReorder.TargetStart(1, 2, 4) == 2
                && MListReorder.TargetStart(2, 2, 0) == 0
                && MListReorder.MoveIndex(1, 1, 2, 4) == 2
                && MListReorder.MoveIndex(3, 1, 2, 4) == 1;
            Check("reorder moves a block without scrambling it", down && up && slots,
                "down=" + down + " up=" + up + " slots=" + slots);

            using (MListView list = new MListView())
            {
                list.Size = new Size(400, 260);
                list.RowHeight = 30;
                list.MultiSelect = true;
                list.AllowReorder = true;
                List<MListItem> rows = new List<MListItem>();
                for (int i = 0; i < 5; i++) rows.Add(new StubRow());
                list.SetItems(rows);

                int from = 0, count = 0, to = -1;
                list.ItemsReordered += delegate(object sender, MListReorderEventArgs e)
                {
                    from = e.From;
                    count = e.Count;
                    to = e.To;
                };

                MListItem dragged = rows[0];
                list.SimulateMouseDown(MouseButtons.Left, new Point(50, 4 + 15));
                list.SimulateMouseMove(new Point(50, 80));
                list.SimulateMouseMove(new Point(50, 160));
                list.SimulateMouseUp(new Point(50, 160));

                bool fired = from == 0 && count == 1 && to == 4;
                bool order = list.Items.Count == 5 && list.Items[4] == dragged
                    && list.IsSelected(4);
                Check("dragging a row reorders it and reports the drop",
                    fired && order, "from=" + from + " count=" + count + " to=" + to);

                // A multi-row block travels as one piece.
                List<MListItem> more = new List<MListItem>();
                for (int i = 0; i < 5; i++) more.Add(new StubRow());
                list.SetItems(more);
                list.SimulateMouseDown(MouseButtons.Left, new Point(50, 4 + 15), Keys.Control);
                list.SimulateMouseUp(new Point(50, 4 + 15));
                list.SimulateMouseDown(MouseButtons.Left, new Point(50, 4 + 30 + 15), Keys.Control);
                list.SimulateMouseUp(new Point(50, 4 + 30 + 15));

                from = 0;
                count = 0;
                to = -1;
                list.SimulateMouseDown(MouseButtons.Left, new Point(50, 4 + 15));
                list.SimulateMouseMove(new Point(50, 90));
                list.SimulateMouseMove(new Point(50, 170));
                list.SimulateMouseUp(new Point(50, 170));

                bool blockFired = from == 0 && count == 2 && to == 3;
                bool blockOrder = list.Items.Count == 5
                    && list.Items[3] == more[0] && list.Items[4] == more[1]
                    && list.SelectedCount == 2 && list.IsSelected(3) && list.IsSelected(4);
                Check("dragging several rows keeps their order",
                    blockFired && blockOrder,
                    "from=" + from + " count=" + count + " to=" + to);

                // The gesture lives only while the left button is held: after the
                // release the block must stop following the pointer.
                List<MListItem> release = new List<MListItem>();
                for (int i = 0; i < 5; i++) release.Add(new StubRow());
                list.SetItems(release);
                from = -1;
                count = 0;
                to = -1;
                list.SimulateMouseDown(MouseButtons.Left, new Point(50, 4 + 15));
                list.SimulateMouseMove(new Point(50, 90));
                bool midDrag = list.Dragging;
                list.SimulateMouseUp(new Point(50, 90));
                bool ended = !list.Dragging && from == 0 && to == 1;
                list.SimulateMouseMove(new Point(50, 190));
                bool noGhost = !list.Dragging && to == 1;
                Check("releasing the left button ends the drag",
                    midDrag && ended && noGhost,
                    "mid=" + midDrag + " ended=" + ended + " to=" + to + " ghost=" + !noGhost);

                // A right-button gesture opens the menu; it never arms a drag.
                list.SetItems(release);
                from = -1;
                to = -1;
                list.SimulateMouseDown(MouseButtons.Right, new Point(50, 4 + 15));
                list.SimulateMouseMove(new Point(50, 150));
                list.SimulateMouseUp(new Point(50, 150));
                Check("right button never starts a drag", !list.Dragging && to == -1, "to=" + to);
            }
        }

        /// <summary>
        /// Macro model: loading must repair broken files, nesting validation must
        /// stop cycles before a run starts, and registered macros must resolve as
        /// normal actions (binding depends on that).
        /// </summary>
        private static void TestMousePoint()
        {
            MouseClickPoint point = MouseClickPoint.Parse("R;120;-40");
            Check("mouse point parses button and coordinates",
                point.Button == MouseClickButton.Right && point.X == 120 && point.Y == -40,
                MouseClickPoint.Format(point));
            Check("mouse point ignores the retired restore flag",
                MouseClickPoint.Format(MouseClickPoint.Parse("R;120;-40;1")) == "R;120;-40",
                MouseClickPoint.Format(MouseClickPoint.Parse("R;120;-40;1")));
            MouseClickPoint legacy = MouseClickPoint.Parse("12,34");
            Check("mouse point accepts legacy comma form",
                legacy.Button == MouseClickButton.Left && legacy.X == 12 && legacy.Y == 34,
                MouseClickPoint.Format(legacy));
            Check("mouse point clamps coordinates",
                MouseClickPoint.Parse("L;99999;-99999").X == 32767 &&
                MouseClickPoint.Parse("L;99999;-99999").Y == -32768,
                MouseClickPoint.Format(MouseClickPoint.Parse("L;99999;-99999")));

            MouseClickPoint withMods = MouseClickPoint.Parse("R;120;-40;CS");
            Check("mouse point keeps held modifiers",
                withMods.Button == MouseClickButton.Right && withMods.Ctrl && withMods.Shift
                    && !withMods.Alt && !withMods.Win
                    && MouseClickPoint.Format(withMods) == "R;120;-40;CS",
                MouseClickPoint.Format(withMods));
            Check("mouse point drops an empty modifier segment",
                MouseClickPoint.Format(MouseClickPoint.Parse("L;5;6;")) == "L;5;6",
                MouseClickPoint.Format(MouseClickPoint.Parse("L;5;6;")));
            MouseClickPoint plain = MouseClickPoint.Parse("L;5;6");
            Check("mouse point without modifiers stays plain",
                !plain.HasModifiers && plain.ModifierText == "",
                MouseClickPoint.Format(plain));
            withMods.Ctrl = false;
            withMods.Shift = false;
            withMods.Alt = true;
            withMods.Win = true;
            string described = ActionCatalog.Describe("mouse.click", MouseClickPoint.Format(withMods));
            Check("mouse point describes the held modifiers",
                described.IndexOf("Alt + Win + 右键", StringComparison.Ordinal) >= 0,
                described);
        }

        private static void TestMacros()
        {
            List<Macro> broken = new List<Macro>();
            broken.Add(new Macro());
            broken.Add(new Macro());
            broken[0].Id = "same";
            broken[1].Id = "same";
            broken[0].Name = "  ";
            broken[0].Steps.Add(new MacroStep());
            broken[0].Steps[0].Action = null;
            broken[0].Steps[0].Param = null;
            broken[0].Steps[0].DelayMs = 99000;
            broken[0].Steps[0].Count = 0;
            broken.Add(null);

            List<Macro> fixedList = ConfigStore.NormalizeMacroList(broken);
            bool idsUnique = true;
            HashSet<string> seen = new HashSet<string>();
            foreach (Macro m in fixedList) if (!seen.Add(m.Id)) idsUnique = false;
            Check("macro normalization repairs a broken file",
                fixedList.Count == 2 && idsUnique
                && fixedList[0].Name.Length > 0
                && fixedList[0].Steps[0].Action == ActionCatalog.None
                && fixedList[0].Steps[0].Param == ""
                && fixedList[0].Steps[0].Note == ""
                && fixedList[0].Steps[0].DelayMs <= 60000
                && fixedList[0].Steps[0].Count >= 1,
                "count=" + fixedList.Count + " unique=" + idsUnique);

            MacroStep noted = new MacroStep();
            noted.Action = "input.keys";
            noted.Param = "Ctrl + C";
            noted.Note = "复制段落";
            MacroStep notedClone = noted.Clone();
            Check("macro step keeps its name when cloned",
                notedClone.Note == "复制段落" && notedClone.Param == "Ctrl + C",
                notedClone.Note);

            List<Macro> named = new List<Macro>();
            Macro holder = new Macro();
            holder.Steps.Add(new MacroStep { Action = "select.up", Param = "1", Note = "  选中  " });
            holder.Steps.Add(new MacroStep { Action = "select.down", Param = "1", Note = new string('长', 70) });
            named.Add(holder);
            List<Macro> trimmed = ConfigStore.NormalizeMacroList(named);
            Check("step names are trimmed and capped",
                trimmed[0].Steps[0].Note == "选中" && trimmed[0].Steps[1].Note.Length == 60,
                trimmed[0].Steps[1].Note.Length + " chars");

            // The list's Key is what paints in the 动作 column and feeds the accessibility
            // name, so a named step must show the name rather than the action text.
            Check("a named step lists under its name",
                new MacroStepListItem(notedClone, false).Key == "复制段落"
                && new MacroStepListItem(new MacroStep { Action = "select.up", Param = "1" }, false).Key
                    == ActionCatalog.Describe("select.up", "1"),
                new MacroStepListItem(notedClone, false).Key);

            Macro inner = new Macro();
            inner.Name = "inner";
            inner.Steps.Add(new MacroStep { Action = "select.up", Param = "1" });

            Macro outer = new Macro();
            outer.Name = "outer";
            outer.RestoreCursor = true;
            outer.Steps.Add(new MacroStep { Action = inner.ActionId, Param = "" });

            Macro loop = new Macro();
            loop.Name = "loop";
            loop.Steps.Add(new MacroStep { Action = loop.ActionId, Param = "" });

            List<Macro> set = new List<Macro>();
            set.Add(inner);
            set.Add(outer);
            set.Add(loop);
            string error = MacroLibrary.ValidateNesting(set, outer);
            Check("macro nesting accepts a DAG", error == null, error);

            error = MacroLibrary.ValidateNesting(set, loop);
            Check("macro nesting rejects a cycle", !string.IsNullOrEmpty(error), error);

            List<KeyMapping> bound = new List<KeyMapping>();
            KeyMapping mapping = new KeyMapping();
            mapping.Action = outer.ActionId;
            bound.Add(mapping);
            Check("macro binding is counted", MacroLibrary.BoundCount(bound, outer) == 1, "");
            Check("restore-cursor flag survives cloning",
                outer.Clone().RestoreCursor == outer.RestoreCursor && outer.RestoreCursor,
                "");

            MacroLibrary.RefreshCatalog(set);
            Check("registered macro resolves as an action",
                ActionCatalog.Get(outer.ActionId) != null
                && ActionCatalog.Get(outer.ActionId).IsMacro
                && ActionCatalog.Get(outer.ActionId).Name == "outer",
                "");
            Check("missing macro has a placeholder",
                ActionCatalog.Get("macro.nope") != null
                && ActionCatalog.Get("macro.nope").Missing,
                "");

            MacroLibrary.RefreshCatalog(new List<Macro>());

            string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "clmacro-test-" + Guid.NewGuid().ToString("N") + ".clmacro");
            try
            {
                MacroFileStore.Save(file, set);
                List<Macro> loaded = MacroFileStore.Load(file);
                Check("macro file round-trips",
                    loaded != null && loaded.Count == 3
                    && loaded[1].Name == "outer"
                    && loaded[1].Steps.Count == 1
                    && loaded[1].Steps[0].Action == inner.ActionId
                    && loaded[0].Steps[0].DelayMs == 50
                    && loaded[1].RestoreCursor,
                    loaded == null ? "null" : "count=" + loaded.Count);
            }
            finally
            {
                try { System.IO.File.Delete(file); }
                catch { }
            }

            // Dragging rows only reorders the list; the editor must write that order
            // back into the macro before saving, and must never drop a step.
            MacroStep first = new MacroStep(); first.Action = "select.up";
            MacroStep second = new MacroStep(); second.Action = "move.down";
            MacroStep third = new MacroStep(); third.Action = "move.left";
            List<MacroStep> steps = new List<MacroStep>();
            steps.Add(first); steps.Add(second); steps.Add(third);
            List<MacroStep> shown = new List<MacroStep>();
            shown.Add(third); shown.Add(first);
            List<MacroStep> applied = MacroLibrary.ApplyStepOrder(steps, shown);
            Check("dragging steps writes the shown order back",
                applied.Count == 3 && applied[0] == third && applied[1] == first && applied[2] == second,
                "count=" + applied.Count);
            List<MacroStep> onlyOne = new List<MacroStep>();
            onlyOne.Add(second);
            List<MacroStep> kept = MacroLibrary.ApplyStepOrder(steps, onlyOne);
            Check("step order never drops unseen rows",
                kept.Count == 3 && kept[0] == second && kept.Contains(first) && kept.Contains(third),
                "count=" + kept.Count);

            // One macro at a time: a second trigger that arrives while a run is in
            // flight must be refused (and must not stop the first one), then work
            // again once the run has ended.
            TestHost runHost = new TestHost();
            using (ActionExecutor runExecutor = new ActionExecutor(runHost))
            {
                List<Macro> runnable = new List<Macro>();
                Macro firstRun = new Macro();
                firstRun.Name = "first";
                firstRun.Steps.Add(new MacroStep { Action = ActionCatalog.None, Param = "", DelayMs = 60000 });
                Macro secondRun = new Macro();
                secondRun.Name = "second";
                secondRun.Steps.Add(new MacroStep { Action = ActionCatalog.None, Param = "", DelayMs = 60000 });
                runnable.Add(firstRun);
                runnable.Add(secondRun);
                MacroLibrary.RefreshCatalog(runnable);
                using (MacroRunner runner = new MacroRunner(runExecutor, runHost, delegate { return runnable; }))
                {
                    string firstError;
                    bool firstOk = runner.Start(firstRun.ActionId, out firstError);
                    string busyError;
                    bool secondOk = runner.Start(secondRun.ActionId, out busyError);
                    bool keptRunning = runner.IsRunning && runner.RunningName == "first";
                    runner.Stop();
                    string laterError;
                    bool laterOk = runner.Start(secondRun.ActionId, out laterError);
                    Check("one macro runs at a time",
                        firstOk && !secondOk && keptRunning
                        && busyError != null && busyError.Contains("正在运行")
                        && laterOk,
                        "first=" + firstOk + " second=" + secondOk + " busy=" + busyError + " later=" + laterOk);
                }
                MacroLibrary.RefreshCatalog(new List<Macro>());
            }
        }

        /// <summary>
        /// Themed inputs: the wrapping used by MDialog and the numeric clamp used
        /// by MNumberBox must work without a message loop.
        /// </summary>
        private static void TestInputs()
        {
            // A single-line label must never be handed a height below the rendered
            // line: the bottom of g/p/q/y gets clipped and the text looks broken.
            // Theme.LineHeight is the only safe source for that height.
            bool captionFits = Theme.LineHeight(Theme.Caption)
                >= TextRenderer.MeasureText("国Agq", Theme.Caption).Height;
            bool bodyFits = Theme.LineHeight(Theme.Body)
                >= TextRenderer.MeasureText("国Agq", Theme.Body).Height;
            Label squeezed = Theme.MakeLabel("CapsLockLite.exe", Theme.Caption, Theme.TextTertiary,
                0, 0, Theme.S(200), Theme.S(10));
            bool labelFits = squeezed.Height >= TextRenderer.MeasureText("国Agq", Theme.Caption).Height;
            squeezed.Dispose();
            Check("single-line label heights cover descenders", captionFits && bodyFits && labelFits,
                "caption=" + captionFits + " body=" + bodyFits + " label=" + labelFits);

            string text = "CapsLock Lite 的自绘输入控件需要把长段落折行，"
                + "并且折行后每一行都不能超过给定宽度，否则对话框正文会被裁掉。";
            List<string> lines = MTextLayout.Wrap(text, Theme.Body, 220, true);
            bool wrapped = lines.Count > 1;
            bool fits = wrapped;
            for (int i = 0; i < lines.Count && fits; i++)
            {
                if (lines[i].Length == 0) fits = false;
                else if (TextRenderer.MeasureText(lines[i], Theme.Body,
                    Size.Empty, TextFormatFlags.NoPadding).Width > 222) fits = false;
            }
            Check("dialog text wraps into several fitting lines", wrapped && fits,
                "lines=" + lines.Count + " fits=" + fits);

            using (MNumberBox box = new MNumberBox())
            {
                box.Minimum = 10;
                box.Maximum = 20;
                box.Value = 99;
                bool high = box.Value == 20;
                box.Value = 1;
                bool low = box.Value == 10;
                box.Value = 15;
                Check("number box clamps to its range", high && low && box.Value == 15,
                    "high=" + high + " low=" + low + " value=" + box.Value);
            }

            // A blank combo means "nothing chosen yet": the field must not fall
            // back to Ctrl + C, and a new send-keys action must start blank.
            using (MKeyComboField combo = new MKeyComboField())
            {
                bool blank = combo.ComboText == "";
                combo.ComboText = "Ctrl + C";
                bool parsed = combo.ComboText == "Ctrl + C";
                combo.ComboText = "";
                bool cleared = combo.ComboText == "";
                combo.ComboText = "not a combo";
                bool rejected = combo.ComboText == "";
                Check("key combo field starts and resets blank", blank && parsed && cleared && rejected,
                    "blank=" + blank + " parsed=" + parsed + " cleared=" + cleared + " rejected=" + rejected);
                Check("send keys action defaults to an empty combo",
                    ActionCatalog.Get("input.keys").DefaultParam == "",
                    ActionCatalog.Get("input.keys").DefaultParam);
            }

            // Escape and the arrows are window navigation keys by default; the
            // capture box has to swallow them so they can be bound like any key.
            using (MKeyCapture capture = new MKeyCapture())
            {
                bool escape = capture.ProcessNavigationForTest(Keys.Escape)
                    && capture.KeyCode == Keys.Escape
                    && capture.IsInputKeyForTest(Keys.Escape);
                Check("key capture takes Escape instead of closing the dialog", escape,
                    capture.KeyCode.ToString());
                bool arrows = true;
                Keys[] arrowsToCheck = new Keys[] { Keys.Up, Keys.Down, Keys.Left, Keys.Right };
                foreach (Keys key in arrowsToCheck)
                {
                    capture.KeyCode = Keys.None;
                    arrows = arrows && capture.IsInputKeyForTest(key)
                        && capture.ProcessNavigationForTest(key)
                        && capture.KeyCode == key;
                }
                Check("key capture takes the arrow keys instead of moving focus", arrows,
                    capture.KeyCode.ToString());
            }
        }

        private static void TestMatching()
        {
            AppSettings settings = ConfigStore.CreateDefault();
            settings.TapAction = "none";
            HotkeyManager manager = new HotkeyManager(settings, IntPtr.Zero);
            MethodInfo handler = typeof(HotkeyManager).GetMethod("OnHookKey",
                BindingFlags.Instance | BindingFlags.NonPublic);
            List<string> fired = new List<string>();
            manager.ActionTriggered += delegate(KeyMapping m) { fired.Add(m.Action); };
            List<KeyMapping> held = new List<KeyMapping>();
            manager.HoldStarted += delegate(KeyMapping m) { held.Add(m); };

            bool capsDown = (bool)handler.Invoke(manager, new object[] { Key(Keys.CapsLock, true) });
            bool keyDown = (bool)handler.Invoke(manager, new object[] { Key(Keys.E, true) });
            handler.Invoke(manager, new object[] { Key(Keys.E, false) });
            bool capsUp = (bool)handler.Invoke(manager, new object[] { Key(Keys.CapsLock, false) });
            Check("caps layer swallows caps", capsDown && capsUp, "");
            // Defaults mark the arrow sends as hold, so the Caps+E binding starts a
            // physical hold instead of one ActionTriggered call.
            Check("caps layer triggers E",
                keyDown && ((fired.Count == 1 && fired[0] == "input.keys")
                    || (held.Count == 1 && held[0].Action == "input.keys")),
                string.Join(",", fired.ToArray()) + "/" + held.Count);

            fired.Clear();
            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, true) });
            handler.Invoke(manager, new object[] { Key(Keys.LMenu, true) });
            bool altE = (bool)handler.Invoke(manager, new object[] { Key(Keys.E, true) });
            handler.Invoke(manager, new object[] { Key(Keys.E, false) });
            handler.Invoke(manager, new object[] { Key(Keys.LMenu, false) });
            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, false) });
            Check("caps+alt does not fire the plain E binding", fired.Count == 0, altE + ":" + string.Join(",", fired.ToArray()));

            fired.Clear();
            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, true) });
            bool unmapped = (bool)handler.Invoke(manager, new object[] { Key(Keys.Q, true) });
            handler.Invoke(manager, new object[] { Key(Keys.Q, false) });
            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, false) });
            Check("unmapped key stays silent in the Caps layer", unmapped && fired.Count == 0, "");

            // A modifier held before CapsLock already reached the target window.
            // While the Caps layer owns the keyboard it must stop acting there,
            // otherwise a Caps+Alt+Q binding that sends F4 arrives as Alt+F4.
            List<string> sent = new List<string>();
            manager.SendKeyState = delegate(Keys k, bool up) { sent.Add(k + (up ? " up" : " down")); };

            fired.Clear();
            handler.Invoke(manager, new object[] { Key(Keys.LMenu, true) });
            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, true) });
            Check("modifier held before caps is released to the target",
                sent.Count == 1 && sent[0] == "LMenu up", string.Join(",", sent.ToArray()));

            bool altUp = (bool)handler.Invoke(manager, new object[] { Key(Keys.LMenu, false) });
            Check("the physical up of a released modifier stays silent",
                altUp && sent.Count == 1, altUp + ":" + string.Join(",", sent.ToArray()));

            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, false) });
            Check("a modifier already released is not restored",
                sent.Count == 1, string.Join(",", sent.ToArray()));

            sent.Clear();
            fired.Clear();
            handler.Invoke(manager, new object[] { Key(Keys.LMenu, true) });
            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, true) });
            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, false) });
            handler.Invoke(manager, new object[] { Key(Keys.LMenu, false) });
            Check("a modifier still held is restored after caps",
                sent.Count == 2 && sent[0] == "LMenu up" && sent[1] == "LMenu down",
                string.Join(",", sent.ToArray()));

            // Multi-key rollover: every key stays physically down (a real keyboard
            // never releases a key because another one was pressed), and only the
            // auto-repeat moves to the newest key. Games rely on this to keep
            // "forward" held while the player taps "left" and "right".
            List<string> events = new List<string>();
            manager.HoldStarted += delegate(KeyMapping m) { events.Add("start:" + (Keys)m.Key); };
            manager.HoldEnded += delegate(KeyMapping m) { events.Add("end:" + (Keys)m.Key); };
            manager.ActionTriggered += delegate(KeyMapping m) { events.Add("action:" + (Keys)m.Key); };

            events.Clear();
            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, true) });
            handler.Invoke(manager, new object[] { Key(Keys.E, true) });
            handler.Invoke(manager, new object[] { Key(Keys.D, true) });
            Check("the previous key keeps its hold when the newest takes over",
                string.Join(",", events.ToArray()) == "start:E,start:D",
                string.Join(",", events.ToArray()));

            events.Clear();
            handler.Invoke(manager, new object[] { Key(Keys.D, false) });
            Check("releasing the newest key only releases it",
                string.Join(",", events.ToArray()) == "end:D",
                string.Join(",", events.ToArray()));

            events.Clear();
            handler.Invoke(manager, new object[] { Key(Keys.E, false) });
            Check("releasing an older key releases it too",
                string.Join(",", events.ToArray()) == "end:E",
                string.Join(",", events.ToArray()));
            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, false) });

            // Letting Caps go with a chord still held must release everything,
            // otherwise the key stays stuck in the target application.
            events.Clear();
            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, true) });
            handler.Invoke(manager, new object[] { Key(Keys.E, true) });
            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, false) });
            Check("releasing caps releases the held chord key",
                string.Join(",", events.ToArray()) == "start:E,end:E",
                string.Join(",", events.ToArray()));
            handler.Invoke(manager, new object[] { Key(Keys.E, false) });

            // A one-shot key takes the repeat but leaves the held key down: the
            // game still sees the movement key pressed.
            KeyMapping oneShot = new KeyMapping();
            oneShot.Caps = true;
            oneShot.Key = (int)Keys.B;
            oneShot.Action = ActionCatalog.None;
            oneShot.TriggerMode = "once";
            settings.Mappings.Add(oneShot);

            events.Clear();
            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, true) });
            handler.Invoke(manager, new object[] { Key(Keys.E, true) });
            handler.Invoke(manager, new object[] { Key(Keys.B, true) });
            Check("a one-shot key keeps the previous hold down",
                string.Join(",", events.ToArray()) == "start:E,action:B",
                string.Join(",", events.ToArray()));
            handler.Invoke(manager, new object[] { Key(Keys.B, false) });
            handler.Invoke(manager, new object[] { Key(Keys.E, false) });
            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, false) });

            events.Clear();
            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, true) });
            handler.Invoke(manager, new object[] { Key(Keys.E, true) });
            handler.Invoke(manager, new object[] { Key(Keys.Q, true) });
            Check("an unmapped key keeps the held chord down",
                string.Join(",", events.ToArray()) == "start:E",
                string.Join(",", events.ToArray()));
            handler.Invoke(manager, new object[] { Key(Keys.Q, false) });
            handler.Invoke(manager, new object[] { Key(Keys.E, false) });
            handler.Invoke(manager, new object[] { Key(Keys.CapsLock, false) });

            manager.Dispose();
        }

        private static KeyboardEventArgs Key(Keys key, bool down)
        {
            return new KeyboardEventArgs(key, down, !down, false, 0);
        }

        private static void TestConflicts()
        {
            KeyMapping first = new KeyMapping();
            first.Id = "conflict-a";
            first.Caps = true;
            first.KeyCode = Keys.E;

            KeyMapping duplicate = new KeyMapping();
            duplicate.Id = "conflict-b";
            duplicate.Caps = true;
            duplicate.KeyCode = Keys.E;

            KeyMapping global = new KeyMapping();
            global.Id = "conflict-c";
            global.Caps = false;
            global.KeyCode = Keys.E;

            KeyMapping disabled = new KeyMapping();
            disabled.Id = "conflict-d";
            disabled.Caps = true;
            disabled.KeyCode = Keys.E;
            disabled.Enabled = false;

            HashSet<string> shadowed = MappingConflicts.FindShadowed(
                new KeyMapping[] { first, duplicate, global, disabled });

            Check("duplicate trigger is flagged",
                shadowed.Count == 1 && shadowed.Contains("conflict-b"),
                string.Join(",", new List<string>(shadowed).ToArray()));
            Check("same key in another scope is not a duplicate",
                !shadowed.Contains("conflict-c"), "");
            Check("disabled duplicate is ignored", !shadowed.Contains("conflict-d"), "");
        }

        private static void TestPresets()
        {
            Check("app settings start at config version 6",
                new AppSettings().ConfigVersion == 6, "");

            ShortcutPreset code = new ShortcutPreset();
            code.Id = "preset-code";
            code.Name = "  Visual Studio Code  ";
            code.Process = "C:\\Program Files\\Microsoft VS Code\\Code.exe";
            KeyMapping copy = new KeyMapping();
            copy.Id = "preset-copy";
            copy.Action = "input.keys";
            copy.Param = "Ctrl + C";
            code.Mappings.Add(copy);

            ShortcutPreset duplicate = new ShortcutPreset();
            duplicate.Id = "preset-code";
            duplicate.Name = new string('长', 70);
            duplicate.Process = "code.EXE";

            ShortcutPreset unbound = new ShortcutPreset();
            unbound.Id = "preset-unbound";

            List<ShortcutPreset> normalized = ConfigStore.NormalizePresetList(
                new List<ShortcutPreset> { code, duplicate, unbound, null });

            Check("preset ids are made unique",
                normalized.Count == 3 && normalized[0].Id != normalized[1].Id,
                "count=" + normalized.Count);
            Check("preset name is trimmed and capped",
                normalized[0].Name == "Visual Studio Code" && normalized[1].Name.Length == 60,
                normalized[0].Name);
            Check("one program keeps one preset",
                normalized[0].Process == "Code" && normalized[1].Process == "",
                normalized[0].Process + " / " + normalized[1].Process);
            Check("unnamed unbound preset gets a fallback name",
                normalized[2].Name == "未命名预设", normalized[2].Name);
            Check("preset shortcuts survive normalization",
                normalized[0].Mappings.Count == 1 && normalized[0].Mappings[0].Param == "Ctrl + C", "");

            ShortcutPreset clone = normalized[0].Clone();
            clone.Mappings[0].Param = "Ctrl + V";
            Check("preset clone owns its mappings",
                normalized[0].Mappings[0].Param == "Ctrl + C", "");

            AppSettings settings = new AppSettings();
            KeyMapping global = new KeyMapping();
            global.Id = "global-one";
            global.Action = "input.keys";
            settings.Mappings.Add(global);
            settings.Presets.Add(normalized[0]);

            Check("preset matches the process name case-insensitively",
                ShortcutSets.ForProcess(settings, "code.exe") == normalized[0].Mappings, "");
            Check("unknown process falls back to the global list",
                ShortcutSets.ForProcess(settings, "notepad") == settings.Mappings, "");
            Check("no foreground process uses the global list",
                ShortcutSets.ForProcess(settings, "") == settings.Mappings, "");
            Check("every preset list is counted",
                ShortcutSets.All(settings).Count == 2, "");
            Check("preset lookup by process finds the owner",
                ShortcutSets.FindByProcess(settings, "CODE") == normalized[0], "");
        }

        private static void TestPluginTrust()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-trust");
            string marker = Path.Combine(root, "probe-ran.txt");
            string execMarker = Path.Combine(root, "probe-exec.txt");
            string source = Path.Combine(root, "trust-probe.cs");
            try
            {
                Directory.CreateDirectory(root);
                string template = @"
using System;
using System.IO;
using CapsLockLite;

public sealed class TrustProbePlugin : IPlugin
{
    static TrustProbePlugin()
    {
        File.WriteAllText(@""__MARKER__"", ""ran"");
    }

    public string Id { get { return ""selftest.trust""; } }
    public string Name { get { return ""Trust Probe""; } }
    public string Version { get { return ""1.0""; } }
    public string Author { get { return ""selftest""; } }
    public string Description { get { return ""trust test""; } }
    public IPluginAction[] CreateActions()
    {
        return new IPluginAction[] { new NoopAction() };
    }
}

public sealed class NoopAction : IPluginAction
{
    public string Id { get { return ""noop""; } }
    public string Category { get { return ""test""; } }
    public string Name { get { return ""noop""; } }
    public string Description { get { return string.Empty; } }
    public ActionParamKind ParamKind { get { return ActionParamKind.None; } }
    public string DefaultParam { get { return string.Empty; } }
    public bool Repeatable { get { return false; } }
    public void Execute(PluginContext context)
    {
        File.WriteAllText(@""__EXEC_MARKER__"", ""executed"");
    }
}
";
                string code = template.Replace("__MARKER__", marker).Replace("__EXEC_MARKER__", execMarker);
                File.WriteAllText(source, code, new UTF8Encoding(false));

                // A file dropped into the plugin folder must not be compiled or
                // executed until its hash appears in the trust store.
                string dropped = Path.Combine(PluginRuntime.PluginDirectory, "dropped.cs");
                File.WriteAllText(dropped, code, new UTF8Encoding(false));
                try { if (File.Exists(marker)) File.Delete(marker); }
                catch { }
                PluginRuntime.Reload();
                Check("dropped plugin is not executed", !File.Exists(marker), "");
                try { if (File.Exists(dropped)) File.Delete(dropped); }
                catch { }
                PluginRuntime.Reload();

                IList<PluginImportResult> staged = PluginRuntime.ImportPaths(new string[] { source });
                Check("import stages without executing",
                    staged.Count == 1 && staged[0].Imported && !File.Exists(marker),
                    DescribeImports(staged));

                PluginRuntime.TrustImports(staged);
                Check("trusted plugin executes on load", File.Exists(marker), "");

                PluginStatus loaded = null;
                for (int i = 0; i < PluginRuntime.Plugins.Count; i++)
                {
                    if (string.Equals(PluginRuntime.Plugins[i].Id, "selftest.trust",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        loaded = PluginRuntime.Plugins[i];
                        break;
                    }
                }
                Check("trusted plugin is loaded", loaded != null && loaded.IsLoaded, "");

                KeyMapping mapping = new KeyMapping();
                mapping.Action = "plugin.selftest.trust.noop";
                mapping.Param = "";
                try { if (File.Exists(execMarker)) File.Delete(execMarker); }
                catch { }
                bool posted = PluginRuntime.Dispatch(mapping, new TestHost());
                for (int i = 0; i < 40 && !File.Exists(execMarker); i++)
                    Thread.Sleep(50);
                Check("plugin action runs on the dedicated plugin thread",
                    posted && File.Exists(execMarker), "posted=" + posted);

                if (loaded != null && File.Exists(loaded.FilePath))
                {
                    File.AppendAllText(loaded.FilePath, "\r\n// tampered", new UTF8Encoding(false));
                    try { File.Delete(marker); } catch { }
                    PluginRuntime.Reload();
                    bool stillLoaded = false;
                    for (int i = 0; i < PluginRuntime.Plugins.Count; i++)
                    {
                        if (string.Equals(PluginRuntime.Plugins[i].FilePath, loaded.FilePath,
                            StringComparison.OrdinalIgnoreCase)
                            && PluginRuntime.Plugins[i].IsLoaded)
                        {
                            stillLoaded = true;
                            break;
                        }
                    }
                    Check("modified plugin is rejected", !stillLoaded, "");

                    bool untrusted = PluginRuntime.UntrustFile(loaded.FilePath);
                    bool stillTrusted = false;
                    for (int i = 0; i < PluginRuntime.Plugins.Count; i++)
                    {
                        if (string.Equals(PluginRuntime.Plugins[i].FilePath, loaded.FilePath,
                            StringComparison.OrdinalIgnoreCase) && PluginRuntime.Plugins[i].Trusted)
                        {
                            stillTrusted = true;
                            break;
                        }
                    }
                    Check("untrusting a plugin drops its trust record", untrusted && !stillTrusted,
                        "untrusted=" + untrusted + " stillTrusted=" + stillTrusted);

                    try { if (File.Exists(loaded.FilePath)) File.Delete(loaded.FilePath); }
                    catch { }
                }
            }
            finally
            {
                try { if (File.Exists(marker)) File.Delete(marker); }
                catch { }
                try { if (File.Exists(execMarker)) File.Delete(execMarker); }
                catch { }
                try { if (File.Exists(source)) File.Delete(source); }
                catch { }
                try { Directory.Delete(root, true); }
                catch { }
            }
        }

        private static void TestPluginPackages()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-import");
            string zip = Path.Combine(root, "bundle.zip");
            List<string> importedFiles = new List<string>();

            try
            {
                Directory.CreateDirectory(root);
                string source = Path.Combine(root, "zip-plugin.cs");
                File.WriteAllText(source, PluginSource, new UTF8Encoding(false));

                using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    archive.CreateEntryFromFile(source, "bundle/zip-plugin.cs");
                    archive.CreateEntry("bundle/readme.txt");
                }

                IList<PluginImportResult> results = PluginRuntime.ImportPaths(new string[] { zip });
                for (int i = 0; i < results.Count; i++)
                {
                    if (results[i].Imported) importedFiles.Add(results[i].FilePath);
                }

                Check("zip bundle is recognised",
                    results.Count == 1 && results[0].Imported, DescribeImports(results));

                // Import is compile-only; trust is the explicit second step that
                // allows the plugin to be instantiated.
                PluginRuntime.TrustImports(results);
                Check("zip plugin action is registered",
                    ActionCatalog.Contains("plugin.selftest.sample.noop"), "");

                string expectedFolder = Path.Combine(PluginRuntime.PluginDirectory, "selftest.sample");
                PluginStatus loaded = null;
                for (int i = 0; i < PluginRuntime.Plugins.Count; i++)
                {
                    if (string.Equals(PluginRuntime.Plugins[i].Id, "selftest.sample",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        loaded = PluginRuntime.Plugins[i];
                        break;
                    }
                }
                Check("import lands in a per-plugin folder",
                    loaded != null
                        && loaded.FilePath.StartsWith(expectedFolder, StringComparison.OrdinalIgnoreCase),
                    loaded == null ? "no plugin status" : loaded.FilePath);
                Check("plugin data folder holds the settings file",
                    string.Equals(Path.GetDirectoryName(new PluginSettingsStore("selftest.sample").FilePath),
                        expectedFolder, StringComparison.OrdinalIgnoreCase),
                    new PluginSettingsStore("selftest.sample").FilePath);

                string bogus = Path.Combine(root, "bogus.cs");
                File.WriteAllText(bogus, "public class NotAPlugin { }", new UTF8Encoding(false));
                IList<PluginImportResult> rejected = PluginRuntime.ImportPaths(new string[] { bogus });
                Check("non plugin source is rejected",
                    rejected.Count == 1 && !rejected[0].Imported && !string.IsNullOrEmpty(rejected[0].Error),
                    DescribeImports(rejected));
            }
            finally
            {
                for (int i = 0; i < importedFiles.Count; i++)
                {
                    try { if (File.Exists(importedFiles[i])) File.Delete(importedFiles[i]); }
                    catch { }
                }
                for (int i = 0; i < PluginRuntime.Plugins.Count; i++)
                {
                    try
                    {
                        string file = PluginRuntime.Plugins[i].FilePath;
                        if (!string.IsNullOrEmpty(file) && File.Exists(file)) File.Delete(file);
                    }
                    catch { }
                }
                PluginRuntime.Reload();
                try { Directory.Delete(root, true); }
                catch { }
            }
        }

        private static string DescribeImports(IList<PluginImportResult> results)
        {
            if (results == null || results.Count == 0) return "no results";
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < results.Count; i++)
            {
                if (i > 0) text.Append("; ");
                text.Append(results[i].DisplayName).Append('=')
                    .Append(results[i].Imported ? "ok" : "fail:" + results[i].Error);
            }
            return text.ToString();
        }

        private static void TestPluginSettings()
        {
            string id = "selftest.settings";
            PluginSettingsStore store = new PluginSettingsStore(id);
            try
            {
                store.Set("alpha", "1");
                store.Set("beta", "中文");
                Check("plugin settings are dirty before save", store.HasChanges, "");

                string error;
                bool saved = store.Save(out error);
                Check("plugin settings save", saved, error);

                PluginSettingsStore reloaded = new PluginSettingsStore(id);
                Check("plugin settings round trip",
                    reloaded.Get("alpha", "") == "1" && reloaded.Get("beta", "") == "中文",
                    reloaded.Get("alpha", "") + "/" + reloaded.Get("beta", ""));
                Check("plugin settings fallback", reloaded.Get("missing", "x") == "x", "");

                PluginStatus status = new PluginStatus();
                Check("plugin without settings page is detected", !status.HasSettings, "");
            }
            finally
            {
                try
                {
                    if (File.Exists(store.FilePath)) File.Delete(store.FilePath);
                    string directory = Path.GetDirectoryName(store.FilePath);
                    if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)
                        && Directory.GetFileSystemEntries(directory).Length == 0)
                        Directory.Delete(directory);
                }
                catch { }
            }
        }

        private static void TestHook()
        {
            using (KeyboardHook hook = new KeyboardHook())
            {
                bool installed = hook.Install();
                if (installed) hook.Uninstall();
                Check("keyboard hook install", installed, "Win32=" + hook.LastError);
            }

            using (MouseHook hook = new MouseHook())
            {
                bool installed = hook.Install();
                if (installed) hook.Uninstall();
                Check("mouse hook install", installed, "Win32=" + hook.LastError);
            }
        }

        /// <summary>
        /// A driver backend injects below dwExtraInfo, so its strokes arrive at the
        /// hooks looking like hardware. The ledger is what keeps the Caps layer from
        /// swallowing its own output; these are the cases the hooks rely on.
        /// </summary>
        private static void TestSyntheticLedger()
        {
            SyntheticLedger.Clear();
            Check("ledger ignores a key it never sent",
                !SyntheticLedger.Consume(Keys.K, true), "");

            SyntheticLedger.Record(Keys.A, true);
            SyntheticLedger.Record(Keys.A, false);
            bool tap = SyntheticLedger.Consume(Keys.A, true) && SyntheticLedger.Consume(Keys.A, false);
            Check("ledger claims its own tap in order", tap, "");

            // The release of a key this app is holding belongs to the user: every
            // release we send is booked above, so an unbooked up must reach the Caps
            // layer. Claiming it used to hide the end of a hold (the key stayed down
            // in the target until Caps Lock itself was released).
            SyntheticLedger.Record(Keys.G, true);
            SyntheticLedger.Consume(Keys.G, true);
            bool physicalUp = !SyntheticLedger.Consume(Keys.G, false);
            SyntheticLedger.Clear();
            Check("a physical key-up is never claimed by a held key", physicalUp, "");
            SyntheticLedger.Clear();
            Check("a claimed stroke is not claimed twice",
                !SyntheticLedger.Consume(Keys.A, true), "");

            // A held key is repeated by the OS, not by us: no entry can predict how
            // many repeats arrive, so the held record marks the key instead of
            // booking every stroke.
            SyntheticLedger.Record(Keys.B, true);
            SyntheticLedger.Consume(Keys.B, true);
            Check("a held key is reported, not booked, on its repeats",
                SyntheticLedger.IsHeldRepeat(Keys.B) && !SyntheticLedger.Consume(Keys.B, true), "");

            SyntheticLedger.Record(Keys.B, false);
            // Until the release is actually delivered, a repeat queued with the hold
            // still counts as ours...
            bool stillMarked = SyntheticLedger.IsHeldRepeat(Keys.B);
            bool released = SyntheticLedger.Consume(Keys.B, false);
            Check("a release keeps marking a repeat still in flight", stillMarked, "");
            // ...and once it lands the key is user input again, with no tail that
            // could swallow the next fast press.
            Check("a delivered release stops claiming the key",
                released && !SyntheticLedger.IsHeldRepeat(Keys.B), "");

            SyntheticLedger.Clear();

            int ttl = SyntheticLedger.TtlMs;
            SyntheticLedger.TtlMs = 20;
            SyntheticLedger.Record(Keys.C, true);
            bool live = SyntheticLedger.IsHeldRepeat(Keys.C);
            Thread.Sleep(80);
            bool expired = !SyntheticLedger.IsHeldRepeat(Keys.C) && !SyntheticLedger.Consume(Keys.C, true);
            SyntheticLedger.TtlMs = ttl;
            Check("an unmatched claim expires instead of eating a real key", live && expired, "");

            SyntheticLedger.Record(Keys.D, true);
            SyntheticLedger.Record(Keys.D, false);
            List<InputStroke> refused = new List<InputStroke>();
            refused.Add(new InputStroke(Keys.D, true));
            refused.Add(new InputStroke(Keys.D, false));
            SyntheticLedger.Forget(refused);
            Check("a refused batch leaves no claims behind",
                !SyntheticLedger.Consume(Keys.D, true) && SyntheticLedger.PendingCount == 0,
                "pending=" + SyntheticLedger.PendingCount);
            SyntheticLedger.Clear();
        }

        /// <summary>
        /// Regression: a driver hold books the key-down it injects. The user's own
        /// key-up must still reach the Caps layer, otherwise the repeat keeps running
        /// until Caps Lock is released.
        /// </summary>
        /// <summary>
        /// Row clipboard: copy detaches from the originals, paste creates new ids
        /// right after the anchor and keeps every editable field.
        /// </summary>
        private static void TestMappingClipboard()
        {
            KeyMapping a = new KeyMapping();
            a.KeyCode = Keys.E;
            a.Action = "input.keys";
            a.Param = "Up";
            a.Note = "上移";
            a.TriggerMode = "hold";
            a.Ctrl = true;
            KeyMapping b = new KeyMapping();
            b.KeyCode = (Keys)MouseKeys.Left;
            b.Action = "input.keys";
            b.Param = "Ctrl+C";
            b.Enabled = false;

            List<KeyMapping> source = new List<KeyMapping>();
            source.Add(a);
            source.Add(b);
            List<KeyMapping> copied = MappingClipboard.Copy(source);
            Check("row clipboard copies every row",
                copied.Count == 2 && !ReferenceEquals(copied[0], a) && copied[0].Id == a.Id, "");

            string before = a.Param;
            a.Param = "changed";
            bool detached = copied[0].Param == before;
            a.Param = before;
            Check("row clipboard is detached from the original", detached, copied[0].Param);

            List<KeyMapping> list = new List<KeyMapping>();
            list.Add(a);
            list.Add(b);
            Check("row paste lands right after the anchor",
                MappingClipboard.InsertIndex(list, a) == 1, "");
            Check("row paste falls back to the end without an anchor",
                MappingClipboard.InsertIndex(list, null) == 2
                    && MappingClipboard.InsertIndex(list, new KeyMapping()) == 2, "");

            List<KeyMapping> pasted = MappingClipboard.PasteInto(list, 1, copied);
            Check("row paste inserts both rows in order",
                list.Count == 4 && pasted.Count == 2
                    && ReferenceEquals(list[1], pasted[0]) && ReferenceEquals(list[2], pasted[1]),
                "count=" + list.Count);
            Check("row paste gives every copy a new id",
                pasted[0].Id != a.Id && pasted[1].Id != b.Id && pasted[0].Id != pasted[1].Id, "");
            Check("row paste keeps chord, action, note and trigger mode",
                pasted[0].KeyCode == Keys.E && pasted[0].Action == "input.keys"
                    && pasted[0].Param == "Up" && pasted[0].Note == "上移"
                    && pasted[0].TriggerMode == "hold" && pasted[0].Ctrl
                    && !pasted[1].Enabled
                    && (int)pasted[1].KeyCode == MouseKeys.Left,
                pasted[0].TriggerText + "/" + pasted[1].Key);

            List<KeyMapping> none = MappingClipboard.PasteInto(list, 0, new List<KeyMapping>());
            Check("an empty row clipboard pastes nothing", none.Count == 0 && list.Count == 4, "");
        }

        private static void TestDriverHoldRelease()
        {
            AppSettings settings = ConfigStore.CreateDefault();
            settings.TapAction = "none";
            using (HotkeyManager manager = new HotkeyManager(settings, IntPtr.Zero))
            {
                MethodInfo handler = typeof(HotkeyManager).GetMethod("OnHookKey",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                List<string> events = new List<string>();
                manager.HoldStarted += delegate(KeyMapping m) { events.Add("start"); };
                manager.HoldEnded += delegate(KeyMapping m) { events.Add("end"); };
                // Keep the self-test away from the real desktop.
                manager.SendKeyState = delegate(Keys key, bool up) { };

                SyntheticLedger.Clear();
                handler.Invoke(manager, new object[] { Key(Keys.CapsLock, true) });
                bool downSeen = (bool)handler.Invoke(manager, new object[] { Key(Keys.E, true) });
                // Exactly what InputPipeline books before handing the held key over.
                SyntheticLedger.Record(Keys.E, true);
                SyntheticLedger.Consume(Keys.E, true);
                bool upSeen = (bool)handler.Invoke(manager, new object[] { Key(Keys.E, false) });
                string held = string.Join(",", events.ToArray());
                // The executor's release is booked now but has not reached the target
                // yet: exactly the window a queued repeat can still land in.
                SyntheticLedger.Record(Keys.E, false);
                events.Clear();
                bool lateRepeat = (bool)handler.Invoke(manager, new object[] { Key(Keys.E, true) });
                string afterRepeat = string.Join(",", events.ToArray());
                // The release lands, so the key is user input again immediately -- no
                // timer to wait out before the next real press fires.
                SyntheticLedger.Consume(Keys.E, false);
                events.Clear();
                bool freshPress = (bool)handler.Invoke(manager, new object[] { Key(Keys.E, true) });
                string afterFresh = string.Join(",", events.ToArray());
                handler.Invoke(manager, new object[] { Key(Keys.E, false) });
                handler.Invoke(manager, new object[] { Key(Keys.CapsLock, false) });
                SyntheticLedger.Clear();

                Check("the physical key-up still ends a driver hold",
                    downSeen && upSeen && held == "start,end", downSeen + "/" + upSeen + ":" + held);
                Check("a repeat left in flight does not restart the hold",
                    lateRepeat && afterRepeat.Length == 0, lateRepeat + ":" + afterRepeat);
                Check("a swallowed repeat does not make the next press look repeated",
                    freshPress && afterFresh == "start", freshPress + ":" + afterFresh);
            }
        }

        /// <summary>
        /// Taps of the same shortcut in quick succession must each fire. The driver
        /// backend books the keys it sends, and the ledger's release tail must not
        /// swallow the next press of a key the app itself just sent.
        /// </summary>
        private static void TestRapidTaps()
        {
            Check("rapid taps fire every time", RapidTaps(Keys.E, "Up", 6), "payload differs");
            // The awkward case: a shortcut whose action re-sends its own trigger key.
            Check("rapid taps of a self-sending shortcut fire every time",
                RapidTaps(Keys.J, "J", 6), "payload equals trigger");
        }

        private static bool RapidTaps(Keys trigger, string payload, int taps)
        {
            AppSettings settings = new AppSettings();
            settings.TapAction = "none";
            KeyMapping mapping = new KeyMapping();
            mapping.Caps = true;
            mapping.KeyCode = trigger;
            mapping.Action = "input.keys";
            mapping.Param = payload;
            mapping.TriggerMode = "hold";
            settings.Mappings.Add(mapping);

            using (HotkeyManager manager = new HotkeyManager(settings, IntPtr.Zero))
            {
                MethodInfo onKey = typeof(HotkeyManager).GetMethod("OnHookKey",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                List<string> events = new List<string>();
                manager.HoldStarted += delegate(KeyMapping m) { events.Add("start"); };
                manager.HoldEnded += delegate(KeyMapping m) { events.Add("end"); };
                manager.SendKeyState = delegate(Keys k, bool up) { };

                bool ctrl, alt, shift, win;
                Keys sent;
                if (!KeyText.TryParseCombo(payload, out ctrl, out alt, out shift, out win, out sent))
                    return false;

                SyntheticLedger.Clear();
                onKey.Invoke(manager, new object[] { Key(Keys.CapsLock, true) });
                for (int i = 0; i < taps; i++)
                {
                    // The user presses the shortcut...
                    onKey.Invoke(manager, new object[] { Key(trigger, true) });
                    // ...the executor sends the held key, booked and then delivered...
                    SyntheticLedger.Record(sent, true);
                    SyntheticLedger.Consume(sent, true);
                    // ...the user lets go, and the executor releases the key.
                    onKey.Invoke(manager, new object[] { Key(trigger, false) });
                    SyntheticLedger.Record(sent, false);
                    SyntheticLedger.Consume(sent, false);
                }
                onKey.Invoke(manager, new object[] { Key(Keys.CapsLock, false) });
                SyntheticLedger.Clear();
                return events.Count == taps * 2;
            }
        }

        /// <summary>
        /// The driver sends scan codes, so every E0 key must carry the extended flag:
        /// without it the Windows key arrives as nothing and Win+Space (the IME tap)
        /// silently does nothing.
        /// </summary>
        private static void TestExtendedKeys()
        {
            Check("windows and menu keys are extended",
                Native.IsExtendedKey(Keys.LWin) && Native.IsExtendedKey(Keys.RWin)
                    && Native.IsExtendedKey(Keys.Apps), "");
            Check("navigation, media and right-hand keys stay extended",
                Native.IsExtendedKey(Keys.Left) && Native.IsExtendedKey(Keys.Delete)
                    && Native.IsExtendedKey(Keys.VolumeUp) && Native.IsExtendedKey(Keys.RControlKey), "");
            Check("ordinary keys are not extended",
                !Native.IsExtendedKey(Keys.A) && !Native.IsExtendedKey(Keys.Space)
                    && !Native.IsExtendedKey(Keys.CapsLock) && !Native.IsExtendedKey(Keys.LShiftKey), "");

            ushort code;
            ushort state;
            // ToKeyStroke(key, down, ...): down == true is a key-down stroke.
            bool winStroke = InterceptionSink.ToKeyStroke(Keys.LWin, true, out code, out state)
                && code == 0x5B && state == 0x02;
            Check("the driver sends the windows key down with the E0 prefix", winStroke, code + "/" + state);

            bool plainStroke = InterceptionSink.ToKeyStroke(Keys.A, true, out code, out state)
                && code == 0x1E && state == 0x00;
            Check("the driver sends an ordinary key down without E0", plainStroke, code + "/" + state);

            bool upStroke = InterceptionSink.ToKeyStroke(Keys.RWin, false, out code, out state)
                && code == 0x5C && state == 0x03;
            Check("a right-windows release is up plus E0", upStroke, code + "/" + state);

            // Pause needs E1, which the driver stroke model cannot express: it must
            // refuse so the pipeline sends it through SendInput instead of turning it
            // into NumLock.
            bool pauseRefused = !InterceptionSink.ToKeyStroke(Keys.Pause, true, out code, out state);
            Check("the driver refuses Pause instead of sending NumLock", pauseRefused, code + "/" + state);
        }

        private static void TestInputPipeline()
        {
            AppSettings settings = new AppSettings();
            settings.InputBackend = InputPipeline.BackendSystem;
            InputPipeline.Apply(settings);
            Check("system backend is SendInput",
                InputPipeline.BackendName == "SendInput" && !InputPipeline.UsingDriver,
                InputPipeline.BackendName);
            Check("system backend never reports an error", InputPipeline.LastError.Length == 0,
                InputPipeline.LastError);

            settings.InputBackend = "nonsense";
            ConfigStore.Normalize(settings);
            Check("an unknown backend falls back to auto",
                settings.InputBackend == InputPipeline.BackendAuto, settings.InputBackend);

            // Either the driver is installed and takes over, or it is missing and the
            // pipeline must name the reason while staying on SendInput.
            settings.InputBackend = InputPipeline.BackendDriver;
            InputPipeline.Apply(settings);
            bool consistent = InputPipeline.UsingDriver
                ? InputPipeline.LastError.Length == 0
                : InputPipeline.LastError.Length > 0 && InputPipeline.BackendName == "SendInput";
            Check("driver backend activates or explains the fallback", consistent,
                InputPipeline.BackendName + " / " + InputPipeline.LastError);

            settings.InputBackend = InputPipeline.BackendAuto;
            InputPipeline.Apply(settings);
            Check("auto backend stays silent when no driver is installed",
                InputPipeline.UsingDriver || InputPipeline.LastError.Length == 0,
                InputPipeline.LastError);

            AppSettings restore = new AppSettings();
            restore.InputBackend = InputPipeline.BackendSystem;
            InputPipeline.Apply(restore);
        }

        /// <summary>
        /// The ledger is what tells this app's own strokes apart from the user's when
        /// the backend injects below dwExtraInfo. Mouse strokes are only booked while
        /// the mouse hook can claim them: an unclaimed booking would otherwise be
        /// matched against a real click minutes later.
        /// </summary>
        private static void TestDeliverySignal()
        {
            AppSettings settings = new AppSettings();
            settings.InputBackend = InputPipeline.BackendSystem;
            InputPipeline.Apply(settings);
            Check("SendInput does not need the ledger", !InputPipeline.TracksDelivery, "");

            SyntheticLedger.Clear();
            SyntheticLedger.Record(Keys.LControlKey, true);
            SyntheticLedger.Record(Keys.LControlKey, false);
            Check("the ledger counts one pending stroke per key and state",
                SyntheticLedger.PendingStrokes(Keys.LControlKey, true) == 1
                    && SyntheticLedger.PendingStrokes(Keys.LControlKey, false) == 1
                    && SyntheticLedger.PendingStrokes(Keys.LMenu, true) == 0,
                "down=" + SyntheticLedger.PendingStrokes(Keys.LControlKey, true)
                    + " up=" + SyntheticLedger.PendingStrokes(Keys.LControlKey, false));

            SyntheticLedger.Clear();
            SyntheticLedger.MouseClaimsActive = true;
            SyntheticLedger.Record((Keys)MouseKeys.Left, true);
            SyntheticLedger.Record((Keys)MouseKeys.Left, false);
            int booked = SyntheticLedger.PendingCount;
            SyntheticLedger.MouseClaimsActive = false;
            Check("leaving the mouse-hook scope drops unclaimed mouse strokes",
                booked == 2 && SyntheticLedger.PendingCount == 0,
                booked + "->" + SyntheticLedger.PendingCount);
            Settings_BackToSystem();
        }

        /// <summary>
        /// A swallowed Caps+鼠标 press must not stay "swallowed" once one of our own
        /// releases passes: the two events are indistinguishable, and leaving the
        /// state set made the app eat its own release, which leaves the target with
        /// a button that never comes up -- clicks then silently do nothing.
        /// </summary>
        private static void TestSwallowedMouseRelease()
        {
            AppSettings settings = ConfigStore.CreateDefault();
            settings.TapAction = "none";
            settings.Mappings.Clear();
            KeyMapping click = new KeyMapping();
            click.Caps = true;
            click.KeyCode = (Keys)MouseKeys.Left;
            click.Action = "input.keys";
            click.Param = "F24";
            click.TriggerMode = "hold";
            settings.Mappings.Add(click);

            using (HotkeyManager manager = new HotkeyManager(settings, IntPtr.Zero))
            {
                MethodInfo onKey = typeof(HotkeyManager).GetMethod("OnHookKey",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo onMouse = typeof(HotkeyManager).GetMethod("OnHookMouse",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                manager.SendKeyState = delegate(Keys k, bool up) { };

                SyntheticLedger.Clear();
                onKey.Invoke(manager, new object[] { Key(Keys.CapsLock, true) });
                bool swallowedDown = (bool)onMouse.Invoke(manager,
                    new object[] { Mouse((Keys)MouseKeys.Left, true) });

                // Our own click is in flight: its release is booked. The user lets
                // go of the physical button first, so that event consumes the claim.
                SyntheticLedger.NoteMouseClick();
                SyntheticLedger.Record((Keys)MouseKeys.Left, false);
                bool physicalUpPassed = !(bool)onMouse.Invoke(manager,
                    new object[] { Mouse((Keys)MouseKeys.Left, false) });
                // ...and now our own release arrives with no entry left.
                bool ownUpSwallowed = (bool)onMouse.Invoke(manager,
                    new object[] { Mouse((Keys)MouseKeys.Left, false) });

                onKey.Invoke(manager, new object[] { Key(Keys.CapsLock, false) });
                SyntheticLedger.Clear();

                Check("a swallowed mouse press is passed once an own release lands",
                    swallowedDown && physicalUpPassed && !ownUpSwallowed,
                    swallowedDown + "/" + physicalUpPassed + "/" + ownUpSwallowed);
            }
        }

        /// <summary>
        /// The pointer pin must let the app's own SetCursorPos through (it lands on the
        /// pinned point) and swallow a hand moving the cursor away, and it must always
        /// expire -- a pin that outlives its action would freeze the mouse.
        /// </summary>
        private static void TestPointerPin()
        {
            PointerPin.Release();
            Check("no pin lets every move through", PointerPin.ShouldPass(10, 10), "");

            PointerPin.Pin(100, 200, 500);
            bool own = PointerPin.ShouldPass(100, 200);
            // No tolerance: a 1 px hand movement is blocked too (SetCursorPos never
            // reaches the hook, so nothing of ours needs the slack).
            bool near = !PointerPin.ShouldPass(101, 200);
            bool away = !PointerPin.ShouldPass(160, 240);
            int blocked = PointerPin.BlockedCount;
            int reported = PointerPin.Release();
            Check("a pin lets the pinned point through and swallows a hand moving away",
                own && near && away && blocked == 2 && reported == 2,
                own + "/" + near + "/" + away + " blocked=" + blocked);

            // A release queued by an earlier action must not drop a newer pin.
            int first = PointerPin.Pin(10, 10, 500);
            int second = PointerPin.Pin(20, 20, 500);
            PointerPin.Release(first);
            bool stillPinned = PointerPin.ShouldPass(30, 30) == false;
            PointerPin.Release(second);
            Check("a stale release cannot drop a newer pin", stillPinned && !PointerPin.IsActive, "");

            // Mid-macro the per-action releases are ignored, so the pointer stays put
            // through the step delays as well.
            PointerPin.BeginMacro();
            int macroPin = PointerPin.Pin(40, 40, 500);
            PointerPin.Release(macroPin);
            bool heldThroughMacro = PointerPin.IsActive && !PointerPin.ShouldPass(60, 60);
            PointerPin.Release();
            Check("a macro keeps the pointer pinned between steps",
                heldThroughMacro && !PointerPin.IsActive, "");
            Check("releasing the pin frees the pointer",
                !PointerPin.IsActive && PointerPin.ShouldPass(160, 240), "");

            PointerPin.Pin(5, 5, 20);
            Thread.Sleep(60);
            Check("a pin always expires on its own",
                !PointerPin.IsActive && PointerPin.ShouldPass(500, 500), "");
            PointerPin.Release();
        }

        private static MouseButtonEventArgs Mouse(Keys key, bool down)
        {
            return new MouseButtonEventArgs(key, down, !down, false, false, 0);
        }

        private static void Settings_BackToSystem()
        {
            AppSettings restore = new AppSettings();
            restore.InputBackend = InputPipeline.BackendSystem;
            InputPipeline.Apply(restore);
            SyntheticLedger.Clear();
        }

        private const string PluginSource = @"
using CapsLockLite;

public sealed class SelfTestPlugin : IPlugin
{
    public string Id { get { return ""selftest.sample""; } }
    public string Name { get { return ""SelfTest""; } }
    public string Version { get { return ""1.0.0""; } }
    public string Author { get { return ""selftest""; } }
    public string Description { get { return string.Empty; } }

    public IPluginAction[] CreateActions()
    {
        return new IPluginAction[] { new NoopAction() };
    }
}

public sealed class NoopAction : IPluginAction
{
    public string Id { get { return ""noop""; } }
    public string Category { get { return ""测试""; } }
    public string Name { get { return ""空动作""; } }
    public string Description { get { return string.Empty; } }
    public ActionParamKind ParamKind { get { return ActionParamKind.None; } }
    public string DefaultParam { get { return string.Empty; } }
    public bool Repeatable { get { return false; } }

    public void Execute(PluginContext context)
    {
    }
}
";

        private static void TestPlugins()
        {
            Check("plugin id accepts dotted id",
                PluginRuntime.IsValidPluginId("com.example.screenshot"), "");
            Check("plugin id rejects single segment",
                !PluginRuntime.IsValidPluginId("screenshot"), "");
            Check("plugin action id rejects slash",
                !PluginRuntime.IsValidPluginActionId("a/b"), "");

            AppSettings settings = new AppSettings();
            KeyMapping mapping = new KeyMapping();
            mapping.Id = "plugin-mapping";
            mapping.Caps = true;
            mapping.KeyCode = Keys.K;
            mapping.Action = "plugin.not.installed.missing";
            mapping.Param = "keep-me";
            mapping.TriggerMode = "hold";
            settings.Mappings.Add(mapping);
            ConfigStore.Normalize(settings);
            Check("missing plugin action keeps id and param",
                settings.Mappings.Count == 1
                    && settings.Mappings[0].Action == "plugin.not.installed.missing"
                    && settings.Mappings[0].Param == "keep-me"
                    && settings.Mappings[0].TriggerMode == "hold",
                settings.Mappings.Count == 1 ? settings.Mappings[0].Action : "count=" + settings.Mappings.Count);

            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-plugin");
            string file = Path.Combine(folder, "selftest-plugin.cs");
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(file, PluginSource, new UTF8Encoding(false));

                PluginStatus status = PluginRuntime.LoadForTest(file);
                Check("plugin compiles and loads", status.IsLoaded, status.Error);
                Check("plugin action is registered",
                    status.IsLoaded && ActionCatalog.Contains("plugin.selftest.sample.noop"), "");
                Check("plugin action exposes metadata",
                    status.IsLoaded
                        && ActionCatalog.Get("plugin.selftest.sample.noop").Category == "测试"
                        && !ActionCatalog.IsRepeatable("plugin.selftest.sample.noop"), "");
            }
            finally
            {
                ActionCatalog.UnregisterPluginActions();
                try { Directory.Delete(folder, true); }
                catch { }
            }

            Check("plugin actions are removed on unload",
                !ActionCatalog.Contains("plugin.selftest.sample.noop"), "");
        }

        /// <summary>
        /// Drop-down parameter (ActionParamKind.Select): the plugin declares the
        /// values, the editor can only store one of them, and the list row shows the
        /// label instead of the raw value.
        /// </summary>
        private static void TestPluginParamChoices()
        {
            List<IPluginAction> actions = new List<IPluginAction>();
            actions.Add(new SelectTestAction());
            List<string> errors = new List<string>();
            ActionCatalog.RegisterPluginActions("selftest.select", actions, errors);
            const string id = "plugin.selftest.select.pick";

            ActionDef def = ActionCatalog.Get(id);
            Check("select parameter registers", def != null && def.ParamKind == ActionParamKind.Select,
                def == null ? "missing" : def.ParamKind.ToString());
            Check("select parameter reads value|label pairs",
                def != null && def.ChoiceCount == 2
                    && def.ChoiceValues[0] == "dark" && def.ChoiceLabels[0] == "深色"
                    && def.ChoiceValues[1] == "light" && def.ChoiceLabels[1] == "浅色",
                def == null ? "" : def.ChoiceCount + " / " + def.ChoiceLabelOf("light"));
            Check("select row shows the label",
                ActionCatalog.Describe(id, "light").IndexOf("浅色", StringComparison.Ordinal) >= 0,
                ActionCatalog.Describe(id, "light"));
            Check("select parameter defaults to the first choice",
                ActionCatalog.NormalizeParam(id, "") == "dark",
                ActionCatalog.NormalizeParam(id, ""));
            Check("select parameter keeps a stored value",
                ActionCatalog.NormalizeParam(id, "light") == "light",
                ActionCatalog.NormalizeParam(id, "light"));

            // A Select action without the companion interface must not crash the host
            // and must not offer a value it cannot explain.
            List<IPluginAction> bare = new List<IPluginAction>();
            bare.Add(new SelectTestAction(false));
            ActionCatalog.RegisterPluginActions("selftest.bare", bare, errors);
            ActionDef bareDef = ActionCatalog.Get("plugin.selftest.bare.pick");
            Check("select without choices is handled",
                bareDef != null && bareDef.ChoiceCount == 0
                    && ActionCatalog.NormalizeParam("plugin.selftest.bare.pick", "") == "",
                bareDef == null ? "missing" : bareDef.ChoiceCount.ToString());

            ActionCatalog.UnregisterPluginActions();
        }

        private sealed class SelectTestAction : IPluginAction, IPluginParamChoices
        {
            private readonly string[] _choices;

            public SelectTestAction() : this(true) { }

            public SelectTestAction(bool withChoices)
            {
                _choices = withChoices ? new[] { "dark|深色", "light|浅色" } : null;
            }

            public string Id { get { return "pick"; } }
            public string Category { get { return "测试"; } }
            public string Name { get { return "选择样式"; } }
            public string Description { get { return "自检用动作"; } }
            public ActionParamKind ParamKind { get { return ActionParamKind.Select; } }
            public string DefaultParam { get { return "dark"; } }
            public bool Repeatable { get { return false; } }
            public string[] ParamChoices { get { return _choices; } }
            public void Execute(PluginContext context) { }
        }

        private sealed class TestHost : IAppHost
        {
            public void OpenCalculator() { }
            public void ReloadFromDisk() { }
            public void ShowMainWindow() { }
            public void ShowStatus(string message, MInfoSeverity severity) { }
            public void ShowStatus(string message, bool error) { }
            public void ShowEscapeResult(string escaped, string original) { }
            public void OpenHelp() { }
        }
    }
}
