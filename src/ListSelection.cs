using System;
using System.Collections.Generic;

namespace CapsLockLite
{
    /// <summary>
    /// Selection state of an <see cref="MListView"/>: one flag per row plus a lead
    /// row (the one the keyboard, Enter and the context menu point at) and an
    /// anchor (where a Shift range started). Kept free of control state so the
    /// self-test can exercise Ctrl / Shift / Ctrl+A without a message loop.
    /// </summary>
    internal sealed class MListSelection
    {
        private bool[] _flags = new bool[0];
        private int _lead = -1;
        private int _anchor = -1;

        public int Count { get { return _flags.Length; } }
        public int Lead { get { return _lead; } }
        public int Anchor { get { return _anchor; } }

        public int SelectedCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _flags.Length; i++)
                {
                    if (_flags[i]) n++;
                }
                return n;
            }
        }

        public bool IsSelected(int index)
        {
            return index >= 0 && index < _flags.Length && _flags[index];
        }

        /// <summary>Resizes to a new row count and drops the selection.</summary>
        public void Reset(int count)
        {
            if (count < 0) count = 0;
            if (_flags.Length != count) _flags = new bool[count];
            else Array.Clear(_flags, 0, _flags.Length);
            _lead = -1;
            _anchor = -1;
        }

        public void Clear()
        {
            if (_flags.Length > 0) Array.Clear(_flags, 0, _flags.Length);
            _lead = -1;
            _anchor = -1;
        }

        public void SelectOnly(int index)
        {
            if (index < 0 || index >= _flags.Length)
            {
                Clear();
                return;
            }
            if (_flags.Length > 0) Array.Clear(_flags, 0, _flags.Length);
            _flags[index] = true;
            _lead = index;
            _anchor = index;
        }

        /// <summary>Ctrl+click: flips one row and moves the anchor to it.</summary>
        public void Toggle(int index)
        {
            if (index < 0 || index >= _flags.Length) return;
            _flags[index] = !_flags[index];
            _lead = index;
            _anchor = index;
        }

        /// <summary>
        /// Moves the keyboard focus without touching the flags. A click on an
        /// already-selected row uses this so a drag can still pick up the block.
        /// </summary>
        public void SetLead(int index)
        {
            if (index < -1 || index >= _flags.Length) return;
            _lead = index;
        }

        public void SetAnchor(int index)
        {
            if (index < -1 || index >= _flags.Length) return;
            _anchor = index;
        }

        /// <summary>Shift+click / Shift+arrow: selects from the anchor to index.</summary>
        public void SelectRange(int index)
        {
            if (index < 0 || index >= _flags.Length) return;
            int from = _anchor;
            if (from < 0 || from >= _flags.Length) from = _lead;
            if (from < 0 || from >= _flags.Length) from = index;
            int min = Math.Min(from, index);
            int max = Math.Max(from, index);
            Array.Clear(_flags, 0, _flags.Length);
            for (int i = min; i <= max; i++) _flags[i] = true;
            _lead = index;
        }

        public void SelectAll()
        {
            if (_flags.Length == 0) return;
            for (int i = 0; i < _flags.Length; i++) _flags[i] = true;
            if (_lead < 0) _lead = 0;
            if (_anchor < 0) _anchor = _lead;
        }

        /// <summary>
        /// Keyboard movement. Shift extends from the anchor, Ctrl moves only the
        /// lead and leaves the other rows selected.
        /// </summary>
        public void MoveLead(int index, bool extend, bool keepOthers)
        {
            if (_flags.Length == 0) return;
            if (index < 0) index = 0;
            if (index >= _flags.Length) index = _flags.Length - 1;

            if (extend)
            {
                SelectRange(index);
                return;
            }
            if (keepOthers)
            {
                _lead = index;
                return;
            }
            SelectOnly(index);
        }

        public List<int> SelectedIndices()
        {
            List<int> list = new List<int>();
            for (int i = 0; i < _flags.Length; i++)
            {
                if (_flags[i]) list.Add(i);
            }
            return list;
        }

        public void SetSelection(IList<int> indices, int lead)
        {
            if (_flags.Length > 0) Array.Clear(_flags, 0, _flags.Length);
            if (indices != null)
            {
                for (int i = 0; i < indices.Count; i++)
                {
                    int index = indices[i];
                    if (index >= 0 && index < _flags.Length) _flags[index] = true;
                }
            }
            _lead = lead >= 0 && lead < _flags.Length ? lead : -1;
            _anchor = _lead;
        }

        /// <summary>True when the selected rows form one unbroken block.</summary>
        public bool IsContiguous()
        {
            int first = -1;
            int last = -1;
            int count = 0;
            for (int i = 0; i < _flags.Length; i++)
            {
                if (!_flags[i]) continue;
                if (first < 0) first = i;
                last = i;
                count++;
            }
            return count > 0 && last - first + 1 == count;
        }
    }

    /// <summary>Carries the block that moved: original start, row count, new start.</summary>
    internal sealed class MListReorderEventArgs : EventArgs
    {
        public readonly int From;
        public readonly int Count;
        public readonly int To;

        public MListReorderEventArgs(int from, int count, int to)
        {
            From = from;
            Count = count;
            To = to;
        }
    }

    /// <summary>
    /// Index arithmetic for a block move. A "slot" is an insertion boundary in
    /// the original row list (0 .. count), so dropping a block onto its own
    /// position is a slot equal to its start index.
    /// </summary>
    internal static class MListReorder
    {
        /// <summary>First index of the block after it is dropped into a slot.</summary>
        public static int TargetStart(int from, int count, int slot)
        {
            if (slot > from) return slot - count;
            return slot;
        }

        /// <summary>Where a row ends up (original indexes) after the block moves.</summary>
        public static int MoveIndex(int index, int from, int count, int slot)
        {
            int to = TargetStart(from, count, slot);
            if (index >= from && index < from + count) return index - from + to;
            if (to < from)
            {
                // Block moved up: the rows it passed shift down by the block size.
                if (index >= to && index < from) return index + count;
            }
            else if (to > from)
            {
                // Block moved down: the rows it passed shift up by the block size.
                if (index >= from + count && index < to + count) return index - count;
            }
            return index;
        }

        /// <summary>Moves a contiguous block inside a list, in place.</summary>
        public static void Move<T>(IList<T> items, int from, int count, int slot)
        {
            if (items == null || count <= 0 || from < 0) return;
            int to = TargetStart(from, count, slot);
            if (to < 0) to = 0;
            if (to > items.Count - count) to = items.Count - count;
            if (to == from) return;

            List<T> block = new List<T>(count);
            for (int i = 0; i < count; i++) block.Add(items[from + i]);
            for (int i = 0; i < count; i++) items.RemoveAt(from);
            for (int i = 0; i < count; i++) items.Insert(to + i, block[i]);
        }
    }
}
