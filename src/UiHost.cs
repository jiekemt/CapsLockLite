using System;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// Wraps the main window as an <see cref="IAppHost"/> so that callers living on the
    /// input thread (actions, macros) can report status, open the window or show a
    /// dialog without touching the UI thread directly. Every call is marshalled; when
    /// it already runs on the UI thread it is invoked inline so dialogs stay modal to
    /// the caller.
    /// </summary>
    internal sealed class UiHost : IAppHost
    {
        private readonly IAppHost _inner;
        private readonly Control _target;

        public UiHost(IAppHost inner, Control target)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            _inner = inner;
            _target = target;
        }

        private void Post(Action work)
        {
            if (work == null) return;
            if (_target != null && !_target.IsDisposed && _target.IsHandleCreated
                && _target.InvokeRequired)
            {
                try
                {
                    _target.BeginInvoke(work);
                    return;
                }
                catch (InvalidOperationException) { }
            }
            work();
        }

        private void Send(Action work)
        {
            if (work == null) return;
            if (_target != null && !_target.IsDisposed && _target.IsHandleCreated
                && _target.InvokeRequired)
            {
                try
                {
                    _target.Invoke(work);
                    return;
                }
                catch (InvalidOperationException) { }
            }
            work();
        }

        public void ShowStatus(string message, MInfoSeverity severity)
        {
            Post(delegate { _inner.ShowStatus(message, severity); });
        }

        public void ShowStatus(string message, bool error)
        {
            Post(delegate { _inner.ShowStatus(message, error); });
        }

        public void ShowEscapeResult(string escaped, string original)
        {
            // A dialog is modal to its caller: marshal synchronously so the action
            // finishes only after the user closed it.
            Send(delegate { _inner.ShowEscapeResult(escaped, original); });
        }

        public void OpenHelp()
        {
            Send(delegate { _inner.OpenHelp(); });
        }

        public void OpenCalculator()
        {
            Post(delegate { _inner.OpenCalculator(); });
        }

        public void ShowMainWindow()
        {
            Post(delegate { _inner.ShowMainWindow(); });
        }

        public void ReloadFromDisk()
        {
            Send(delegate { _inner.ReloadFromDisk(); });
        }
    }
}
