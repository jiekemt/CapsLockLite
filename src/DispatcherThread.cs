using System;
using System.Threading;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// An STA thread with its own WinForms message loop and an anchor control that
    /// work can be marshalled onto. Two of them exist: one owns the low-level hooks
    /// (a hook callback runs on the thread that installed the hook, and the system
    /// waits for it, so it must never be the UI thread), one owns input injection and
    /// macro scheduling (so that a busy UI can neither delay a stroke nor a step).
    /// </summary>
    internal sealed class DispatcherThread : IDisposable
    {
        private readonly string _name;
        private readonly Thread _thread;
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private Control _anchor;
        private bool _disposed;

        public DispatcherThread(string name)
        {
            _name = string.IsNullOrEmpty(name) ? "CapsLockLite.Worker" : name;
            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = _name;
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.WaitOne(5000);
        }

        /// <summary>The anchor control, created on (and owned by) that thread.</summary>
        public Control Anchor
        {
            get { return _anchor; }
        }

        /// <summary>True when the thread created its anchor and pumps messages.</summary>
        public bool IsReady
        {
            get { return _anchor != null; }
        }

        public bool IsOwnThread
        {
            get { return Thread.CurrentThread == _thread; }
        }

        private void Run()
        {
            try
            {
                _anchor = new Control();
                IntPtr handle = _anchor.Handle;   // force handle creation on this thread
                GC.KeepAlive(handle);
                _ready.Set();
                Application.Run(new ApplicationContext());
            }
            catch (Exception ex)
            {
                ConfigStore.Log("钩子线程启动失败：" + ex);
                _ready.Set();
            }
        }

        /// <summary>Queues work on the hook thread; returns immediately.</summary>
        public bool Post(Action work)
        {
            if (work == null) return false;
            Control anchor = _anchor;
            if (_disposed || anchor == null)
            {
                work();     // no thread: run here rather than losing the work
                return false;
            }
            try
            {
                anchor.BeginInvoke(work);
                return true;
            }
            catch (Exception ex)
            {
                ConfigStore.Log("提交钩子线程任务失败：" + ex.Message);
                return false;
            }
        }

        /// <summary>Runs work on the hook thread and waits for it to finish.</summary>
        public bool Send(Action work)
        {
            if (work == null) return false;
            Control anchor = _anchor;
            if (_disposed || anchor == null || IsOwnThread)
            {
                work();
                return true;
            }
            try
            {
                anchor.Invoke(work);
                return true;
            }
            catch (Exception ex)
            {
                ConfigStore.Log("钩子线程调用失败：" + ex.Message);
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Control anchor = _anchor;
            if (anchor != null)
            {
                try { anchor.BeginInvoke(new Action(delegate { Application.ExitThread(); })); }
                catch { }
            }
            if (_thread.IsAlive && !IsOwnThread)
            {
                try { _thread.Join(2000); }
                catch { }
            }
        }
    }
}
