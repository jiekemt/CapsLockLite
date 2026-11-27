using System;

namespace CapsLockLite
{
    /// <summary>
    /// Keeps the pointer on the point a pointer action just chose, by swallowing the
    /// user's mouse movement while that action runs.
    ///
    /// Why: a macro exists to do something exactly, and a hand moving the mouse in the
    /// middle of a click (or of a hover that the next step relies on) makes it click
    /// somewhere else. Correctness of the macro wins over "the pointer still follows my
    /// hand for 30 ms".
    ///
    /// The rule is distance based, not a blanket mute: a move that lands within
    /// <see cref="TolerancePixels"/> of the pinned point is let through. That is what
    /// lets this app's own SetCursorPos work -- it generates a move to exactly the
    /// pinned point -- while a user's move (which lands somewhere else) is swallowed.
    /// The pin always expires, and it is released explicitly when the action or the
    /// macro ends, so the pointer can never stay frozen.
    /// </summary>
    internal static class PointerPin
    {
        /// <summary>
        /// No slack at all. It was 2 px so that this app's own SetCursorPos would pass,
        /// but measured: SetCursorPos never reaches the hook (it is applied by the
        /// window manager), so the slack only let a user's small hand movement through
        /// and made the pinned point drift by up to 2 px.
        /// </summary>
        private const int TolerancePixels = 0;

        private static readonly object _gate = new object();
        private static bool _active;
        private static int _x;
        private static int _y;
        private static int _until;
        private static int _blocked;
        private static int _generation;
        private static bool _macro;
        private static int _seen;
        private static int _totalBlocked;
        /// <summary>
        /// How long a pin lasts while a macro runs. Between two steps there is nothing
        /// to pin to yet, but the cursor must still stay where the last pointer action
        /// put it -- otherwise the macro's own "wait" steps are exactly the window in
        /// which a moving hand drags the pointer away (measured: 3 of 3 clicks drifted).
        /// </summary>
        private const int MacroRunMs = 30000;

        /// <summary>
        /// Marks the start of a macro run: from now on the pointer stays pinned (and
        /// per-action releases are ignored) until <see cref="Release()"/> at the end.
        /// </summary>
        public static void BeginMacro()
        {
            lock (_gate) _macro = true;
        }

        /// <summary>True while a point is pinned (and the pin has not expired).</summary>
        public static bool IsActive
        {
            get
            {
                lock (_gate) return Active;
            }
        }

        /// <summary>
        /// Moves swallowed since the last call, and resets. Reported once per macro
        /// instead of once per pointer action, which kept the log readable.
        /// </summary>
        public static int TakeBlocked()
        {
            lock (_gate)
            {
                int total = _totalBlocked;
                _totalBlocked = 0;
                return total;
            }
        }

        /// <summary>How many move events the hook handed to the pin at all.</summary>
        public static int SeenCount
        {
            get
            {
                lock (_gate) return _seen;
            }
        }

        /// <summary>How many user moves were swallowed since the pin was set.</summary>
        public static int BlockedCount
        {
            get
            {
                lock (_gate) return _blocked;
            }
        }

        /// <summary>
        /// Pins the pointer to a point for at most <paramref name="durationMs"/>.
        /// Returns a generation token: a release that arrives late (queued by an earlier
        /// action) must not drop a pin that a newer action has already set.
        /// </summary>
        public static int Pin(int x, int y, int durationMs)
        {
            lock (_gate)
            {
                if (!_active) _blocked = 0;
                _active = true;
                _x = x;
                _y = y;
                _blocked = 0;
                _generation++;
                int duration = _macro ? MacroRunMs : (durationMs < 1 ? 1 : durationMs);
                _until = unchecked(Environment.TickCount + duration);
                return _generation;
            }
        }

        /// <summary>Releases only if the pin is still the one this token was made for.</summary>
        public static int Release(int generation)
        {
            lock (_gate)
            {
                // Mid-macro the pin has to outlive a single action: the next step may be
                // a wait, and the pointer must not move during it.
                if (_macro) return 0;
                if (!_active || generation != _generation) return 0;
            }
            return Release();
        }

        /// <summary>Drops the pin and reports how many moves it swallowed.</summary>
        public static int Release()
        {
            lock (_gate)
            {
                bool was = _active;
                _macro = false;
                _active = false;
                int blocked = _blocked;
                _blocked = 0;
                _seen = 0;
                if (was) _totalBlocked += blocked;
                return blocked;
            }
        }

        /// <summary>
        /// Called from the mouse hook for every move. False means "swallow it".
        /// </summary>
        public static bool ShouldPass(int x, int y)
        {
            lock (_gate)
            {
                if (!Active) return true;
                _seen++;
                int dx = x - _x;
                int dy = y - _y;
                if (dx <= TolerancePixels && dx >= -TolerancePixels
                    && dy <= TolerancePixels && dy >= -TolerancePixels)
                {
                    return true;
                }
                _blocked++;
                return false;
            }
        }

        /// <summary>Caller must hold the gate.</summary>
        private static bool Active
        {
            get
            {
                if (!_active) return false;
                if (unchecked(Environment.TickCount - _until) < 0) return true;
                // Expired: drop it so the pointer can never stay frozen.
                _active = false;
                _blocked = 0;
                return false;
            }
        }
    }
}
