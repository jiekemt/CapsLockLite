using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapsLockLite
{
    internal sealed class KeyboardEventArgs : EventArgs
    {
        public Keys Key { get; private set; }
        public bool IsDown { get; private set; }
        public bool IsUp { get; private set; }
        public bool IsInjected { get; private set; }
        public uint ScanCode { get; private set; }

        public KeyboardEventArgs(Keys key, bool isDown, bool isUp, bool injected, uint scanCode)
        {
            Key = key;
            IsDown = isDown;
            IsUp = isUp;
            IsInjected = injected;
            ScanCode = scanCode;
        }
    }

    internal sealed class KeyboardHook : IDisposable
    {
        private Native.LowLevelKeyboardProc _proc;
        private IntPtr _hook = IntPtr.Zero;

        public event Func<KeyboardEventArgs, bool> KeyEvent;
        public bool ProcessInjected { get; set; }
        public int LastError { get; private set; }

        public bool IsInstalled
        {
            get { return _hook != IntPtr.Zero; }
        }

        public bool Install()
        {
            if (_hook != IntPtr.Zero) return true;
            _proc = HookCallback;
            IntPtr module = Native.GetModuleHandle(null);
            _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, module, 0);
            if (_hook == IntPtr.Zero)
            {
                LastError = Marshal.GetLastWin32Error();
                ConfigStore.Log("安装键盘钩子失败，Win32 错误码：" + LastError);
                return false;
            }
            return true;
        }

        public void Uninstall()
        {
            if (_hook != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && KeyEvent != null)
                {
                    Native.KBDLLHOOKSTRUCT data = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    bool injected = (data.flags & Native.LLKHF_INJECTED) != 0;
                    bool ourOwn = injected && data.dwExtraInfo == Native.SyntheticMarker;
                    if (!ourOwn && (!injected || ProcessInjected))
                    {
                        int msg = wParam.ToInt32();
                        bool down = msg == Native.WM_KEYDOWN || msg == Native.WM_SYSKEYDOWN;
                        bool up = msg == Native.WM_KEYUP || msg == Native.WM_SYSKEYUP;
                        KeyboardEventArgs args = new KeyboardEventArgs((Keys)data.vkCode, down, up, injected, data.scanCode);
                        if (KeyEvent(args))
                            return new IntPtr(1);
                    }
                }
            }
            catch (Exception ex)
            {
                ConfigStore.Log("键盘钩子回调异常：" + ex);
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Uninstall();
            _proc = null;
        }
    }

    internal sealed class HotkeyManager : IDisposable
    {
        private readonly KeyboardHook _hook = new KeyboardHook();
        private readonly MouseHook _mouseHook = new MouseHook();
        private readonly HashSet<Keys> _physicalModifiers = new HashSet<Keys>();
        private readonly HashSet<Keys> _swallowedModifiers = new HashSet<Keys>();
        private readonly HashSet<Keys> _downKeys = new HashSet<Keys>();
        private readonly HashSet<Keys> _swallowedKeys = new HashSet<Keys>();
        private readonly HashSet<Keys> _heldBeforeCaps = new HashSet<Keys>();
        private readonly HashSet<Keys> _suppressedModifiers = new HashSet<Keys>();
        // Mouse buttons the Caps layer already consumed. Their up event must be
        // swallowed too, otherwise the target sees an unmatched button release.
        private readonly HashSet<Keys> _swallowedMouseButtons = new HashSet<Keys>();
        // Wheel messages arrive in arbitrary deltas (a precision touchpad sends
        // many small ones); accumulate until a whole 120 notch is reached so one
        // gesture fires one action instead of dozens.
        private int _wheelAccum;
        private int _wheelSign;
        /// <summary>Sanity bound for a stuck device, not a speed limit.</summary>
        private const int MaxNotchesPerMessage = 50;
        private DateTime _wheelAt;
        // Chord keys that are physically down. Every one of them keeps its key
        // down on the target's behalf (a real keyboard never releases a key just
        // because another one was pressed), while only the newest one owns the
        // auto-repeat. This is what lets a game keep "forward" held while the
        // player taps "left" and "right".
        private sealed class ChordEntry
        {
            public Keys Key;
            public KeyMapping Mapping;
            public bool HeldOnTarget;
        }

        private readonly List<ChordEntry> _chordEntries = new List<ChordEntry>();
        private Timer _repeatTimer;
        private KeyMapping _repeatMapping;
        private Keys _repeatKey;
        private bool _repeatGlobal;
        private readonly System.Diagnostics.Stopwatch _repeatClock = new System.Diagnostics.Stopwatch();
        private long _repeatNextMs;
        private bool _repeatPrecise;
        private bool _repeatPhysical;
        private const int RepeatDelayMs = 450;
        private const int RepeatIntervalMs = 33;
        private AppSettings _settings;
        private IntPtr _owner;
        private bool _capsDown;
        private bool _capsChordUsed;
        private DateTime _capsDownAt;
        private bool _running;
        private bool _pausedForDialog;
        private string _lastIssueReport = "";
        private bool _mouseHookReported;
        private volatile bool _processInjected;
        private DispatcherThread _thread;
        private bool _disposed;

        /// <summary>
        /// Marschal to the UI thread for the few things a hook callback cannot do
        /// itself (the coordinate picker opens/closes windows). Set by the form.
        /// </summary>
        internal Action<Action> UiPost;

        private DispatcherThread Thread
        {
            get
            {
                if (_thread == null) _thread = new DispatcherThread("CapsLockLite.Hooks");
                return _thread;
            }
        }

        /// <summary>
        /// The timer needs a message loop, so it is created on the hook thread the
        /// first time a repeat starts rather than in the constructor.
        /// </summary>
        private Timer RepeatTimer
        {
            get
            {
                if (_repeatTimer == null)
                {
                    _repeatTimer = new Timer();
                    _repeatTimer.Interval = RepeatDelayMs;
                    _repeatTimer.Tick += RepeatTick;
                }
                return _repeatTimer;
            }
        }

        private void PostToUi(Action work)
        {
            Action<Action> post = UiPost;
            if (post != null)
            {
                post(work);
                return;
            }
            work();
        }

        public event Action<KeyMapping> ActionTriggered;
        public event Action<string, MInfoSeverity> StatusChanged;

        /// <summary>
        /// input.keys in hold mode: one key-down at press, repeated key-downs for
        /// auto-repeat, one key-up at release. Other repeatable actions keep firing
        /// discrete executions through ActionTriggered.
        /// </summary>
        public event Action<KeyMapping> HoldStarted;
        public event Action<KeyMapping> HoldRepeated;
        public event Action<KeyMapping> HoldEnded;

        public bool IsRunning { get { return _running; } }
        public bool ProcessInjected
        {
            get { return _processInjected; }
            set
            {
                _processInjected = value;
                if (_thread == null) return;
                _thread.Post(delegate { _hook.ProcessInjected = value; });
            }
        }

        public HotkeyManager(AppSettings settings, IntPtr owner)
        {
            _settings = settings;
            _owner = owner;
            _hook.KeyEvent += OnHookKey;
            _mouseHook.MouseEvent += OnHookMouse;
        }

        /// <summary>
        /// Sends one key transition to the system. Replaced in self-tests so a
        /// simulated sequence never types into the real desktop.
        /// </summary>
        internal Action<Keys, bool> SendKeyState = delegate(Keys key, bool up) { InputPipeline.SendKeyState(key, up); };

        /// <summary>
        /// Modifiers pressed before CapsLock were already delivered to the target
        /// window. Once the Caps layer owns the keyboard they must stop acting on
        /// it: a Caps+Alt+Q binding that sends F4 would otherwise reach the target
        /// as Alt+F4 and close it. Synthesize an up for each such modifier and
        /// remember it so the matching physical key-up is swallowed instead of
        /// sending a second up.
        /// </summary>
        private void SuppressHeldModifiers()
        {
            foreach (Keys key in _physicalModifiers)
            {
                // Modifiers pressed while Caps was already down were never passed
                // on to the target, so they have nothing to release.
                if (_swallowedModifiers.Contains(key)) continue;
                if (!_suppressedModifiers.Add(key)) continue;
                SendKeyState(key, true);
            }
        }

        /// <summary>
        /// Puts a modifier that is still physically held back into the target now
        /// that the Caps layer is done with it. Modifiers released during the
        /// chord were already dropped from the set by HandleModifier, so their
        /// key-up is the only thing the target has to see.
        /// </summary>
        private void RestoreSuppressedModifiers()
        {
            foreach (Keys key in _suppressedModifiers)
            {
                // The physical key-up is what removes the entry, so a key still in
                // the set is still held by the user even though GetAsyncKeyState
                // reports the up we synthesized for the target.
                if (!_physicalModifiers.Contains(key)) continue;
                SendKeyState(key, false);
            }
            _suppressedModifiers.Clear();
        }

        public void UpdateSettings(AppSettings settings)
        {
            // A reference assignment is atomic; the hook thread reads the newest
            // settings on its next event, which is what the layer needs.
            _settings = settings;
        }

        public void UpdateOwner(IntPtr owner)
        {
            _owner = owner;
        }

        /// <summary>
        /// Installs the hooks on the hook thread and waits until they are in place.
        /// Everything below this line runs on that thread.
        /// </summary>
        public bool Start()
        {
            if (_settings == null || !_settings.Enabled) return false;
            bool started = false;
            Thread.Send(delegate { started = StartCore(); });
            return started;
        }

        private bool StartCore()
        {
            if (_running) return true;
            if (_settings == null || !_settings.Enabled) return false;
            _hook.ProcessInjected = _processInjected;
            ResetState();

            if (!_hook.Install())
            {
                RaiseStatus("键盘钩子安装失败。请尝试以管理员身份运行，或检查安全软件。",
                    MInfoSeverity.Error);
                return false;
            }

            // The mouse hook stays installed for as long as the layer runs. It used to
            // follow Caps because nothing else needed it, but it is also the only way
            // to see that this app's own click has really been handed on: with the hook
            // only present while Caps was held, a macro's Ctr+click had no delivery
            // signal and had to rely on a timer, which is what let the modifier come up
            // before the click occasionally. A refused mouse hook only costs the mouse
            // triggers and that signal, so it must not stop the app.
            EnsureMouseHook();
            _running = true;
            ReportConflicts();
            return true;
        }

        public void Stop()
        {
            if (_thread == null)
            {
                _running = false;
                return;
            }
            _thread.Send(delegate { StopCore(); });
        }

        private void StopCore()
        {
            if (!_running && !_hook.IsInstalled && !_mouseHook.IsInstalled)
            {
                ResetState();
                return;
            }
            _hook.Uninstall();
            _mouseHook.Uninstall();
            _running = false;
            ResetState();
        }

        public void PauseForDialog()
        {
            if (_pausedForDialog) return;
            _pausedForDialog = true;
            Stop();
        }

        public void ResumeAfterDialog()
        {
            if (!_pausedForDialog) return;
            _pausedForDialog = false;
            if (_settings != null && _settings.Enabled) Start();
        }

        /// <summary>
        /// Global hotkeys are no longer registered: only the Caps layer is
        /// supported, so a WM_HOTKEY never belongs to this app. The entry point
        /// stays so WndProc keeps one uniform message path.
        /// </summary>
        public bool HandleHotkeyMessage(int id)
        {
            return false;
        }

        private static bool IsHoldMode(KeyMapping mapping)
        {
            return mapping != null && mapping.TriggerMode == "hold" && ActionCatalog.IsRepeatable(mapping.Action);
        }

        /// <summary>
        /// input.keys can be held like a physical key. Cursor moves and other
        /// repeatable actions stay "repeat the action" because there is no key to
        /// hold in the target application.
        /// </summary>
        private static bool IsPhysicalHold(KeyMapping mapping)
        {
            return IsHoldMode(mapping) && mapping.Action == "input.keys";
        }

        private void StartRepeat(KeyMapping mapping, Keys key, bool global)
        {
            if (mapping == null || key == Keys.None || !IsHoldMode(mapping)) return;
            // Only the repeat moves to the new key; a key that is still physically
            // down keeps its hold on the target.
            StopRepeatOnly();
            _repeatMapping = mapping;
            _repeatKey = key;
            _repeatGlobal = global;
            _repeatPhysical = IsPhysicalHold(mapping);
            RepeatTimer.Interval = RepeatDelayMs;
            RepeatTimer.Start();
        }

        private void RepeatTick(object sender, EventArgs e)
        {
            if (_repeatMapping == null)
            {
                StopRepeat();
                return;
            }
            if (_repeatGlobal)
            {
                if ((Native.GetAsyncKeyState((int)_repeatKey) & 0x8000) == 0)
                {
                    StopRepeat();
                    return;
                }
            }
            else if (!_capsDown || !_downKeys.Contains(_repeatKey))
            {
                StopRepeat();
                return;
            }

            if (_repeatPrecise)
            {
                long now = _repeatClock.ElapsedMilliseconds;
                if (now + 1 < _repeatNextMs) return;
                _repeatNextMs += RepeatIntervalMs;
                if (_repeatNextMs <= now) _repeatNextMs = now + RepeatIntervalMs;
            }

            KeyMapping mapping = _repeatMapping;
            if (!mapping.Enabled)
            {
                // The user disabled the binding while holding it: release the key
                // so it cannot stay stuck down in the target application.
                StopRepeat();
                return;
            }

            if (_repeatPhysical)
            {
                if (HoldRepeated != null) HoldRepeated(mapping);
            }
            else if (ActionTriggered != null)
            {
                ActionTriggered(mapping);
            }

            if (!_repeatPrecise)
            {
                // A WinForms timer cannot resolve 33 ms: the system tick is about
                // 15.6 ms, so Interval=33 effectively becomes 46.8 ms (21 repeats
                // per second). Ask for a 1 ms clock and drive the cadence from a
                // stopwatch instead; the timer just wakes us often enough.
                EnterPreciseRepeat();
            }
        }

        private void EnterPreciseRepeat()
        {
            _repeatPrecise = true;
            Native.TimeBeginPeriod(1);
            _repeatClock.Restart();
            _repeatNextMs = RepeatIntervalMs;
            if (_repeatTimer != null) _repeatTimer.Interval = 1;
        }

        private void LeavePreciseRepeat()
        {
            if (!_repeatPrecise) return;
            _repeatPrecise = false;
            _repeatClock.Stop();
            Native.TimeEndPeriod(1);
            if (_repeatTimer != null) _repeatTimer.Interval = RepeatDelayMs;
        }

        private void StopRepeat()
        {
            KeyMapping held = _repeatMapping;
            bool physical = _repeatPhysical;
            StopRepeatOnly();

            // Release a physically held key exactly once, no matter which path
            // stopped the repeat (focus loss, disable, dispose).
            if (physical && held != null && HoldEnded != null) HoldEnded(held);
        }

        /// <summary>
        /// Stops the auto-repeat but deliberately keeps the held key down: the
        /// newest chord key takes over the repeat while older keys stay pressed.
        /// </summary>
        private void StopRepeatOnly()
        {
            if (_repeatTimer != null) _repeatTimer.Stop();
            LeavePreciseRepeat();
            _repeatPhysical = false;
            _repeatMapping = null;
            _repeatKey = Keys.None;
            _repeatGlobal = false;
        }

        private ChordEntry FindChord(Keys key)
        {
            for (int i = 0; i < _chordEntries.Count; i++)
            {
                if (_chordEntries[i].Key == key) return _chordEntries[i];
            }
            return null;
        }

        /// <summary>
        /// Releases the key that one chord entry holds down in the target. Called
        /// when the user lifts that physical key, or when Caps/the app lets go.
        /// </summary>
        private void ReleaseChordHold(ChordEntry entry)
        {
            if (entry == null || !entry.HeldOnTarget) return;
            entry.HeldOnTarget = false;
            if (HoldEnded != null) HoldEnded(entry.Mapping);
        }

        /// <summary>Ends the whole Caps layer: repeat stops, every held key goes up.</summary>
        private void ReleaseAllChords()
        {
            // The repeat is just a view of a chord entry; the entry owns the hold,
            // so releasing through both would send the key-up twice.
            StopRepeatOnly();
            for (int i = _chordEntries.Count - 1; i >= 0; i--)
            {
                ReleaseChordHold(_chordEntries[i]);
            }
            _chordEntries.Clear();
        }

        /// <summary>
        /// Starts the output for one chord key. The previous key is NOT released:
        /// it stays down in the target (games rely on that for simultaneous
        /// movement) and only gives up the auto-repeat.
        /// </summary>
        private void DeliverChord(Keys key, KeyMapping mapping)
        {
            if (mapping == null) return;
            StopRepeatOnly();

            ChordEntry entry = FindChord(key);
            if (entry == null)
            {
                entry = new ChordEntry();
                entry.Key = key;
                _chordEntries.Add(entry);
            }
            entry.Mapping = mapping;

            if (IsPhysicalHold(mapping))
            {
                if (!entry.HeldOnTarget)
                {
                    entry.HeldOnTarget = true;
                    if (HoldStarted != null) HoldStarted(mapping);
                }
                StartRepeat(mapping, key, false);
            }
            else
            {
                if (ActionTriggered != null) ActionTriggered(mapping);
                if (IsHoldMode(mapping)) StartRepeat(mapping, key, false);
            }
        }

        /// <summary>
        /// The newest key was released: hand the auto-repeat back to the newest
        /// key that is still physically down. Its hold is untouched, so nothing
        /// is pressed a second time.
        /// </summary>
        private void ResumeHeldChord()
        {
            for (int i = _chordEntries.Count - 1; i >= 0; i--)
            {
                ChordEntry entry = _chordEntries[i];
                if (!_downKeys.Contains(entry.Key)) continue;
                if (entry.Mapping != null && IsHoldMode(entry.Mapping))
                    StartRepeat(entry.Mapping, entry.Key, false);
                return;
            }
        }

        private void ResetState()
        {
            // Booked injections belong to a run of the layer; a stale claim could
            // otherwise swallow the first physical key after a restart.
            SyntheticLedger.Clear();
            ReleaseAllChords();
            _mouseHook.Uninstall();
            SyntheticLedger.MouseClaimsActive = false;
            _capsDown = false;
            _capsChordUsed = false;
            _physicalModifiers.Clear();
            _swallowedModifiers.Clear();
            _downKeys.Clear();
            _swallowedKeys.Clear();
            _heldBeforeCaps.Clear();
            _suppressedModifiers.Clear();
            _swallowedMouseButtons.Clear();
            _wheelAccum = 0;
            _wheelSign = 0;
        }

        /// <summary>
        /// Installs the mouse hook if it is not there yet. It stays installed for the
        /// whole run (see StartCore): the Caps layer uses it not only for mouse
        /// triggers but also to see when its own clicks have been handed on.
        /// </summary>
        private void EnsureMouseHook()
        {
            if (_mouseHook.IsInstalled || _mouseHook.Install())
            {
                SyntheticLedger.MouseClaimsActive = true;
                return;
            }
            // Report once per session: a security tool that blocks the mouse hook
            // must not fill the log on every Caps press.
            if (_mouseHookReported) return;
            _mouseHookReported = true;
            ConfigStore.Log("鼠标钩子不可用：鼠标触发方式将不生效。");
        }

        /// <summary>
        /// Tells the user which shortcuts cannot fire: a duplicate Caps trigger
        /// loses to the first match. Staying silent used to leave a green toggle
        /// on a dead row.
        /// </summary>
        private void ReportConflicts()
        {
            if (_settings == null) return;
            // Presets carry their own lists; a duplicate only matters inside the
            // list that is active for a given program, so count per list and sum.
            List<List<KeyMapping>> lists = ShortcutSets.All(_settings);
            int duplicates = 0;
            for (int i = 0; i < lists.Count; i++)
            {
                duplicates += MappingConflicts.FindShadowed(lists[i]).Count;
            }
            if (duplicates == 0)
            {
                _lastIssueReport = "";
                return;
            }

            string message = duplicates + " 个快捷键组合重复，只有列表中的第一个会生效";
            // Start() also runs when a dialog closes; repeating an unchanged
            // warning there would be noise, so only report real changes.
            if (message == _lastIssueReport) return;
            _lastIssueReport = message;

            RaiseStatus(message, MInfoSeverity.Warning);
            ConfigStore.Log("快捷键启动检查：" + message);
        }


        private void RaiseStatus(string text, MInfoSeverity severity)
        {
            Action<string, MInfoSeverity> handler = StatusChanged;
            if (handler != null) handler(text, severity);
        }

        private bool OnHookKey(KeyboardEventArgs e)
        {
            // A driver backend injects below dwExtraInfo, so its strokes come back
            // looking like hardware. SyntheticLedger claims exactly the ones this app
            // sent (downs only: an unbooked key-up is always the user letting go) and
            // they are passed to the target instead of becoming input here.
            if (SyntheticLedger.Consume(e.Key, e.IsDown)) return false;

            Keys pressed = e.Key & Keys.KeyCode;
            if (MousePointCapture.IsActive)
            {
                // While the coordinate picker is open the app hides its windows and
                // this hook is the only thing still receiving input; CapsLock confirms,
                // Esc cancels, and both are swallowed so the lock never toggles.
                if (pressed == Keys.CapsLock || pressed == Keys.Escape)
                {
                    if (e.IsDown)
                    {
                        if (pressed == Keys.CapsLock)
                        {
                            PostToUi(delegate { MousePointCapture.ConfirmAtCursor(); });
                        }
                        else
                        {
                            PostToUi(delegate { MousePointCapture.Cancel(); });
                        }
                    }
                    return true;
                }
            }

            if (_settings == null) return false;
            Keys key = e.Key & Keys.KeyCode;

            if (key == Keys.CapsLock)
            {
                if (e.IsDown)
                {
                    if (!_capsDown)
                    {
                        ReleaseAllChords();
                        _capsDown = true;
                        _capsChordUsed = false;
                        _capsDownAt = DateTime.UtcNow;
                        _heldBeforeCaps.Clear();
                        foreach (Keys held in _downKeys) _heldBeforeCaps.Add(held);
                        SuppressHeldModifiers();
                        EnsureMouseHook();
                    }
                    return true;
                }
                if (e.IsUp)
                {
                    if (!_capsDown) return false;
                    bool tap = !_capsChordUsed && (DateTime.UtcNow - _capsDownAt).TotalMilliseconds <= _settings.LongPressMs;
                    ReleaseAllChords();
                    _capsDown = false;
                    // The mouse hook stays installed: it is not only for Caps triggers,
                    // it is also how this app learns that its own click was delivered.
                    _heldBeforeCaps.Clear();
                    RestoreSuppressedModifiers();
                    if (tap) PerformTapAction();
                    return true;
                }
            }

            if (KeyText.IsModifier(key))
            {
                return HandleModifier(key, e);
            }

            bool repeat;
            if (e.IsDown)
            {
                // A key this app holds at the target repeats through the OS, and those
                // downs are not a new chord (see SyntheticLedger). Checked before
                // _downKeys so a swallowed repeat leaves no trace: the next real press
                // must still count as a first press. A key the user is still holding
                // skips this and keeps its normal auto-repeat handling below.
                if (!_downKeys.Contains(key) && SyntheticLedger.IsHeldRepeat(key)) return true;

                repeat = !_downKeys.Add(key);

                if (_swallowedKeys.Contains(key))
                {
                    // The key was already consumed by the Caps layer and is still held
                    // after Caps was released. Keep suppressing its auto-repeat.
                    return true;
                }

                if (_capsDown)
                {
                    if (_heldBeforeCaps.Contains(key))
                    {
                        _capsChordUsed = true;
                        return false;
                    }
                    _capsChordUsed = true;
                    KeyMapping mapping = FindCapsMapping(key);
                    if (mapping != null)
                    {
                        _swallowedKeys.Add(key);
                        if (!repeat)
                        {
                            // Rollover: the previous key keeps its hold down, the
                            // newest one takes over the auto-repeat.
                            DeliverChord(key, mapping);
                        }
                        return true;
                    }
                    _swallowedKeys.Add(key);
                    // No mapping means no output of its own, so the auto-repeat
                    // stops; keys that are already held stay down.
                    if (!repeat)
                    {
                        StopRepeatOnly();
                    }
                    return true;
                }
                return false;
            }

            if (e.IsUp)
            {
                ChordEntry entry = FindChord(key);
                bool wasRepeating = !_repeatGlobal && _repeatKey == key;
                _downKeys.Remove(key);
                if (entry != null)
                {
                    _chordEntries.Remove(entry);
                    if (wasRepeating) StopRepeatOnly();
                    // Lifting the physical key is the only thing that releases the
                    // key the layer was holding for it.
                    ReleaseChordHold(entry);
                    if (wasRepeating) ResumeHeldChord();
                }
                else if (wasRepeating)
                {
                    StopRepeatOnly();
                }
                if (_swallowedKeys.Remove(key))
                    return true;
                return false;
            }

            return false;
        }

        /// <summary>
        /// Mouse triggers only exist inside the Caps layer: apart from Caps the
        /// mouse behaves exactly as before. A button with no binding is passed
        /// through instead of being swallowed, so holding Caps does not make the
        /// mouse dead in a program that has no mouse shortcut.
        /// </summary>
        private bool OnHookMouse(MouseButtonEventArgs e)
        {
            // See OnHookKey: a driver-injected click carries no marker, so the ledger
            // is the only thing that tells it apart from a real press.
            if (SyntheticLedger.Consume(e.Key, e.IsDown))
            {
                // An own release also ends the "a physical press of this button is
                // being swallowed" state. Without this, a physical up that arrived
                // before our booked one consumed the claim, the state stayed set,
                // and our real release was then swallowed -- the target kept the
                // button down, which shows up as clicks that silently do nothing.
                if (!e.IsDown) _swallowedMouseButtons.Remove(e.Key & Keys.KeyCode);
                return false;
            }

            if (_settings == null) return false;
            if (MousePointCapture.IsActive) return false;
            if (e.IsInjected && !ProcessInjected) return false;

            Keys key = e.Key & Keys.KeyCode;
            if (!_capsDown)
            {
                _swallowedMouseButtons.Remove(key);
                return false;
            }

            if (e.IsWheel) return HandleWheel(key, e.Delta);

            if (e.IsDown)
            {
                KeyMapping mapping = FindCapsMapping(key);
                if (mapping == null) return false;
                if (SyntheticLedger.MouseClickRecent(400))
                {
                    // The click was ours but its ledger entry was gone (claimed by
                    // another event, or the layer restarted between send and hook).
                    // Report it: the target loses the click and the shortcut fires.
                    ConfigStore.Log("Caps 层吞下了自己发出的鼠标按键：" + KeyText.FormatKey(key)
                        + "（账本未认领），这次点击不会到达目标窗口。");
                }
                _downKeys.Add(key);
                _capsChordUsed = true;
                _swallowedMouseButtons.Add(key);
                DeliverChord(key, mapping);
                return true;
            }

            if (e.IsUp)
            {
                _downKeys.Remove(key);
                ChordEntry entry = FindChord(key);
                bool wasRepeating = !_repeatGlobal && _repeatKey == key;
                if (entry != null)
                {
                    _chordEntries.Remove(entry);
                    if (wasRepeating) StopRepeatOnly();
                    ReleaseChordHold(entry);
                    if (wasRepeating) ResumeHeldChord();
                }
                else if (wasRepeating)
                {
                    StopRepeatOnly();
                }
                bool swallow = _swallowedMouseButtons.Remove(key);
                if (swallow && SyntheticLedger.MouseClickRecent(400))
                {
                    // Our own click's release was eaten by the Caps layer: the
                    // target now holds a button-down it will never see released.
                    ConfigStore.Log("Caps 层吞下了自己发出的鼠标抬起：" + KeyText.FormatKey(key)
                        + "，目标窗口的该按钮会保持按下。");
                }
                return swallow;
            }

            return false;
        }

        /// <summary>
        /// Fires the wheel shortcut once per 120-unit notch. A notch has no key-up,
        /// so it is always a single shot and never owns the auto-repeat.
        /// </summary>
        private bool HandleWheel(Keys key, int delta)
        {
            int sign = key == (Keys)MouseKeys.WheelUp ? 1 : -1;
            KeyMapping mapping = FindCapsMapping(sign > 0
                ? (Keys)MouseKeys.WheelUp : (Keys)MouseKeys.WheelDown);
            // The other direction is not bound: let it scroll normally instead of
            // swallowing it for nothing.
            if (mapping == null) return false;

            DateTime now = DateTime.UtcNow;
            // A pause or a direction change starts a fresh accumulation, so an old
            // partial notch cannot complete a later gesture.
            if (sign != _wheelSign || (now - _wheelAt).TotalMilliseconds > 800)
            {
                _wheelAccum = 0;
                _wheelSign = sign;
            }
            _wheelAt = now;
            if (delta < 0) delta = -delta;
            int step = _settings == null ? 120 : _settings.WheelStep;
            if (step < 40) step = 40;
            _wheelAccum += delta == 0 ? step : delta;

            int notches = _wheelAccum / step;
            _wheelAccum -= notches * step;
            if (notches <= 0) return true;

            // Only a runaway device is bounded: one OS message can carry several
            // notches when the wheel is flicked, and every one of them is a real user
            // action. Truncating those (it used to stop at 3) is what made "I scrolled
            // eight notches and only three fired".
            if (notches > MaxNotchesPerMessage) notches = MaxNotchesPerMessage;
            _capsChordUsed = true;
            for (int i = 0; i < notches; i++)
            {
                if (ActionTriggered != null) ActionTriggered(mapping);
            }
            return true;
        }

        private bool HandleModifier(Keys key, KeyboardEventArgs e)
        {
            if (e.IsDown)
            {
                _physicalModifiers.Add(key);
                if (_capsDown)
                {
                    _capsChordUsed = true;
                    if (!_swallowedModifiers.Contains(key))
                    {
                        _swallowedModifiers.Add(key);
                        return true;
                    }
                    return true;
                }
                return false;
            }

            if (e.IsUp)
            {
                _physicalModifiers.Remove(key);
                // A modifier the Caps layer already released on the target's
                // behalf must not send a second up when the user lets go.
                if (_suppressedModifiers.Remove(key))
                    return true;
                if (_swallowedModifiers.Remove(key))
                    return true;
                return false;
            }

            return false;
        }

        private bool CtrlDown
        {
            get
            {
                return _physicalModifiers.Contains(Keys.LControlKey)
                    || _physicalModifiers.Contains(Keys.RControlKey)
                    || _physicalModifiers.Contains(Keys.ControlKey);
            }
        }

        private bool AltDown
        {
            get
            {
                return _physicalModifiers.Contains(Keys.LMenu)
                    || _physicalModifiers.Contains(Keys.RMenu)
                    || _physicalModifiers.Contains(Keys.Menu);
            }
        }

        private bool ShiftDown
        {
            get
            {
                return _physicalModifiers.Contains(Keys.LShiftKey)
                    || _physicalModifiers.Contains(Keys.RShiftKey)
                    || _physicalModifiers.Contains(Keys.ShiftKey);
            }
        }

        private bool WinDown
        {
            get
            {
                return _physicalModifiers.Contains(Keys.LWin)
                    || _physicalModifiers.Contains(Keys.RWin);
            }
        }

        private KeyMapping FindCapsMapping(Keys key)
        {
            bool ctrl = CtrlDown;
            bool alt = AltDown;
            bool shift = ShiftDown;
            bool win = WinDown;
            // The active list follows the foreground program, so the same chord
            // can do different things in different apps.
            foreach (KeyMapping m in ShortcutSets.Active(_settings))
            {
                if (m == null || !m.Enabled || !m.Caps) continue;
                if (((Keys)m.Key & Keys.KeyCode) != key) continue;
                if (m.Ctrl != ctrl || m.Alt != alt || m.Shift != shift || m.Win != win) continue;
                return m;
            }
            return null;
        }

        private void PerformTapAction()
        {
            string tap = _settings.TapAction;
            if (tap == "none") return;
            if (tap == "esc")
            {
                InputPipeline.Tap(Keys.Escape);
                return;
            }
            if (tap == "ime")
            {
                // Win + Space is the Windows "next input method" chord; sending it
                // from here keeps the caps layer usable while switching languages.
                InputPipeline.SendCombo(Keys.Space, false, false, false, true, 1);
                return;
            }
            InputPipeline.Tap(Keys.CapsLock);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DispatcherThread thread = _thread;
            _thread = null;
            if (thread != null)
            {
                thread.Send(delegate { DisposeCore(); });
                thread.Dispose();
            }
            else
            {
                DisposeCore();
            }
        }

        private void DisposeCore()
        {
            StopCore();
            if (_repeatTimer != null)
            {
                _repeatTimer.Dispose();
                _repeatTimer = null;
            }
            _hook.Dispose();
            _mouseHook.Dispose();
        }
    }
}
