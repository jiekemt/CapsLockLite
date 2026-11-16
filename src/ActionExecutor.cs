using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CapsLockLite
{
    internal interface IAppHost
    {
        void OpenCalculator();
        void ReloadFromDisk();
        void ShowMainWindow();
        void ShowStatus(string message, MInfoSeverity severity);
        void ShowStatus(string message, bool error);
        void ShowEscapeResult(string escaped, string original);
        void OpenHelp();
    }

    internal sealed class ActionExecutor : IDisposable
    {
        private readonly IAppHost _host;
        private readonly ConcurrentQueue<KeyMapping> _queue = new ConcurrentQueue<KeyMapping>();
        private readonly Dictionary<string, DateTime> _lastRun = new Dictionary<string, DateTime>();
        private readonly System.Windows.Forms.Timer _timer;
        private bool _pumpPosted;
        private bool _disposed;

        // Physical key-hold sessions (used by 按住连发 on input.keys): the main
        // key and modifiers stay down between repeated key-down events, exactly
        // like a real keyboard. Several keys can be held at once: pressing a new
        // chord key does not release the previous one, it only takes over the
        // auto-repeat, so a game keeps moving forward while the player taps left
        // and right.
        private sealed class HeldKey
        {
            public KeyMapping Mapping;
            public Keys Key;
            public bool Ctrl;
            public bool Alt;
            public bool Shift;
            public bool Win;
        }

        private readonly List<HeldKey> _holds = new List<HeldKey>();

        public ActionExecutor(IAppHost host)
        {
            _host = host;
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 15;
            _timer.Tick += Pump;
            _timer.Start();
        }

        public void Enqueue(KeyMapping mapping)
        {
            if (mapping == null) return;
            DateTime last;
            if (_lastRun.TryGetValue(mapping.Id, out last))
            {
                if ((DateTime.UtcNow - last).TotalMilliseconds < 25) return;
            }
            _lastRun[mapping.Id] = DateTime.UtcNow;
            _queue.Enqueue(mapping);

            // Waiting for the 15 ms timer tick costs one WM_TIMER period, which is
            // visible on single-shot shortcuts. Posting the drain to the message
            // loop runs it right after the hook callback returns while keeping
            // execution on the UI thread.
            PostPump();
        }

        private void PostPump()
        {
            if (_pumpPosted) return;
            Control target = _host as Control;
            if (target == null || !target.IsHandleCreated) return;
            try
            {
                _pumpPosted = true;
                target.BeginInvoke(new Action(Drain));
            }
            catch (InvalidOperationException)
            {
                _pumpPosted = false;
            }
        }

        private void Drain()
        {
            _pumpPosted = false;
            Pump(null, EventArgs.Empty);
        }

        private void Pump(object sender, EventArgs e)
        {
            int processed = 0;
            while (processed < 12)
            {
                KeyMapping mapping;
                if (!_queue.TryDequeue(out mapping)) break;
                processed++;
                try
                {
                    Execute(mapping);
                }
                catch (Exception ex)
                {
                    ConfigStore.Log("执行动作失败：" + mapping.Action + " - " + ex);
                    if (_host != null) _host.ShowStatus("执行失败：" + ex.Message, MInfoSeverity.Error);
                }
            }
        }

        private void Execute(KeyMapping m)
        {
            int count = ActionCatalog.ParseCount(m.Param, 1);
            switch (m.Action)
            {
                case "move.word.left":
                    Native.SendKeyCombo(Keys.Left, true, false, false, false, count);
                    break;
                case "move.word.right":
                    Native.SendKeyCombo(Keys.Right, true, false, false, false, count);
                    break;

                case "select.up":
                    Native.SendKeyCombo(Keys.Up, false, false, true, false, count);
                    break;
                case "select.down":
                    Native.SendKeyCombo(Keys.Down, false, false, true, false, count);
                    break;
                case "select.left":
                    Native.SendKeyCombo(Keys.Left, false, false, true, false, count);
                    break;
                case "select.right":
                    Native.SendKeyCombo(Keys.Right, false, false, true, false, count);
                    break;
                case "select.word.left":
                    Native.SendKeyCombo(Keys.Left, true, false, true, false, count);
                    break;
                case "select.word.right":
                    Native.SendKeyCombo(Keys.Right, true, false, true, false, count);
                    break;
                case "select.current.word":
                    SelectCurrentWord();
                    break;
                case "select.current.line":
                    Native.SendKeyCombo(Keys.Home, false, false, false, false, 1);
                    Native.SendKeyCombo(Keys.End, false, false, true, false, 1);
                    break;

                case "delete.line":
                    Native.SendKeyCombo(Keys.Home, false, false, false, false, 1);
                    Native.SendKeyCombo(Keys.End, false, false, true, false, 1);
                    Native.SendKeyCombo(Keys.Delete, false, false, false, false, 1);
                    break;
                case "delete.all":
                    Native.SendKeyCombo(Keys.A, true, false, false, false, 1);
                    Native.SendKeyCombo(Keys.Delete, false, false, false, false, 1);
                    break;
                case "delete.to.line.start":
                    Native.SendKeyCombo(Keys.Home, false, false, true, false, 1);
                    Native.SendKeyCombo(Keys.Delete, false, false, false, false, 1);
                    break;
                case "delete.to.line.end":
                    Native.SendKeyCombo(Keys.End, false, false, true, false, 1);
                    Native.SendKeyCombo(Keys.Delete, false, false, false, false, 1);
                    break;
                case "delete.to.page.start":
                    Native.SendKeyCombo(Keys.Home, true, false, true, false, 1);
                    Native.SendKeyCombo(Keys.Delete, false, false, false, false, 1);
                    break;
                case "delete.to.page.end":
                    Native.SendKeyCombo(Keys.End, true, false, true, false, 1);
                    Native.SendKeyCombo(Keys.Delete, false, false, false, false, 1);
                    break;


                case "window.topmost":
                    WindowTools.ToggleTopmost(WindowTools.GetActiveWindow());
                    break;
                case "window.transparency":
                    if (!WindowTools.ToggleTransparency(WindowTools.GetActiveWindow()))
                        if (_host != null) _host.ShowStatus("当前窗口不支持透明度调整。", MInfoSeverity.Warning);
                    break;
                case "window.minimize":
                    WindowTools.Minimize(WindowTools.GetActiveWindow());
                    break;
                case "window.maximize":
                    WindowTools.ToggleMaximize(WindowTools.GetActiveWindow());
                    break;
                case "window.close":
                    WindowTools.Close(WindowTools.GetActiveWindow());
                    break;
                case "window.snap.left":
                    WindowTools.Snap(WindowTools.GetActiveWindow(), true);
                    break;
                case "window.snap.right":
                    WindowTools.Snap(WindowTools.GetActiveWindow(), false);
                    break;

                case "system.calc":
                    RunOnStaThread(delegate { if (_host != null) _host.OpenCalculator(); });
                    break;
                case "system.show":
                    if (_host != null) _host.ShowMainWindow();
                    break;
                case "system.translate":
                    TranslateSelection();
                    break;
                case "system.escape":
                    EscapeSelection();
                    break;
                case "system.toggle.caps":
                    Native.ToggleCapsLock();
                    break;
                case "system.media.next":
                    Native.SendVirtualKey(Keys.MediaNextTrack);
                    break;
                case "system.media.prev":
                    Native.SendVirtualKey(Keys.MediaPreviousTrack);
                    break;
                case "system.media.play":
                    Native.SendVirtualKey(Keys.MediaPlayPause);
                    break;
                case "system.volume.up":
                    Native.SendVirtualKey(Keys.VolumeUp);
                    break;
                case "system.volume.down":
                    Native.SendVirtualKey(Keys.VolumeDown);
                    break;
                case "system.volume.mute":
                    Native.SendVirtualKey(Keys.VolumeMute);
                    break;

                case "launch.program":
                    RunOnStaThread(delegate { LaunchProgram(m.Param); });
                    break;
                case "launch.folder":
                    RunOnStaThread(delegate { OpenFolder(m.Param); });
                    break;
                case "launch.url":
                    RunOnStaThread(delegate { OpenUrl(m.Param); });
                    break;
                case "input.text":
                    RunOnStaThread(delegate { Native.SendUnicodeText(m.Param); });
                    break;
                case "input.keys":
                    SendCustomKeys(m.Param);
                    break;
                case "mouse.click":
                    ClickAt(m.Param);
                    break;
                case "none":
                    break;

                default:
                    // Macro actions are scheduled on the UI thread by MacroRunner;
                    // the runner calls back into ExecuteAction for the inner steps.
                    if (ActionCatalog.IsMacroActionId(m.Action))
                    {
                        Func<string, bool> hook = MacroStartHook;
                        if (hook != null) hook(m.Action);
                        break;
                    }
                    // Anything else is a plugin action id ("plugin.<plugin>.<action>").
                    // Plugin code runs on its own STA thread so a slow or modal
                    // action cannot block the keyboard hook/UI thread.
                    PluginRuntime.Dispatch(m, _host);
                    break;
            }
        }

        /// <summary>
        /// Runs one action by id. Macro steps go through here so a step behaves
        /// exactly like the same action bound to a shortcut (plugin routing,
        /// STA threads, status messages).
        /// </summary>
        internal void ExecuteAction(string action, string param)
        {
            KeyMapping mapping = new KeyMapping();
            mapping.Action = string.IsNullOrEmpty(action) ? ActionCatalog.None : action;
            mapping.Param = param == null ? "" : param;
            Execute(mapping);
        }

        /// <summary>Set by the form so a bound macro action can start the runner.</summary>
        internal Func<string, bool> MacroStartHook;

        private static void SelectCurrentWord()
        {
            // Move one character right first so the caret is not sitting exactly
            // on a word boundary. Then collapse at the word start and extend to
            // the word end; selecting left-then-right in one pass only covered
            // the part between the caret and the word end.
            //
            // All four steps go out in a single SendInput batch: the target
            // application consumes them in order, so the fixed sleeps that used to
            // sit between the calls (and raced with slow editors) are gone.
            Native.InputBatch batch = new Native.InputBatch();
            batch.Tap(Keys.Right);
            batch.Combo(Keys.Left, true, false, true, false, 1);
            batch.Tap(Keys.Left);
            batch.Combo(Keys.Right, true, false, true, false, 1);
            batch.Send();
        }

        private void SendCustomKeys(string param)
        {
            bool ctrl, alt, shift, win;
            Keys key;
            if (!KeyText.TryParseCombo(param, out ctrl, out alt, out shift, out win, out key))
            {
                if (_host != null) _host.ShowStatus("自定义按键格式无效：" + param, MInfoSeverity.Error);
                return;
            }
            Native.SendKeyCombo(key, ctrl, alt, shift, win, 1);
        }

        /// <summary>
        /// Starts a held-key session: modifiers and the main key go down once and
        /// stay down. RepeatKeyHold sends further key-down events (auto-repeat) and
        /// EndKeyHold sends the single key-up. A mapping that is already held is
        /// left untouched so the newest key can share the output with it.
        /// </summary>
        internal bool BeginKeyHold(KeyMapping mapping)
        {
            if (mapping == null) return false;
            if (FindHold(mapping) != null) return true;

            bool ctrl, alt, shift, win;
            Keys key;
            if (!KeyText.TryParseCombo(mapping.Param == null ? "" : mapping.Param,
                out ctrl, out alt, out shift, out win, out key))
            {
                if (_host != null) _host.ShowStatus("自定义按键格式无效：" + mapping.Param, MInfoSeverity.Error);
                return false;
            }

            HeldKey hold = new HeldKey();
            hold.Mapping = mapping;
            hold.Key = key;
            hold.Ctrl = ctrl;
            hold.Alt = alt;
            hold.Shift = shift;
            hold.Win = win;
            _holds.Add(hold);

            if (ctrl) Native.SendKeyState(Keys.LControlKey, false);
            if (alt) Native.SendKeyState(Keys.LMenu, false);
            if (shift) Native.SendKeyState(Keys.LShiftKey, false);
            if (win) Native.SendKeyState(Keys.LWin, false);
            return Native.SendKeyState(key, false);
        }

        private HeldKey FindHold(KeyMapping mapping)
        {
            for (int i = 0; i < _holds.Count; i++)
            {
                if (ReferenceEquals(_holds[i].Mapping, mapping)) return _holds[i];
            }
            return null;
        }

        /// <summary>
        /// One auto-repeat step: another key-down while the key stays held. A null
        /// mapping repeats the newest hold.
        /// </summary>
        internal bool RepeatKeyHold(KeyMapping mapping)
        {
            HeldKey hold = mapping == null
                ? (_holds.Count > 0 ? _holds[_holds.Count - 1] : null)
                : FindHold(mapping);
            if (hold == null) return false;
            return Native.SendKeyState(hold.Key, false);
        }

        /// <summary>
        /// Releases the hold of one mapping (or every hold when null). Modifiers are
        /// only released once no other hold still needs them.
        /// </summary>
        internal void EndKeyHold(KeyMapping mapping)
        {
            if (mapping == null)
            {
                for (int i = _holds.Count - 1; i >= 0; i--)
                {
                    HeldKey all = _holds[i];
                    _holds.RemoveAt(i);
                    ReleaseHold(all);
                }
                return;
            }

            HeldKey hold = FindHold(mapping);
            if (hold == null) return;
            _holds.Remove(hold);
            ReleaseHold(hold);
        }

        private void ReleaseHold(HeldKey hold)
        {
            bool sameKey = false;
            for (int i = 0; i < _holds.Count; i++)
            {
                if (_holds[i].Key == hold.Key) sameKey = true;
            }
            if (!sameKey) Native.SendKeyState(hold.Key, true);

            if (hold.Win && !AnyHeld(false, false, false, true)) Native.SendKeyState(Keys.LWin, true);
            if (hold.Shift && !AnyHeld(false, false, true, false)) Native.SendKeyState(Keys.LShiftKey, true);
            if (hold.Alt && !AnyHeld(false, true, false, false)) Native.SendKeyState(Keys.LMenu, true);
            if (hold.Ctrl && !AnyHeld(true, false, false, false)) Native.SendKeyState(Keys.LControlKey, true);
        }

        private bool AnyHeld(bool ctrl, bool alt, bool shift, bool win)
        {
            for (int i = 0; i < _holds.Count; i++)
            {
                HeldKey hold = _holds[i];
                if (ctrl && hold.Ctrl) return true;
                if (alt && hold.Alt) return true;
                if (shift && hold.Shift) return true;
                if (win && hold.Win) return true;
            }
            return false;
        }

        /// <summary>
        /// Moves the cursor to a virtual-desktop point and clicks the chosen button.
        /// Modifier keys (Ctrl/Alt/Shift/Win) are pressed before the move so hover
        /// state already sees them, and always released again. A short pause lets
        /// hover-driven UI settle before the press.
        /// </summary>
        private void ClickAt(string param)
        {
            MouseClickPoint point = MouseClickPoint.Parse(param);

            Native.InputBatch held = new Native.InputBatch();
            if (point.Ctrl) held.Down(Keys.LControlKey);
            if (point.Alt) held.Down(Keys.LMenu);
            if (point.Shift) held.Down(Keys.LShiftKey);
            if (point.Win) held.Down(Keys.LWin);
            bool pressing = point.HasModifiers && held.Send();
            if (pressing) Thread.Sleep(10);

            try
            {
                if (!Native.SetCursorPos(point.X, point.Y))
                {
                    if (_host != null) _host.ShowStatus("无法移动到坐标 (" + point.X + ", " + point.Y + ")。", MInfoSeverity.Warning);
                    return;
                }
                Thread.Sleep(15);

                uint down;
                uint up;
                switch (point.Button)
                {
                    case MouseClickButton.Right:
                        down = Native.MOUSEEVENTF_RIGHTDOWN;
                        up = Native.MOUSEEVENTF_RIGHTUP;
                        break;
                    case MouseClickButton.Middle:
                        down = Native.MOUSEEVENTF_MIDDLEDOWN;
                        up = Native.MOUSEEVENTF_MIDDLEUP;
                        break;
                    default:
                        down = Native.MOUSEEVENTF_LEFTDOWN;
                        up = Native.MOUSEEVENTF_LEFTUP;
                        break;
                }
                Native.mouse_event(down, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(10);
                Native.mouse_event(up, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(10);
            }
            finally
            {
                if (pressing) ReleaseHeldModifiers(point);
            }
        }

        private static void ReleaseHeldModifiers(MouseClickPoint point)
        {
            Native.InputBatch batch = new Native.InputBatch();
            if (point.Win) batch.Up(Keys.LWin);
            if (point.Shift) batch.Up(Keys.LShiftKey);
            if (point.Alt) batch.Up(Keys.LMenu);
            if (point.Ctrl) batch.Up(Keys.LControlKey);
            batch.Send();
        }

        private void TranslateSelection()
        {
            RunOnStaThread(delegate
            {
                string text = CaptureSelection();
                string url;
                if (!string.IsNullOrEmpty(text))
                {
                    if (text.Length > 1500) text = text.Substring(0, 1500);
                    url = "https://www.bing.com/translator?text=" + Uri.EscapeDataString(text);
                }
                else
                {
                    PostStatus("未获取到选中文字，已打开翻译首页。", MInfoSeverity.Info);
                    url = "https://www.bing.com/translator";
                }
                OpenUrl(url);
            });
        }

        private void EscapeSelection()
        {
            RunOnStaThread(delegate
            {
                string text = CaptureSelection();
                if (string.IsNullOrEmpty(text))
                {
                    PostStatus("未获取到选中文字。", MInfoSeverity.Error);
                    return;
                }
                string escaped = KeyText.EscapeForJavaScript(text);
                PostEscapeResult(escaped, text);
            });
        }

        /// <summary>
        /// Clipboard access and the wait for the target application to finish
        /// copying must not run on the UI thread: it is the thread that services
        /// the low-level keyboard hook.
        /// </summary>
        private void RunOnStaThread(ThreadStart action)
        {
            if (action == null) return;
            Thread worker = new Thread((ThreadStart)delegate
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    ConfigStore.Log("剪贴板/翻译动作失败：" + ex);
                    PostStatus("操作失败：" + ex.Message, MInfoSeverity.Error);
                }
            });
            worker.IsBackground = true;
            worker.Name = "CapsLockLite.Clipboard";
            try { worker.SetApartmentState(ApartmentState.STA); }
            catch { }
            worker.Start();
        }

        private void PostStatus(string message, MInfoSeverity severity)
        {
            if (_host == null || string.IsNullOrEmpty(message)) return;
            ISynchronizeInvoke synchronizer = _host as ISynchronizeInvoke;
            if (synchronizer != null && synchronizer.InvokeRequired)
            {
                try
                {
                    synchronizer.BeginInvoke(new Action<string, MInfoSeverity>(_host.ShowStatus),
                        new object[] { message, severity });
                }
                catch { }
                return;
            }
            _host.ShowStatus(message, severity);
        }

        private void PostEscapeResult(string escaped, string original)
        {
            if (_host == null) return;
            ISynchronizeInvoke synchronizer = _host as ISynchronizeInvoke;
            if (synchronizer != null && synchronizer.InvokeRequired)
            {
                try
                {
                    synchronizer.BeginInvoke(new Action<string, string>(_host.ShowEscapeResult),
                        new object[] { escaped, original });
                }
                catch { }
                return;
            }
            _host.ShowEscapeResult(escaped, original);
        }

        private string CaptureSelection()
        {
            uint before = Native.GetClipboardSequenceNumber();
            if (!Native.SendKeyCombo(Keys.C, true, false, false, false, 1)) return "";
            // Wait for the copy to actually land instead of sleeping a fixed
            // 160 ms: a slow application used to hand back the stale clipboard.
            if (!Native.WaitForClipboardChange(before, 800)) return "";
            return GetClipboardText();
        }

        private static string GetClipboardText()
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                        return Clipboard.GetText(TextDataFormat.UnicodeText);
                    return "";
                }
                catch
                {
                    Thread.Sleep(30);
                }
            }
            return "";
        }

        private void LaunchProgram(string path)
        {
            string target = ExpandPath(path);
            if (string.IsNullOrEmpty(target))
            {
                PostStatus("尚未设置要运行的程序。", MInfoSeverity.Warning);
                return;
            }
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = target;
            info.UseShellExecute = true;
            Process.Start(info);
        }

        private void OpenFolder(string path)
        {
            string target = ExpandPath(path);
            if (string.IsNullOrEmpty(target))
            {
                PostStatus("尚未设置要打开的文件夹。", MInfoSeverity.Warning);
                return;
            }
            if (Directory.Exists(target))
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = target;
                info.UseShellExecute = true;
                Process.Start(info);
            }
            else
            {
                OpenUrl(target);
            }
        }

        private void OpenUrl(string url)
        {
            string target = (url ?? "").Trim();
            if (target.Length == 0)
            {
                PostStatus("尚未设置网址。", MInfoSeverity.Warning);
                return;
            }
            if (target.IndexOf("://", StringComparison.Ordinal) < 0)
                target = "https://" + target;
            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = target;
            info.UseShellExecute = true;
            Process.Start(info);
        }

        private static string ExpandPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string text = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return text.Trim();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            EndKeyHold(null);
            _timer.Stop();
            _timer.Dispose();
        }
    }
}
