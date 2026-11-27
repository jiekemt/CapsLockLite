using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Thread = System.Threading.Thread;

namespace CapsLockLite
{
    /// <summary>
    /// Runs a macro chain on the UI thread. Each step is handed to the existing
    /// ActionExecutor so a macro behaves exactly like a single bound shortcut,
    /// and nested macros are pushed on a small frame stack instead of being
    /// expanded up front. That keeps the runtime cost predictable no matter how
    /// many times a nested macro repeats.
    /// </summary>
    internal sealed class MacroRunner : IDisposable
    {
        private const int MaxExecutedSteps = 10000;
        private const int MaxFrames = 16;

        private sealed class Frame
        {
            public Macro Macro;
            public int Index;
            public int Remaining;
            public long ReadyAt;
            public int DelayAfterChild;
            public bool AwaitingChild;
            /// <summary>Set while a step's deferred work (a click) is still running.</summary>
            public bool WaitingStep;
            /// <summary>Delay to apply once that work has finished.</summary>
            public int PendingDelay;
        }

        private readonly ActionExecutor _executor;
        private readonly IAppHost _host;
        private readonly Func<IList<Macro>> _macros;
        private readonly Timer _timer;
        private readonly List<Frame> _frames = new List<Frame>();
        private long _executed;
        private bool _disposed;
        // Per-step timing, written to the log so a slow macro can be read instead of
        // guessed at. One line per step, at millisecond resolution.
        private long _stepStartedAt;
        private string _stepLabel;
        private long _runStartedAt;
        private int _steps;
        private long _slowestMs;
        private string _slowestLabel;

        /// <summary>A step this slow is worth its own line; anything else is summarised.</summary>
        private const int SlowStepMs = 150;
        private bool _wakePosted;
        // A WinForms timer cannot go below the system timer resolution (~15.6 ms), and
        // the executor's pending queue is drained by one too. Asking for a 1 ms
        // resolution while a macro runs turns every "re-check in a millisecond" into an
        // actual millisecond: measured, a key step drops from 15 ms to ~5 ms.

        private bool _restorePending;
        private Point _restorePoint;

        public event EventHandler StateChanged;

        public MacroRunner(ActionExecutor executor, IAppHost host, Func<IList<Macro>> macros)
        {
            _executor = executor;
            _host = host;
            _macros = macros;
            _timer = new Timer();
            _timer.Interval = 15;
            _timer.Tick += OnTick;
        }

        public bool IsRunning
        {
            get { return _frames.Count > 0; }
        }

        public string RunningName { get; private set; }

        /// <summary>Starts the macro behind "macro.&lt;id&gt;". Returns false + reason on error.</summary>
        public bool Start(string macroActionId, out string error)
        {
            error = null;
            // One macro at a time: a second trigger while a run is in flight is
            // ignored instead of stopping (or stacking on) the current one.
            if (IsRunning)
            {
                error = "宏“" + (RunningName ?? "当前宏") + "”正在运行，结束后才能触发下一个宏。";
                return false;
            }
            IList<Macro> list = _macros == null ? null : _macros();
            Macro macro = MacroLibrary.FindByActionId(list, macroActionId);
            if (macro == null)
            {
                error = "宏不存在，可能已被删除。";
                return false;
            }
            error = MacroLibrary.ValidateNesting(list, macro);
            if (error != null) return false;
            if (macro.Steps == null || macro.Steps.Count == 0)
            {
                error = "这个宏还没有任何动作。";
                return false;
            }

            // Remember where the pointer was before the first step runs; the option is
            // per macro and off by default.
            _restorePending = macro.RestoreCursor;
            if (_restorePending) _restorePoint = Cursor.Position;

            Frame frame = new Frame();
            frame.Macro = macro;
            frame.ReadyAt = Environment.TickCount;
            _frames.Add(frame);
            _executed = 0;
            RunningName = macro.Name;
            // The whole run keeps the pointer where the last pointer action put it.
            PointerPin.BeginMacro();
            if (_executor != null) _executor.MacroActive = true;
            // Fresh counters for this run: they end up in one summary line.
            _steps = 0;
            _slowestMs = 0;
            _slowestLabel = null;
            PointerPin.TakeBlocked();
            if (_executor != null) _executor.ResetClickStats();
            _timer.Start();
            RaiseState();
            return true;
        }

        public void Stop()
        {
            Stop(null, true);
        }

        private void Stop(string reason, bool notify)
        {
            bool wasRunning = _frames.Count > 0;
            _frames.Clear();
            _timer.Stop();
            if (wasRunning && _runStartedAt != 0)
            {
                ConfigStore.Log(RunSummary(RunningName ?? "宏") + "（手动停止）");
                _runStartedAt = 0;
            }
            _steps = 0;
            _slowestMs = 0;
            _slowestLabel = null;
            RunningName = null;
            // Drop the pointer pin before anything else moves the cursor back.
            PointerPin.Release();
            if (_executor != null) _executor.MacroActive = false;
            if (!wasRunning) return;
            RestorePointer();
            RaiseState();
            if (notify && reason != null && _host != null)
                _host.ShowStatus(reason, MInfoSeverity.Info);
        }

        /// <summary>
        /// Puts the cursor back once the run ends, however it ends (finished,
        /// stopped by the user, or aborted by the step cap).
        /// </summary>
        private void RestorePointer()
        {
            if (!_restorePending) return;
            _restorePending = false;
            Native.SetCursorPos(_restorePoint.X, _restorePoint.Y);
        }

        /// <summary>
        /// Runs the next step now instead of at the next 15 ms tick. The executor
        /// raises this as soon as a deferred mouse action has finished.
        /// </summary>
        internal void Wake()
        {
            if (_disposed) return;
            OnTick(null, EventArgs.Empty);
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (_frames.Count == 0)
            {
                _timer.Stop();
                return;
            }
            long now = Environment.TickCount;
            int guard = 0;
            while (_frames.Count > 0 && guard++ < 8)
            {
                Frame frame = _frames[_frames.Count - 1];

                // A step that deferred part of its work (a click with modifiers) is not
                // done yet. Neither the next step nor the end of the macro may run
                // before it lands: restoring the pointer under a click that has not
                // happened yet was exactly the "last click applies at the very end"
                // bug, and the delay has to start counting from the real end of the
                // step, not from the moment it was handed over.
                if (frame.WaitingStep)
                {
                    if (_executor != null && _executor.Busy)
                    {
                        // The step's strokes are still in flight. A timer tick would
                        // cost 10-16 ms (WM_TIMER floor), so the re-check is posted.
                        RequestFastWake();
                        return;
                    }
                    frame.WaitingStep = false;
                    frame.ReadyAt = now + frame.PendingDelay;
                    if (_stepLabel != null)
                    {
                        long took = Elapsed(_stepStartedAt);
                        _steps++;
                        if (took > _slowestMs)
                        {
                            _slowestMs = took;
                            _slowestLabel = _stepLabel;
                        }
                        // A normal step is a millisecond or two; only the slow ones are
                        // worth a line of their own, the timings are summarised at the end.
                        if (took >= SlowStepMs)
                        {
                            ConfigStore.Log("宏步骤 " + _stepLabel + " 用时 " + took + "ms（偏慢）");
                        }
                        _stepLabel = null;
                    }
                }

                if (frame.Index >= frame.Macro.Steps.Count)
                {
                    _frames.RemoveAt(_frames.Count - 1);
                    if (_frames.Count > 0)
                    {
                        Frame parent = _frames[_frames.Count - 1];
                        parent.AwaitingChild = false;
                        parent.ReadyAt = now + parent.DelayAfterChild;
                    }
                    else
                    {
                        _timer.Stop();
                        SetTick(15);
                        // Release the pin *before* the pointer goes back, otherwise the
                        // restore move itself would be swallowed by our own pin.
                        PointerPin.Release();
                        if (_executor != null) _executor.MacroActive = false;
                        string finishedName = RunningName;
                        RunningName = null;
                        RestorePointer();
                        RaiseState();
                        if (_runStartedAt != 0)
                        {
                            ConfigStore.Log(RunSummary(finishedName));
                            _runStartedAt = 0;
                            _steps = 0;
                            _slowestMs = 0;
                            _slowestLabel = null;
                        }
                        if (_host != null) _host.ShowStatus("宏执行完毕。", MInfoSeverity.Success);
                        return;
                    }
                    continue;
                }
                if (_executed >= MaxExecutedSteps)
                {
                    Stop("宏执行的步数过多，已自动停止。", true);
                    return;
                }
                if (now < frame.ReadyAt)
                {
                    int remaining = (int)(frame.ReadyAt - now);
                    SetTick(remaining > 15 ? 15 : (remaining < 1 ? 1 : remaining));
                    return;
                }

                MacroStep step = frame.Macro.Steps[frame.Index];
                if (step == null)
                {
                    frame.Index++;
                    continue;
                }
                if (frame.Remaining <= 0) frame.Remaining = Math.Max(1, step.Count);

                if (ActionCatalog.IsMacroActionId(step.Action))
                {
                    IList<Macro> list = _macros == null ? null : _macros();
                    Macro child = MacroLibrary.FindByActionId(list, step.Action);
                    if (child == null)
                    {
                        if (_host != null) _host.ShowStatus("宏动作不存在，已跳过这一步。", MInfoSeverity.Error);
                        frame.Index++;
                        frame.Remaining = 0;
                        frame.ReadyAt = now + Math.Max(0, step.DelayMs);
                        continue;
                    }
                    if (_frames.Count >= MaxFrames || IsInStack(child.Id))
                    {
                        Stop("宏的嵌套出现循环或层数过深，已停止。", true);
                        return;
                    }
                    frame.Remaining--;
                    if (frame.Remaining <= 0)
                    {
                        frame.Index++;
                        frame.Remaining = 0;
                    }
                    frame.AwaitingChild = true;
                    frame.DelayAfterChild = Math.Max(0, step.DelayMs);
                    Frame nested = new Frame();
                    nested.Macro = child;
                    nested.ReadyAt = now;
                    _frames.Add(nested);
                    continue;
                }

                _executed++;
                if (_runStartedAt == 0) _runStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                _stepStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                _stepLabel = (frame.Index + 1) + "/" + frame.Macro.Steps.Count + " "
                    + step.Action + DescribeParam(step.Param);
                if (_executor != null) _executor.ExecuteAction(step.Action, step.Param);
                frame.WaitingStep = true;
                frame.PendingDelay = Math.Max(0, step.DelayMs);
                frame.Remaining--;
                if (frame.Remaining <= 0)
                {
                    frame.Index++;
                    frame.Remaining = 0;
                }
            }
        }

        /// <summary>Milliseconds since a <see cref="System.Diagnostics.Stopwatch"/> tick.</summary>
        private static long Elapsed(long startedAt)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            return (now - startedAt) * 1000 / System.Diagnostics.Stopwatch.Frequency;
        }

        /// <summary>Short param text for the log, never a whole coordinate list.</summary>
        private static string DescribeParam(string param)
        {
            if (string.IsNullOrEmpty(param)) return "";
            if (param.Length <= 24) return " " + param;
            return " " + param.Substring(0, 24) + "…";
        }

        /// <summary>
        /// One line for a whole run: how long it took, what it did, how fast the steps
        /// were, and the few numbers that matter when a macro misbehaves. Per-step and
        /// per-click lines were written for every run before; that is what made the log
        /// grow to megabytes, and a large log is slow to append to (antivirus re-reads
        /// it), which then disturbed the very macro it was recording.
        /// </summary>
        private string RunSummary(string name)
        {
            StringBuilder text = new StringBuilder();
            text.Append("宏完成 “").Append(name ?? "宏").Append("” 总用时 ")
                .Append(Elapsed(_runStartedAt)).Append("ms，步骤 ").Append(_steps);
            if (_slowestMs > 0)
            {
                text.Append("，最慢 ").Append(_slowestLabel ?? "?").Append(" ").Append(_slowestMs).Append("ms");
            }
            string clicks = _executor == null ? "" : _executor.ClickStatsText();
            if (clicks.Length > 0) text.Append("，").Append(clicks);
            int blocked = PointerPin.TakeBlocked();
            if (blocked > 0) text.Append("，指针拦截 ").Append(blocked).Append(" 次鼠标移动");
            return text.ToString();
        }

        /// <summary>Adjusts the tick so a busy wait costs ~1 ms, not a full timer period.</summary>
        /// <summary>Re-runs a tick on the next message turn, ~1 ms later, not 10-16 ms.</summary>
        private void RequestFastWake()
        {
            if (_wakePosted || _disposed) return;
            ActionExecutor executor = _executor;
            if (executor == null) return;
            _wakePosted = true;
            executor.Post(delegate
            {
                _wakePosted = false;
                if (_disposed || _frames.Count == 0) return;
                Native.SleepPrecise(1);
                OnTick(null, EventArgs.Empty);
            });
        }

        private void SetTick(int milliseconds)
        {
            if (milliseconds < 1) milliseconds = 1;
            if (_timer.Interval == milliseconds) return;
            _timer.Interval = milliseconds;
        }

        private bool IsInStack(string macroId)
        {
            for (int i = 0; i < _frames.Count; i++)
            {
                Macro m = _frames[i].Macro;
                if (m != null && string.Equals(m.Id, macroId, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void RaiseState()
        {
            EventHandler handler = StateChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _frames.Clear();
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer.Dispose();
        }
    }
}
