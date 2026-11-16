using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>Built-in file browser for the config directory.</summary>
    internal sealed class ConfigFolderForm : Form
    {
        private readonly string _directory;
        private readonly Label _status;
        private readonly MListView _list;

        public bool Changed { get; private set; }

        public ConfigFolderForm(string directory)
        {
            _directory = directory;

            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "配置文件";
            ClientSize = new Size(Theme.S(760), Theme.S(560));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            int top = Theme.DialogHeader(this, "配置文件",
                "查看配置文件与日志；双击打开，可直接编辑 settings.json。");

            MCard pathCard = new MCard();
            pathCard.SetBounds(Theme.S(24), top, ClientSize.Width - Theme.S(48), Theme.S(74));
            Controls.Add(pathCard);

            pathCard.Controls.Add(Theme.MakeLabel("当前目录", Theme.CaptionStrong, Theme.TextTertiary,
                Theme.S(16), Theme.S(10), Theme.S(100), Theme.S(18)));

            Label path = Theme.MakeLabel(PathDisplay.Friendly(directory), Theme.Body, Theme.TextPrimary,
                Theme.S(16), Theme.S(32), pathCard.Width - Theme.S(130), Theme.S(24));
            path.AutoEllipsis = true;
            pathCard.Controls.Add(path);
            ToolTip tip = new ToolTip();
            tip.SetToolTip(path, directory);

            MButton copy = new MButton();
            copy.Text = "复制路径";
            copy.Kind = MButtonKind.Subtle;
            copy.SetBounds(pathCard.Width - Theme.S(96), Theme.S(22), Theme.S(84), Theme.S(30));
            copy.Click += delegate
            {
                try { Clipboard.SetText(_directory); SetStatus("目录路径已复制", Theme.Success); }
                catch { SetStatus("复制失败", Theme.Danger); }
            };
            pathCard.Controls.Add(copy);

            MCard listCard = new MCard();
            listCard.Padding = new Padding(1);
            listCard.SetBounds(Theme.S(24), top + Theme.S(90), ClientSize.Width - Theme.S(48),
                Math.Max(Theme.S(120), ClientSize.Height - top - Theme.S(90) - Theme.S(84)));
            Controls.Add(listCard);

            _list = new MListView();
            _list.RowHeight = Theme.S(44);
            _list.EmptyText = "目录中还没有文件";
            _list.ItemActivated += delegate { OpenSelected(); };
            listCard.Controls.Add(_list);
            _list.SetBounds(1, 1, listCard.ClientSize.Width - 1, listCard.ClientSize.Height - 1);

            _status = Theme.MakeLabel("", Theme.Caption, Theme.TextTertiary,
                Theme.S(24), ClientSize.Height - Theme.S(64), Theme.S(400), Theme.S(20));
            Controls.Add(_status);

            MButton open = new MButton();
            open.Text = "打开";
            open.Kind = MButtonKind.Primary;
            open.SetBounds(ClientSize.Width - Theme.S(24) - Theme.S(76) - Theme.S(8) - Theme.S(84) - Theme.S(8) - Theme.S(84),
                ClientSize.Height - Theme.S(70), Theme.S(84), Theme.S(34));
            open.Click += delegate { OpenSelected(); };
            Controls.Add(open);

            MButton refresh = new MButton();
            refresh.Text = "刷新";
            refresh.Kind = MButtonKind.Secondary;
            refresh.SetBounds(ClientSize.Width - Theme.S(24) - Theme.S(76) - Theme.S(8) - Theme.S(84),
                ClientSize.Height - Theme.S(70), Theme.S(76), Theme.S(34));
            refresh.Click += delegate { RefreshFiles(); };
            Controls.Add(refresh);

            MButton close = new MButton();
            close.Text = "关闭";
            close.Kind = MButtonKind.Secondary;
            close.SetBounds(ClientSize.Width - Theme.S(24) - Theme.S(76),
                ClientSize.Height - Theme.S(70), Theme.S(76), Theme.S(34));
            close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(close);

            RefreshFiles();
            Native.SetDarkTitleBar(Handle, Theme.IsDark);
        }

        private void SetStatus(string text, Color color)
        {
            _status.Text = text;
            _status.ForeColor = color;
        }

        private void RefreshFiles()
        {
            List<MListItem> rows = new List<MListItem>();
            try
            {
                DirectoryInfo info = new DirectoryInfo(_directory);
                if (info.Exists)
                {
                    FileInfo[] files = info.GetFiles();
                    Array.Sort(files, delegate(FileInfo a, FileInfo b)
                    {
                        return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    });
                    foreach (FileInfo file in files)
                    {
                        rows.Add(new ChoiceListItem(file.FullName, null, file.Name,
                            FormatSize(file.Length) + " · " + file.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                            null, file.FullName));
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus("读取目录失败：" + ex.Message, Theme.Danger);
            }
            _list.SetItems(rows);
            if (rows.Count > 0) _list.SelectedIndex = 0;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024)
                return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        private void OpenSelected()
        {
            ChoiceListItem item = _list.SelectedItem as ChoiceListItem;
            string path = item == null ? Path.Combine(_directory, "settings.json") : item.Tag as string;
            if (path == null || !File.Exists(path))
            {
                SetStatus("文件不存在，请先运行程序生成配置。", Theme.Danger);
                return;
            }

            bool editable = string.Equals(Path.GetFileName(path), "settings.json",
                StringComparison.OrdinalIgnoreCase);
            using (TextFileForm dialog = new TextFileForm(path, editable))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Changed)
                {
                    Changed = true;
                    RefreshFiles();
                    SetStatus("配置已保存。", Theme.Success);
                }
            }
        }
    }

    /// <summary>Plain text viewer/editor used for settings.json and the log.</summary>
    internal sealed class TextFileForm : Form
    {
        private readonly string _path;
        private readonly MTextArea _text;
        private readonly MTextView _view;

        public bool Changed { get; private set; }

        public TextFileForm(string path, bool editable)
        {
            _path = path;

            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = Path.GetFileName(path);
            ClientSize = new Size(Theme.S(820), Theme.S(600));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            Controls.Add(Theme.MakeLabel(Path.GetFileName(path), Theme.Title, Theme.TextPrimary,
                Theme.S(24), Theme.S(18), Theme.S(500), Theme.S(26)));

            Label subtitle = Theme.MakeLabel(PathDisplay.Shorten(PathDisplay.Friendly(path), 72),
                Theme.Caption, Theme.TextTertiary, Theme.S(24), Theme.S(48), Theme.S(760), Theme.S(18));
            subtitle.AutoEllipsis = true;
            Controls.Add(subtitle);

            MCard card = new MCard();
            card.Padding = new Padding(1);
            card.SetBounds(Theme.S(24), Theme.S(82), ClientSize.Width - Theme.S(48),
                ClientSize.Height - Theme.S(82) - Theme.S(84));
            Controls.Add(card);

            string content;
            try { content = File.ReadAllText(path, Encoding.UTF8); }
            catch (Exception ex) { content = "读取文件失败：" + ex.Message; }

            if (editable)
            {
                _text = new MTextArea();
                _text.Monospace = true;
                _text.Text = content;
                card.Controls.Add(_text);
                _text.SetBounds(Theme.S(8), Theme.S(8), card.ClientSize.Width - Theme.S(16),
                    card.ClientSize.Height - Theme.S(16));
                _text.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            }
            else
            {
                _view = new MTextView();
                _view.Monospace = true;
                _view.Text = content;
                card.Controls.Add(_view);
                _view.SetBounds(Theme.S(8), Theme.S(8), card.ClientSize.Width - Theme.S(16),
                    card.ClientSize.Height - Theme.S(16));
                _view.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            }

            Label hint = Theme.MakeLabel(editable
                    ? "保存前会校验 JSON；保存后程序自动重新加载。"
                    : "该文件为只读预览。",
                Theme.Caption, Theme.TextTertiary, Theme.S(24),
                ClientSize.Height - Theme.S(62), Theme.S(420), Theme.S(18));
            Controls.Add(hint);

            if (editable)
            {
                MButton save = new MButton();
                save.Text = "保存";
                save.Kind = MButtonKind.Primary;
                save.SetBounds(ClientSize.Width - Theme.S(24) - Theme.S(84) - Theme.S(8) - Theme.S(84),
                    ClientSize.Height - Theme.S(68), Theme.S(84), Theme.S(34));
                save.Click += delegate { SaveFile(); };
                Controls.Add(save);
            }

            MButton close = new MButton();
            close.Text = editable ? "取消" : "关闭";
            close.Kind = MButtonKind.Secondary;
            close.SetBounds(ClientSize.Width - Theme.S(24) - Theme.S(84),
                ClientSize.Height - Theme.S(68), Theme.S(84), Theme.S(34));
            close.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(close);

            Native.SetDarkTitleBar(Handle, Theme.IsDark);
        }

        private void SaveFile()
        {
            if (_text == null) return;
            string error = ConfigStore.ValidateJson(_text.Text);
            if (error != null)
            {
                MDialog.Tell(this, "无法保存", "JSON 格式不正确：\r\n\r\n" + error, MDialogKind.Warning);
                return;
            }
            try
            {
                File.WriteAllText(_path, _text.Text, new UTF8Encoding(false));
                Changed = true;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MDialog.Tell(this, "无法保存", "保存失败：" + ex.Message, MDialogKind.Error);
            }
        }
    }

    /// <summary>Shows the JavaScript-escaped clipboard text.</summary>
    internal sealed class EscapeResultForm : Form
    {
        public EscapeResultForm(string escaped, string original)
        {
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "转义结果";
            ClientSize = new Size(Theme.S(640), Theme.S(480));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            Controls.Add(Theme.MakeLabel("JavaScript 转义结果", Theme.Title, Theme.TextPrimary,
                Theme.S(24), Theme.S(18), Theme.S(400), Theme.S(26)));
            Controls.Add(Theme.MakeLabel("已转义 " + (original == null ? 0 : original.Length) + " 个字符。",
                Theme.Caption, Theme.TextTertiary, Theme.S(24), Theme.S(48), Theme.S(500), Theme.S(18)));

            MCard card = new MCard();
            card.Padding = new Padding(1);
            card.SetBounds(Theme.S(24), Theme.S(80), ClientSize.Width - Theme.S(48),
                ClientSize.Height - Theme.S(80) - Theme.S(80));
            Controls.Add(card);

            MTextView box = new MTextView();
            box.Monospace = true;
            box.Text = escaped;
            card.Controls.Add(box);
            box.SetBounds(Theme.S(8), Theme.S(8), card.ClientSize.Width - Theme.S(16),
                card.ClientSize.Height - Theme.S(16));

            MButton copy = new MButton();
            copy.Text = "复制结果";
            copy.Kind = MButtonKind.Primary;
            copy.SetBounds(ClientSize.Width - Theme.S(24) - Theme.S(96) - Theme.S(8) - Theme.S(76),
                ClientSize.Height - Theme.S(64), Theme.S(96), Theme.S(34));
            copy.Click += delegate
            {
                try { Clipboard.SetText(escaped); }
                catch { }
            };
            Controls.Add(copy);

            MButton close = new MButton();
            close.Text = "关闭";
            close.Kind = MButtonKind.Secondary;
            close.SetBounds(ClientSize.Width - Theme.S(24) - Theme.S(76),
                ClientSize.Height - Theme.S(64), Theme.S(76), Theme.S(34));
            close.Click += delegate { Close(); };
            Controls.Add(close);

            Native.SetDarkTitleBar(Handle, Theme.IsDark);
        }
    }
}
