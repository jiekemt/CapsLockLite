using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// Picks the program a shortcut preset is bound to. Lists the windows the
    /// user already has open (that is the shortest path: the program they mean is
    /// usually running) and still offers 浏览… for anything else.
    /// </summary>
    internal sealed class ProgramPickerForm : Form
    {
        private readonly AppSettings _settings;
        private readonly string _excludePresetId;
        private readonly MSearchBox _search;
        private readonly MListView _list;
        private readonly Label _status;
        private List<MListItem> _allRows = new List<MListItem>();

        public string ProcessName { get; private set; }

        public ProgramPickerForm(AppSettings settings, string excludePresetId)
        {
            _settings = settings;
            _excludePresetId = excludePresetId;

            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "绑定程序";
            ClientSize = new Size(Theme.S(600), Theme.S(520));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            int top = Theme.DialogHeader(this, "绑定程序",
                "选择打开它时自动切换到这套快捷键的程序。列表里是当前运行中的程序。");

            _search = new MSearchBox();
            _search.CueText = "搜索运行中的程序…";
            _search.SearchTextChanged += delegate { ApplyFilter(); };
            _search.SetBounds(Theme.S(24), top, ClientSize.Width - Theme.S(48), Theme.S(Theme.CtrlHeight));
            Controls.Add(_search);

            MCard listCard = new MCard();
            listCard.Padding = new Padding(1);
            // Full-bleed list: a rounded card would leave a corner ring behind it.
            listCard.Radius = 0;
            listCard.SetBounds(Theme.S(24), top + Theme.S(Theme.CtrlHeight) + Theme.S(10),
                ClientSize.Width - Theme.S(48), ClientSize.Height - top - Theme.S(Theme.CtrlHeight) - Theme.S(112));
            Controls.Add(listCard);

            _list = new MListView();
            _list.RowHeight = Theme.S(46);
            _list.EmptyText = "没有找到运行中的程序\r\n用「浏览…」选择程序文件";
            _list.ItemActivated += delegate { AcceptSelected(); };
            listCard.Controls.Add(_list);
            _list.SetBounds(1, 1, listCard.ClientSize.Width - 2, listCard.ClientSize.Height - 2);

            // Buttons are sized from their own text: a fixed width clips "浏览…"
            // as soon as the system font renders wider than the design assumed.
            int gap = Theme.S(8);
            int buttonPad = Theme.S(30);
            int browseWidth = Math.Max(Theme.S(88),
                Theme.Measure("浏览…", Theme.BodyStrong).Width + buttonPad);
            int cancelWidth = Math.Max(Theme.S(88),
                Theme.Measure("暂不绑定", Theme.BodyStrong).Width + buttonPad);
            int okWidth = Math.Max(Theme.S(88),
                Theme.Measure("绑定", Theme.BodyStrong).Width + buttonPad);
            int buttonTop = ClientSize.Height - Theme.S(62);
            int buttonHeight = Theme.S(34);
            int right = ClientSize.Width - Theme.S(24);
            int okLeft = right - okWidth;
            int cancelLeft = okLeft - gap - cancelWidth;
            int browseLeft = cancelLeft - gap - browseWidth;

            _status = Theme.MakeLabel("", Theme.Caption, Theme.TextTertiary,
                Theme.S(24), buttonTop, Math.Max(Theme.S(120), browseLeft - Theme.S(12) - Theme.S(24)), Theme.S(20));
            Controls.Add(_status);

            MButton browse = new MButton();
            browse.Text = "浏览…";
            browse.Kind = MButtonKind.Subtle;
            browse.SetBounds(browseLeft, buttonTop, browseWidth, buttonHeight);
            browse.Click += delegate { Browse(); };
            Controls.Add(browse);

            MButton cancel = new MButton();
            cancel.Text = "暂不绑定";
            cancel.Kind = MButtonKind.Secondary;
            cancel.SetBounds(cancelLeft, buttonTop, cancelWidth, buttonHeight);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            MButton ok = new MButton();
            ok.Text = "绑定";
            ok.Kind = MButtonKind.Primary;
            ok.SetBounds(okLeft, buttonTop, okWidth, buttonHeight);
            ok.Click += delegate { AcceptSelected(); };
            Controls.Add(ok);

            LoadPrograms();
            Native.SetDarkTitleBar(Handle, Theme.IsDark);
        }

        private void LoadPrograms()
        {
            List<string> names = new List<string>();
            string self = Process.GetCurrentProcess().ProcessName;
            try
            {
                Process[] processes = Process.GetProcesses();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < processes.Length; i++)
                {
                    Process process = processes[i];
                    try
                    {
                        if (process.MainWindowHandle == IntPtr.Zero) continue;
                        string name = process.ProcessName;
                        if (string.IsNullOrEmpty(name)) continue;
                        if (string.Equals(name, self, StringComparison.OrdinalIgnoreCase)) continue;
                        if (seen.Add(name)) names.Add(name);
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
            }
            catch { }

            names.Sort(StringComparer.CurrentCultureIgnoreCase);
            _allRows = new List<MListItem>();
            for (int i = 0; i < names.Count; i++)
            {
                string process = ConfigStore.NormalizeProcessName(names[i]);
                if (process.Length == 0) continue;
                ShortcutPreset bound = ShortcutSets.FindByProcess(_settings, process);
                string hint = bound == null
                    ? "未绑定"
                    : (string.Equals(bound.Id, _excludePresetId, StringComparison.Ordinal)
                        ? "当前预设"
                        : "已绑定「" + bound.Name + "」");
                _allRows.Add(new ChoiceListItem(process, "运行中", process + ".exe", hint, "", process));
            }
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string query = _search == null ? "" : _search.Text.Trim();
            List<MListItem> rows = new List<MListItem>();
            for (int i = 0; i < _allRows.Count; i++)
            {
                ChoiceListItem row = _allRows[i] as ChoiceListItem;
                if (row == null) continue;
                if (query.Length > 0 && row.Key.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                rows.Add(row);
            }
            _list.SetItems(rows);
            if (rows.Count > 0) _list.SelectedIndex = 0;
            _status.Text = rows.Count == 0 ? "没有匹配的程序" : "共 " + rows.Count + " 个运行中的程序";
        }

        private void AcceptSelected()
        {
            ChoiceListItem row = _list.SelectedItem as ChoiceListItem;
            if (row == null)
            {
                _status.Text = "先选择一个程序，或用「浏览…」挑程序文件";
                return;
            }
            ProcessName = row.Tag as string;
            if (string.IsNullOrEmpty(ProcessName)) return;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Browse()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "选择程序";
                dialog.Filter = "程序|*.exe|所有文件|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                string process = ConfigStore.NormalizeProcessName(Path.GetFileName(dialog.FileName));
                if (process.Length == 0) return;
                ProcessName = process;
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}
