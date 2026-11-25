using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>Searchable action picker with keyboard-first navigation.</summary>
    internal sealed class ActionPickerForm : Form
    {
        private readonly MSearchBox _search;
        private readonly MSegmented _typeFilter;
        private readonly MListView _list;
        private readonly string _initialId;

        public ActionDef SelectedAction { get; private set; }

        public ActionPickerForm(string currentId)
        {
            _initialId = currentId;

            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "选择动作";
            ClientSize = new Size(Theme.S(560), Theme.S(600));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            int top = Theme.DialogHeader(this, "选择动作", "↑↓ 选择，Enter 确认，Esc 取消");

            _search = new MSearchBox();
            _search.CueText = "搜索动作或分类…";
            _search.SearchTextChanged += delegate { RefreshList(false); };
            // 在搜索框里按 ↓ 直接进列表，按 Enter 选择当前高亮项。
            _search.Inner.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Down)
                {
                    if (_list.Items.Count > 0)
                    {
                        if (_list.SelectedIndex < 0) _list.SelectedIndex = 0;
                        _list.Focus();
                    }
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    Accept();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            Controls.Add(_search);

            // 动作多时先按来源筛选：原生 / 插件 / 宏，默认全部。
            _typeFilter = new MSegmented();
            _typeFilter.Items.Add("全部");
            _typeFilter.Items.Add("原生");
            _typeFilter.Items.Add("插件");
            _typeFilter.Items.Add("宏");
            _typeFilter.SelectedIndex = 0;
            _typeFilter.SelectedIndexChanged += delegate
            {
                RefreshList(false);
                // 换筛选后不预选第一行；把焦点交给列表，方便直接用方向键选。
                if (_list.Items.Count > 0) _list.Focus();
            };
            Controls.Add(_typeFilter);

            MCard card = new MCard();
            card.Padding = new Padding(1);
            // Full-bleed list: a rounded card would leave a corner ring behind it.
            card.Radius = 0;
            Controls.Add(card);

            _list = new MListView();
            _list.RowHeight = Theme.S(46);
            _list.EmptyText = "没有匹配的动作";
            _list.ItemActivated += delegate { Accept(); };
            card.Controls.Add(_list);

            MButton cancel = new MButton();
            cancel.Text = "取消";
            cancel.Kind = MButtonKind.Secondary;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            MButton ok = new MButton();
            ok.Text = "选择";
            ok.Kind = MButtonKind.Primary;
            ok.Click += delegate { Accept(); };
            Controls.Add(ok);

            // layout
            _search.SetBounds(Theme.S(24), top, ClientSize.Width - Theme.S(48), Theme.S(Theme.CtrlHeight));
            _typeFilter.SetBounds(Theme.S(24), top + Theme.S(44), Theme.S(336), Theme.S(32));
            card.SetBounds(Theme.S(24), top + Theme.S(84), ClientSize.Width - Theme.S(48),
                Math.Max(Theme.S(120), ClientSize.Height - top - Theme.S(84) - Theme.S(76)));
            _list.SetBounds(1, 1, card.ClientSize.Width - 1, card.ClientSize.Height - 1);
            cancel.SetBounds(ClientSize.Width - Theme.S(24) - Theme.S(84) - Theme.S(8) - Theme.S(92),
                ClientSize.Height - Theme.S(56), Theme.S(84), Theme.S(34));
            ok.SetBounds(ClientSize.Width - Theme.S(24) - Theme.S(92),
                ClientSize.Height - Theme.S(56), Theme.S(92), Theme.S(34));

            RefreshList(true);
            Shown += delegate { _list.Focus(); };
            Native.SetDarkTitleBar(Handle, Theme.IsDark);
        }

        private void RefreshList(bool initial)
        {
            string query = _search.Text.Trim();
            int kind = _typeFilter.SelectedIndex; // 0 全部 1 原生 2 插件 3 宏
            List<MListItem> rows = new List<MListItem>();
            int select = -1;

            foreach (ActionDef def in ActionCatalog.All)
            {
                if (kind == 1 && (def.IsPlugin || def.IsMacro)) continue;
                if (kind == 2 && !def.IsPlugin) continue;
                if (kind == 3 && !def.IsMacro) continue;
                if (query.Length > 0)
                {
                    bool match = def.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0
                        || def.Category.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0
                        || def.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!match) continue;
                }
                string category = def.IsPlugin ? "插件 · " + def.Category : def.Category;
                ChoiceListItem item = new ChoiceListItem(def.Id, category, def.Name,
                    null, def.Repeatable ? "连发" : null, def);
                rows.Add(item);
                if (def.Id == _initialId && select < 0) select = rows.Count - 1;
            }

            ChoiceListItem current = _list.SelectedItem as ChoiceListItem;
            string keepId = current != null ? current.Id : null;

            _list.SetItems(rows);
            if (initial)
            {
                if (select >= 0) _list.SelectedIndex = select;
                else if (rows.Count > 0) _list.SelectedIndex = 0;
            }
            else if (keepId != null)
            {
                // 筛选/搜索后原选中项仍可见就保留，否则清空，不自动跳第一行。
                int keep = rows.FindIndex(delegate(MListItem row) { return ((ChoiceListItem)row).Id == keepId; });
                if (keep >= 0) _list.SelectedIndex = keep;
            }
        }

        private void Accept()
        {
            ChoiceListItem item = _list.SelectedItem as ChoiceListItem;
            if (item == null) return;
            SelectedAction = item.Tag as ActionDef;
            if (SelectedAction == null) return;
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // 焦点还在筛选条/搜索框时，↓ 直接进列表；搜索框里 Enter 等于“选择”。
            if (keyData == Keys.Down && (_typeFilter.Focused || _search.Inner.Focused))
            {
                if (_list.Items.Count > 0)
                {
                    if (_list.SelectedIndex < 0) _list.SelectedIndex = 0;
                    _list.Focus();
                }
                return true;
            }
            if (keyData == Keys.Enter && _search.Inner.Focused)
            {
                Accept();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
