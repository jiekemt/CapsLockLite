using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// Remembers the strokes this app injected when the sink cannot mark them.
    ///
    /// SendInput events carry Native.SyntheticMarker in dwExtraInfo, so the hooks
    /// recognize them by value. A driver backend injects below that marker (and
    /// below the user-mode hook chain, which is the whole point), so its strokes
    /// arrive at the hook looking exactly like hardware. Without this ledger the
    /// Caps layer would swallow its own output -- Caps+X sending Ctrl+C would come
    /// back as a physical Caps+Ctrl+C chord and fire again.
    ///
    /// Two rules, learned the hard way:
    /// * one entry per sent stroke, matched FIFO, so the exact strokes this app
    ///   produced are recognized and nothing else is;
    /// * a "held" record per key, because the OS derives auto-repeat for a key the
    ///   app holds and no entry can predict how many repeats arrive. The record is
    ///   dropped the moment the app's own release is delivered (or the user lets go
    ///   of the key), never by a timer, so the next real press is never swallowed.
    /// </summary>
    internal static class SyntheticLedger
    {
        private sealed class Entry
        {
            public int Key;
            public bool Down;
            public long At;
            public long Sequence;
        }

        private sealed class HeldKey
        {
            /// <summary>Which key-down booked this hold; 0 for an unpaired release.</summary>
            public long Sequence;
            /// <summary>0 while the key is still down, otherwise when the release was sent.</summary>
            public long ReleasedAt;
        }

        private const int DefaultTtlMs = 2000;
        // An injected release that never arrives (driver gone, dropped stroke) must
        // not keep claiming the key forever.
        private const int ReleaseSafetyMs = 1000;

        private static readonly object _gate = new object();
        private static readonly List<Entry> _entries = new List<Entry>();
        private static readonly Dictionary<int, HeldKey> _held = new Dictionary<int, HeldKey>();
        private static long _sequence;
        private static int _ttlMs = DefaultTtlMs;
        private static bool _mouseClaimsActive;

        /// <summary>How long an unmatched entry is trusted. Tests lower it.</summary>
        internal static int TtlMs
        {
            get { return _ttlMs; }
            set { _ttlMs = value < 0 ? 0 : value; }
        }

        internal static int PendingCount
        {
            get
            {
                lock (_gate) { return _entries.Count; }
            }
        }

        /// <summary>
        /// How many booked strokes of this kind are still waiting for the hook to
        /// hand them to the target. Zero means the driver's stroke has been
        /// delivered, which is the only reliable "the modifier is down now" signal
        /// available for a backend that injects below dwExtraInfo.
        /// </summary>
        internal static int PendingStrokes(Keys key, bool down)
        {
            int code = KeyCode(key);
            lock (_gate)
            {
                Prune();
                int count = 0;
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Key == code && _entries[i].Down == down) count++;
                }
                return count;
            }
        }

        /// <summary>Call BEFORE handing the stroke to the sink.</summary>
        public static void Record(Keys key, bool down)
        {
            int code = KeyCode(key);
            lock (_gate)
            {
                Prune();
                _lastRecordAt = (int)Now;
                Entry entry = new Entry();
                entry.Key = code;
                entry.Down = down;
                entry.At = Now;
                if (down)
                {
                    entry.Sequence = ++_sequence;
                    HeldKey held = new HeldKey();
                    held.Sequence = entry.Sequence;
                    held.ReleasedAt = 0;
                    _held[code] = held;
                }
                else
                {
                    HeldKey previous;
                    _held.TryGetValue(code, out previous);
                    entry.Sequence = previous == null ? 0 : previous.Sequence;
                    // Keep the record until this release is actually delivered: a
                    // repeat the OS derived from the hold may still be queued and has
                    // to stay recognized as ours. No time tail, so a fast re-press of
                    // the same key is never eaten afterwards.
                    if (previous != null) previous.ReleasedAt = Now;
                }
                _entries.Add(entry);
            }
        }

        /// <summary>
        /// True when this stroke was produced by the app; the caller must pass it
        /// through instead of feeding it to the Caps layer.
        /// </summary>
        public static bool Consume(Keys key, bool down)
        {
            int code = KeyCode(key);
            lock (_gate)
            {
                Prune();
                for (int i = 0; i < _entries.Count; i++)
                {
                    Entry entry = _entries[i];
                    if (entry.Key != code || entry.Down != down) continue;
                    _entries.RemoveAt(i);
                    // Our release has landed: the key is no longer held by us, so an
                    // unbooked key-down of it is user input again.
                    if (!down) _held.Remove(code);
                    return true;
                }
                // Nothing booked matches, so this is not a stroke the app sent. The
                // caller decides what to do (see IsHeldRepeat): claiming it here used
                // to hide the user's key-up from the Caps layer, which kept repeating
                // the action until Caps itself was released.
                return false;
            }
        }

        /// <summary>
        /// True when this key is one the app is holding at the target, so an unbooked
        /// key-down can only be an auto-repeat the OS derived from that hold -- it
        /// must not start a new chord. Never true for a release.
        /// </summary>
        public static bool IsHeldRepeat(Keys key)
        {
            int code = KeyCode(key);
            lock (_gate)
            {
                Prune();
                return _held.ContainsKey(code);
            }
        }

        /// <summary>
        /// Drops the entries of a batch the sink refused, so a fallback send does
        /// not leave claims behind for strokes that were never delivered.
        /// </summary>
        public static void Forget(IList<InputStroke> strokes)
        {
            if (strokes == null) return;
            lock (_gate)
            {
                for (int i = strokes.Count - 1; i >= 0; i--)
                {
                    int code = KeyCode(strokes[i].Key);
                    bool down = strokes[i].Down;
                    for (int j = _entries.Count - 1; j >= 0; j--)
                    {
                        Entry entry = _entries[j];
                        if (entry.Key != code || entry.Down != down) continue;
                        _entries.RemoveAt(j);
                        HeldKey held;
                        if (_held.TryGetValue(code, out held) && held.Sequence == entry.Sequence)
                            _held.Remove(code);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// True while the mouse hook is installed. Bookings exist so the Caps layer
        /// can recognize its own clicks; with the hook gone nothing can claim a mouse
        /// stroke, and a leftover booking would later be matched against the user's
        /// own click -- so mouse strokes are only booked while this is set. It also
        /// keeps PendingCount a true "still in flight" count.
        /// </summary>
        public static bool MouseClaimsActive
        {
            get { return _mouseClaimsActive; }
            set
            {
                _mouseClaimsActive = value;
                if (value) return;
                lock (_gate)
                {
                    for (int i = _entries.Count - 1; i >= 0; i--)
                    {
                        if (MouseKeys.IsMouseKey((Keys)_entries[i].Key)) _entries.RemoveAt(i);
                    }
                    foreach (KeyValuePair<int, HeldKey> pair in new List<KeyValuePair<int, HeldKey>>(_held))
                    {
                        if (MouseKeys.IsMouseKey((Keys)pair.Key)) _held.Remove(pair.Key);
                    }
                }
            }
        }

        private static int _lastRecordAt;

        /// <summary>Tick of the last booking: when the app last injected something.</summary>
        internal static int LastRecordTick
        {
            get { return _lastRecordAt; }
        }

        private static int _lastClickAt;

        /// <summary>Called right before a mouse click is sent (see MouseClickRecent).</summary>
        public static void NoteMouseClick()
        {
            _lastClickAt = Environment.TickCount;
        }

        /// <summary>
        /// True when this app sent a click this recently. Used only to explain a
        /// swallowed click in the log: a claim that misses leaves the target without
        /// the click, which looks exactly like "the action did nothing".
        /// </summary>
        public static bool MouseClickRecent(int windowMs)
        {
            return unchecked(Environment.TickCount - _lastClickAt) <= windowMs;
        }

        public static void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
                _held.Clear();
            }
        }

        private static long Now
        {
            get { return Environment.TickCount; }
        }

        private static int KeyCode(Keys key)
        {
            return (int)(key & Keys.KeyCode);
        }

        private static bool Expired(long at, long now)
        {
            // TickCount wraps every ~49 days; the signed difference survives the wrap.
            return unchecked((int)(now - at)) > _ttlMs;
        }

        private static void Prune()
        {
            long now = Now;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry entry = _entries[i];
                if (!Expired(entry.At, now)) continue;
                _entries.RemoveAt(i);
                // A key-down that never arrived must stop being claimed, or the
                // physical key would be eaten forever after one failed send.
                if (!entry.Down) continue;
                HeldKey held;
                if (!_held.TryGetValue(entry.Key, out held)) continue;
                if (held.Sequence == entry.Sequence) _held.Remove(entry.Key);
            }
            if (_held.Count == 0) return;
            List<int> stale = null;
            foreach (KeyValuePair<int, HeldKey> pair in _held)
            {
                HeldKey held = pair.Value;
                if (held.ReleasedAt == 0) continue;
                if (unchecked((int)(now - held.ReleasedAt)) <= ReleaseSafetyMs) continue;
                if (stale == null) stale = new List<int>();
                stale.Add(pair.Key);
            }
            if (stale == null) return;
            for (int i = 0; i < stale.Count; i++) _held.Remove(stale[i]);
        }
    }
}
