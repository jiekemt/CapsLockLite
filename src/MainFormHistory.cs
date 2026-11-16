using System;
using System.Collections.Generic;

namespace CapsLockLite
{
    // Undo for the shortcut page. Every edit path (list, keyboard, clipboard,
    // context menu) funnels into MappingList — the selected preset, or the global
    // list — so a snapshot of that one list makes Ctrl+Z work everywhere on the
    // page without mixing presets.
    internal sealed partial class MainForm
    {
        private const int MappingHistoryLimit = 50;

        private sealed class MappingSnapshot
        {
            public List<KeyMapping> Mappings;
            public string Label;
        }

        private readonly List<MappingSnapshot> _mappingUndo = new List<MappingSnapshot>();
        private readonly List<MappingSnapshot> _mappingRedo = new List<MappingSnapshot>();

        private List<KeyMapping> CloneMappings()
        {
            List<KeyMapping> source = MappingList;
            List<KeyMapping> copy = new List<KeyMapping>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                KeyMapping mapping = source[i];
                if (mapping != null) copy.Add(mapping.Clone());
            }
            return copy;
        }

        /// <summary>Remembers the shortcut list before a mutation; Ctrl+Z returns here.</summary>
        private void PushMappingUndo(string label)
        {
            PushMappingUndo(CaptureMappings(label));
        }

        /// <summary>Takes the snapshot early, when a dialog might still be cancelled.</summary>
        private MappingSnapshot CaptureMappings(string label)
        {
            MappingSnapshot snapshot = new MappingSnapshot();
            snapshot.Mappings = CloneMappings();
            snapshot.Label = label;
            return snapshot;
        }

        private void PushMappingUndo(MappingSnapshot snapshot)
        {
            if (snapshot == null) return;
            _mappingUndo.Add(snapshot);
            if (_mappingUndo.Count > MappingHistoryLimit) _mappingUndo.RemoveAt(0);
            _mappingRedo.Clear();
        }

        private void UndoMappings()
        {
            RewindMappings(true);
        }

        private void RedoMappings()
        {
            RewindMappings(false);
        }

        private void RewindMappings(bool undo)
        {
            List<MappingSnapshot> from = undo ? _mappingUndo : _mappingRedo;
            if (from.Count == 0)
            {
                ShowStatus(undo ? "没有可撤回的快捷键修改" : "没有可重做的快捷键修改", MInfoSeverity.Info);
                return;
            }
            MappingSnapshot snapshot = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);

            // The state being replaced becomes the opposite stack's next entry, so
            // Ctrl+Z / Ctrl+Y can be pressed back and forth.
            MappingSnapshot current = new MappingSnapshot();
            current.Mappings = CloneMappings();
            current.Label = snapshot.Label;
            List<MappingSnapshot> to = undo ? _mappingRedo : _mappingUndo;
            to.Add(current);
            if (to.Count > MappingHistoryLimit) to.RemoveAt(0);

            // Rewrite in place: the active list may be a preset's list, and
            // replacing settings.Mappings would silently leave that preset behind.
            List<KeyMapping> target = MappingList;
            target.Clear();
            for (int i = 0; i < snapshot.Mappings.Count; i++)
                target.Add(snapshot.Mappings[i].Clone());
            SaveAndApply();
            RefreshList();
            ShowStatus((undo ? "已撤回：" : "已重做：") + snapshot.Label, MInfoSeverity.Success);
        }

        /// <summary>Drops the stack when the list was replaced wholesale.</summary>
        private void ClearMappingHistory()
        {
            _mappingUndo.Clear();
            _mappingRedo.Clear();
        }
    }
}
