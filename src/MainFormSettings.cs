using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapsLockLite
{
    internal sealed partial class MainForm
    {
        private sealed class SettingRow
        {
            public Control Right;
            public Label Title;
            public Label Desc;
            public int Y;
            public int Height;
        }

        private readonly List<SettingRow> _rowInfos = new List<SettingRow>();
        private int _aboutHeight = 430;
        private int _rowHeight = 64;
        private int _dataHeight = 218;

        // Safe distances: every vertical gap is explicit so nothing can overlap.
        private const int RowPadTop = 10;
        private const int RowPadBottom = 12;
        private const int LabelGap = 2;
        private const int BlockGap = 14;

        // ---- settings page --------------------------------------------------
        private Panel BuildSettingsPage()
        {
            Panel page = new Panel();
            page.BackColor = Theme.Background;
            _rowInfos.Clear();

            // Row height follows the text that must fit inside it, plus padding.
            int titleH = Theme.LineHeight(Theme.BodyStrong) + 2;
            int descH = Theme.LineHeight(Theme.Caption) + 2;
            _rowHeight = Theme.S(RowPadTop) + titleH + Theme.S(LabelGap)
                + descH + Theme.S(RowPadBottom);

            _segments = new MSegmented();
            _segments.Items.Add("外观");
            _segments.Items.Add("行为");
            _segments.Items.Add("启动与数据");
            _segments.SelectedIndex = _settingsSection;
            _segments.SelectedIndexChanged += delegate
            {
                _settingsSection = _segments.SelectedIndex;
                RebuildSettingsSection();
            };
            page.Controls.Add(_segments);

            _settingsHost = new Panel();
            _settingsHost.BackColor = Theme.Background;
            page.Controls.Add(_settingsHost);

            _cardAppearance = BuildAppearanceCard();
            _cardBehavior = BuildBehaviorCard();
            _cardData = BuildDataCard();
            _settingsHost.Controls.Add(_cardAppearance);
            _settingsHost.Controls.Add(_cardBehavior);
            _settingsHost.Controls.Add(_cardData);

            RebuildSettingsSection();
            return page;
        }

        private MCard NewCard()
        {
            MCard card = new MCard();
            card.BackColor = Color.Transparent;
            return card;
        }

        /// <summary>
        /// Adds a settings row: title, optional description and a right-aligned
        /// control. Every element is placed from the previous one's Bottom so the
        /// row can never overlap its separator or the next block.
        /// </summary>
        private int AddRow(MCard card, string title, string desc, Control right, int y)
        {
            int padX = Theme.S(18);
            int titleY = y + Theme.S(RowPadTop);

            Label titleLabel = Theme.MakeLine(title, Theme.BodyStrong, Theme.TextPrimary,
                padX, titleY, Theme.S(300));
            card.Controls.Add(titleLabel);

            Label descLabel = null;
            if (!string.IsNullOrEmpty(desc))
            {
                descLabel = Theme.MakeLine(desc, Theme.Caption, Theme.TextTertiary,
                    padX, titleLabel.Bottom + Theme.S(LabelGap), Theme.S(430));
                card.Controls.Add(descLabel);
            }

            if (right != null)
            {
                card.Controls.Add(right);
                SettingRow row = new SettingRow();
                row.Right = right;
                row.Title = titleLabel;
                row.Desc = descLabel;
                row.Y = y;
                row.Height = _rowHeight;
                _rowInfos.Add(row);
            }

            // Separator is painted by the card itself, so no sibling window can
            // ever erase it.
            card.AddSeparator(y + _rowHeight);
            return y + _rowHeight;
        }

        private MCard BuildAppearanceCard()
        {
            MCard card = NewCard();
            int y = 0;

            _darkToggle = new MToggle();
            _darkToggle.Size = new Size(Theme.S(40), Theme.S(20));
            _darkToggle.CheckedChanged += delegate
            {
                if (_syncing) return;
                _settings.DarkMode = _darkToggle.Checked;
                SaveQuiet();
                BeginInvoke(new Action(ApplyTheme));
            };
            y = AddRow(card, "深色模式", "使用深色背景与浅色文字，切换后立即生效。", _darkToggle, y);

            _accentSwatch = new Label();
            _accentSwatch.Size = new Size(Theme.S(22), Theme.S(22));
            _accentSwatch.BackColor = Theme.Accent;
            y = AddRow(card, "强调色", "跟随 Windows 系统强调色，用于按钮与选中状态。", _accentSwatch, y);

            return card;
        }

        private MCard BuildBehaviorCard()
        {
            MCard card = NewCard();
            int y = 0;

            _tapCombo = new MComboBox();
            _tapCombo.Items.Add("切换大小写");
            _tapCombo.Items.Add("发送 Esc");
            _tapCombo.Items.Add("不操作");
            _tapCombo.Width = Theme.S(150);
            _tapCombo.SelectedIndexChanged += delegate
            {
                if (_syncing) return;
                if (_tapCombo.SelectedIndex == 1) _settings.TapAction = "esc";
                else if (_tapCombo.SelectedIndex == 2) _settings.TapAction = "none";
                else _settings.TapAction = "caps";
                SaveQuiet();
                ShowStatus("短按 Caps Lock 的行为已更新", MInfoSeverity.Info);
            };
            y = AddRow(card, "短按 Caps Lock", "轻按且没有按组合键时执行的动作。", _tapCombo, y);

            Panel numericHost = new Panel();
            numericHost.BackColor = Color.Transparent;
            numericHost.Size = new Size(Theme.S(140), Theme.S(32));

            _longPress = new MNumberBox();
            _longPress.Minimum = 150;
            _longPress.Maximum = 2000;
            _longPress.Increment = 50;
            _longPress.SetBounds(0, 0, Theme.S(84), Theme.S(28));
            _longPress.ValueChanged += delegate
            {
                if (_syncing) return;
                _settings.LongPressMs = _longPress.Value;
                SaveQuiet();
            };
            numericHost.Controls.Add(_longPress);

            Label unit = Theme.MakeLine("毫秒", Theme.Caption, Theme.TextTertiary,
                Theme.S(92), Theme.S(6), Theme.S(44));
            numericHost.Controls.Add(unit);

            y = AddRow(card, "长按判定", "按住超过该时长且未按其它键，不触发短按动作。", numericHost, y);

            return card;
        }

        private MCard BuildDataCard()
        {
            MCard card = NewCard();
            int y = 0;

            _startupToggle = new MToggle();
            _startupToggle.Size = new Size(Theme.S(40), Theme.S(20));
            _startupToggle.CheckedChanged += delegate
            {
                if (_syncing) return;
                bool enabled = _startupToggle.Checked;
                bool applied;
                PauseEngine();
                try { applied = StartupManager.Apply(enabled, _settings.StartMinimized); }
                finally { ResumeEngine(); }
                if (!applied)
                {
                    _settings.RunAtStartup = false;
                    _syncing = true;
                    _startupToggle.Checked = false;
                    _syncing = false;
                    SaveQuiet();
                    ShowStatus("系统拒绝写入开机启动项：" + StartupManager.LastError, MInfoSeverity.Error);
                    return;
                }
                _settings.RunAtStartup = enabled;
                SaveQuiet();
            };
            y = AddRow(card, "开机自动启动", "登录 Windows 后自动运行，仅写入当前用户。", _startupToggle, y);

            _minimizedToggle = new MToggle();
            _minimizedToggle.Size = new Size(Theme.S(40), Theme.S(20));
            _minimizedToggle.CheckedChanged += delegate
            {
                if (_syncing) return;
                _settings.StartMinimized = _minimizedToggle.Checked;
                if (_settings.RunAtStartup)
                {
                    PauseEngine();
                    try { StartupManager.Apply(true, _settings.StartMinimized); }
                    finally { ResumeEngine(); }
                }
                SaveQuiet();
            };
            y = AddRow(card, "启动时最小化", "启动后只显示托盘图标，不弹出窗口。", _minimizedToggle, y);

            Label pathTitle = Theme.MakeLine("配置文件", Theme.BodyStrong, Theme.TextPrimary,
                Theme.S(18), y + Theme.S(RowPadTop), Theme.S(300));
            card.Controls.Add(pathTitle);

            _configPath = Theme.MakeLine(PathDisplay.Friendly(ConfigStore.DisplayConfigPath),
                Theme.Caption, Theme.TextTertiary, Theme.S(18),
                pathTitle.Bottom + Theme.S(LabelGap), Theme.S(430));
            card.Controls.Add(_configPath);

            ToolTip tip = new ToolTip();
            tip.SetToolTip(_configPath, ConfigStore.ConfigPath);

            // Buttons start below the path line with a guaranteed gap.
            int buttonY = _configPath.Bottom + Theme.S(BlockGap);
            MButton open = new MButton();
            open.Text = "配置文件…";
            open.Kind = MButtonKind.Secondary;
            open.SetBounds(Theme.S(18), buttonY, Theme.S(100), Theme.S(30));
            open.Click += delegate { OpenConfigFolder(); };
            card.Controls.Add(open);

            MButton export = new MButton();
            export.Text = "导出…";
            export.Kind = MButtonKind.Secondary;
            export.SetBounds(Theme.S(126), buttonY, Theme.S(72), Theme.S(30));
            export.Click += delegate { ExportSettings(); };
            card.Controls.Add(export);

            MButton import = new MButton();
            import.Text = "导入…";
            import.Kind = MButtonKind.Secondary;
            import.SetBounds(Theme.S(206), buttonY, Theme.S(72), Theme.S(30));
            import.Click += delegate { ImportSettings(); };
            card.Controls.Add(import);

            MButton copy = new MButton();
            copy.Text = "复制路径";
            copy.Kind = MButtonKind.Subtle;
            copy.SetBounds(Theme.S(286), buttonY, Theme.S(88), Theme.S(30));
            copy.Click += delegate
            {
                try { Clipboard.SetText(ConfigStore.ConfigPath); ShowStatus("路径已复制", MInfoSeverity.Success); }
                catch { ShowStatus("复制失败", MInfoSeverity.Error); }
            };
            card.Controls.Add(copy);

            // Card height comes from the last element, never a magic number.
            _dataHeight = copy.Bottom + Theme.S(16);
            return card;
        }

        private void RebuildSettingsSection()
        {
            if (_settingsHost == null) return;
            _cardAppearance.Visible = _settingsSection == 0;
            _cardBehavior.Visible = _settingsSection == 1;
            _cardData.Visible = _settingsSection == 2;
            LayoutSettingsPage();
        }

        private int CardHeightFor(int section)
        {
            // Derived from measured content: two rows, plus the config block for section 2.
            if (section == 2) return _dataHeight;
            return _rowHeight * 2;
        }

        private void LayoutSettingsPage()
        {
            if (_pageSettings == null || _segments == null) return;
            int w = _pageSettings.ClientSize.Width;
            int h = _pageSettings.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            int segWidth = Math.Min(Theme.S(360), Math.Max(Theme.S(240), w / 2));
            _segments.SetBounds(0, 0, segWidth, Theme.S(34));
            _settingsHost.SetBounds(0, Theme.S(46), w, Math.Max(Theme.S(80), h - Theme.S(46)));

            MCard card = _settingsSection == 0 ? _cardAppearance
                : _settingsSection == 1 ? _cardBehavior : _cardData;
            if (card == null) return;

            int cardHeight = CardHeightFor(_settingsSection);
            card.SetBounds(0, 0, w, Math.Min(cardHeight, _settingsHost.ClientSize.Height));

            int padX = Theme.S(18);
            foreach (SettingRow row in _rowInfos)
            {
                if (row.Right == null) continue;

                // Right control keeps a fixed margin from the card edge; text gets
                // the remaining width so it can never run underneath it.
                int x = card.ClientSize.Width - padX - row.Right.Width;
                row.Right.Location = new Point(
                    Math.Max(padX + Theme.S(80), x),
                    row.Y + row.Height / 2 - row.Right.Height / 2);

                int textWidth = Math.Max(Theme.S(80),
                    row.Right.Left - padX - Theme.S(16));
                if (row.Title != null) row.Title.Width = textWidth;
                if (row.Desc != null) row.Desc.Width = textWidth;
            }

            if (_configPath != null)
                _configPath.Width = Math.Max(Theme.S(120), card.ClientSize.Width - padX * 2);
        }

        private void SyncControlsFromSettings()
        {
            _syncing = true;
            try
            {
                if (_darkToggle != null) _darkToggle.Checked = _settings.DarkMode;
                if (_startupToggle != null) _startupToggle.Checked = _settings.RunAtStartup;
                if (_minimizedToggle != null) _minimizedToggle.Checked = _settings.StartMinimized;
                if (_tapCombo != null)
                {
                    if (_settings.TapAction == "esc") _tapCombo.SelectedIndex = 1;
                    else if (_settings.TapAction == "none") _tapCombo.SelectedIndex = 2;
                    else _tapCombo.SelectedIndex = 0;
                }
                if (_longPress != null)
                {
                    int value = _settings.LongPressMs;
                    if (value < _longPress.Minimum) value = _longPress.Minimum;
                    if (value > _longPress.Maximum) value = _longPress.Maximum;
                    _longPress.Value = value;
                }
                if (_configPath != null)
                    _configPath.Text = PathDisplay.Friendly(ConfigStore.DisplayConfigPath);
                if (_accentSwatch != null)
                    _accentSwatch.BackColor = Theme.Accent;
            }
            finally { _syncing = false; }
        }

        // ---- about page -----------------------------------------------------
        private Panel BuildAboutPage()
        {
            Panel page = new Panel();
            page.BackColor = Theme.Background;

            _cardAbout = NewCard();
            page.Controls.Add(_cardAbout);

            int pad = Theme.S(20);
            int contentWidth = Theme.S(560);
            int y = Theme.S(14);

            MLogo logo = new MLogo();
            logo.Size = new Size(Theme.S(48), Theme.S(48));
            logo.Location = new Point(pad, y);
            _cardAbout.Controls.Add(logo);

            Label name = Theme.MakeLine("CapsLock Lite",
                new Font("Microsoft YaHei UI", 15f, FontStyle.Bold), Theme.TextPrimary,
                pad + Theme.S(62), y - Theme.S(4), Theme.S(320));
            _cardAbout.Controls.Add(name);

            Label version = Theme.MakeLine(AppInfo.AboutVersion + " · 受 Capslock+ 启发的独立实现",
                Theme.Caption, Theme.TextTertiary,
                pad + Theme.S(62), name.Bottom + Theme.S(1), contentWidth - Theme.S(62));
            _cardAbout.Controls.Add(version);

            y = Math.Max(logo.Bottom, version.Bottom) + Theme.S(12);

            Label intro = Theme.MakeBlock(
                "把 Caps Lock 当作组合键前缀：短按切换大小写，按住构成组合键调用任意动作。"
                + "支持图形化交互；支持海量插件；支持自定义宏。",
                Theme.Body, Theme.TextSecondary, pad, y, contentWidth);
            _cardAbout.Controls.Add(intro);
            y += intro.Height + Theme.S(10);

            Label featuresTitle = Theme.MakeLine("快捷操作", Theme.Subtitle, Theme.TextPrimary,
                pad, y, Theme.S(200));
            _cardAbout.Controls.Add(featuresTitle);
            y += featuresTitle.Height + Theme.S(6);

            // Two aligned columns: a key label and its meaning, no manual spacing.
            string[,] shortcuts = new string[,]
            {
                { "Ctrl + N", "新增快捷键",   "Esc",      "清空筛选" },
                { "Ctrl + F", "搜索",         "Ctrl + D", "切换深色模式" },
                { "Enter",    "编辑选中",     "Space",    "启用 / 停用" },
                { "Del",      "删除选中",     "F5",       "重新加载配置" },
                { "Ctrl/Shift", "点选 / 连选", "Ctrl + A", "全选" },
                { "按住拖动",   "调整顺序",     "拖动多选", "整块移动" }
            };

            int rowHeight = Math.Max(Theme.S(23), Theme.LineHeight(Theme.Body));
            int colWidth = contentWidth / 2;
            int keyWidth = Theme.S(92);
            for (int row = 0; row < 6; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    string keyText = shortcuts[row, col * 2];
                    if (keyText.Length == 0) continue;
                    int x = pad + col * colWidth;
                    Label keyLabel = Theme.MakeLine(keyText, Theme.BodyStrong,
                        Theme.Accent, x, y, keyWidth);
                    _cardAbout.Controls.Add(keyLabel);

                    Label descLabel = Theme.MakeLine(shortcuts[row, col * 2 + 1], Theme.Body,
                        Theme.TextSecondary, x + keyWidth, y, colWidth - keyWidth - Theme.S(8));
                    _cardAbout.Controls.Add(descLabel);
                }
                y += rowHeight;
            }
            y += Theme.S(10);

            Label limitTitle = Theme.MakeLine("兼容性提示", Theme.Subtitle, Theme.TextPrimary,
                pad, y, Theme.S(200));
            _cardAbout.Controls.Add(limitTitle);
            y += limitTitle.Height + Theme.S(6);

            Label limits = Theme.MakeBlock(
                "• 以管理员身份运行的程序可能无法接收模拟按键。\r\n" +
                "• 低层键盘钩子可能被杀毒软件误报，请加入白名单。\r\n" +
                "• 若同时运行原版 Capslock+，请先退出其中一个。",
                Theme.Caption, Theme.TextSecondary, pad, y, contentWidth);
            _cardAbout.Controls.Add(limits);
            y += limits.Height + Theme.S(8);

            LinkLabel docs = new LinkLabel();
            docs.Text = "Capslock+ 官方说明";
            docs.Font = Theme.Body;
            docs.LinkColor = Theme.Accent;
            docs.ActiveLinkColor = Theme.AccentPressed;
            docs.LinkBehavior = LinkBehavior.HoverUnderline;
            docs.AutoSize = true;
            docs.Location = new Point(pad, y);
            docs.LinkClicked += delegate { OpenHelp(); };
            _cardAbout.Controls.Add(docs);

            LinkLabel configLink = new LinkLabel();
            configLink.Text = "打开配置文件";
            configLink.Font = Theme.Body;
            configLink.LinkColor = Theme.Accent;
            configLink.ActiveLinkColor = Theme.AccentPressed;
            configLink.LinkBehavior = LinkBehavior.HoverUnderline;
            configLink.AutoSize = true;
            configLink.Location = new Point(pad + docs.Width + Theme.S(24), y);
            configLink.LinkClicked += delegate { OpenConfigFolder(); };
            _cardAbout.Controls.Add(configLink);

            y += Math.Max(Theme.S(22), Theme.LineHeight(Theme.Body)) + Theme.S(6);

            Label disclaimer = Theme.MakeBlock(
                "本程序为独立实现，与 Capslock+ 作者无关联；代码与图标均为DeepSeek原创。",
                Theme.Caption, Theme.TextTertiary, pad, y, contentWidth);
            _cardAbout.Controls.Add(disclaimer);
            y += disclaimer.Height + Theme.S(10);

            _aboutHeight = y;
            return page;
        }

        private void LayoutAboutPage()
        {
            if (_pageAbout == null || _cardAbout == null) return;
            int w = _pageAbout.ClientSize.Width;
            int h = _pageAbout.ClientSize.Height;
            if (w <= 0 || h <= 0) return;
            _cardAbout.SetBounds(0, 0, Math.Min(w, Theme.S(600)), Math.Min(_aboutHeight, h));
        }
    }
}
