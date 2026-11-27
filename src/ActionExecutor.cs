using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
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
        private readonly Control _self;
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
            /// <summary>A mouse button stays down like a key does; it never auto-repeats.</summary>
            public bool Mouse;
        }

        private readonly List<HeldKey> _holds = new List<HeldKey>();

        // A mouse action that carries modifiers is split across two message-loop
        // turns: the modifiers go out now, the mouse event in the next turn. That turn
        // boundary is what lets this app's own low-level keyboard hook -- serviced by
        // this same thread -- hand the modifiers to the target before the click is
        // written (measured: keyboard 3.3 ms when this thread pumps, 65-78 ms when it
        // does not; a mouse click is 2-3 ms either way, which is why a later click
        // used to overtake an earlier key press).
        //
        // Everything else stays synchronous on the calling thread. An earlier attempt
        // moved whole actions to a second thread: it fixed the modifier order but let
        // steps overtake each other and let a macro end -- pointer restored, "宏执行
        // 完毕" shown -- while its last click was still in flight.
        /// <summary>Pause after the modifiers in the fallback (no message loop) path.</summary>
        private const int ModifierSettleMs = 10;

        /// <summary>
        /// How long the button stays down. This is not a "delivery" safety margin --
        /// down and up are both messages and arrive either way (measured with 0 ms:
        /// every click still landed). It exists for consumers that POLL the button
        /// state instead of reacting to the messages: at 60 Hz a whole press can fall
        /// between two polls, so the press has to be visible for a moment.
        /// </summary>
        private const int ClickHoldMs = 10;
        /// <summary>
        /// Upper bound for pinning the pointer during one click. The pin now covers the
        /// whole action -- move, modifier delivery, press, release -- so it has to be
        /// generous; it is always released explicitly, this is only the safety net.
        /// </summary>
        private const int ClickPinMs = 1500;
        /// <summary>How long a hover keeps the pointer pinned while a macro runs.</summary>
        private const int HoverPinMs = 2000;
        /// <summary>The same for a plain binding, where nobody expects a frozen pointer.</summary>
        private const int StandalonePinMs = 300;
        /// <summary>Set by the macro runner while a macro is running.</summary>
        internal bool MacroActive;

        // Click statistics for the one summary line the macro runner writes. A line per
        // click is what made the log grow (and, once it was large, what made logging slow
        // enough to disturb the macro), so normal clicks are counted, not written.
        private int _clickCount;
        private int _clickFailed;
        private int _clickCorrections;
        private int _clickDrift;
        private int _holdMin;
        private int _holdMax;

        internal void ResetClickStats()
        {
            _clickCount = 0;
            _clickFailed = 0;
            _clickCorrections = 0;
            _clickDrift = 0;
            _holdMin = int.MaxValue;
            _holdMax = 0;
        }

        /// <summary>"点击 3 次（修饰键 15–31ms）" or "" when nothing was clicked.</summary>
        internal string ClickStatsText()
        {
            if (_clickCount == 0) return "";
            StringBuilder text = new StringBuilder();
            text.Append("点击 ").Append(_clickCount).Append(" 次");
            if (_holdMax > 0)
                text.Append("（修饰键 ").Append(_holdMin == int.MaxValue ? _holdMax : _holdMin)
                    .Append('–').Append(_holdMax).Append("ms）");
            if (_clickCorrections > 0) text.Append("，落位修正 ").Append(_clickCorrections).Append(" 次");
            if (_clickDrift > 0) text.Append("，被拖动 ").Append(_clickDrift).Append(" 次");
            if (_clickFailed > 0) text.Append("，发送失败 ").Append(_clickFailed).Append(" 次");
            return text.ToString();
        }

        /// <summary>How long a mouse action may wait for its modifiers to land.</summary>
        private const int DeliveryWindowMs = 400;
        /// <summary>
        /// Quiet window between a click and its modifier release. The release is not
        /// delayed by sleeping (that would starve the hook thread): the queue simply
        /// is not drained for the window, so the hook gets the click first.
        /// </summary>
        private const int ReleaseWindowMs = 12;
        /// <summary>Beat the target gets to act on a claimed click before the modifiers go up.</summary>
        private const int ReleaseMarginMs = 8;
        /// <summary>
        /// After a click the next step waits this long, so a key of the next step can
        /// never land between the click's press and its release (a Shift in that gap
        /// would turn it into a completely different click).
        /// </summary>
        private const int ClickQuietMs = 8;

        private enum PendingKind
        {
            ClickAt,
            KeyClick,
            HoldDown,
            HoldUp,
            /// <summary>Releases the modifiers once the click has been delivered.</summary>
            ReleaseMods
        }

        /// <summary>
        /// A mouse action whose modifiers were sent in one message-loop turn and whose
        /// mouse event waits for the next one, but only once the modifiers have really
        /// been handed to the target. The pump timer below drains this queue on the UI
        /// thread, so nothing blocks the thread that services the keyboard hook.
        /// </summary>
        private sealed class PendingMouse
        {
            public PendingKind Kind;
            public MouseClickPoint Point;
            public Keys Key;
            public bool Ctrl;
            public bool Alt;
            public bool Shift;
            public bool Win;
            public bool ReleaseButton = true;
            /// <summary>ClickAt only: the cursor was already moved when it was queued.</summary>
            public bool Moved;
            public int Deadline;
            /// <summary>ReleaseMods only: when the click's release was seen.</summary>
            public int ClaimedAt;
            /// <summary>ReleaseMods only: also drop the pointer pin it belongs to.</summary>
            public bool ReleasesPin;
            /// <summary>ReleaseMods only: which pin (a stale token must not drop a newer one).</summary>
            public int PinGeneration;
            /// <summary>When the modifiers were sent, for the click log line.</summary>
            public int PressedAt;
        }

        private readonly List<PendingMouse> _pending = new List<PendingMouse>();
        private bool _pendingWakePosted;
        private static int _lastClickAt;
        private static int _clickQuietUntil;
        private bool _draining;
        private bool _shuttingDown;

        /// <summary>Raised when the pending queue has drained.</summary>
        internal event Action Idle;

        /// <summary>
        /// Runs work on the executor's thread. The low-level hooks now live on their
        /// own thread, so hold start/repeat/end callbacks arrive there and must be
        /// handed over instead of touching the hold list from two threads.
        /// </summary>
        internal void Post(Action work)
        {
            if (work == null) return;
            Control target = Anchor;
            if (target != null && target.IsHandleCreated && !target.IsDisposed)
            {
                try { target.BeginInvoke(work); return; }
                catch (InvalidOperationException) { }
            }
            work();
        }

        /// <summary>
        /// True while a mouse action is still waiting for its modifiers or for its own
        /// release. Macro steps wait for it, and so does the end of a macro: the
        /// pointer must not be restored before the last click has landed.
        /// </summary>
        internal bool Busy
        {
            get
            {
                if (_pending.Count > 0 || _draining) return true;
                if (unchecked(Environment.TickCount - _clickQuietUntil) < 0) return true;
                if (!InputPipeline.TracksDelivery) return false;
                // Strokes of ours the hook has not handed on yet: starting the next
                // step now lets a later click overtake an earlier key press (the
                // click is not gated by the hook, the key is). Bounded in time so a
                // stopped hook can never wedge the run.
                if (SyntheticLedger.PendingCount == 0) return false;
                return unchecked(Environment.TickCount - SyntheticLedger.LastRecordTick) <= DeliveryWindowMs;
            }
        }

        /// <summary>
        /// Creates an executor that sends from <paramref name="self"/>'s thread. Passing
        /// no anchor keeps it on the host's thread, which is what the self-tests use.
        /// </summary>
        public ActionExecutor(IAppHost host)
            : this(host, host as Control)
        {
        }

        public ActionExecutor(IAppHost host, Control self)
        {
            _host = host;
            _self = self;
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 15;
            _timer.Tick += Pump;
            _timer.Start();
        }

        public void Enqueue(KeyMapping mapping)
        {
            if (mapping == null) return;
            DateTime last;
            // A wheel notch is one deliberate action: two of them 10 ms apart are two
            // scrolls, not a repeated trigger, so the double-fire guard must not eat
            // them (it used to cap the wheel at ~40 actions per second).
            if (!MouseKeys.IsWheel((Keys)mapping.Key) && _lastRun.TryGetValue(mapping.Id, out last))
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

        /// <summary>The control that owns this executor's thread.</summary>
        private Control Anchor
        {
            get
            {
                if (_self != null) return _self;
                return _host as Control;
            }
        }

        private void PostPump()
        {
            if (_pumpPosted) return;
            Control target = Anchor;
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
            RunPending();
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
                    InputPipeline.SendCombo(Keys.Left, true, false, false, false, count);
                    break;
                case "move.word.right":
                    InputPipeline.SendCombo(Keys.Right, true, false, false, false, count);
                    break;

                case "select.up":
                    InputPipeline.SendCombo(Keys.Up, false, false, true, false, count);
                    break;
                case "select.down":
                    InputPipeline.SendCombo(Keys.Down, false, false, true, false, count);
                    break;
                case "select.left":
                    InputPipeline.SendCombo(Keys.Left, false, false, true, false, count);
                    break;
                case "select.right":
                    InputPipeline.SendCombo(Keys.Right, false, false, true, false, count);
                    break;
                case "select.word.left":
                    InputPipeline.SendCombo(Keys.Left, true, false, true, false, count);
                    break;
                case "select.word.right":
                    InputPipeline.SendCombo(Keys.Right, true, false, true, false, count);
                    break;
                case "select.current.word":
                    SelectCurrentWord();
                    break;
                case "select.current.line":
                    InputPipeline.SendCombo(Keys.Home, false, false, false, false, 1);
                    InputPipeline.SendCombo(Keys.End, false, false, true, false, 1);
                    break;

                case "delete.line":
                    InputPipeline.SendCombo(Keys.Home, false, false, false, false, 1);
                    InputPipeline.SendCombo(Keys.End, false, false, true, false, 1);
                    InputPipeline.SendCombo(Keys.Delete, false, false, false, false, 1);
                    break;
                case "delete.all":
                    InputPipeline.SendCombo(Keys.A, true, false, false, false, 1);
                    InputPipeline.SendCombo(Keys.Delete, false, false, false, false, 1);
                    break;
                case "delete.to.line.start":
                    InputPipeline.SendCombo(Keys.Home, false, false, true, false, 1);
                    InputPipeline.SendCombo(Keys.Delete, false, false, false, false, 1);
                    break;
                case "delete.to.line.end":
                    InputPipeline.SendCombo(Keys.End, false, false, true, false, 1);
                    InputPipeline.SendCombo(Keys.Delete, false, false, false, false, 1);
                    break;
                case "delete.to.page.start":
                    InputPipeline.SendCombo(Keys.Home, true, false, true, false, 1);
                    InputPipeline.SendCombo(Keys.Delete, false, false, false, false, 1);
                    break;
                case "delete.to.page.end":
                    InputPipeline.SendCombo(Keys.End, true, false, true, false, 1);
                    InputPipeline.SendCombo(Keys.Delete, false, false, false, false, 1);
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
                    InputPipeline.Tap(Keys.CapsLock);
                    break;
                case "system.media.next":
                    InputPipeline.Tap(Keys.MediaNextTrack);
                    break;
                case "system.media.prev":
                    InputPipeline.Tap(Keys.MediaPreviousTrack);
                    break;
                case "system.media.play":
                    InputPipeline.Tap(Keys.MediaPlayPause);
                    break;
                case "system.volume.up":
                    InputPipeline.Tap(Keys.VolumeUp);
                    break;
                case "system.volume.down":
                    InputPipeline.Tap(Keys.VolumeDown);
                    break;
                case "system.volume.mute":
                    InputPipeline.Tap(Keys.VolumeMute);
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
                    RunOnStaThread(delegate { InputPipeline.SendText(m.Param); });
                    break;
                case "input.keys":
                    SendCustomKeys(m.Param);
                    break;
                case "mouse.move":
                    MoveTo(m.Param);
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
            // All four steps go out as one stroke batch: the target application
            // consumes them in order, so the fixed sleeps that used to sit between
            // the calls (and raced with slow editors) are gone.
            List<InputStroke> strokes = new List<InputStroke>();
            strokes.Add(new InputStroke(Keys.Right, true));
            strokes.Add(new InputStroke(Keys.Right, false));
            AddCombo(strokes, Keys.Left, true, false, true, false);
            strokes.Add(new InputStroke(Keys.Left, true));
            strokes.Add(new InputStroke(Keys.Left, false));
            AddCombo(strokes, Keys.Right, true, false, true, false);
            InputPipeline.SendStrokes(strokes);
        }

        /// <summary>
        /// Appends one modifier chord (modifiers down, key down/up, modifiers up in
        /// reverse) so several chords can share one batch.
        /// </summary>
        private static void AddCombo(List<InputStroke> strokes, Keys key,
            bool ctrl, bool alt, bool shift, bool win)
        {
            if (ctrl) strokes.Add(new InputStroke(Keys.LControlKey, true));
            if (alt) strokes.Add(new InputStroke(Keys.LMenu, true));
            if (shift) strokes.Add(new InputStroke(Keys.LShiftKey, true));
            if (win) strokes.Add(new InputStroke(Keys.LWin, true));
            strokes.Add(new InputStroke(key, true));
            strokes.Add(new InputStroke(key, false));
            if (win) strokes.Add(new InputStroke(Keys.LWin, false));
            if (shift) strokes.Add(new InputStroke(Keys.LShiftKey, false));
            if (alt) strokes.Add(new InputStroke(Keys.LMenu, false));
            if (ctrl) strokes.Add(new InputStroke(Keys.LControlKey, false));
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
            // A mouse key is not a virtual key: it goes out as a real click or wheel
            // notch, with the combination's modifiers held around it.
            if (MouseKeys.IsMouseKey(key))
            {
                SendMouseKeyClick(key, ctrl, alt, shift, win);
                return;
            }
            InputPipeline.SendCombo(key, ctrl, alt, shift, win, 1);
        }

        /// <summary>
        /// Wheel delta for a notch key (+120 up, -120 down), or 0 when the key is not
        /// a wheel. <see cref="Native.MOUSEEVENTF_WHEEL"/> carries the delta, not a flag.
        /// </summary>
        internal static int MouseWheelDelta(Keys key)
        {
            if ((key & Keys.KeyCode) == (Keys)MouseKeys.WheelUp) return 120;
            if ((key & Keys.KeyCode) == (Keys)MouseKeys.WheelDown) return -120;
            return 0;
        }

        /// <summary>mouse_event flag for one mouse button, or 0 for anything else.</summary>
        internal static uint MouseButtonFlag(Keys key, bool up)
        {
            // Kept as the named mapping the self-test checks; the send path lives in
            // SendInputSink so driver injection can replace it.
            return SendInputSink.MouseFlag(key, up);
        }

        /// <summary>
        /// One mouse button press or release. A wheel notch has no up state: it fires
        /// a single notch on the down call and ignores the release. The active sink
        /// tags the event (or books it) so the mouse hook never reads it back as a
        /// Caps + 鼠标 trigger.
        /// </summary>
        private static bool SendMouseState(Keys key, bool up)
        {
            int delta = MouseWheelDelta(key);
            if (delta != 0)
            {
                if (up) return true;
                return InputPipeline.MouseWheel(delta);
            }
            return InputPipeline.MouseButton(key, up);
        }

        /// <summary>
        /// A complete mouse "key press": modifiers down, the button or notch, the
        /// button up, modifiers up. Same shape the 鼠标点击 action uses, minus the
        /// cursor move — this one clicks wherever the pointer already is.
        /// </summary>
        private void SendMouseKeyClick(Keys key, bool ctrl, bool alt, bool shift, bool win)
        {
            // Without modifiers the click has nothing to wait for, so it stays in
            // this turn and keeps its old, immediate timing.
            if (!(ctrl || alt || shift || win) || !CanDefer())
            {
                RunKeyClick(key, ctrl, alt, shift, win, false);
                return;
            }
            PressModifiers(ctrl, alt, shift, win);
            PendingMouse item = new PendingMouse();
            item.Kind = PendingKind.KeyClick;
            item.Key = key;
            item.Ctrl = ctrl;
            item.Alt = alt;
            item.Shift = shift;
            item.Win = win;
            QueuePending(item);
            QueueReleaseModifiers(ctrl, alt, shift, win, key, false, 0);
        }

        /// <summary>
        /// Modifiers, then the button or notch, then the modifiers again.
        /// <paramref name="modifiersDown"/> is set when the caller already pressed
        /// them in the previous message-loop turn.
        /// </summary>
        private void RunKeyClick(Keys key, bool ctrl, bool alt, bool shift, bool win,
            bool modifiersDown)
        {
            bool pressing = !modifiersDown && (ctrl || alt || shift || win)
                && PressModifiers(ctrl, alt, shift, win);
            if (pressing) Thread.Sleep(ModifierSettleMs);
            try
            {
                ClickButton(key);
            }
            finally
            {
                if (pressing) ReleaseModifiers(ctrl, alt, shift, win);
            }
        }

        /// <summary>
        /// One complete mouse press: a wheel notch fires once, with no release.
        /// Returns false when the sink refused a stroke, which is the only failure
        /// this layer can still observe after the click was sent.
        /// </summary>
        private static bool ClickButton(Keys key)
        {
            Point ignoredDown;
            Point ignoredUp;
            return ClickButton(key, out ignoredDown, out ignoredUp);
        }

        /// <summary>
        /// One complete mouse press, reporting where the cursor really was when the
        /// press and the release went out. Those two points are what the target ends up
        /// acting on, so a wrong click can be read from the log instead of guessed at.
        /// </summary>
        private static bool ClickButton(Keys key, out Point downAt, out Point upAt)
        {
            downAt = Cursor.Position;
            upAt = downAt;
            bool down = SendMouseState(key, false);
            if (MouseKeys.IsWheel(key)) return down;
            Thread.Sleep(ClickHoldMs);
            upAt = Cursor.Position;
            bool up = SendMouseState(key, true);
            // Hold back the next step for a beat: the release has only been *sent*.
            _clickQuietUntil = unchecked(Environment.TickCount + ClickQuietMs);
            return down && up;
        }

        /// <summary>
        /// Puts the cursor on the point and checks that it really is there, a bounded
        /// number of times. SetCursorPos can be overtaken by a mouse delta that the
        /// input stack had already accepted, and then the click would go out from
        /// wherever the hand left it -- so the position is verified immediately before
        /// the press instead of trusted after a single call.
        /// </summary>
        private static bool EnsureAt(MouseClickPoint point, out int corrections)
        {
            corrections = 0;
            for (int i = 0; i < 4; i++)
            {
                if (Native.SetCursorPos(point.X, point.Y)) break;
                corrections++;
            }
            for (int i = 0; i < 4; i++)
            {
                Point now = Cursor.Position;
                if (now.X == point.X && now.Y == point.Y) return true;
                corrections++;
                if (!Native.SetCursorPos(point.X, point.Y)) break;
            }
            Point last = Cursor.Position;
            if (last.X == point.X && last.Y == point.Y) return true;
            ConfigStore.Log("落位失败：目标 (" + point.X + ", " + point.Y + ")，实际 ("
                + last.X + ", " + last.Y + ")。");
            return false;
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

            // A mouse button is held like a key: down on press, up on release, no
            // auto-repeat. A wheel notch has no up state, so it only fires once.
            if (MouseKeys.IsMouseKey(key))
            {
                if (MouseKeys.IsWheel(key))
                {
                    SendMouseKeyClick(key, ctrl, alt, shift, win);
                    return false;
                }
                HeldKey mouse = new HeldKey();
                mouse.Mapping = mapping;
                mouse.Key = key;
                mouse.Ctrl = ctrl;
                mouse.Alt = alt;
                mouse.Shift = shift;
                mouse.Win = win;
                mouse.Mouse = true;
                _holds.Add(mouse);

                // A held button with modifiers follows the same rule as a click: the
                // modifiers go out now, the button in the next turn.
                if ((ctrl || alt || shift || win) && CanDefer())
                {
                    PressModifiers(ctrl, alt, shift, win);
                    PendingMouse press = new PendingMouse();
                    press.Kind = PendingKind.HoldDown;
                    press.Key = key;
                    QueuePending(press);
                    return true;
                }
                if (ctrl) InputPipeline.SendKeyState(Keys.LControlKey, false);
                if (alt) InputPipeline.SendKeyState(Keys.LMenu, false);
                if (shift) InputPipeline.SendKeyState(Keys.LShiftKey, false);
                if (win) InputPipeline.SendKeyState(Keys.LWin, false);
                SendMouseState(key, false);
                return true;
            }

            HeldKey hold = new HeldKey();
            hold.Mapping = mapping;
            hold.Key = key;
            hold.Ctrl = ctrl;
            hold.Alt = alt;
            hold.Shift = shift;
            hold.Win = win;
            _holds.Add(hold);

            if (ctrl) InputPipeline.SendKeyState(Keys.LControlKey, false);
            if (alt) InputPipeline.SendKeyState(Keys.LMenu, false);
            if (shift) InputPipeline.SendKeyState(Keys.LShiftKey, false);
            if (win) InputPipeline.SendKeyState(Keys.LWin, false);
            return InputPipeline.SendKeyState(key, false);
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
            // A held mouse button does not auto-repeat: re-sending the down event
            // would only spam the target with presses it already has.
            if (hold.Mouse) return false;
            return InputPipeline.SendKeyState(hold.Key, false);
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
            if (hold.Mouse)
            {
                // A deferred press may still be in the queue; posting the release
                // behind it keeps the order (the message queue is FIFO) and lets the
                // modifiers come up only after the button is up.
                PendingMouse up = new PendingMouse();
                up.Kind = PendingKind.HoldUp;
                up.Key = hold.Key;
                up.ReleaseButton = !sameKey;
                up.Win = hold.Win && !AnyHeld(false, false, false, true);
                up.Shift = hold.Shift && !AnyHeld(false, false, true, false);
                up.Alt = hold.Alt && !AnyHeld(false, true, false, false);
                up.Ctrl = hold.Ctrl && !AnyHeld(true, false, false, false);
                QueuePending(up);
                return;
            }

            if (!sameKey) InputPipeline.SendKeyState(hold.Key, true);

            if (hold.Win && !AnyHeld(false, false, false, true)) InputPipeline.SendKeyState(Keys.LWin, true);
            if (hold.Shift && !AnyHeld(false, false, true, false)) InputPipeline.SendKeyState(Keys.LShiftKey, true);
            if (hold.Alt && !AnyHeld(false, true, false, false)) InputPipeline.SendKeyState(Keys.LMenu, true);
            if (hold.Ctrl && !AnyHeld(true, false, false, false)) InputPipeline.SendKeyState(Keys.LControlKey, true);
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
        /// The 鼠标点击 action. A click that carries modifiers is split across two
        /// message-loop turns: the modifiers go out now, the mouse event in the next
        /// turn, so this thread's own low-level keyboard hook has a chance to hand
        /// them to the target first.
        /// </summary>
        private void ClickAt(string param)
        {
            MouseClickPoint point = MouseClickPoint.Parse(param);
            if (!point.HasModifiers)
            {
                DoClickAt(point);
                return;
            }
            if (!CanDefer())
            {
                // No message loop to hand over to (self-tests, a plugin thread):
                // fall back to the old blind pause.
                PressModifiers(point.Ctrl, point.Alt, point.Shift, point.Win);
                Thread.Sleep(ModifierSettleMs);
                try
                {
                    DoClickAt(point);
                }
                finally
                {
                    ReleaseModifiers(point.Ctrl, point.Alt, point.Shift, point.Win);
                }
                return;
            }
            // Pin the pointer before it is moved: the action spans several message turns
            // ("move now, click after the modifiers have landed"), and a hand moving the
            // mouse in any of those gaps would drag the cursor off the point.
            int pin = PointerPin.Pin(point.X, point.Y, ClickPinMs);
            int corrections = 0;
            if (!EnsureAt(point, out corrections))
            {
                PointerPin.Release(pin);
                PostStatus("无法移动到坐标 (" + point.X + ", " + point.Y + ")。", MInfoSeverity.Warning);
                return;
            }
            PressModifiers(point.Ctrl, point.Alt, point.Shift, point.Win);
            PendingMouse item = new PendingMouse();
            item.Kind = PendingKind.ClickAt;
            item.PressedAt = Environment.TickCount;
            item.Moved = true;
            item.Point = point;
            item.Ctrl = point.Ctrl;
            item.Alt = point.Alt;
            item.Shift = point.Shift;
            item.Win = point.Win;
            item.Key = MouseClickKey(point.Button);
            QueuePending(item);
            QueueReleaseModifiers(point.Ctrl, point.Alt, point.Shift, point.Win, item.Key, true, pin);
        }

        /// <summary>
        /// Queues the modifier release behind the click. It only runs once this app's
        /// mouse hook has handed the click's release on, so the whole press-and-release
        /// really happens with the modifiers down -- otherwise the target sees "Ctrl
        /// held, then a plain click", which is a different command.
        /// </summary>
        private void QueueReleaseModifiers(bool ctrl, bool alt, bool shift, bool win, Keys button,
            bool releasesPin, int pinGeneration)
        {
            PendingMouse release = new PendingMouse();
            release.Kind = PendingKind.ReleaseMods;
            release.Key = button;
            release.ReleasesPin = releasesPin;
            release.PinGeneration = pinGeneration;
            release.Ctrl = ctrl;
            release.Alt = alt;
            release.Shift = shift;
            release.Win = win;
            QueuePending(release);
        }

        private void DoClickAt(MouseClickPoint point)
        {
            DoClickAt(point, 0, false);
        }

        /// <summary>
        /// Moves the cursor to a virtual-desktop point and clicks the chosen button.
        /// <paramref name="pressedAt"/> is the tick the modifiers were sent, so the log
        /// can show how long they had been held when the click went out.
        /// </summary>
        /// <summary>
        /// Moves the cursor to a virtual-desktop point and clicks the chosen button,
        /// with no pause in between: a click is meant to land exactly where the cursor
        /// was put, and UI that only opens on hover is not what these coordinates
        /// target. A macro that needs a hover should move first (a separate step) and
        /// click afterwards, which also makes the hover time explicit.
        /// </summary>
        private void DoClickAt(MouseClickPoint point, int pressedAt, bool moved)
        {
            int pin = 0;
            int corrections = 0;
            if (moved)
            {
                // A deferred click was moved into place earlier; the press happens now,
                // so this is the moment the position has to be true again.
                EnsureAt(point, out corrections);
            }
            if (!moved)
            {
                // Pin first, then move: a hand moving the mouse between the two would
                // otherwise drag the cursor off the point before the click.
                pin = PointerPin.Pin(point.X, point.Y, ClickPinMs);
                if (!EnsureAt(point, out corrections))
                {
                    PointerPin.Release(pin);
                    PostStatus("无法移动到坐标 (" + point.X + ", " + point.Y + ")。", MInfoSeverity.Warning);
                    return;
                }
            }

            bool sent;
            Point downAt;
            Point upAt;
            try
            {
                // The active sink tags the click (or books it) so the low-level mouse
                // hook never turns it back into a Caps+鼠标 trigger.
                sent = ClickButton(MouseClickKey(point.Button), out downAt, out upAt);
            }
            catch
            {
                if (!moved) PointerPin.Release(pin);
                throw;
            }
            // Only the unusual clicks get a line of their own; the rest are counted and
            // summarised once per macro run. When a click lands wrong, these three cases
            // are what the log has to show.
            _clickCount++;
            bool movedDuringClick = downAt.X != upAt.X || downAt.Y != upAt.Y;
            if (movedDuringClick)
            {
                _clickDrift++;
                ConfigStore.Log("点击期间光标被移动：按下 (" + downAt.X + ", " + downAt.Y
                    + ") → 抬起 (" + upAt.X + ", " + upAt.Y + ")。");
            }
            if (corrections > 0) _clickCorrections++;
            if (!sent) _clickFailed++;
            if (corrections > 0)
            {
                ConfigStore.Log("落位修正 " + corrections + " 次后点击 " + MouseClickPoint.Format(point)
                    + "：按下 (" + downAt.X + ", " + downAt.Y + ")。");
            }
            if (!sent)
            {
                ConfigStore.Log("鼠标点击 " + MouseClickPoint.Format(point) + " 发送失败（"
                    + InputPipeline.BackendName + "）。");
            }
            if (pressedAt != 0)
            {
                int hold = unchecked(Environment.TickCount - pressedAt);
                if (hold < _holdMin) _holdMin = hold;
                if (hold > _holdMax) _holdMax = hold;
            }
            if (!moved)
            {
                // Keep the pin until the click's release has been seen by the mouse hook:
                // releasing right after sending it lets a moving hand drag the cursor in
                // the milliseconds before the target processes the up, and the target then
                // reads "pressed here, released there" -- measured as up to 4 px of drift.
                // The queued item runs behind the click, so the step still ends at the
                // right moment.
                QueueReleaseModifiers(false, false, false, false, MouseClickKey(point.Button), true, pin);
            }
        }

        /// <summary>
        /// The 鼠标移动 action: park the cursor, with no click and no pause. Hover time
        /// is the *next* step's delay, which keeps it visible in the macro instead of
        /// hidden inside a click.
        /// </summary>
        private void MoveTo(string param)
        {
            MouseClickPoint point = MouseClickPoint.Parse(param);
            // A move is usually a hover: the following steps expect the cursor to stay
            // put while the menu opens. Pin it for the step's delay (a lot longer than
            // a click needs), and the macro releases the pin when it ends.
            PointerPin.Pin(point.X, point.Y, MacroActive ? HoverPinMs : StandalonePinMs);
            if (!Native.SetCursorPos(point.X, point.Y))
            {
                PointerPin.Release();
                PostStatus("无法移动到坐标 (" + point.X + ", " + point.Y + ")。", MInfoSeverity.Warning);
                return;
            }
            // Silent on success: a move step is one of the steps the macro summary
            // counts, and the log is not a step recorder any more.
        }

        private static Keys MouseClickKey(MouseClickButton button)
        {
            if (button == MouseClickButton.Right) return (Keys)MouseKeys.Right;
            if (button == MouseClickButton.Middle) return (Keys)MouseKeys.Middle;
            return (Keys)MouseKeys.Left;
        }

        /// <summary>
        /// Modifiers go down in one batch before a mouse event. The caller either
        /// releases them in this turn (no message loop) or in the next one.
        /// </summary>
        private static bool PressModifiers(bool ctrl, bool alt, bool shift, bool win)
        {
            List<InputStroke> strokes = new List<InputStroke>();
            if (ctrl) strokes.Add(new InputStroke(Keys.LControlKey, true));
            if (alt) strokes.Add(new InputStroke(Keys.LMenu, true));
            if (shift) strokes.Add(new InputStroke(Keys.LShiftKey, true));
            if (win) strokes.Add(new InputStroke(Keys.LWin, true));
            if (strokes.Count == 0) return false;
            return InputPipeline.SendStrokes(strokes);
        }

        private static void ReleaseModifiers(bool ctrl, bool alt, bool shift, bool win)
        {
            List<InputStroke> strokes = new List<InputStroke>();
            if (win) strokes.Add(new InputStroke(Keys.LWin, false));
            if (shift) strokes.Add(new InputStroke(Keys.LShiftKey, false));
            if (alt) strokes.Add(new InputStroke(Keys.LMenu, false));
            if (ctrl) strokes.Add(new InputStroke(Keys.LControlKey, false));
            if (strokes.Count > 0) InputPipeline.SendStrokes(strokes);
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
            if (!InputPipeline.SendCombo(Keys.C, true, false, false, false, 1)) return "";
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

        /// <summary>Adds a mouse action that has to wait for its modifiers to land.</summary>
        private void QueuePending(PendingMouse item)
        {
            item.Deadline = unchecked(Environment.TickCount + DeliveryWindowMs);
            _pending.Add(item);
            RequestPendingWake();
        }

        /// <summary>
        /// Re-runs the pending queue on the next message turn. A WinForms timer cannot
        /// fire faster than the ~10 ms WM_TIMER floor, which used to make every wait for
        /// "the click has been delivered" cost a full timer period; a posted message
        /// runs immediately, and the 1 ms pause keeps it from spinning.
        /// </summary>
        private void RequestPendingWake()
        {
            if (_pendingWakePosted || _pending.Count == 0 || _disposed) return;
            Control anchor = Anchor;
            if (anchor == null || !anchor.IsHandleCreated || anchor.IsDisposed) return;
            _pendingWakePosted = true;
            try
            {
                anchor.BeginInvoke(new Action(delegate
                {
                    _pendingWakePosted = false;
                    if (_pending.Count == 0 || _disposed) return;
                    Native.SleepPrecise(1);
                    RunPending();
                    RequestPendingWake();
                }));
            }
            catch (InvalidOperationException)
            {
                _pendingWakePosted = false;
            }
        }

        /// <summary>
        /// Drains the pending queue. Runs only when the modifiers of the head entry have
        /// been delivered (or its window expired), so the click can never overtake them.
        /// </summary>
        private void RunPending()
        {
            if (_draining || _pending.Count == 0) return;
            _draining = true;
            bool idle = false;
            try
            {
                while (_pending.Count > 0)
                {
                    PendingMouse item = _pending[0];
                    if (item.Kind != PendingKind.HoldUp && !Delivered(item))
                    {
                        return;
                    }
                    _pending.RemoveAt(0);
                    try
                    {
                        RunPendingItem(item);
                    }
                    catch (Exception ex)
                    {
                        ConfigStore.Log("鼠标动作失败：" + ex);
                    }
                }
                idle = true;
            }
            finally
            {
                _draining = false;
                if (_pending.Count == 0)
                {
                    if (idle)
                    {
                        Action handler = Idle;
                        if (handler != null)
                        {
                            try { handler(); }
                            catch { }
                        }
                    }
                }
            }
        }

        /// <summary>True when the head entry's modifiers are already at the target.</summary>
        private static bool Delivered(PendingMouse item)
        {
            if (item.Kind == PendingKind.ReleaseMods)
            {
                // Keep the modifiers down for a quiet window after the click, then until
                // this app's own mouse hook has handed the click's release on, and then
                // one more beat so the target acts on it. Waiting here (not sleeping)
                // keeps the hook thread free.
                if (!InputPipeline.TracksDelivery)
                {
                    // SendInput keeps the order in one queue, so a short quiet window
                    // is all this needs.
                    return unchecked(Environment.TickCount - _lastClickAt) >= ReleaseWindowMs;
                }
                if (SyntheticLedger.PendingStrokes(item.Key, false) > 0)
                    return unchecked(Environment.TickCount - item.Deadline) >= 0;
                if (item.ClaimedAt == 0) item.ClaimedAt = Environment.TickCount;
                return unchecked(Environment.TickCount - item.ClaimedAt) >= ReleaseMarginMs;
            }
            if (!InputPipeline.TracksDelivery) return true;
            if (SyntheticLedger.PendingCount == 0) return true;
            return unchecked(Environment.TickCount - item.Deadline) >= 0;
        }

        private void RunPendingItem(PendingMouse item)
        {
            switch (item.Kind)
            {
                case PendingKind.ClickAt:
                    DoClickAt(item.Point, item.PressedAt, item.Moved);
                    _lastClickAt = Environment.TickCount;
                    break;
                case PendingKind.KeyClick:
                    ClickButton(item.Key);
                    _lastClickAt = Environment.TickCount;
                    break;
                case PendingKind.ReleaseMods:
                    try
                    {
                        ReleaseModifiers(item.Ctrl, item.Alt, item.Shift, item.Win);
                    }
                    finally
                    {
                        // The click is over (modifiers came up after its release was
                        // seen), so the pointer may move again.
                        if (item.ReleasesPin) PointerPin.Release(item.PinGeneration);
                    }
                    break;
                case PendingKind.HoldDown:
                    SendMouseState(item.Key, false);
                    break;
                case PendingKind.HoldUp:
                    if (item.ReleaseButton) SendMouseState(item.Key, true);
                    if (item.Win) InputPipeline.SendKeyState(Keys.LWin, true);
                    if (item.Shift) InputPipeline.SendKeyState(Keys.LShiftKey, true);
                    if (item.Alt) InputPipeline.SendKeyState(Keys.LMenu, true);
                    if (item.Ctrl) InputPipeline.SendKeyState(Keys.LControlKey, true);
                    break;
            }
        }

        /// <summary>True when there is a message loop to hand a turn to.</summary>
        private bool CanDefer()
        {
            Control target = Anchor;
            return !_shuttingDown && target != null && target.IsHandleCreated && !target.IsDisposed;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _shuttingDown = true;
            // Release holds and flush whatever was queued before the loop goes away,
            // so a mouse button is never left down.
            EndKeyHold(null);
            RunPending();
            _timer.Stop();
            _timer.Dispose();
        }
    }
}