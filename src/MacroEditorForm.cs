using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// Macro editor: name, note and an ordered step list. Steps can be dragged,
    /// cut/copied/pasted like text, and each one carries a delay and a repeat
    /// count so a macro can express "wait a bit, then do it three times".
    /// </summary>
    internal sealed class MacroEditorForm : Form
    {
        private static List<MacroStep> _clipboard = new List<MacroStep>();

        private const int StepHistoryLimit = 50;

        // Undo for the step list. A snapshot is a deep copy of the steps taken
        // before one mutation, so restoring it is just swapping the list back.
        // Text fields keep their own native undo; this stack only covers steps.
        private readonly List<StepSnapshot> _stepUndo = new List<StepSnapshot>();
        private readonly List<StepSnapshot> _stepRedo = new List<StepSnapshot>();

        private sealed class StepSnapshot
        {
            public List<MacroStep> Steps;
            public string Label;
        }

        private readonly Macro _draft;
        private readonly Macro _original;
        private readonly IList<Macro> _allMacros;

        private Label _nameLabel;
        private Label _noteLabel;
        private MTextBox _nameBox;
        private MTextBox _noteBox;
        private MToggle _restoreToggle;
        private Label _restoreLabel;
        private MListView _steps;
        private MCard _infoCard;
        private MCard _stepsCard;
        private MButton _addAction;
        private MButton _editStep;
        private MButton _remove;
        private MButton _cancel;
        private MButton _save;
        private Label _stepCount;

        public Macro Result { get; private set; }

        public MacroEditorForm(Macro macro, IList<Macro> allMacros)
            : this(macro, allMacros, macro == null)
        {
        }

        public MacroEditorForm(Macro macro, IList<Macro> allMacros, bool isNew)
        {
            _original = macro;
            _draft = macro == null ? new Macro() : macro.Clone();
            _allMacros = allMacros ?? new List<Macro>();

            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = isNew ? "新建宏" : "编辑宏";
            ClientSize = new Size(Theme.S(900), Theme.S(640));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            int top = Theme.DialogHeader(this, Text, "按顺序执行；每步可设延迟与次数，列表可拖动排序。");
            int pad = Theme.S(Theme.PagePadding);
            int width = ClientSize.Width - pad * 2;

            _infoCard = new MCard();
            Controls.Add(_infoCard);

            _nameLabel = Theme.MakeLabel("名称", Theme.Body, Theme.TextSecondary, 0, 0, Theme.S(90), Theme.S(28));
            _infoCard.Controls.Add(_nameLabel);
            _nameBox = new MTextBox();
            _nameBox.Cue = "例如：一键整理窗口";
            _nameBox.Text = _draft.Name;
            _infoCard.Controls.Add(_nameBox);

            _noteLabel = Theme.MakeLabel("备注", Theme.Body, Theme.TextSecondary, 0, 0, Theme.S(90), Theme.S(28));
            _infoCard.Controls.Add(_noteLabel);
            _noteBox = new MTextBox();
            _noteBox.Cue = "可选，写清楚这个宏做什么";
            _noteBox.Text = _draft.Note;
            _infoCard.Controls.Add(_noteBox);

            _restoreToggle = new MToggle();
            _restoreToggle.Checked = _draft.RestoreCursor;
            _infoCard.Controls.Add(_restoreToggle);
            _restoreLabel = Theme.MakeLabel("执行后返回鼠标原位置", Theme.Body, Theme.TextSecondary,
                0, 0, Theme.S(260), Theme.S(24));
            _infoCard.Controls.Add(_restoreLabel);

            _addAction = MakeButton("添加动作…", MButtonKind.Primary, delegate { AddStepFromCatalog(); });
            _editStep = MakeButton("编辑…", MButtonKind.Secondary, delegate { EditSelectedStep(); });
            _remove = MakeButton("删除", MButtonKind.Danger, delegate { RemoveSteps(); });
            Controls.Add(_addAction);
            Controls.Add(_editStep);
            Controls.Add(_remove);

            _stepsCard = new MCard();
            _stepsCard.Padding = new Padding(1);
            // Full-bleed list: a rounded card would leave a corner ring behind it.
            _stepsCard.Radius = 0;
            Controls.Add(_stepsCard);

            _steps = new MListView();
            _steps.RowHeight = Theme.S(46);
            _steps.HeaderHeight = Theme.S(34);
            _steps.MultiSelect = true;
            _steps.AllowReorder = true;
            _steps.EmptyText = "还没有步骤\r\n点“添加动作…”，从动作库里挑一个";
            _steps.HeaderPainter = delegate(Graphics g, Rectangle r)
            {
                Theme.Prepare(g);
                using (SolidBrush b = new SolidBrush(Theme.Surface))
                    g.FillRectangle(b, r);
                TextRenderer.DrawText(g, "步数", Theme.CaptionStrong,
                    new Rectangle(Theme.S(18), r.Y, Theme.S(40), r.Height), Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "动作", Theme.CaptionStrong,
                    new Rectangle(Theme.S(74), r.Y, Theme.S(220), r.Height), Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "参数", Theme.CaptionStrong,
                    new Rectangle(r.Right - Theme.S(266), r.Y, Theme.S(120), r.Height), Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "延迟", Theme.CaptionStrong,
                    new Rectangle(r.Right - Theme.S(134), r.Y, Theme.S(60), r.Height), Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "次数", Theme.CaptionStrong,
                    new Rectangle(r.Right - Theme.S(60), r.Y, Theme.S(44), r.Height), Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
            };
            _steps.ItemActivated += delegate { EditSelectedStep(); };
            _steps.ItemDeleteRequested += delegate { RemoveSteps(); };
            _steps.ItemContextRequested += delegate { ShowStepMenu(); };
            _steps.EmptyContextRequested += delegate { ShowStepMenu(); };
            _steps.SelectionChanged += delegate { UpdateButtons(); };
            _steps.ItemsReordered += delegate
            {
                // Numbers follow the visual order, so rebuild the rows after a drop.
                // The model still has the old order here, which is exactly what the
                // undo snapshot needs.
                PushStepUndo("调整步骤顺序");
                int lead = _steps.SelectedIndex;
                SyncStepsFromList();
                RefreshSteps(lead);
            };
            _stepsCard.Controls.Add(_steps);

            _stepCount = Theme.MakeLabel("", Theme.Caption, Theme.TextTertiary, 0, 0, Theme.S(240), Theme.S(18));
            Controls.Add(_stepCount);

            _cancel = MakeButton("取消", MButtonKind.Secondary, delegate { DialogResult = DialogResult.Cancel; Close(); });
            _save = MakeButton("保存", MButtonKind.Primary, delegate { SaveAndClose(); });
            Controls.Add(_cancel);
            Controls.Add(_save);
            AcceptButton = null;
            CancelButton = null;

            LayoutEditor(top, pad, width);
            RefreshSteps(-1);
            UpdateStepCount();
            UpdateButtons();
        }

        private MButton MakeButton(string text, MButtonKind kind, EventHandler click)
        {
            MButton button = new MButton();
            button.Text = text;
            button.Kind = kind;
            button.Font = Theme.BodyStrong;
            button.Click += click;
            return button;
        }

        private void LayoutEditor(int top, int pad, int width)
        {
            int y = top;
            int row = Theme.S(34);
            int gap = Theme.S(8);

            _infoCard.SetBounds(pad, y, width, Theme.S(136));
            _nameLabel.SetBounds(Theme.S(16), Theme.S(20), Theme.S(80), Theme.S(28));
            _nameBox.SetBounds(Theme.S(100), Theme.S(14), width - Theme.S(132), Theme.S(34));
            _noteLabel.SetBounds(Theme.S(16), Theme.S(62), Theme.S(80), Theme.S(28));
            _noteBox.SetBounds(Theme.S(100), Theme.S(56), width - Theme.S(132), Theme.S(34));
            // Keep the option on the right edge of the card: label first, switch last.
            int toggleWidth = Theme.S(40);
            int restoreLabelWidth = Theme.S(208);
            int restoreRight = width - Theme.S(16);
            _restoreToggle.SetBounds(restoreRight - toggleWidth, Theme.S(102), toggleWidth, Theme.S(20));
            _restoreLabel.SetBounds(restoreRight - toggleWidth - Theme.S(10) - restoreLabelWidth,
                Theme.S(100), restoreLabelWidth, Theme.S(24));
            _restoreLabel.TextAlign = ContentAlignment.MiddleRight;

            y += Theme.S(136) + Theme.S(12);
            int x = pad;
            _addAction.SetBounds(x, y, Theme.S(112), row); x += Theme.S(112) + gap;
            _editStep.SetBounds(x, y, Theme.S(80), row); x += Theme.S(80) + gap;
            _remove.SetBounds(x, y, Theme.S(64), row);
            _stepCount.SetBounds(pad + width - Theme.S(240), y, Theme.S(240), row);
            _stepCount.TextAlign = ContentAlignment.MiddleRight;

            y += row + Theme.S(10);
            int footer = Theme.S(34);
            int buttonY = ClientSize.Height - pad - footer;
            int cardBottom = buttonY - Theme.S(16);
            int cardHeight = Math.Max(Theme.S(160), cardBottom - y);
            _stepsCard.SetBounds(pad, y, width, cardHeight);
            _steps.SetBounds(1, 1,
                Math.Max(1, _stepsCard.ClientSize.Width - 2),
                Math.Max(1, _stepsCard.ClientSize.Height - 2));

            _cancel.SetBounds(ClientSize.Width - pad - Theme.S(96) - Theme.S(96) - gap, buttonY,
                Theme.S(96), footer);
            _save.SetBounds(ClientSize.Width - pad - Theme.S(96), buttonY, Theme.S(96), footer);
        }

        /// <summary>
        /// The list owns the visual order; the macro model owns the step objects, so a
        /// drag has to be written back before the editor closes. Without this the rows
        /// move on screen but saving keeps the old order.
        /// </summary>
        private void SyncStepsFromList()
        {
            if (_draft == null || _steps == null) return;
            List<MacroStep> ordered = new List<MacroStep>(_draft.Steps.Count);
            foreach (MListItem item in _steps.Items)
            {
                MacroStepListItem row = item as MacroStepListItem;
                if (row != null && row.Step != null) ordered.Add(row.Step);
            }
            _draft.Steps = MacroLibrary.ApplyStepOrder(_draft.Steps, ordered);
        }

        private List<MacroStep> CloneSteps()
        {
            List<MacroStep> copy = new List<MacroStep>(_draft.Steps.Count);
            for (int i = 0; i < _draft.Steps.Count; i++) copy.Add(_draft.Steps[i].Clone());
            return copy;
        }

        /// <summary>Records the current step list as the state to go back to.</summary>
        private void PushStepUndo(string label)
        {
            StepSnapshot snapshot = new StepSnapshot();
            snapshot.Steps = CloneSteps();
            snapshot.Label = label;
            PushStepUndo(snapshot);
        }

        private void PushStepUndo(StepSnapshot snapshot)
        {
            if (snapshot == null) return;
            _stepUndo.Add(snapshot);
            if (_stepUndo.Count > StepHistoryLimit) _stepUndo.RemoveAt(0);
            _stepRedo.Clear();
        }

        private void UndoSteps()
        {
            if (_stepUndo.Count == 0) return;
            StepSnapshot snapshot = _stepUndo[_stepUndo.Count - 1];
            _stepUndo.RemoveAt(_stepUndo.Count - 1);
            StepSnapshot redo = new StepSnapshot();
            redo.Steps = CloneSteps();
            redo.Label = snapshot.Label;
            _stepRedo.Add(redo);
            if (_stepRedo.Count > StepHistoryLimit) _stepRedo.RemoveAt(0);
            ApplySteps(snapshot.Steps);
        }

        private void RedoSteps()
        {
            if (_stepRedo.Count == 0) return;
            StepSnapshot snapshot = _stepRedo[_stepRedo.Count - 1];
            _stepRedo.RemoveAt(_stepRedo.Count - 1);
            StepSnapshot undo = new StepSnapshot();
            undo.Steps = CloneSteps();
            undo.Label = snapshot.Label;
            _stepUndo.Add(undo);
            if (_stepUndo.Count > StepHistoryLimit) _stepUndo.RemoveAt(0);
            ApplySteps(snapshot.Steps);
        }

        private void ApplySteps(List<MacroStep> steps)
        {
            _draft.Steps = new List<MacroStep>(steps.Count);
            for (int i = 0; i < steps.Count; i++) _draft.Steps.Add(steps[i].Clone());
            RefreshSteps(-1);
        }

        private void RefreshSteps(int selectIndex)
        {
            List<MListItem> rows = new List<MListItem>();
            for (int i = 0; i < _draft.Steps.Count; i++)
            {
                MacroStep step = _draft.Steps[i];
                bool missing = ActionCatalog.Get(step.Action).Missing;
                MacroStepListItem row = new MacroStepListItem(step, missing);
                row.StepNumber = i + 1;
                rows.Add(row);
            }
            int lead = selectIndex >= 0 && selectIndex < rows.Count ? selectIndex
                : rows.Count == 0 ? -1 : Math.Min(Math.Max(0, _steps.SelectedIndex), rows.Count - 1);
            _steps.SetItems(rows);
            if (lead >= 0) _steps.SelectedIndex = lead;
            UpdateStepCount();
            UpdateButtons();
        }

        private void UpdateStepCount()
        {
            if (_stepCount == null) return;
            int count = _draft.Steps.Count;
            _stepCount.Text = count == 0 ? "共 0 步" : "共 " + count + " 步  ·  " + TotalRuns() + " 次执行";
        }

        private int TotalRuns()
        {
            int total = 0;
            for (int i = 0; i < _draft.Steps.Count; i++) total += Math.Max(1, _draft.Steps[i].Count);
            return total;
        }

        private void UpdateButtons()
        {
            bool any = _steps != null && _steps.SelectedCount > 0;
            bool single = _steps != null && _steps.SelectedCount == 1;
            if (_editStep != null) _editStep.Enabled = single;
            if (_remove != null) _remove.Enabled = any;
        }

        /// <summary>
        /// Step list menu: everything the toolbar used to show, plus the clipboard
        /// commands, so the toolbar can stay short (「添加动作… / 编辑… / 删除」).
        /// </summary>
        private void ShowStepMenu()
        {
            bool any = _steps.SelectedCount > 0;
            bool single = _steps.SelectedCount == 1;
            MPopupMenu menu = new MPopupMenu();
            menu.AddItem("添加动作…", "Ctrl+N", delegate { AddStepFromCatalog(); });
            menu.AddItem("编辑步骤…", "Enter", delegate { EditSelectedStep(); }, single);
            if (_clipboard.Count > 0)
                menu.AddItem("粘贴", "Ctrl+V", delegate { PasteSteps(); });
            else
                menu.AddItem("粘贴", "Ctrl+V", delegate { PasteSteps(); }, false);
            if (any)
            {
                menu.AddSeparator();
                menu.AddItem("剪切", "Ctrl+X", delegate { CutSteps(); });
                menu.AddItem("复制", "Ctrl+C", delegate { CopySteps(); });
                menu.AddItem("删除", "Delete", delegate { RemoveSteps(); });
                menu.AddSeparator();
            }
            menu.AddItem("全选", "Ctrl+A", delegate { _steps.SelectAll(); });
            menu.AddSeparator();
            menu.AddItem("撤回", "Ctrl+Z", delegate { UndoSteps(); }, _stepUndo.Count > 0);
            menu.AddItem("重做", "Ctrl+Y", delegate { RedoSteps(); }, _stepRedo.Count > 0);
            menu.ShowAt(Cursor.Position);
        }

        private int InsertIndex()
        {
            if (_steps.SelectedCount == 0) return _draft.Steps.Count;
            int max = -1;
            foreach (MListItem item in _steps.SelectedItems)
            {
                int index = _steps.Items.IndexOf(item);
                if (index > max) max = index;
            }
            return max < 0 ? _draft.Steps.Count : max + 1;
        }

        private void AddStepFromCatalog()
        {
            ActionPickerForm picker = new ActionPickerForm(null);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedAction == null) return;
            ActionDef def = picker.SelectedAction;
            if (string.Equals(def.Id, _draft.ActionId, StringComparison.OrdinalIgnoreCase))
            {
                MDialog.Tell(this, "不能嵌套自己", "宏不能在自身内部再调用自己。", MDialogKind.Warning);
                return;
            }
            MacroStep step = new MacroStep();
            step.Action = def.Id;
            step.Param = ActionCatalog.NormalizeParam(def.Id, ActionCatalog.DefaultParamFor(def));
            step.DelayMs = 50;
            step.Count = 1;
            // Snapshot before the dialog: the step is only inserted when the user
            // confirms, but the list it is compared against is the pre-insert one.
            StepSnapshot before = new StepSnapshot();
            before.Steps = CloneSteps();
            before.Label = "添加步骤";
            if (!EditStepDialog(step, def, true)) return;
            PushStepUndo(before);
            int at = InsertIndex();
            _draft.Steps.Insert(at, step);
            RefreshSteps(at);
        }

        private void AddStepFromMacro()
        {
            List<Macro> choices = new List<Macro>();
            for (int i = 0; i < _allMacros.Count; i++)
            {
                Macro m = _allMacros[i];
                if (m == null || m.Id == _draft.Id) continue;
                choices.Add(m);
            }
            if (choices.Count == 0)
            {
                MDialog.Tell(this, "没有可嵌套的宏", "先创建另一个宏，再把它加进来。", MDialogKind.Info);
                return;
            }
            MacroPickerForm picker = new MacroPickerForm(choices);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedMacro == null) return;
            MacroStep step = new MacroStep();
            step.Action = picker.SelectedMacro.ActionId;
            step.Param = "";
            step.DelayMs = 50;
            step.Count = 1;
            StepSnapshot before = new StepSnapshot();
            before.Steps = CloneSteps();
            before.Label = "添加步骤";
            if (!EditStepDialog(step, ActionCatalog.Get(step.Action), true)) return;
            PushStepUndo(before);
            int at = InsertIndex();
            _draft.Steps.Insert(at, step);
            RefreshSteps(at);
        }

        private void EditSelectedStep()
        {
            MacroStepListItem row = _steps.SelectedItem as MacroStepListItem;
            if (row == null) return;
            int index = _draft.Steps.IndexOf(row.Step);
            if (index < 0) return;
            ActionDef def = ActionCatalog.Get(row.Step.Action);
            // The dialog mutates the step in place, so the snapshot must be taken
            // before it opens and only kept when the user confirms.
            StepSnapshot before = new StepSnapshot();
            before.Steps = CloneSteps();
            before.Label = "编辑步骤";
            if (!EditStepDialog(row.Step, def, false)) return;
            PushStepUndo(before);
            RefreshSteps(index);
        }

        private bool EditStepDialog(MacroStep step, ActionDef def, bool isNew)
        {
            MacroStepForm form = new MacroStepForm(step, def, isNew);
            if (form.ShowDialog(this) != DialogResult.OK) return false;
            return true;
        }

        private void CutSteps()
        {
            CopySteps();
            RemoveSteps("剪切步骤");
        }

        private void CopySteps()
        {
            List<MacroStep> picked = SelectedSteps();
            if (picked.Count == 0) return;
            _clipboard = new List<MacroStep>();
            for (int i = 0; i < picked.Count; i++) _clipboard.Add(picked[i].Clone());
            UpdateButtons();
        }

        private void PasteSteps()
        {
            if (_clipboard.Count == 0) return;
            PushStepUndo("粘贴步骤");
            int at = InsertIndex();
            for (int i = 0; i < _clipboard.Count; i++)
                _draft.Steps.Insert(at + i, _clipboard[i].Clone());
            RefreshSteps(at);
        }

        private void RemoveSteps()
        {
            RemoveSteps("删除步骤");
        }

        private void RemoveSteps(string label)
        {
            List<MacroStep> picked = SelectedSteps();
            if (picked.Count == 0) return;
            if (picked.Count > 1 && !MDialog.Confirm(this, "删除步骤",
                "确定要删除选中的 " + picked.Count + " 个步骤吗？", "删除", "取消", MDialogKind.Warning))
                return;
            PushStepUndo(label);
            int first = _draft.Steps.IndexOf(picked[0]);
            for (int i = 0; i < picked.Count; i++) _draft.Steps.Remove(picked[i]);
            RefreshSteps(Math.Min(first, _draft.Steps.Count - 1));
        }

        private List<MacroStep> SelectedSteps()
        {
            List<MacroStep> list = new List<MacroStep>();
            foreach (MListItem item in _steps.SelectedItems)
            {
                MacroStepListItem row = item as MacroStepListItem;
                if (row != null) list.Add(row.Step);
            }
            return list;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool ctrl = (keyData & Keys.Control) == Keys.Control;
            bool shift = (keyData & Keys.Shift) == Keys.Shift;
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            // While the name or note box is being typed in, Ctrl+Z and the clipboard
            // shortcuts belong to the text box, not to the step list.
            if (_nameBox != null && _nameBox.ContainsFocus) return base.ProcessCmdKey(ref msg, keyData);
            if (_noteBox != null && _noteBox.ContainsFocus) return base.ProcessCmdKey(ref msg, keyData);
            if (ctrl && key == Keys.Z) { if (shift) RedoSteps(); else UndoSteps(); return true; }
            if (ctrl && key == Keys.Y) { RedoSteps(); return true; }
            if (ctrl && key == Keys.S) { SaveAndClose(); return true; }
            if (ctrl && key == Keys.X) { CutSteps(); return true; }
            if (ctrl && key == Keys.C) { CopySteps(); return true; }
            if (ctrl && key == Keys.V) { PasteSteps(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SaveAndClose()
        {
            string name = _nameBox.Text == null ? "" : _nameBox.Text.Trim();
            if (name.Length == 0)
            {
                MDialog.Tell(this, "还差一个名字", "给这个宏起个名字，方便在动作列表里找到它。", MDialogKind.Warning);
                _nameBox.FocusInner();
                return;
            }
            if (MacroLibrary.NameExists(_allMacros, name, _draft.Id))
            {
                MDialog.Tell(this, "名字重复", "已经有另一个宏叫“" + name + "”，换一个名字。", MDialogKind.Warning);
                _nameBox.FocusInner();
                return;
            }
            if (_draft.Steps.Count == 0)
            {
                MDialog.Tell(this, "还没有步骤", "至少添加一个动作再保存。", MDialogKind.Warning);
                return;
            }
            SyncStepsFromList();
            _draft.RestoreCursor = _restoreToggle.Checked;
            _draft.Name = name;
            _draft.Note = _noteBox.Text == null ? "" : _noteBox.Text.Trim();

            // Validate against the would-be list so a new macro that nests an
            // existing one, or an edit that closes a loop, is caught here.
            List<Macro> probe = new List<Macro>();
            for (int i = 0; i < _allMacros.Count; i++)
            {
                Macro m = _allMacros[i];
                if (m == null) continue;
                probe.Add(string.Equals(m.Id, _draft.Id, StringComparison.OrdinalIgnoreCase) ? _draft : m);
            }
            if (_original == null) probe.Add(_draft);
            string error = MacroLibrary.ValidateNesting(probe, _draft);
            if (error != null)
            {
                MDialog.Tell(this, "宏结构有问题", error, MDialogKind.Warning);
                return;
            }

            Result = _draft;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>Pick one of the existing macros to nest inside another macro.</summary>
    internal sealed class MacroPickerForm : Form
    {
        private readonly MListView _list;
        private readonly List<Macro> _macros;
        private MButton _okButton;
        private MButton _cancelButton;

        public Macro SelectedMacro { get; private set; }

        public MacroPickerForm(List<Macro> macros)
        {
            _macros = macros ?? new List<Macro>();
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "选择宏";
            ClientSize = new Size(Theme.S(520), Theme.S(480));

            int top = Theme.DialogHeader(this, "选择宏", "嵌套后会按顺序执行这个宏的全部步骤。");
            MCard card = new MCard();
            card.Padding = new Padding(1);
            // Full-bleed list: a rounded card would leave a corner ring behind it.
            card.Radius = 0;
            Controls.Add(card);
            _list = new MListView();
            _list.RowHeight = Theme.S(52);
            _list.EmptyText = "没有可嵌套的宏";
            _list.ItemActivated += delegate { Accept(); };
            card.Controls.Add(_list);

            _cancelButton = new MButton();
            _cancelButton.Text = "取消";
            _cancelButton.Kind = MButtonKind.Secondary;
            _cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_cancelButton);
            _okButton = new MButton();
            _okButton.Text = "选择";
            _okButton.Kind = MButtonKind.Primary;
            _okButton.Click += delegate { Accept(); };
            Controls.Add(_okButton);

            int pad = Theme.S(Theme.PagePadding);
            int footer = Theme.S(34);
            card.SetBounds(pad, top, ClientSize.Width - pad * 2, ClientSize.Height - top - footer - Theme.S(48));
            _list.SetBounds(1, 1, card.ClientSize.Width - 2, card.ClientSize.Height - 2);
            int y = ClientSize.Height - pad - footer;
            _okButton.SetBounds(ClientSize.Width - pad - Theme.S(88), y, Theme.S(88), footer);
            _cancelButton.SetBounds(ClientSize.Width - pad - Theme.S(88) * 2 - Theme.S(8), y, Theme.S(88), footer);

            List<MListItem> rows = new List<MListItem>();
            for (int i = 0; i < _macros.Count; i++)
                rows.Add(new ChoiceListItem(_macros[i].Id, "宏动作", _macros[i].Name,
                    _macros[i].StepCount + " 个动作" + (string.IsNullOrEmpty(_macros[i].Note) ? "" : "  ·  " + _macros[i].Note),
                    null, _macros[i]));
            _list.SetItems(rows);
            if (rows.Count > 0) _list.SelectedIndex = 0;
        }

        private void Accept()
        {
            ChoiceListItem row = _list.SelectedItem as ChoiceListItem;
            if (row == null) return;
            SelectedMacro = row.Tag as Macro;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if (keyData == Keys.Enter)
            {
                Accept();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    /// <summary>Edits one macro step: action (changeable), parameter, delay, count.</summary>
    internal sealed class MacroStepForm : Form
    {
        private readonly MacroStep _step;
        private ActionDef _def;

        private MTextBox _text;
        private MTextArea _area;
        private MButton _browse;
        private MNumberBox _number;
        private MComboBox _choice;
        private MKeyComboField _comboField;
        private MMousePointField _point;
        private MNumberBox _delay;
        private MNumberBox _count;
        private Label _noteLabel;
        private MTextBox _noteBox;
        private Label _actionLabel;
        private Label _paramLabel;
        private MCard _card;
        private Label _paramHint;
        private Label _actionName;
        private MButton _changeButton;
        private Label _error;
        private Label _delayLabel;
        private Label _delayUnit;
        private Label _countLabel;
        private Label _countUnit;
        private MButton _okButton;
        private MButton _cancelButton;

        public MacroStepForm(MacroStep step, ActionDef def, bool isNew)
        {
            _step = step;
            _def = def ?? ActionCatalog.Get(step.Action);

            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = isNew ? "添加步骤" : "编辑步骤";
            ClientSize = new Size(Theme.S(560), Theme.S(340));

            int top = Theme.DialogHeader(this, Text, "延迟是这一步执行完到下一步开始之间的等待时间。");
            int pad = Theme.S(Theme.PagePadding);
            int width = ClientSize.Width - pad * 2;

            _card = new MCard();
            Controls.Add(_card);

            _actionLabel = Theme.MakeLabel("动作", Theme.Body, Theme.TextSecondary, 0, 0, Theme.S(70), Theme.S(28));
            _card.Controls.Add(_actionLabel);
            _actionName = Theme.MakeLabel("", Theme.BodyStrong, Theme.TextPrimary, 0, 0, width - Theme.S(160), Theme.S(28));
            _actionName.Text = _def.Category + " · " + _def.Name;
            _actionName.AutoEllipsis = true;
            _card.Controls.Add(_actionName);
            _changeButton = new MButton();
            _changeButton.Text = "更换…";
            _changeButton.Kind = MButtonKind.Secondary;
            _changeButton.Click += delegate { ChangeAction(); };
            _card.Controls.Add(_changeButton);

            _paramLabel = Theme.MakeLabel("参数", Theme.Body, Theme.TextSecondary, 0, 0, Theme.S(70), Theme.S(28));
            // Four-character names (点击设置) must fit at every DPI; a fixed width
            // clipped the last glyph, so let the label size to its text instead.
            _paramLabel.AutoSize = true;
            _card.Controls.Add(_paramLabel);
            _paramHint = Theme.MakeLabel("", Theme.Caption, Theme.TextTertiary, 0, 0, width - Theme.S(100), Theme.S(18));
            _card.Controls.Add(_paramHint);

            _text = new MTextBox();
            _card.Controls.Add(_text);
            _area = new MTextArea();
            _card.Controls.Add(_area);
            _browse = new MButton();
            _browse.Text = "浏览…";
            _browse.Kind = MButtonKind.Secondary;
            _browse.Click += delegate { BrowseParam(); };
            _card.Controls.Add(_browse);
            _number = new MNumberBox();
            _number.Minimum = 1;
            _number.Maximum = 9999;
            _card.Controls.Add(_number);
            _choice = new MComboBox();
            _card.Controls.Add(_choice);
            _comboField = new MKeyComboField();
            _comboField.ComboChanged += delegate { if (_error != null) _error.Text = ""; };
            _card.Controls.Add(_comboField);
            _point = new MMousePointField();
            _point.Visible = false;
            _card.Controls.Add(_point);

            // Added after the parameter editors so the dialog still opens with the
            // keyboard on the parameter, not on the optional name box.
            _noteLabel = Theme.MakeLabel("命名", Theme.Body, Theme.TextSecondary, 0, 0, Theme.S(60), Theme.S(28));
            _card.Controls.Add(_noteLabel);
            // The name is what the row shows in the 动作 column; the action itself
            // moves to the small second line so it stays identifiable.
            _noteBox = new MTextBox();
            _noteBox.Cue = "可选：给这一步起个名字，例如“复制选中内容”";
            _card.Controls.Add(_noteBox);

            _delayLabel = Theme.MakeLabel("执行后等待", Theme.Body, Theme.TextSecondary, 0, 0, Theme.S(90), Theme.S(28));
            _card.Controls.Add(_delayLabel);
            _delay = new MNumberBox();
            _delay.Minimum = 0;
            _delay.Maximum = 60000;
            _delay.Increment = 25;
            _card.Controls.Add(_delay);
            _delayUnit = Theme.MakeLabel("毫秒", Theme.Caption, Theme.TextTertiary, 0, 0, Theme.S(40), Theme.S(28));
            _card.Controls.Add(_delayUnit);

            _countLabel = Theme.MakeLabel("重复次数", Theme.Body, Theme.TextSecondary, 0, 0, Theme.S(90), Theme.S(28));
            _card.Controls.Add(_countLabel);
            _count = new MNumberBox();
            _count.Minimum = 1;
            _count.Maximum = 999;
            _card.Controls.Add(_count);
            _countUnit = Theme.MakeLabel("次", Theme.Caption, Theme.TextTertiary, 0, 0, Theme.S(30), Theme.S(28));
            _card.Controls.Add(_countUnit);

            _error = Theme.MakeLabel("", Theme.Caption, Theme.Danger, 0, 0, Theme.S(200), Theme.S(30));
            Controls.Add(_error);

            _cancelButton = new MButton();
            _cancelButton.Text = "取消";
            _cancelButton.Kind = MButtonKind.Secondary;
            _cancelButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_cancelButton);
            _okButton = new MButton();
            _okButton.Text = "确定";
            _okButton.Kind = MButtonKind.Primary;
            _okButton.Click += delegate { Accept(); };
            Controls.Add(_okButton);
            AcceptButton = null;
            CancelButton = null;

            BindParamEditor(_def);
            LayoutRows(top, pad, width);
            _noteBox.Text = _step.Note ?? "";
            _delay.Value = Math.Max(0, _step.DelayMs);
            _count.Value = Math.Max(1, _step.Count);
            StartPosition = FormStartPosition.CenterParent;
        }

        private int _headerTop;
        private int _cardPad;

        /// <summary>Measures every row from the current action's description.</summary>
        private void LayoutRows(int top, int pad, int width)
        {
            _headerTop = top;
            _cardPad = pad;
            int footer = Theme.S(34);
            int y = Theme.S(12);
            // The caption column grows with the measured label so a four-character
            // name never gets clipped and the editors stay aligned with it.
            int fieldLeft = Theme.S(16) + _paramLabel.PreferredSize.Width + Theme.S(10);
            if (fieldLeft < Theme.S(80)) fieldLeft = Theme.S(80);
            _noteLabel.SetBounds(Theme.S(16), y + Theme.S(2), Theme.S(60), Theme.S(28));
            _noteBox.SetBounds(fieldLeft, y, Math.Max(Theme.S(120), width - fieldLeft - Theme.S(16)), Theme.S(30));

            y += Theme.S(42);
            _actionLabel.SetBounds(Theme.S(16), y, Theme.S(60), Theme.S(30));
            _actionName.SetBounds(fieldLeft, y, width - Theme.S(250), Theme.S(30));
            _changeButton.SetBounds(width - Theme.S(100), y - Theme.S(2), Theme.S(84), Theme.S(32));

            y += Theme.S(42);
            // Same geometry as MappingEditorForm's action card: caption column on the
            // left, a full-width control on the right, an optional 浏览… button, and
            // the description wrapped underneath with a measured gap.
            int innerRight = width - Theme.S(16);
            int innerWidth = Math.Max(Theme.S(120), innerRight - fieldLeft);
            bool hasBrowse = _def.ParamKind == ActionParamKind.Path || _def.ParamKind == ActionParamKind.Folder;
            int browseButtonWidth = hasBrowse
                ? Math.Max(Theme.S(84), Theme.Measure("浏览…", Theme.BodyStrong).Width + Theme.S(16))
                : 0;
            int browseWidth = hasBrowse ? browseButtonWidth + Theme.S(8) : 0;
            int fieldWidth = Math.Max(Theme.S(120), innerWidth - browseWidth);

            _paramLabel.Location = new Point(Theme.S(16), y + Theme.S(2));
            _text.SetBounds(fieldLeft, y, fieldWidth, Theme.S(30));
            _number.SetBounds(fieldLeft, y + Theme.S(1), Theme.S(96), Theme.S(30));
            _choice.SetBounds(fieldLeft, y, Math.Max(Theme.S(160),
                Math.Min(Theme.S(320), innerWidth)), Theme.S(30));
            // The send-keys parameter is the same inline field the shortcut editor
            // uses (modifier chips + key capture + live preview), not a read-only
            // box that opens a second dialog.
            _comboField.SetBounds(fieldLeft, y, innerWidth, Theme.S(74));
            _point.SetBounds(fieldLeft, y, innerWidth,
                _def.ParamKind == ActionParamKind.MousePoint ? Theme.S(64) : Theme.S(112));
            _area.SetBounds(fieldLeft, y, innerWidth, Theme.S(96));
            _browse.SetBounds(innerRight - browseButtonWidth, y - Theme.S(1), browseButtonWidth, Theme.S(32));

            int paramBottom = _def.ParamKind == ActionParamKind.Text ? y + Theme.S(96)
                : _def.ParamKind == ActionParamKind.MouseClick ? y + Theme.S(112)
                : _def.ParamKind == ActionParamKind.MousePoint ? y + Theme.S(64)
                : _def.ParamKind == ActionParamKind.KeyCombo ? y + Theme.S(74)
                : y + Theme.S(30);
            int descWidth = innerWidth;
            List<string> descLines = MTextLayout.Wrap(_def.Description ?? "", Theme.Caption, descWidth, true);
            int descHeight = Math.Max(Theme.S(18),
                MTextLayout.LineHeight(Theme.Caption) * Math.Max(1, descLines.Count));
            _paramHint.Text = string.Join(Environment.NewLine, descLines.ToArray());
            int descTop = _def.ParamKind == ActionParamKind.None ? y : paramBottom + Theme.S(8);
            _paramHint.SetBounds(fieldLeft, descTop, descWidth, descHeight + Theme.S(2));

            int rowY = descTop + descHeight + Theme.S(14);
            int cardHeight = rowY + Theme.S(30) + Theme.S(16);
            ClientSize = new Size(ClientSize.Width,
                top + cardHeight + Theme.S(16) + footer + pad);
            _card.SetBounds(pad, top, width, cardHeight);

            _delayLabel.SetBounds(Theme.S(16), rowY, Theme.S(90), Theme.S(30));
            _delay.SetBounds(Theme.S(110), rowY, Theme.S(90), Theme.S(30));
            _delayUnit.SetBounds(Theme.S(208), rowY, Theme.S(40), Theme.S(30));
            _countLabel.SetBounds(Theme.S(270), rowY, Theme.S(90), Theme.S(30));
            _count.SetBounds(Theme.S(360), rowY, Theme.S(80), Theme.S(30));
            _countUnit.SetBounds(Theme.S(448), rowY, Theme.S(30), Theme.S(30));

            int buttonY = ClientSize.Height - pad - footer;
            _okButton.SetBounds(ClientSize.Width - pad - Theme.S(88), buttonY, Theme.S(88), footer);
            _cancelButton.SetBounds(ClientSize.Width - pad - Theme.S(88) * 2 - Theme.S(8), buttonY, Theme.S(88), footer);
            int buttonsLeft = ClientSize.Width - pad - Theme.S(88) * 2 - Theme.S(8);
            _error.SetBounds(pad, buttonY, Math.Max(Theme.S(80), buttonsLeft - pad - Theme.S(12)), footer);
        }

        private void ChangeAction()
        {
            ActionPickerForm picker = new ActionPickerForm(_def.Id);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedAction == null) return;
            _def = picker.SelectedAction;
            _actionName.Text = _def.Category + " · " + _def.Name;
            _error.Text = "";
            _step.Param = ActionCatalog.NormalizeParam(_def.Id, ActionCatalog.DefaultParamFor(_def));
            BindParamEditor(_def);
            LayoutRows(_headerTop, _cardPad, ClientSize.Width - _cardPad * 2);
        }

        private void BindParamEditor(ActionDef def)
        {
            _text.Visible = false;
            _number.Visible = false;
            _area.Visible = false;
            _browse.Visible = false;
            _comboField.Visible = false;
            _point.Visible = false;
            _choice.Visible = false;
            switch (def.ParamKind)
            {
                case ActionParamKind.Count:
                    _number.Visible = true;
                    _number.Value = ActionCatalog.ParseCount(_step.Param, 1);
                    _paramLabel.Text = "次数";
                    break;
                case ActionParamKind.Select:
                    // Same drop-down the shortcut editor uses, so a plugin action
                    // cannot get a hand-typed value inside a macro step either.
                    _choice.Visible = true;
                    _choice.Items.Clear();
                    for (int i = 0; i < def.ChoiceCount; i++) _choice.Items.Add(def.ChoiceLabels[i]);
                    {
                        int index = def.ChoiceIndexOf(_step.Param);
                        if (index < 0 && def.ChoiceCount > 0) index = 0;
                        if (_choice.Items.Count > 0) _choice.SelectedIndex = index < 0 ? 0 : index;
                    }
                    _paramLabel.Text = "选项";
                    break;
                case ActionParamKind.MouseClick:
                    _point.ClickOptions = true;
                    _point.Visible = true;
                    _point.Value = _step.Param;
                    _paramLabel.Text = "点击设置";
                    break;
                case ActionParamKind.MousePoint:
                    _point.ClickOptions = false;
                    _point.Visible = true;
                    _point.Value = _step.Param;
                    _paramLabel.Text = "目标坐标";
                    break;
                case ActionParamKind.KeyCombo:
                    _comboField.Visible = true;
                    _comboField.ComboText = _step.Param ?? "";
                    _paramLabel.Text = "组合键";
                    break;
                case ActionParamKind.None:
                    _paramLabel.Text = "参数";
                    break;
                case ActionParamKind.Text:
                    _area.Visible = true;
                    _area.Text = _step.Param ?? "";
                    _paramLabel.Text = "文本";
                    break;
                default:
                    _text.Visible = true;
                    _text.Text = _step.Param ?? "";
                    _paramLabel.Text = def.ParamKind == ActionParamKind.Path ? "路径"
                        : def.ParamKind == ActionParamKind.Folder ? "文件夹"
                        : def.ParamKind == ActionParamKind.Url ? "网址" : "参数";
                    _browse.Visible = def.ParamKind == ActionParamKind.Path || def.ParamKind == ActionParamKind.Folder;
                    break;
            }
        }

        private void BrowseParam()
        {
            if (_def.ParamKind == ActionParamKind.Folder)
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "选择文件夹";
                    if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
                        _text.Text = dialog.SelectedPath;
                }
                return;
            }
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择程序或文件";
                dialog.Filter = "程序和文件 (*.*)|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.FileName))
                    _text.Text = dialog.FileName;
            }
        }

        private void Accept()
        {
            string combo = "";
            if (_def.ParamKind == ActionParamKind.KeyCombo)
            {
                combo = _comboField.ComboText == null ? "" : _comboField.ComboText.Trim();
                if (combo.Length == 0)
                {
                    _error.Text = "请设置一个有效的组合键。";
                    return;
                }
            }
            _error.Text = "";
            switch (_def.ParamKind)
            {
                case ActionParamKind.Count:
                    _step.Param = _number.Value.ToString();
                    break;
                case ActionParamKind.KeyCombo:
                    _step.Param = combo;
                    break;
                case ActionParamKind.None:
                    _step.Param = "";
                    break;
                case ActionParamKind.Text:
                    _step.Param = _area.Text == null ? "" : _area.Text.Trim();
                    break;
                case ActionParamKind.MouseClick:
                case ActionParamKind.MousePoint:
                    _step.Param = _point.Value;
                    break;
                case ActionParamKind.Select:
                    {
                        int index = _choice.SelectedIndex;
                        _step.Param = index >= 0 && index < _def.ChoiceCount
                            ? _def.ChoiceValues[index]
                            : (_def.ChoiceCount > 0 ? _def.ChoiceValues[0] : "");
                        break;
                    }
                default:
                    _step.Param = _text.Text == null ? "" : _text.Text.Trim();
                    break;
            }
            _step.DelayMs = _delay.Value;
            _step.Count = _count.Value;
            string note = _noteBox.Text == null ? "" : _noteBox.Text.Trim();
            _step.Note = note.Length > 60 ? note.Substring(0, 60) : note;
            _step.Action = _def.Id;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                // The combo field's capture box records Escape as a key; only
                // cancel the dialog when the key box is not the focused control.
                if (MKeyCapture.AnyFocused(this)) return false;
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if (keyData == Keys.Enter)
            {
                // Let the focused value field handle Enter itself (commit + blur);
                // the key-capture box needs Enter as the key it records. Otherwise a
                // focused button keeps Enter semantics, and the default action runs
                // when nothing more specific is focused.
                if (ActiveControl is MNumberBox || ActiveControl is MKeyComboField
                    || ActiveControl is MKeyCapture) return false;
                MButton button = ActiveControl as MButton;
                if (button != null) { button.PerformClick(); return true; }
                Accept();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
