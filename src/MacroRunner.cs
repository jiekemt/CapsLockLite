using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

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
        }

        private readonly ActionExecutor _executor;
        private readonly IAppHost _host;
        private readonly Func<IList<Macro>> _macros;
        private readonly Timer _timer;
        private readonly List<Frame> _frames = new List<Frame>();
        private long _executed;
        private bool _disposed;
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
            RunningName = null;
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
                        RunningName = null;
                        RestorePointer();
                        RaiseState();
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
                if (now < frame.ReadyAt) return;

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
                if (_executor != null) _executor.ExecuteAction(step.Action, step.Param);
                frame.Remaining--;
                frame.ReadyAt = now + Math.Max(0, step.DelayMs);
                if (frame.Remaining <= 0)
                {
                    frame.Index++;
                    frame.Remaining = 0;
                }
            }
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
