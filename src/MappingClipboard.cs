using System;
using System.Collections.Generic;

namespace CapsLockLite
{
    /// <summary>
    /// Row-level clipboard for the shortcut list: the selected rows travel to a
    /// fresh copy of themselves. It is deliberately separate from the chord
    /// clipboard on the keyboard board -- that one is bound to `Caps + key + 修饰键`,
    /// this one is not bound to anything, so Ctrl+C / Ctrl+V can serve both without
    /// guessing which one the user meant.
    /// </summary>
    internal static class MappingClipboard
    {
        /// <summary>Clones the rows so later edits to the originals do not leak in.</summary>
        public static List<KeyMapping> Copy(IEnumerable<KeyMapping> source)
        {
            List<KeyMapping> copy = new List<KeyMapping>();
            if (source == null) return copy;
            foreach (KeyMapping mapping in source)
            {
                if (mapping != null) copy.Add(mapping.Clone());
            }
            return copy;
        }

        /// <summary>
        /// Index right after the anchor row, or the end of the list when the anchor
        /// is missing (nothing selected, or the row was filtered away).
        /// </summary>
        public static int InsertIndex(IList<KeyMapping> list, KeyMapping anchor)
        {
            if (list == null) return 0;
            if (anchor == null) return list.Count;
            int index = list.IndexOf(anchor);
            return index < 0 ? list.Count : index + 1;
        }

        /// <summary>
        /// Inserts fresh copies (new ids) at index and returns them in order. The
        /// chord, action, parameter, note and trigger mode come along, so a pasted
        /// row behaves exactly like the row it came from until it is edited.
        /// </summary>
        public static List<KeyMapping> PasteInto(IList<KeyMapping> list, int index, IList<KeyMapping> clipboard)
        {
            List<KeyMapping> pasted = new List<KeyMapping>();
            if (list == null || clipboard == null || clipboard.Count == 0) return pasted;
            if (index < 0) index = 0;
            if (index > list.Count) index = list.Count;
            foreach (KeyMapping source in clipboard)
            {
                if (source == null) continue;
                KeyMapping copy = source.Clone();
                copy.Id = Guid.NewGuid().ToString("N");
                list.Insert(index, copy);
                pasted.Add(copy);
                index++;
            }
            return pasted;
        }
    }
}
