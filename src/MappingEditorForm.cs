using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>Records or edits a single key mapping.</summary>
    internal sealed class MappingEditorForm : Form
    {
        private readonly KeyMapping _mapping;
        private readonly bool _isNew;

        private MChipToggle _caps;
        private MChipToggle _ctrl;
        private MChipToggle _alt;
        private MChipToggle _shift;
        private MChipToggle _win;
        private MKeyCapture _key;
        private MSegmented _mode;
        private MComboBox _actionSlot;
        private Label _actionDesc;
        private Label _preview;
        private Label _error;
        private Panel _paramHost;

        private MCard _triggerCard;
        private MCard _actionCard;
        private Label _triggerTitle;
        private Label _actionTitle;
        private Label _keyCaption;
        private Label _modeCaption;
        private Label _hint;
        private MButton _cancel;
        private MButton _save;

        private int _contentTop;
        private ActionDef _def;
        private MNumberBox _count;
        private MTextBox _text;
        private MTextArea _area;
        private MKeyComboField _comboField;
        private MTextBox _note;
        private Label _noteCaption;
        private MMousePointField _point;
        private Label _paramCaption;
        private MButton _paramButton;
        private Label _paramUnit;

        public KeyMapping Result { get; private set; }

        public MappingEditorForm(KeyMapping mapping, bool isNew)
        {
            _mapping = mapping.Clone();
            _isNew = isNew;

            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = isNew ? "新增快捷键" : "编辑快捷键";
            ClientSize = new Size(Theme.S(700), Theme.S(560));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            _contentTop = Theme.DialogHeader(this, Text, "设置触发组合键，以及按下后要执行的动作。");

            BuildTriggerCard();
            BuildActionCard();
            BuildFooter();

            // ---- initial state ----
            _caps.Checked = _mapping.Caps;
            _ctrl.Checked = _mapping.Ctrl;
            _alt.Checked = _mapping.Alt;
            _shift.Checked = _mapping.Shift;
            _win.Checked = _mapping.Win;
            _key.KeyCode = (Keys)_mapping.Key;

            _def = ActionCatalog.Get(_mapping.Action);
            _actionSlot.DisplayText = _def.Category + " · " + _def.Name;
            bool hold = _mapping.TriggerMode == "hold" ||
                (string.IsNullOrEmpty(_mapping.TriggerMode) && _def.Repeatable);
            _mode.SelectedIndex = hold ? 1 : 0;
            UpdateModeState();

            string initial = _mapping.Param;
            if (string.IsNullOrEmpty(initial)) initial = ActionCatalog.DefaultParamFor(_def);
            BuildParamArea(_def, initial);
            _actionDesc.Text = _def.Description;
            UpdatePreview();

            EventHandler changed = delegate { UpdatePreview(); };
            _caps.CheckedChanged += changed;
            _ctrl.CheckedChanged += changed;
            _alt.CheckedChanged += changed;
            _shift.CheckedChanged += changed;
            _win.CheckedChanged += changed;
            _key.KeyChanged += changed;

            LayoutDialog();
            Native.SetDarkTitleBar(Handle, Theme.IsDark);
        }

        // ---- construction ---------------------------------------------------
        private void BuildTriggerCard()
        {
            _triggerCard = new MCard();
            Controls.Add(_triggerCard);

            _triggerTitle = Theme.MakeLine("触发方式", Theme.Subtitle, Theme.TextPrimary,
                0, 0, Theme.S(180));
            _triggerCard.Controls.Add(_triggerTitle);

            _preview = Theme.MakeLine("", new Font("Microsoft YaHei UI", 12f, FontStyle.Bold),
                Theme.Accent, 0, 0, Theme.S(200));
            _preview.TextAlign = ContentAlignment.MiddleRight;
            _triggerCard.Controls.Add(_preview);

            _caps = AddChip(_triggerCard, "Caps Lock");
            _ctrl = AddChip(_triggerCard, "Ctrl");
            _alt = AddChip(_triggerCard, "Alt");
            _shift = AddChip(_triggerCard, "Shift");
            _win = AddChip(_triggerCard, "Win");

            _keyCaption = Theme.MakeLine("主键", Theme.Body, Theme.TextSecondary, 0, 0, Theme.S(40));
            _triggerCard.Controls.Add(_keyCaption);

            _key = new MKeyCapture();
            _triggerCard.Controls.Add(_key);

            _modeCaption = Theme.MakeLine("触发类型", Theme.Body, Theme.TextSecondary, 0, 0, Theme.S(68));
            _triggerCard.Controls.Add(_modeCaption);

            _mode = new MSegmented();
            _mode.Items.Add("按下单发");
            _mode.Items.Add("按住连发");
            _triggerCard.Controls.Add(_mode);

            _hint = Theme.MakeLine(
                "点击输入框后按一个主键；修饰键由上方开关决定，无需真的按住 Caps Lock。",
                Theme.Caption, Theme.TextTertiary, 0, 0, Theme.S(200));
            _triggerCard.Controls.Add(_hint);
        }

        private void BuildActionCard()
        {
            _actionCard = new MCard();
            Controls.Add(_actionCard);

            _actionTitle = Theme.MakeLine("执行动作", Theme.Subtitle, Theme.TextPrimary,
                0, 0, Theme.S(180));
            _actionCard.Controls.Add(_actionTitle);

            _actionSlot = new MComboBox();
            _actionSlot.Click += delegate { PickAction(); };
            _actionCard.Controls.Add(_actionSlot);

            _actionDesc = Theme.MakeLine("", Theme.Caption, Theme.TextTertiary, 0, 0, Theme.S(200));
            _actionCard.Controls.Add(_actionDesc);

            _paramHost = new Panel();
            _paramHost.BackColor = Color.Transparent;
            _actionCard.Controls.Add(_paramHost);
        }

        private void BuildFooter()
        {
            _error = Theme.MakeLine("", Theme.Caption, Theme.Danger, 0, 0, Theme.S(200));
            Controls.Add(_error);

            _cancel = new MButton();
            _cancel.Text = "取消";
            _cancel.Kind = MButtonKind.Secondary;
            _cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_cancel);

            _save = new MButton();
            _save.Text = "保存";
            _save.Kind = MButtonKind.Primary;
            _save.Click += delegate { SaveAndClose(); };
            Controls.Add(_save);
        }

        private MChipToggle AddChip(Control parent, string text)
        {
            MChipToggle chip = new MChipToggle();
            chip.Text = text;
            chip.FitToText();
            parent.Controls.Add(chip);
            return chip;
        }

        // ---- measured layout -------------------------------------------------
        /// <summary>
        /// Every Y comes from the previous element's Bottom. Nothing here is a
        /// hand-tuned offset, so a taller font or a longer hint cannot make one
        /// label paint over the next one.
        /// </summary>
        private void LayoutDialog()
        {
            int width = Theme.S(700);
            int pagePad = Theme.S(22);
            int cardWidth = width - pagePad * 2;
            int cardPad = Theme.S(20);

            // ---- trigger card ----
            int y = Theme.S(12);
            _triggerTitle.Location = new Point(cardPad, y);
            _preview.Location = new Point(cardPad + Theme.S(186), y);
            _preview.Width = Math.Max(Theme.S(140), cardWidth - cardPad * 2 - Theme.S(186));
            y = Math.Max(_triggerTitle.Bottom, _preview.Bottom) + Theme.S(8);
            _triggerCard.AddSeparator(y);
            y += Theme.S(14);

            int chipTop = y;
            int x = cardPad;
            MChipToggle[] chips = { _caps, _ctrl, _alt, _shift, _win };
            for (int i = 0; i < chips.Length; i++)
            {
                chips[i].Location = new Point(x, chipTop);
                x = chips[i].Right + Theme.S(8);
            }
            y = chipTop + _caps.Height + Theme.S(14);

            int fieldTop = y;
            int fieldHeight = Theme.S(36);
            _keyCaption.Width = Theme.S(52);
            _keyCaption.Location = new Point(cardPad,
                fieldTop + (fieldHeight - _keyCaption.Height) / 2);
            _key.SetBounds(cardPad + Theme.S(54), fieldTop, Theme.S(168), fieldHeight);

            int modeX = cardPad + Theme.S(54) + Theme.S(168) + Theme.S(22);
            _modeCaption.Width = Theme.S(94);
            _modeCaption.Location = new Point(modeX,
                fieldTop + (fieldHeight - _modeCaption.Height) / 2);
            int segmentX = modeX + Theme.S(96);
            _mode.SetBounds(segmentX, fieldTop,
                Math.Max(Theme.S(160), cardWidth - segmentX - cardPad), Theme.S(34));

            y = fieldTop + fieldHeight + Theme.S(12);
            _hint.Location = new Point(cardPad, y);
            _hint.Width = Math.Max(Theme.S(200), cardWidth - cardPad * 2);
            y = _hint.Bottom + Theme.S(12);

            _triggerCard.SetBounds(pagePad, _contentTop, cardWidth, y);

            // ---- action card ----
            int actionTop = _triggerCard.Bottom + Theme.S(14);
            int y2 = Theme.S(12);
            _actionTitle.Location = new Point(cardPad, y2);
            y2 = _actionTitle.Bottom + Theme.S(8);
            _actionSlot.SetBounds(cardPad, y2, Math.Max(Theme.S(120), cardWidth - cardPad * 2),
                Theme.S(36));
            y2 = _actionSlot.Bottom + Theme.S(6);
            _actionDesc.Location = new Point(cardPad, y2);
            _actionDesc.Width = Math.Max(Theme.S(120), cardWidth - cardPad * 2);

            int paramHeight = ParamHostHeight(_def);
            int paramTop = _actionDesc.Bottom + Theme.S(6);
            _paramHost.SetBounds(cardPad, paramTop,
                Math.Max(Theme.S(120), cardWidth - cardPad * 2), paramHeight);
            LayoutParamFields();
            y2 = paramTop + paramHeight + Theme.S(12);
            _actionCard.SetBounds(pagePad, actionTop, cardWidth, y2);

            // ---- footer ----
            int footerY = _actionCard.Bottom + Theme.S(10);
            _error.Location = new Point(pagePad, footerY);
            _error.Width = Math.Max(Theme.S(120), cardWidth - Theme.S(210));
            int buttonsY = _error.Bottom + Theme.S(10);
            _save.SetBounds(width - pagePad - Theme.S(92), buttonsY, Theme.S(92), Theme.S(34));
            _cancel.SetBounds(_save.Left - Theme.S(12) - Theme.S(84), buttonsY,
                Theme.S(84), Theme.S(34));

            ClientSize = new Size(width, buttonsY + Theme.S(34) + Theme.S(18));
        }

        private static int ParamHostHeight(ActionDef def)
        {
            if (def == null) return 0;
            switch (def.ParamKind)
            {
                case ActionParamKind.None:
                    return 0;
                case ActionParamKind.Text:
                    return Theme.S(100);
                case ActionParamKind.MouseClick:
                    // Modifier chips, button row and the X/Y row: the field is
                    // 112 tall before the note row is added.
                    return ActionCatalog.SupportsNote(def.Id) ? Theme.S(186) : Theme.S(146);
                case ActionParamKind.KeyCombo:
                    return ActionCatalog.SupportsNote(def.Id) ? Theme.S(118) : Theme.S(78);
                default:
                    return Theme.S(34);
            }
        }

        private void UpdatePreview()
        {
            string mods = KeyText.FormatModifiers(_caps.Checked, _ctrl.Checked, _alt.Checked,
                _shift.Checked, _win.Checked);
            string keyName = _key.KeyCode == Keys.None ? "请按主键" : KeyText.FormatKey(_key.KeyCode);
            _preview.Text = mods.Length == 0 ? keyName : mods + " + " + keyName;
            _preview.ForeColor = _key.KeyCode == Keys.None ? Theme.TextTertiary : Theme.Accent;
        }

        private void UpdateModeState()
        {
            bool repeatable = _def != null && _def.Repeatable;
            // The segmented control owns its disabled look and cursor; it also
            // refuses clicks while disabled, so no extra guard is needed here.
            _mode.Enabled = repeatable;
            if (!repeatable) _mode.SelectedIndex = 0;
        }

        private void PickAction()
        {
            using (ActionPickerForm picker = new ActionPickerForm(_def == null ? ActionCatalog.None : _def.Id))
            {
                if (picker.ShowDialog(this) != DialogResult.OK || picker.SelectedAction == null) return;
                ActionDef previous = _def;
                string previousParam = ReadParam();
                _def = picker.SelectedAction;
                _actionSlot.DisplayText = _def.Category + " · " + _def.Name;
                _actionSlot.Invalidate();
                string value = previous != null && previous.Id == _def.Id
                    ? previousParam : ActionCatalog.DefaultParamFor(_def);
                BuildParamArea(_def, value);
                _actionDesc.Text = _def.Description;
                UpdateModeState();
                LayoutDialog();
            }
        }

        private void BuildParamArea(ActionDef def, string value)
        {
            while (_paramHost.Controls.Count > 0)
            {
                Control old = _paramHost.Controls[0];
                _paramHost.Controls.RemoveAt(0);
                old.Dispose();
            }
            _count = null;
            _text = null;
            _area = null;
            _comboField = null;
            _note = null;
            _noteCaption = null;
            _point = null;
            _paramCaption = null;
            _paramButton = null;
            _paramUnit = null;
            if (def == null || def.ParamKind == ActionParamKind.None) return;

            string label = "参数";
            if (def.ParamKind == ActionParamKind.Count) label = "次数";
            else if (def.ParamKind == ActionParamKind.Path) label = "路径";
            else if (def.ParamKind == ActionParamKind.Folder) label = "文件夹";
            else if (def.ParamKind == ActionParamKind.Url) label = "网址";
            else if (def.ParamKind == ActionParamKind.Text) label = "文本";
            else if (def.ParamKind == ActionParamKind.KeyCombo) label = "组合键";
            else if (def.ParamKind == ActionParamKind.MouseClick) label = "点击设置";

            _paramCaption = Theme.MakeLabel(label, Theme.Body, Theme.TextSecondary,
                0, Theme.S(6), Theme.S(70), Theme.S(22));
            // Names are at most four characters (点击设置); let the label size itself
            // instead of clipping at a fixed width when the DPI scales the font.
            _paramCaption.AutoSize = true;
            _paramHost.Controls.Add(_paramCaption);

            if (def.ParamKind == ActionParamKind.Count)
            {
                _count = new MNumberBox();
                _count.Minimum = 1;
                _count.Maximum = 100;
                _count.Value = ActionCatalog.ParseCount(value, 1);
                _paramHost.Controls.Add(_count);

                _paramUnit = Theme.MakeLabel("次", Theme.Caption, Theme.TextTertiary,
                    0, Theme.S(6), Theme.S(30), Theme.S(22));
                _paramHost.Controls.Add(_paramUnit);
            }
            else if (def.ParamKind == ActionParamKind.Path || def.ParamKind == ActionParamKind.Folder)
            {
                _text = MakeTextBox(value);
                _paramHost.Controls.Add(_text);

                _paramButton = new MButton();
                _paramButton.Text = "浏览…";
                _paramButton.Kind = MButtonKind.Secondary;
                _paramButton.Click += delegate { Browse(def, _text); };
                _paramHost.Controls.Add(_paramButton);
            }
            else if (def.ParamKind == ActionParamKind.Url)
            {
                _text = MakeTextBox(value);
                _paramHost.Controls.Add(_text);
            }
            else if (def.ParamKind == ActionParamKind.Text)
            {
                _area = new MTextArea();
                _area.Text = value ?? "";
                _paramHost.Controls.Add(_area);
            }
            else if (def.ParamKind == ActionParamKind.KeyCombo)
            {
                _comboField = new MKeyComboField();
                _comboField.ComboText = value;
                _paramHost.Controls.Add(_comboField);

                // Optional name shown instead of the raw combination in the
                // shortcut list. Only actions that would otherwise read as raw
                // input (key sends, mouse clicks) get one.
                if (ActionCatalog.SupportsNote(def.Id))
                {
                    _noteCaption = Theme.MakeLabel("注释", Theme.Body, Theme.TextSecondary,
                        0, Theme.S(4), Theme.S(64), Theme.S(22));
                    _paramHost.Controls.Add(_noteCaption);

                    _note = MakeTextBox(_mapping.Note);
                    _note.Cue = "可选，例如：复制";
                    _paramHost.Controls.Add(_note);
                }
            }
            else if (def.ParamKind == ActionParamKind.MouseClick)
            {
                _point = new MMousePointField();
                _point.Value = value;
                _paramHost.Controls.Add(_point);

                if (ActionCatalog.SupportsNote(def.Id))
                {
                    _noteCaption = Theme.MakeLabel("注释", Theme.Body, Theme.TextSecondary,
                        0, Theme.S(4), Theme.S(64), Theme.S(22));
                    _paramHost.Controls.Add(_noteCaption);

                    _note = MakeTextBox(_mapping.Note);
                    _note.Cue = "可选，例如：点开菜单";
                    _paramHost.Controls.Add(_note);
                }
            }

            LayoutParamFields();
        }

        /// <summary>
        /// Places the parameter controls from the host's current width. The host is
        /// sized later by LayoutDialog, so this runs again after every action change
        /// instead of baking a width that the panel did not have yet.
        /// </summary>
        private void LayoutParamFields()
        {
            if (_def == null || _paramCaption == null) return;

            int fieldLeft = Theme.S(72);
            if (_paramCaption != null)
                fieldLeft = Math.Max(fieldLeft, _paramCaption.PreferredSize.Width + Theme.S(8));
            int buttonWidth = _paramButton == null ? 0
                : Math.Max(Theme.S(84), Theme.Measure("浏览…", Theme.BodyStrong).Width + Theme.S(16));
            int fieldWidth = Math.Max(Theme.S(80),
                _paramHost.Width - fieldLeft - buttonWidth - Theme.S(4));

            _paramCaption.Location = new Point(0, Theme.S(6));

            if (_count != null)
            {
                _count.SetBounds(fieldLeft, Theme.S(2), Theme.S(84), Theme.S(28));
                if (_paramUnit != null)
                    _paramUnit.Location = new Point(fieldLeft + Theme.S(90), Theme.S(6));
            }
            else if (_comboField != null)
            {
                _comboField.SetBounds(fieldLeft, 0,
                    Math.Max(Theme.S(220), _paramHost.Width - fieldLeft), Theme.S(74));
                if (_noteCaption != null)
                {
                    _noteCaption.Location = new Point(0, Theme.S(84));
                    if (_note != null)
                        _note.SetBounds(fieldLeft, Theme.S(80),
                            Math.Max(Theme.S(80), _paramHost.Width - fieldLeft), Theme.S(30));
                }
            }
            else if (_point != null)
            {
                _point.SetBounds(fieldLeft, 0, Math.Max(Theme.S(220), _paramHost.Width - fieldLeft), Theme.S(112));
                if (_noteCaption != null)
                {
                    _noteCaption.Location = new Point(0, Theme.S(122));
                    if (_note != null)
                        _note.SetBounds(fieldLeft, Theme.S(118),
                            Math.Max(Theme.S(80), _paramHost.Width - fieldLeft), Theme.S(30));
                }
            }
            else if (_area != null)
            {
                _area.SetBounds(fieldLeft, Theme.S(2),
                    Math.Max(Theme.S(80), _paramHost.Width - fieldLeft), Theme.S(96));
            }
            else if (_text != null)
            {
                if (_def.ParamKind == ActionParamKind.Url)
                    _text.SetBounds(fieldLeft, Theme.S(2),
                        Math.Max(Theme.S(80), _paramHost.Width - fieldLeft), Theme.S(30));
                else
                    _text.SetBounds(fieldLeft, Theme.S(2), fieldWidth, Theme.S(30));
            }

            if (_paramButton != null)
                _paramButton.SetBounds(_paramHost.Width - buttonWidth, 0, buttonWidth, Theme.S(32));
        }

        private MTextBox MakeTextBox(string value)
        {
            MTextBox box = new MTextBox();
            box.Text = value ?? "";
            return box;
        }

        private void Browse(ActionDef def, MTextBox target)
        {
            if (def.Id == "launch.folder" || def.ParamKind == ActionParamKind.Folder)
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "选择要打开的文件夹";
                    if (target.Text.Length > 0 && Directory.Exists(target.Text)) dialog.SelectedPath = target.Text;
                    if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
                }
            }
            else
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "选择要运行的程序或文件";
                    dialog.Filter = "程序与快捷方式|*.exe;*.lnk;*.bat;*.cmd;*.com|所有文件|*.*";
                    dialog.CheckFileExists = true;
                    if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
                }
            }
        }

        private string ReadParam()
        {
            if (_def == null || _def.ParamKind == ActionParamKind.None) return "";
            if (_def.ParamKind == ActionParamKind.Count)
                return (_count == null ? 1 : _count.Value).ToString();
            if (_def.ParamKind == ActionParamKind.KeyCombo)
                return _comboField == null ? "" : _comboField.ComboText.Trim();
            if (_area != null) return _area.Text.Trim();
            if (_point != null) return _point.Value;
            return _text == null ? "" : _text.Text.Trim();
        }

        /// <summary>
        /// Only the Caps layer may bind a key. Returns the message to show, or
        /// null when the trigger is acceptable; extracted so the rule is covered
        /// by the self-test without driving the dialog.
        /// </summary>
        internal static string TriggerValidationError(bool caps)
        {
            return caps ? null : "触发方式只支持 Caps Lock 组合键：请勾选 Caps Lock。";
        }

        private void SaveAndClose()
        {
            _error.Text = "";
            if (_key.KeyCode == Keys.None)
            {
                _error.Text = "请先按一个主键。";
                _key.Focus();
                return;
            }

            bool ctrl = _ctrl.Checked;
            bool alt = _alt.Checked;
            bool shift = _shift.Checked;
            bool win = _win.Checked;
            bool caps = _caps.Checked;
            Keys key = _key.KeyCode & Keys.KeyCode;

            if (!caps)
            {
                // Only the Caps layer may bind keys: registering a system-wide
                // hotkey would fight with the OS and other applications.
                _error.Text = TriggerValidationError(caps);
                return;
            }

            if (_def == null)
            {
                _error.Text = "请选择一个动作。";
                return;
            }

            string param = ReadParam();
            if (_def.ParamKind == ActionParamKind.Path && param.Length == 0)
            {
                _error.Text = "请填写程序路径或文件夹。";
                return;
            }
            if (_def.ParamKind == ActionParamKind.Url && (param.Length == 0 || param == "https://"))
            {
                _error.Text = "请填写要打开的网址。";
                return;
            }
            if (_def.ParamKind == ActionParamKind.Text && param.Length == 0)
            {
                _error.Text = "请输入要发送的文本。";
                return;
            }
            if (_def.ParamKind == ActionParamKind.KeyCombo)
            {
                bool c2, a2, s2, w2;
                Keys k2;
                if (!KeyText.TryParseCombo(param, out c2, out a2, out s2, out w2, out k2))
                {
                    _error.Text = "请设置一个有效的组合键。";
                    return;
                }
            }

            _mapping.Caps = caps;
            _mapping.Ctrl = ctrl;
            _mapping.Alt = alt;
            _mapping.Shift = shift;
            _mapping.Win = win;
            _mapping.KeyCode = key;
            _mapping.Action = _def.Id;
            _mapping.Param = ActionCatalog.NormalizeParam(_def.Id, param);
            _mapping.Note = _note == null ? "" : _note.Text.Trim();
            if (_mapping.Note.Length > 40) _mapping.Note = _mapping.Note.Substring(0, 40);
            _mapping.TriggerMode = _def.Repeatable && _mode.SelectedIndex == 1 ? "hold" : "once";
            _mapping.Enabled = _isNew || _mapping.Enabled;
            if (string.IsNullOrEmpty(_mapping.Id)) _mapping.Id = Guid.NewGuid().ToString("N");

            Result = _mapping.Clone();
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                // While the key box has focus, Escape is the key being recorded
                // rather than "cancel", so leave it to the capture control.
                if (MKeyCapture.AnyFocused(this)) return false;
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
