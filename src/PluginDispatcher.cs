using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// Runs plugin actions on one dedicated STA thread with a message loop. The
    /// low-level keyboard hook and the main window both live on the UI thread, so
    /// a plugin that sleeps, waits on the clipboard, opens a modal window or
    /// blocks on a network path must not run there. A dedicated STA thread still
    /// supports WinForms and Clipboard, and plugin reports are marshalled back to
    /// the main window through ISynchronizeInvoke.
    /// </summary>
    internal sealed class PluginDispatcher : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Queue<Action> _queue = new Queue<Action>();
        private readonly ManualResetEvent _ready = new ManualResetEvent(false);
        private const int MaxQueueLength = 64;
        private readonly Thread _thread;
        private Control _anchor;
        private bool _pumping;
        private bool _disposed;

        public PluginDispatcher()
        {
            _thread = new Thread(Run);
            _thread.IsBackground = true;
            _thread.Name = "CapsLockLite.PluginUI";
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.WaitOne(5000);
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
                ConfigStore.Log("插件 UI 线程启动失败：" + ex);
                _ready.Set();
            }
        }

        /// <summary>Queues an action. Returns false when the thread is gone.</summary>
        public bool Post(Action action)
        {
            if (action == null) return false;
            lock (_gate)
            {
                if (_disposed) return false;
                if (_queue.Count >= MaxQueueLength) return false;
                _queue.Enqueue(action);
                if (_pumping) return true;
                _pumping = true;
            }

            try
            {
                Control anchor = _anchor;
                if (anchor == null)
                {
                    lock (_gate) { _pumping = false; }
                    return false;
                }
                anchor.BeginInvoke(new Action(Pump));
                return true;
            }
            catch (Exception ex)
            {
                ConfigStore.Log("提交插件动作失败：" + ex.Message);
                lock (_gate) { _pumping = false; }
                return false;
            }
        }

        private void Pump()
        {
            while (true)
            {
                Action action;
                lock (_gate)
                {
                    if (_disposed || _queue.Count == 0)
                    {
                        _pumping = false;
                        return;
                    }
                    action = _queue.Dequeue();
                }

                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    ConfigStore.Log("插件动作失败：" + ex);
                }
            }
        }

        public void Dispose()
        {
            bool first;
            lock (_gate)
            {
                first = !_disposed;
                _disposed = true;
                _queue.Clear();
            }
            if (!first) return;

            Control anchor = _anchor;
            if (anchor != null)
            {
                try
                {
                    anchor.BeginInvoke(new Action(delegate { Application.ExitThread(); }));
                }
                catch { }
            }

            if (_thread != null && _thread.IsAlive && Thread.CurrentThread != _thread)
            {
                try { _thread.Join(2000); }
                catch { }
            }
        }
    }
}
