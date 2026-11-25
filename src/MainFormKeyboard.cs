using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// The keyboard section of the shortcut page. The board is part of the page
    /// itself now: the modifier keys are the filter (their selected state is drawn
    /// on the cap as a diagonal mask), the selected key narrows the list below,
    /// and the whole section folds away with an animated collapse.
    /// </summary>
    internal sealed partial class MainForm
    {
        private const string KeysHintFull = "Ctrl/Shift 多选 · 拖动主键搬运 · 拖动排序 · 双击编辑 · 右键更多";
        private const string KeysHintMedium = "拖动排序 · 双击编辑 · 右键更多";
        private const string KeysHintShort = "双击编辑 · 右键更多";

        private const int KeyboardAnimationMs = 220;
        private const int MouseAnimationMs = 220;

        private Panel _keyboardHost;
        private MKeyboardView _keyboard;
        private MKeyboardHeader _keyboardHeader;

        private Timer _keyboardTimer;
        private float _keyboardProgress = 1f;
        private float _keyboardFrom = 1f;
        private float _keyboardTarget = 1f;
        private DateTime _keyboardStarted;
        private bool _keyboardExpanded = true;

        private MMousePad _mousePad;
        private bool _mousePadCollapsed;
        private Timer _mouseTimer;
        private float _mouseProgress;      // 0 = open card, 1 = rail
        private float _mouseFrom;
        private float _mouseTarget;
        private DateTime _mouseStarted;

        private bool _filterCaps;
        private bool _filterCtrl;
        private bool _filterAlt;
        private bool _filterShift;
        private bool _filterWin;
        private Keys _selectedKey = Keys.None;

        // ---- construction ----------------------------------------------------
        private void BuildKeyboardParts(Control page)
        {
            _keyboardHeader = new MKeyboardHeader();
            _keyboardHeader.Toggled += delegate { ToggleKeyboard(); };
            page.Controls.Add(_keyboardHeader);

            _keyboardHost = new Panel();
            _keyboardHost.BackColor = Theme.Background;
            page.Controls.Add(_keyboardHost);

            _keyboard = new MKeyboardView();
            _keyboard.ModifierToggleRequested += delegate(KeyModifier modifier)
            {
                ToggleKeyboardFilter(modifier);
            };
            _keyboard.SelectionChanged += delegate { OnKeyboardSelectionChanged(); };
            _keyboard.KeyActivated += delegate { ActivateSelectedKey(); };
            _keyboard.KeyContextRequested += delegate { ShowKeyboardMenu(); };
            _keyboard.KeyDragDropped += delegate(Keys from, Keys to) { MoveKeyBindings(from, to); };
            // A cap dragged past the board edge can land on a mouse zone.
            _keyboard.ExternalDropTarget = delegate(Point screen)
            {
                return _mousePad != null && _mousePad.Visible
                    ? _mousePad.ZoneAtScreen(screen) : Keys.None;
            };
            _keyboard.KeyDragDroppedExternal += delegate(Keys from, Keys to) { MoveKeyBindings(from, to); };
            _keyboard.ExternalTargetChanged += delegate(Keys key)
            {
                if (_mousePad != null) _mousePad.SetDropTarget(key);
            };
            _keyboard.DragPointerMoved += delegate(DragGhostCard card, Point screen, bool outside)
            {
                if (_mousePad != null && _mousePad.Visible)
                    _mousePad.SetExternalDrag(card, screen, outside);
            };
            _keyboard.DragFinished += delegate
            {
                if (_mousePad != null) _mousePad.SetExternalDrag(null, Point.Empty, false);
            };
            _keyboardHost.Controls.Add(_keyboard);

            _mousePad = new MMousePad();
            // ... and a zone dragged past the card edge can land on a keycap.
            _mousePad.ExternalDropTarget = delegate(Point screen)
            {
                return _keyboard == null ? Keys.None : _keyboard.KeyAtScreen(screen);
            };
            _mousePad.ZoneDropped += delegate(Keys from, Keys to) { MoveKeyBindings(from, to); };
            _mousePad.ExternalTargetChanged += delegate(Keys key)
            {
                if (_keyboard != null) _keyboard.SetExternalDropKey(key);
            };
            _mousePad.DragPointerMoved += delegate(DragGhostCard card, Point screen, bool outside)
            {
                if (_keyboard != null) _keyboard.SetExternalDrag(card, screen, outside);
            };
            _mousePad.DragFinished += delegate
            {
                if (_keyboard != null) _keyboard.SetExternalDrag(null, Point.Empty, false);
            };
            _mousePad.ZoneClicked += delegate(Keys key) { SelectMouseZone(key, true); };
            _mousePad.ZoneContextRequested += delegate(Keys key)
            {
                SelectMouseZone(key, false);
                ShowKeyboardMenu();
            };
            _mousePad.ZoneActivated += delegate(Keys key)
            {
                SelectMouseZone(key, false);
                ActivateSelectedKey();
            };
            _mousePad.CollapseRequested += delegate(bool collapse)
            {
                StartMouseAnimation(collapse ? 1f : 0f);
            };
            page.Controls.Add(_mousePad);
        }

        /// <summary>
        /// Selects a mouse zone. A plain click toggles like a keycap; the context
        /// menu and the double-click path force the selection because they act on
        /// the zone the user just pointed at.
        /// </summary>
        private void SelectMouseZone(Keys key, bool toggle)
        {
            if (key == Keys.None) return;
            if (_keyboard != null) _keyboard.SetSelectedKey(Keys.None);
            _selectedKey = toggle && _selectedKey == key ? Keys.None : key;
            RefreshList();
        }

        /// <summary>Height the board wants at this page width, capped so the list keeps room.</summary>
        private int KeyboardContentHeight(int pageWidth, int pageHeight, int headerTop)
        {
            int natural = (int)Math.Round(pageWidth
                * (double)KeyboardLayout.TotalHeight / KeyboardLayout.TotalWidth);
            int reserved = Theme.S(110);
            int available = pageHeight - headerTop - Theme.S(28) - Theme.S(12) - reserved;
            int height = Math.Min(natural, Math.Max(Theme.S(80), available));
            return Math.Max(Theme.S(60), height);
        }

        /// <summary>Positions header, clipped host and board; returns the section bottom.</summary>
        private int LayoutKeyboard(int headerTop, int width, int fullHeight)
        {
            if (_keyboardHeader == null || _keyboardHost == null || _keyboard == null) return headerTop;

            _keyboardHeader.SetBounds(0, headerTop, width, Theme.S(28));

            int visible = (int)Math.Round(fullHeight * _keyboardProgress);
            if (visible < 0) visible = 0;
            int hostTop = headerTop + Theme.S(28) + Theme.S(4);

            _keyboardHost.SetBounds(0, hostTop, width, visible);
            // The board keeps its full height inside the host: the host's client
            // area clips it, so collapsing reveals it instead of squashing the keys.
            _keyboard.SetBounds(0, 0, width, fullHeight);
            _keyboardHeader.ChevronProgress = _keyboardProgress;
            return hostTop + visible;
        }

        // ---- collapse / expand ------------------------------------------------
        /// <summary>
        /// Slide the mouse card between the open width and the 30px rail. Same clock
        /// as the keyboard: 15 ms ticks, 220 ms, ease-out cubic, and the value is
        /// driven from here so an interrupted toggle can reverse from where it is.
        /// </summary>
        private void StartMouseAnimation(float target)
        {
            _mouseFrom = _mouseProgress;
            _mouseTarget = target;
            _mouseStarted = DateTime.UtcNow;
            if (_mouseTimer == null)
            {
                _mouseTimer = new Timer();
                _mouseTimer.Interval = 15;
                _mouseTimer.Tick += delegate { TickMouseAnimation(); };
            }
            _mouseTimer.Start();
            TickMouseAnimation();
        }

        private void TickMouseAnimation()
        {
            if (IsDisposed) return;

            float t = (float)((DateTime.UtcNow - _mouseStarted).TotalMilliseconds
                / (double)MouseAnimationMs);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            float eased = 1f - (float)Math.Pow(1.0 - t, 3.0);   // ease-out cubic
            _mouseProgress = _mouseFrom + (_mouseTarget - _mouseFrom) * eased;

            if (t >= 1f)
            {
                _mouseProgress = _mouseTarget;
                if (_mouseTimer != null) _mouseTimer.Stop();
            }

            bool collapsed = _mouseProgress >= 0.999f;
            if (collapsed != _mousePadCollapsed)
            {
                _mousePadCollapsed = collapsed;
                // A hidden card must not keep a filter that has no visible source.
                if (collapsed && MouseKeys.IsMouseKey(_selectedKey))
                {
                    _selectedKey = Keys.None;
                    RefreshList();
                }
            }
            if (_mousePad != null) _mousePad.CollapseProgress = _mouseProgress;
            LayoutContent();
        }

        private void DisposeMouseAnimation()
        {
            if (_mouseTimer == null) return;
            _mouseTimer.Stop();
            _mouseTimer.Dispose();
            _mouseTimer = null;
        }

        // ---- keyboard collapse / expand ---------------------------------------
        private void ToggleKeyboard()
        {
            _keyboardExpanded = !_keyboardExpanded;
            _keyboardFrom = _keyboardProgress;
            _keyboardTarget = _keyboardExpanded ? 1f : 0f;
            _keyboardStarted = DateTime.UtcNow;

            if (_keyboardTimer == null)
            {
                _keyboardTimer = new Timer();
                _keyboardTimer.Interval = 15;
                _keyboardTimer.Tick += delegate { TickKeyboardAnimation(); };
            }
            _keyboardTimer.Start();
            TickKeyboardAnimation();
        }

        private void TickKeyboardAnimation()
        {
            if (IsDisposed) return;

            float t = (float)((DateTime.UtcNow - _keyboardStarted).TotalMilliseconds
                / (double)KeyboardAnimationMs);
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            float eased = 1f - (float)Math.Pow(1.0 - t, 3.0);   // ease-out cubic
            _keyboardProgress = _keyboardFrom + (_keyboardTarget - _keyboardFrom) * eased;

            if (t >= 1f)
            {
                _keyboardProgress = _keyboardTarget;
                if (_keyboardTimer != null) _keyboardTimer.Stop();
            }
            if (_keyboardHeader != null)
            {
                _keyboardHeader.ChevronProgress = _keyboardProgress;
                _keyboardHeader.Invalidate();
            }
            LayoutContent();
        }

        private void DisposeKeyboardAnimation()
        {
            if (_keyboardTimer == null) return;
            _keyboardTimer.Stop();
            _keyboardTimer.Dispose();
            _keyboardTimer = null;
        }

        // ---- filter ----------------------------------------------------------
        private bool HasKeyboardFilter
        {
            get { return _filterCaps || _filterCtrl || _filterAlt || _filterShift || _filterWin; }
        }

        private void ToggleKeyboardFilter(KeyModifier modifier)
        {
            switch (modifier)
            {
                case KeyModifier.Caps: _filterCaps = !_filterCaps; break;
                case KeyModifier.Ctrl: _filterCtrl = !_filterCtrl; break;
                case KeyModifier.Alt: _filterAlt = !_filterAlt; break;
                case KeyModifier.Shift: _filterShift = !_filterShift; break;
                case KeyModifier.Win: _filterWin = !_filterWin; break;
                default: return;
            }
            // Clicking a modifier is a new selection on the board, so the main key
            // picked before lets go — the same hand-off as clicking another key.
            // Keeping it made the two clicks behave differently for no reason.
            _selectedKey = Keys.None;
            if (_keyboard != null) _keyboard.SetSelectedKey(Keys.None);
            RefreshList();
        }

        private void ClearKeyboardFilter()
        {
            if (!HasKeyboardFilter) return;
            _filterCaps = false;
            _filterCtrl = false;
            _filterAlt = false;
            _filterShift = false;
            _filterWin = false;
            RefreshList();
        }

        private string KeyboardFilterText()
        {
            List<string> parts = new List<string>();
            if (_filterCaps) parts.Add("Caps Lock");
            if (_filterCtrl) parts.Add("Ctrl");
            if (_filterAlt) parts.Add("Alt");
            if (_filterShift) parts.Add("Shift");
            if (_filterWin) parts.Add("Win");
            if (parts.Count == 0) return "未选择修饰键";
            return string.Join(" + ", parts.ToArray());
        }

        private void OnKeyboardSelectionChanged()
        {
            if (_keyboard == null) return;
            Keys key = _keyboard.SelectedKey;
            if (key == _selectedKey) return;
            _selectedKey = key;
            RefreshList();
        }

        /// <summary>Pushes the current data/filter into the board; never refreshes the list.</summary>
        private void RefreshKeyboard()
        {
            if (_keyboard != null)
            {
                _keyboard.SetMappings(MappingList);
                _keyboard.SetFilter(_filterCaps, _filterCtrl, _filterAlt, _filterShift, _filterWin);
                _keyboard.SetSelectedKey(_selectedKey);
            }
            if (_mousePad != null)
            {
                _mousePad.SetMappings(MappingList);
                _mousePad.SetFilter(_filterCaps, _filterCtrl, _filterAlt, _filterShift, _filterWin);
                _mousePad.SelectedKey = _selectedKey;
            }
        }

        private void UpdateKeyboardSummary(int visibleCount)
        {
            if (_keyboardHeader == null) return;
            // While nothing is selected the board stays neutral on purpose, so the
            // header has to say how to mark it instead of showing a count.
            string text = HasKeyboardFilter
                ? KeyboardFilterText() + " · " + visibleCount + " 条"
                : "点选修饰键以标明主键";
            if (_selectedKey != Keys.None) text += " · 主键 " + KeyText.FormatKey(_selectedKey);
            _keyboardHeader.Summary = text;
            _keyboardHeader.ChevronProgress = _keyboardProgress;
            UpdateKeysHint();
        }

        /// <summary>
        /// Footer hint. The list supports multi-select and drag reordering, both
        /// surfaces open the editor on a double-click and both have a right-click
        /// menu, so one hint fits every state; only its width decides the form.
        /// </summary>
        private void UpdateKeysHint()
        {
            if (_hintLabel == null) return;
            int width = _hintLabel.Width > 0 ? _hintLabel.Width : Theme.S(400);
            if (Theme.Measure(KeysHintFull, Theme.Caption).Width <= width)
                _hintLabel.Text = KeysHintFull;
            else if (Theme.Measure(KeysHintMedium, Theme.Caption).Width <= width)
                _hintLabel.Text = KeysHintMedium;
            else
                _hintLabel.Text = KeysHintShort;
        }

        private void ClearKeyboardSelection()
        {
            if (_selectedKey == Keys.None && _keyboard != null)
            {
                _keyboard.ClearSelection();
                return;
            }
            _selectedKey = Keys.None;
            if (_keyboard != null) _keyboard.SetSelectedKey(Keys.None);
            RefreshList();
        }

        // ---- editing ---------------------------------------------------------
        private MappingListItem KeyboardSelectedRow
        {
            get { return _list == null ? null : _list.SelectedItem as MappingListItem; }
        }

        /// <summary>All mappings on the selected key inside the current filter, in list order.</summary>
        private List<KeyMapping> MappingsOnSelectedKey()
        {
            return MappingsOnKey(_selectedKey);
        }

        /// <summary>
        /// The only mapping on the selected key inside the current filter, or null
        /// when the key is empty or carries several combos.
        /// </summary>
        private KeyMapping SingleSelectedKeyMapping()
        {
            // Without a chord there is no "the" shortcut on this key: the neutral
            // overview only narrows the list, editing waits for a modifier.
            if (!HasKeyboardFilter) return null;
            List<KeyMapping> found = MappingsOnSelectedKey();
            return found.Count == 1 ? found[0] : null;
        }

        /// <summary>
        /// Double-click / Enter on a cap: open the editor for the shortcut sitting on
        /// that key. A key with several combos first asks which one to edit, so a
        /// double-click never guesses. An empty key in a chord starts a new shortcut
        /// on it, which is the fastest way to bind a key that carries nothing yet.
        /// </summary>
        private void ActivateSelectedKey()
        {
            if (_selectedKey == Keys.None || !HasKeyboardFilter) return;
            List<KeyMapping> onKey = MappingsOnSelectedKey();
            if (onKey.Count == 0)
            {
                AddMappingForKey();
                return;
            }

            if (onKey.Count == 1)
            {
                EditMapping(onKey[0]);
                return;
            }

            MPopupMenu menu = new MPopupMenu();
            for (int i = 0; i < onKey.Count; i++)
            {
                KeyMapping mapping = onKey[i];
                menu.AddItem(mapping.TriggerText, mapping.ActionText,
                    delegate { EditMapping(mapping); });
            }
            menu.AddSeparator();
            menu.AddItem("新增到此主键…", "Ctrl+N", delegate { AddMappingForKey(); });
            menu.ShowAt(Cursor.Position);
        }

        private void AddMappingForKey()
        {
            KeyMapping draft = new KeyMapping();
            // Only the Caps layer can be saved, so Caps is always pre-checked;
            // the modifier filters still pre-check the other chips.
            draft.Caps = true;
            draft.Ctrl = _filterCtrl;
            draft.Alt = _filterAlt;
            draft.Shift = _filterShift;
            draft.Win = _filterWin;
            draft.KeyCode = _selectedKey;
            draft.Action = "input.keys";
            // The trigger's modifier chips are pre-checked above, but the key
            // combination this action sends stays empty for the user to pick.
            draft.Param = "";
            draft.Note = "";
            AddMappingWithDraft(draft);
        }

        /// <summary>
        /// Drag-and-drop on the board: every shortcut sitting on <paramref name="from"/>
        /// inside the current chord moves onto <paramref name="to"/>. Kept as a plain
        /// model edit so the list, the board and the saved file stay one story.
        /// </summary>
        private void MoveKeyBindings(Keys from, Keys to)
        {
            if (from == Keys.None || to == Keys.None || from == to) return;

            List<KeyMapping> moving = MappingsOnKey(from);
            if (moving.Count == 0) return;

            List<KeyMapping> occupied = MappingsOnKey(to);
            if (occupied.Count > 0)
            {
                string message = "主键 " + KeyText.FormatKey(to) + " 已有 " + occupied.Count
                    + " 条相同组合，移动后将覆盖它们。是否继续？";
                if (!MDialog.Confirm(this, "移动快捷键", message)) return;
            }
            PushMappingUndo("移动快捷键");
            List<KeyMapping> mappings = MappingList;
            foreach (KeyMapping existing in occupied) mappings.Remove(existing);

            foreach (KeyMapping mapping in moving) mapping.KeyCode = to;

            _selectedKey = to;
            SaveAndApply();
            RefreshList();
            ShowStatus("已把 " + moving.Count + " 条快捷键从 " + KeyText.FormatKey(from)
                + " 移到 " + KeyText.FormatKey(to), MInfoSeverity.Success);
        }

        /// <summary>Filtered mappings on one key; used by the drag handler.</summary>
        private List<KeyMapping> MappingsOnKey(Keys key)
        {
            List<KeyMapping> found = new List<KeyMapping>();
            if (key == Keys.None) return found;
            foreach (KeyMapping mapping in MappingList)
            {
                if (mapping == null) continue;
                if ((mapping.Key & (int)Keys.KeyCode) != ((int)key & (int)Keys.KeyCode)) continue;
                if (!KeyboardFilter.Matches(mapping, _filterCaps, _filterCtrl,
                    _filterAlt, _filterShift, _filterWin)) continue;
                found.Add(mapping);
            }
            return found;
        }

        /// <summary>Enter / double-click on the board: edit the combo sitting on that key.</summary>
        private void EditSelectedKeyMapping()
        {
            KeyMapping single = SingleSelectedKeyMapping();
            if (single != null)
            {
                EditMapping(single);
                return;
            }
            MappingListItem row = KeyboardSelectedRow;
            if (row != null) EditMapping(row.Mapping);
        }

        // Bindings copied from a key travel through here. Cut/copy/paste are just
        // model edits, so the clipboard survives page switches and list rebuilds.
        private List<KeyMapping> _keyClipboard;

        /// <summary>True when there is a chord key to read from or write to.</summary>
        private bool HasBindingTarget
        {
            get { return HasKeyboardFilter && _selectedKey != Keys.None; }
        }

        private bool HasKeyClipboard
        {
            get { return _keyClipboard != null && _keyClipboard.Count > 0; }
        }

        /// <summary>Copies every shortcut on the selected key into the in-app clipboard.</summary>
        private void CopyKeyBindings()
        {
            if (!HasBindingTarget)
            {
                ShowStatus("先在键盘上选一个主键，再复制", MInfoSeverity.Info);
                return;
            }
            List<KeyMapping> source = MappingsOnKey(_selectedKey);
            if (source.Count == 0)
            {
                ShowStatus(KeyText.FormatKey(_selectedKey) + " 上没有可复制的快捷键", MInfoSeverity.Info);
                return;
            }
            _keyClipboard = new List<KeyMapping>();
            foreach (KeyMapping mapping in source) _keyClipboard.Add(mapping.Clone());
            ShowStatus("已复制 " + source.Count + " 条快捷键（" + KeyText.FormatKey(_selectedKey) + "）",
                MInfoSeverity.Success);
        }

        /// <summary>Copies and removes the selected key's shortcuts.</summary>
        private void CutKeyBindings()
        {
            if (!HasBindingTarget)
            {
                ShowStatus("先在键盘上选一个主键，再剪切", MInfoSeverity.Info);
                return;
            }
            List<KeyMapping> source = MappingsOnKey(_selectedKey);
            if (source.Count == 0)
            {
                ShowStatus(KeyText.FormatKey(_selectedKey) + " 上没有可剪切的快捷键", MInfoSeverity.Info);
                return;
            }
            _keyClipboard = new List<KeyMapping>();
            PushMappingUndo("剪切快捷键");
            foreach (KeyMapping mapping in source)
            {
                _keyClipboard.Add(mapping.Clone());
                MappingList.Remove(mapping);
            }
            Keys from = _selectedKey;
            SaveAndApply();
            RefreshList();
            ShowStatus("已剪切 " + source.Count + " 条快捷键（" + KeyText.FormatKey(from) + "）",
                MInfoSeverity.Success);
        }

        /// <summary>Deletes every shortcut on the selected chord; used by the Del command.</summary>
        private void DeleteKeyBindings()
        {
            if (!HasBindingTarget)
            {
                ShowStatus("先在键盘上选一个主键，再删除", MInfoSeverity.Info);
                return;
            }
            Keys key = _selectedKey;
            List<KeyMapping> source = MappingsOnKey(key);
            if (source.Count == 0)
            {
                ShowStatus(KeyText.FormatKey(key) + " 上没有可删除的快捷键", MInfoSeverity.Info);
                return;
            }
            string message = "确定要删除 " + KeyText.FormatKey(key) + " 上的 "
                + source.Count + " 条快捷键吗？";
            if (!MDialog.Confirm(this, "删除快捷键", message)) return;

            PushMappingUndo("删除快捷键");
            foreach (KeyMapping mapping in source) MappingList.Remove(mapping);
            SaveAndApply();
            RefreshList();
            ShowStatus("已删除 " + source.Count + " 条快捷键（" + KeyText.FormatKey(key) + "）",
                MInfoSeverity.Info);
        }

        /// <summary>Writes the clipboard onto the selected key, asking before overwriting.</summary>
        private void PasteKeyBindings()
        {
            if (!HasKeyClipboard)
            {
                ShowStatus("剪贴板是空的", MInfoSeverity.Info);
                return;
            }
            if (!HasBindingTarget)
            {
                ShowStatus("先在键盘上选一个目标主键，再粘贴", MInfoSeverity.Info);
                return;
            }
            if (!_filterCaps)
            {
                ShowStatus("粘贴目标需要 Caps Lock 组合键", MInfoSeverity.Info);
                return;
            }

            Keys to = _selectedKey;
            List<KeyMapping> occupied = MappingsOnKey(to);
            if (occupied.Count > 0)
            {
                string message = "主键 " + KeyText.FormatKey(to) + " 已有 " + occupied.Count
                    + " 条相同组合，粘贴后将覆盖它们。是否继续？";
                if (!MDialog.Confirm(this, "粘贴快捷键", message)) return;
            }
            PushMappingUndo("粘贴快捷键");
            List<KeyMapping> mappings = MappingList;
            foreach (KeyMapping existing in occupied) mappings.Remove(existing);

            int pasted = 0;
            KeyMapping lastPasted = null;
            // The clipboard roams across chords: paste always lands in the chord
            // that is selected right now (Caps is the required prefix, the other
            // toggles come from the filter), not in the chord that was copied.
            foreach (KeyMapping clipboard in _keyClipboard)
            {
                KeyMapping copy = clipboard.Clone();
                copy.Id = Guid.NewGuid().ToString("N");
                copy.KeyCode = to;
                copy.Caps = true;
                copy.Ctrl = _filterCtrl;
                copy.Alt = _filterAlt;
                copy.Shift = _filterShift;
                copy.Win = _filterWin;
                mappings.Add(copy);
                lastPasted = copy;
                pasted++;
            }
            SaveAndApply();
            RefreshList();
            string target = lastPasted != null ? lastPasted.TriggerText : KeyText.FormatKey(to);
            ShowStatus("已把 " + pasted + " 条快捷键粘贴到 " + target, MInfoSeverity.Success);
        }

        private void ShowKeyboardMenu()
        {
            MPopupMenu menu = new MPopupMenu();
            string keyName = _selectedKey == Keys.None ? "键位" : KeyText.FormatKey(_selectedKey);
            menu.AddItem("新增到 " + keyName, "Ctrl+N", delegate { AddMappingForKey(); });

            MappingListItem row = KeyboardSelectedRow;
            // Row actions only make sense for a concrete chord: in the neutral
            // overview the key is just the target of "新增到".
            if (row != null && HasKeyboardFilter)
            {
                menu.AddSeparator();
                menu.AddItem("编辑…", "Enter", delegate { EditMapping(row.Mapping); });
                menu.AddItem(row.Mapping.Enabled ? "停用" : "启用", "Space",
                    delegate { ToggleRow(row); });
                menu.AddItem("删除", "Del", delegate { DeleteRow(row); });
            }

            // Clipboard works per key, so it stays available even when the filter
            // narrowed the list down to nothing.
            if (HasBindingTarget)
            {
                menu.AddSeparator();
                bool hasSource = MappingsOnKey(_selectedKey).Count > 0;
                menu.AddItem("剪切", "Ctrl+X", delegate { CutKeyBindings(); }, hasSource);
                menu.AddItem("复制", "Ctrl+C", delegate { CopyKeyBindings(); }, hasSource);
                menu.AddItem("粘贴到 " + keyName, "Ctrl+V", delegate { PasteKeyBindings(); },
                    HasKeyClipboard);
            }
            menu.ShowAt(Cursor.Position);
        }
    }
}
