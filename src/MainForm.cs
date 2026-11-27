using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapsLockLite
{
    internal sealed partial class MainForm : Form, IAppHost
    {
        private const int SidebarWidth = 212;

        private AppSettings _settings;
        private HotkeyManager _engine;
        private ActionExecutor _executor;
        private UiHost _uiHost;
        private DispatcherThread _inputThread;
        private bool _exiting;
        private bool _syncing;
        private bool _balloonShown;
        private int _uiDpi = 96;
        private bool _dpiRebuildScheduled;
        private bool _windowSizeReady;
        private Timer _windowSaveTimer;
        private readonly bool _startMinimized;
        private readonly string _initialPage;
        private readonly string _demo;
        private Icon _appIcon;
        // True once the "start in tray" first-show suppression has been used.
        private bool _initialShowHandled;
        // True once the window has actually been on screen in this process.
        private bool _everShown;
        // True once the hotkey engine / executor have been created.
        private bool _runtimeStarted;

        // shell
        private Panel _sidebar;
        private Panel _divider;
        private Panel _content;
        private Label _titleLabel;
        private Label _subtitleLabel;
        private Label _statusLabel;
        private MLogo _logo;
        private Label _tagline;
        private MInfoBar _infoBar;
        private Timer _infoTimer;
        private MNavItem _navKeys;
        private MNavItem _navSettings;
        private MNavItem _navAbout;
        private string _currentPage = "keys";
        // True once LayoutShell has sized the shell; page switches before that
        // (the initial one in the constructor) must not lay out against the
        // placeholder 150x150 panel sizes.
        private bool _shellReady;

        // pages
        private Panel _pageHost;
        private Panel _pageKeys;
        private Panel _pageSettings;
        private Panel _pageAbout;

        // keys page
        private MSearchBox _search;
        private MButton _addButton;
        private MIconButton _moreButton;
        private MCard _listCard;
        private MListView _list;
        private Label _countLabel;
        private Label _hintLabel;

        // settings page
        private MSegmented _segments;
        private Panel _settingsHost;
        private MCard _cardAppearance;
        private MCard _cardBehavior;
        private MCard _cardData;
        private MToggle _darkToggle;
        private MToggle _startupToggle;
        private MToggle _minimizedToggle;
        private MComboBox _tapCombo;
        private MComboBox _inputBackend;
        private MNumberBox _longPress;
        private MNumberBox _wheelStep;
        private Label _configPath;
        private Label _accentSwatch;
        private int _settingsSection;

        // about page
        private MCard _cardAbout;

        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayToggleItem;

        private const string HelpUrl = "https://capslox.com/capslock-plus/";
        private const int DefaultClientWidth = 1160;
        private const int DefaultClientHeight = 780;

        public MainForm(bool startMinimized, string initialPage, string demo)
        {
            Theme.SetDpi(DeviceDpi);
            _uiDpi = Theme.Dpi;
            // Config first, so the plugin subsystem knows whether the user has
            // opted in. Plugins are not loaded at all while disabled.
            _settings = ConfigStore.LoadOrCreate();
            Theme.Apply(_settings.DarkMode);
            PluginRuntime.Initialize();
            ConfigStore.Normalize(_settings);

            _startMinimized = startMinimized || _settings.StartMinimized;
            _initialPage = string.IsNullOrEmpty(initialPage) ? "keys" : initialPage;
            _demo = demo ?? "";

            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;
            ClientSize = StartupClientSize();
            MinimumSize = new Size(Theme.S(880), Theme.S(680));
            StartPosition = FormStartPosition.CenterScreen;
            Text = "CapsLock Lite";
            KeyPreview = true;
            DoubleBuffered = true;
            _appIcon = LoadAppIcon();
            Icon = _appIcon;

            AllowDrop = true;
            DragEnter += HandlePluginDragEnter;
            DragDrop += HandlePluginDragDrop;

            BuildShell();
            CreateTray();

            _infoTimer = new Timer();
            _infoTimer.Interval = 4200;
            _infoTimer.Tick += delegate
            {
                _infoTimer.Stop();
                if (_infoBar != null) _infoBar.Visible = false;
            };

            SwitchPage(_initialPage);
            RefreshList();
            SyncControlsFromSettings();

            Resize += delegate
            {
                LayoutShell();
                ScheduleWindowSizeSave();
            };
            Shown += MainFormShown;
            FormClosing += MainFormClosing;
            LayoutShell();
        }

        private static Icon LoadAppIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) return icon;
            }
            catch { }
            return SystemIcons.Application;
        }

        // ---- shell ---------------------------------------------------------
        private void BuildShell()
        {
            _sidebar = new Panel();
            _sidebar.BackColor = Theme.Background;
            Controls.Add(_sidebar);

            // The logo doubles as the master switch. It is the one global state
            // that belongs to the whole app rather than to a page, and a greyscale
            // logo reads as "paused" without adding a second control to the rail.
            _logo = new MLogo();
            _logo.Location = new Point(Theme.S(22), Theme.S(22));
            _logo.Size = new Size(Theme.S(38), Theme.S(38));
            _logo.Cursor = Cursors.Hand;
            _logo.AccessibleName = "启用或暂停全部快捷键";
            _logo.Click += delegate { SetEnabled(!_settings.Enabled); };
            _sidebar.Controls.Add(_logo);

            Label name = Theme.MakeLabel("CapsLock Lite", new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                Theme.TextPrimary, Theme.S(70), Theme.S(20), Theme.S(140), Theme.S(24));
            name.AutoSize = true;
            _sidebar.Controls.Add(name);

            _tagline = Theme.MakeLabel("键盘效率增强", Theme.Caption, Theme.TextTertiary,
                Theme.S(71), Theme.S(44), Theme.S(140), Theme.S(18));
            _tagline.AutoSize = true;
            _sidebar.Controls.Add(_tagline);

            _divider = new Panel();
            _divider.BackColor = Theme.Stroke;
            _sidebar.Controls.Add(_divider);

            _navKeys = MakeNav("快捷键", "\uE765", Theme.S(88));
            _navSettings = MakeNav("基本设置", "\uE713", Theme.S(132));
            _navMacros = MakeNav("宏动作", "\uE943", Theme.S(176));
            _navPlugins = MakeNav("插件", "\uE71D", Theme.S(220));
            _navAbout = MakeNav("帮助与关于", "\uE946", Theme.S(264));
            _navKeys.Click += delegate { SwitchPage("keys"); };
            _navSettings.Click += delegate { SwitchPage("settings"); };
            _navMacros.Click += delegate { SwitchPage("macros"); };
            _navPlugins.Click += delegate { SwitchPage("plugins"); };
            _navAbout.Click += delegate { SwitchPage("about"); };
            _sidebar.Controls.Add(_navKeys);
            _sidebar.Controls.Add(_navSettings);
            _sidebar.Controls.Add(_navMacros);
            _sidebar.Controls.Add(_navPlugins);
            _sidebar.Controls.Add(_navAbout);

            _statusLabel = Theme.MakeLabel("● 运行中", Theme.Caption, Theme.Success,
                Theme.S(71), Theme.S(64), Theme.S(160), Theme.S(20));
            _sidebar.Controls.Add(_statusLabel);

            Label version = Theme.MakeLabel(AppInfo.VersionText, Theme.Caption, Theme.TextTertiary,
                Theme.S(24), 0, Theme.S(160), Theme.S(18));
            _sidebar.Controls.Add(version);
            version.Name = "versionLabel";

            _content = new Panel();
            _content.BackColor = Theme.Background;
            Controls.Add(_content);

            _titleLabel = Theme.MakeLabel("快捷键", Theme.Display, Theme.TextPrimary,
                0, 0, Theme.S(520), Theme.S(38));
            _content.Controls.Add(_titleLabel);

            _subtitleLabel = Theme.MakeLabel("", Theme.Caption, Theme.TextSecondary,
                0, 0, Theme.S(520), Theme.S(20));
            _content.Controls.Add(_subtitleLabel);

            _pageHost = new Panel();
            _pageHost.BackColor = Theme.Background;
            _content.Controls.Add(_pageHost);

            _pageKeys = BuildKeysPage();
            _pageSettings = BuildSettingsPage();
            _pageMacros = BuildMacrosPage();
            _pagePlugins = BuildPluginsPage();
            _pageAbout = BuildAboutPage();
            _pageHost.Controls.Add(_pageKeys);
            _pageHost.Controls.Add(_pageSettings);
            _pageHost.Controls.Add(_pageMacros);
            _pageHost.Controls.Add(_pagePlugins);
            _pageHost.Controls.Add(_pageAbout);

            _infoBar = new MInfoBar();
            _content.Controls.Add(_infoBar);

            AttachDropTargets(this);
        }

        private MNavItem MakeNav(string text, string glyph, int y)
        {
            MNavItem item = new MNavItem();
            item.Text = text;
            item.Glyph = glyph;
            item.Location = new Point(Theme.S(12), y);
            item.Size = new Size(Theme.S(SidebarWidth - 24), Theme.S(40));
            return item;
        }

        private void LayoutShell()
        {
            if (_sidebar == null || _content == null) return;
            int rail = Theme.S(SidebarWidth);
            _sidebar.Bounds = new Rectangle(0, 0, rail, ClientSize.Height);
            _content.Bounds = new Rectangle(rail + 1, 0,
                Math.Max(Theme.S(200), ClientSize.Width - rail - 1), ClientSize.Height);

            // hairline between rail and content
            if (_divider != null)
            {
                _divider.SetBounds(rail - 1, 0, 1, ClientSize.Height);
                _divider.BringToFront();
            }

            PositionSidebarFooter();
            LayoutContent();
            _shellReady = true;
        }

        private void PositionSidebarFooter()
        {
            int h = ClientSize.Height;
            int pad = Theme.S(24);

            // Footer rhythm: the running indicator moved up next to the brand
            // block, so only the version stays at the bottom of the rail.
            int versionY = h - Theme.S(36);

            if (_statusLabel != null && _tagline != null)
            {
                // Under the tagline, flush with the text column, exactly where the
                // old footer row used to be. Anchored to the tagline so a DPI or
                // font change cannot reintroduce a gap.
                _statusLabel.Location = new Point(_tagline.Left, _tagline.Bottom + Theme.S(4));
            }

            // Nav rows start below the brand block instead of a fixed offset: with
            // the running indicator under the tagline, S(88) collided with it.
            int navTop = Theme.S(96);
            if (_statusLabel != null) navTop = Math.Max(navTop, _statusLabel.Bottom + Theme.S(14));
            MNavItem[] navs = { _navKeys, _navSettings, _navMacros, _navPlugins, _navAbout };
            for (int i = 0; i < navs.Length; i++)
            {
                if (navs[i] == null) continue;
                navs[i].Location = new Point(Theme.S(12), navTop + i * Theme.S(44));
            }

            Control versionLabel = null;
            foreach (Control c in _sidebar.Controls)
                if (c.Name == "versionLabel") versionLabel = c;
            if (versionLabel != null)
            {
                versionLabel.Location = new Point(pad, versionY);

                // The label was created with a shared width; measure it so the
                // version text never looks like it is floating inside a wide box.
                Size versionSize = TextRenderer.MeasureText(versionLabel.Text, versionLabel.Font,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                versionLabel.Size = new Size(versionSize.Width + Theme.S(2),
                    Math.Max(versionLabel.Height, versionSize.Height));
            }
        }

        private void LayoutContent()
        {
            if (_content == null) return;
            int w = _content.ClientSize.Width;
            int h = _content.ClientSize.Height;
            int pad = Theme.S(Theme.PagePadding);

            int textWidth = Math.Max(Theme.S(120),
                w - pad * 2 - Theme.S(150) - (_currentPage == "keys" ? Theme.S(330) : 0));
            int titleY = Theme.S(22);
            int titleH = Math.Max(Theme.S(38), Theme.BlockHeight(_titleLabel.Text, Theme.Display, textWidth));
            _titleLabel.SetBounds(pad, titleY, textWidth, titleH);

            int subY = titleY + titleH + Theme.S(2);
            int subH = Math.Max(Theme.S(18), Theme.BlockHeight(_subtitleLabel.Text, Theme.Caption, textWidth));
            _subtitleLabel.SetBounds(pad, subY, textWidth, subH);

            // The preset selector replaces the old master switch in the header and
            // is only shown on the shortcuts page.
            LayoutPresetBar(w, pad);

            // Keep the body clear of the measured header, whatever it wrapped to.
            int bodyTop = Math.Max(Theme.S(96), subY + subH + Theme.S(12));
            Rectangle body = new Rectangle(pad, bodyTop,
                Math.Max(Theme.S(200), w - pad * 2),
                Math.Max(Theme.S(120), h - bodyTop - Theme.S(46)));
            _pageHost.Bounds = body;

            Rectangle inner = new Rectangle(0, 0, body.Width, body.Height);
            _pageKeys.Bounds = inner;
            _pageSettings.Bounds = inner;
            _pageMacros.Bounds = inner;
            _pagePlugins.Bounds = inner;
            _pageAbout.Bounds = inner;

            _infoBar.SetBounds(pad, h - Theme.S(40), Math.Max(Theme.S(120), w - pad * 2), Theme.S(32));

            LayoutKeysPage();
            LayoutSettingsPage();
            LayoutMacrosPage();
            LayoutPluginsPage();
            LayoutAboutPage();
        }

        // ---- keys page -----------------------------------------------------
        private Panel BuildKeysPage()
        {
            Panel page = new Panel();
            page.BackColor = Theme.Background;

            // Lives in the content header (top right), not inside the page body.
            BuildPresetBar(_content);

            _search = new MSearchBox();
            _search.CueText = "搜索快捷键、功能或分类…";
            _search.SearchTextChanged += delegate { RefreshList(); };
            page.Controls.Add(_search);

            _addButton = new MButton();
            _addButton.Text = "新增";
            _addButton.Kind = MButtonKind.Primary;
            _addButton.Click += delegate { AddMapping(); };
            page.Controls.Add(_addButton);

            _moreButton = new MIconButton();
            _moreButton.Glyph = "\uE712";
            _moreButton.Click += delegate { ShowMoreMenu(); };
            page.Controls.Add(_moreButton);

            BuildKeyboardParts(page);

            _listCard = new MCard();
            _listCard.Padding = new Padding(1);
            // A full-bleed list needs square corners: the rounded card behind it
            // would leave a mismatched corner ring.
            _listCard.Radius = 0;
            page.Controls.Add(_listCard);

            _list = new MListView();
            _list.HeaderHeight = Theme.S(38);
            _list.MultiSelect = true;
            _list.AllowReorder = true;
            _list.EmptyText = "这个筛选下没有快捷键\r\n在主键标明状态下双击，可快速添加快捷键";
            _list.HeaderPainter = delegate(Graphics g, Rectangle r)
            {
                Theme.Prepare(g);
                using (SolidBrush b = new SolidBrush(Theme.Surface))
                    g.FillRectangle(b, r);
                TextRenderer.DrawText(g, "快捷键", Theme.CaptionStrong,
                    new Rectangle(Theme.S(18), r.Y, Theme.S(200), r.Height), Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "功能", Theme.CaptionStrong,
                    new Rectangle(Theme.S(248), r.Y, Theme.S(240), r.Height), Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "类型", Theme.CaptionStrong,
                    new Rectangle(r.Right - Theme.S(150), r.Y, Theme.S(60), r.Height), Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
            };
            _list.SelectionChanged += delegate { UpdateListCount(); };
            _list.ItemActivated += delegate { EditSelected(); };
            _list.ItemToggleRequested += delegate { ToggleSelected(); };
            _list.ItemDeleteRequested += delegate { DeleteSelected(); };
            _list.ItemContextRequested += delegate { ShowListMenu(); };
            _list.EmptyContextRequested += delegate { ShowEmptyListMenu(); };
            _list.ItemsReordered += delegate(object sender, MListReorderEventArgs e) { ReorderMappings(e); };
            _listCard.Controls.Add(_list);

            _countLabel = Theme.MakeLabel("共 0 个快捷键", Theme.Caption, Theme.TextTertiary, 0, 0, Theme.S(200), Theme.S(18));
            page.Controls.Add(_countLabel);

            _hintLabel = Theme.MakeLabel(KeysHintFull, Theme.Caption, Theme.TextTertiary,
                0, 0, Theme.S(400), Theme.S(18));
            _hintLabel.TextAlign = ContentAlignment.MiddleRight;
            _hintLabel.AutoEllipsis = true;
            page.Controls.Add(_hintLabel);

            return page;
        }

        private void LayoutKeysPage()
        {
            if (_pageKeys == null || _search == null) return;
            int w = _pageKeys.ClientSize.Width;
            int h = _pageKeys.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            int ctrl = Theme.S(Theme.CtrlHeight);
            int addW = Theme.S(76);
            int moreW = Theme.S(Theme.CtrlHeight);
            int gap = Theme.S(8);

            int searchW = Math.Max(Theme.S(120), w - addW - moreW - gap * 2);
            _search.SetBounds(0, 0, searchW, ctrl);
            _addButton.SetBounds(searchW + gap, 0, addW, ctrl);
            _moreButton.SetBounds(searchW + gap * 2 + addW, 0, moreW, ctrl);

            // Keyboard first: it is the primary surface of this page.
            int headerTop = ctrl + Theme.S(14);
            int boardHeight = KeyboardContentHeight(w, h, headerTop);
            int sectionBottom = LayoutKeyboard(headerTop, w, boardHeight);

            int footer = Theme.S(22);
            int listTop = sectionBottom + (_keyboardProgress > 0.02f ? Theme.S(12) : Theme.S(8));
            int listHeight = Math.Max(Theme.S(70), h - listTop - footer - Theme.S(6));

            // The mouse card sits beside the list and matches the numpad column of
            // the board above it: same left edge (18.5u) and same right edge (22.5u),
            // so the two surfaces line up instead of drifting with window size.
            int mouseGap = Theme.S(12);
            int boardPad = Theme.S(2);
            float keyUnit = Math.Min(
                Math.Max(1f, w - boardPad * 2) / KeyboardLayout.TotalWidth,
                (float)boardHeight / KeyboardLayout.TotalHeight);
            int boardRight = (int)Math.Round(boardPad
                + (w - boardPad * 2 - keyUnit * KeyboardLayout.TotalWidth) / 2f
                + keyUnit * KeyboardLayout.TotalWidth);
            int railWidth = Theme.S(30);
            int openWidth = Math.Max(Theme.S(120), (int)Math.Round(keyUnit * 4f));
            // The card width follows the collapse animation, so the list keeps
            // growing into the space the card gives up frame by frame.
            int mouseWidth = (int)Math.Round(railWidth + (openWidth - railWidth)
                * (1f - _mouseProgress));
            if (mouseWidth < railWidth) mouseWidth = railWidth;
            if (mouseWidth > openWidth) mouseWidth = openWidth;
            bool showMouse = _mousePad != null && w >= Theme.S(860);
            if (_mousePad != null)
            {
                _mousePad.Visible = showMouse;
                if (showMouse)
                {
                    // The card is exactly four key units wide, so the badge inside it
                    // can use the board's key unit and match a keycap badge 1:1. The
                    // body is laid out for the open width and slides out as the card
                    // narrows, so collapsing never squashes the mouse.
                    _mousePad.BadgeUnit = keyUnit;
                    _mousePad.ExpandedWidth = openWidth;
                    _mousePad.SetBounds(boardRight - mouseWidth, listTop, mouseWidth, listHeight);
                }
            }
            int listWidth = showMouse
                ? Math.Max(Theme.S(260), boardRight - mouseWidth - mouseGap)
                : w;
            _listCard.SetBounds(0, listTop, listWidth, listHeight);
            if (_list != null)
            {
                _list.SetBounds(1, 1,
                    Math.Max(1, _listCard.ClientSize.Width - 2),
                    Math.Max(1, _listCard.ClientSize.Height - 2));
            }

            _countLabel.SetBounds(0, h - footer, Math.Max(Theme.S(80), w / 2), footer);
            int hintWidth = Math.Max(Theme.S(80), w - w / 2);
            _hintLabel.SetBounds(w / 2, h - footer, hintWidth, footer);
            // Pick the longest hint that actually fits, instead of ellipsising.
            UpdateKeysHint();
        }

        private void RefreshList()
        {
            if (_list == null) return;
            string query = _search == null ? "" : _search.Text.Trim();
            bool keyFilter = _selectedKey != Keys.None;

            // Remember what was selected by identity, not row index: filtering,
            // editing and reordering all change the row positions underneath.
            HashSet<string> keep = new HashSet<string>();
            MappingListItem leadRow = _list.SelectedItem as MappingListItem;
            string leadId = leadRow == null ? null : leadRow.Mapping.Id;
            foreach (MListItem item in _list.SelectedItems)
            {
                MappingListItem selectedRow = item as MappingListItem;
                if (selectedRow != null) keep.Add(selectedRow.Mapping.Id);
            }

            List<MListItem> rows = new List<MListItem>();
            List<KeyMapping> mappings = MappingList;
            HashSet<string> shadowed = MappingConflicts.FindShadowed(mappings);
            foreach (KeyMapping mapping in mappings)
            {
                if (mapping == null) continue;
                if (!KeyboardFilter.Matches(mapping, _filterCaps, _filterCtrl,
                    _filterAlt, _filterShift, _filterWin)) continue;
                if (keyFilter
                    && (mapping.Key & (int)Keys.KeyCode) != ((int)_selectedKey & (int)Keys.KeyCode)) continue;
                if (query.Length > 0)
                {
                    bool match = mapping.TriggerText.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0
                        || mapping.ActionText.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0
                        || mapping.Action.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!match) continue;
                }
                rows.Add(new MappingListItem(mapping, shadowed.Contains(mapping.Id)));
            }
            _list.SetItems(rows);

            List<int> restore = new List<int>();
            int lead = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                MappingListItem row = rows[i] as MappingListItem;
                if (row == null) continue;
                if (keep.Contains(row.Mapping.Id)) restore.Add(i);
                if (leadId != null && row.Mapping.Id == leadId) lead = i;
            }
            if (restore.Count > 0)
            {
                if (lead < 0) lead = restore[0];
                _list.ApplySelection(restore, lead);
            }
            else if (rows.Count > 0)
            {
                _list.SelectedIndex = 0;
            }

            UpdateListCount();
            // Board highlight and header summary follow every list refresh, so
            // edit / toggle / delete / reload / import stay in sync in one place.
            RefreshKeyboard();
            UpdateKeyboardSummary(rows.Count);
        }

        /// <summary>
        /// Footer counter: plain total, or "已选 N 条" once several rows are
        /// picked. Kept in one place so every refresh and click agrees.
        /// </summary>
        private void UpdateListCount()
        {
            if (_countLabel == null || _list == null) return;
            int total = _list.Items.Count;
            int picked = _list.SelectedCount;
            _countLabel.Text = picked > 1
                ? "已选 " + picked + " 条 · 共 " + total + " 个快捷键"
                : "共 " + total + " 个快捷键";
        }

        private MappingListItem SelectedRow
        {
            get { return _list == null ? null : _list.SelectedItem as MappingListItem; }
        }

        /// <summary>Every selected row, in list order (never null).</summary>
        private List<MappingListItem> SelectedRows
        {
            get
            {
                List<MappingListItem> rows = new List<MappingListItem>();
                if (_list == null) return rows;
                foreach (MListItem item in _list.SelectedItems)
                {
                    MappingListItem row = item as MappingListItem;
                    if (row != null) rows.Add(row);
                }
                return rows;
            }
        }

        private void EditSelected()
        {
            List<MappingListItem> rows = SelectedRows;
            if (rows.Count == 0) return;
            if (rows.Count > 1)
            {
                ShowStatus("已选中 " + rows.Count + " 条；编辑一次只处理一条", MInfoSeverity.Info);
                return;
            }
            EditMapping(rows[0].Mapping);
        }

        private void ToggleSelected()
        {
            List<MappingListItem> rows = SelectedRows;
            if (rows.Count == 0) return;

            // A switch click (or Space) points at one row: flip only that one and
            // leave the rest of a multi-row selection alone.
            int hotspot = _list.ToggleHotspotIndex;
            if (hotspot >= 0 && hotspot < _list.Items.Count)
            {
                MappingListItem one = _list.Items[hotspot] as MappingListItem;
                if (one != null)
                {
                    ToggleRow(one);
                    return;
                }
            }
            if (rows.Count == 1)
            {
                ToggleRow(rows[0]);
                return;
            }
            bool allEnabled = true;
            foreach (MappingListItem row in rows)
            {
                if (!row.Mapping.Enabled) { allEnabled = false; break; }
            }
            SetRowsEnabled(rows, !allEnabled);
        }

        private void SetRowsEnabled(List<MappingListItem> rows, bool enabled)
        {
            if (rows == null || rows.Count == 0) return;
            foreach (MappingListItem row in rows) row.Mapping.Enabled = enabled;
            SaveAndApply();
            RefreshList();
            ShowStatus("已" + (enabled ? "启用 " : "停用 ") + rows.Count + " 条快捷键",
                MInfoSeverity.Success);
        }

        /// <summary>
        /// Right-click on a shortcut row: one row gets the full menu, a multi-row
        /// selection gets the batch actions. Same themed popup as the key menu.
        /// </summary>
        private void ShowListMenu()
        {
            List<MappingListItem> rows = SelectedRows;
            if (rows.Count == 0) return;

            MPopupMenu menu = new MPopupMenu();
            if (rows.Count == 1)
            {
                MappingListItem row = rows[0];
                menu.AddItem("编辑…", "Enter", delegate { EditMapping(row.Mapping); });
                menu.AddItem(row.Mapping.Enabled ? "停用" : "启用", "Space",
                    delegate { ToggleRow(row); });
                menu.AddSeparator();
                menu.AddItem("复制", "Ctrl+C", delegate { CopySelectedRows(); });
                menu.AddItem("剪切", "Ctrl+X", delegate { CutSelectedRows(); });
                menu.AddItem("粘贴", "Ctrl+V", delegate { PasteRows(); }, HasRowClipboard);
                menu.AddSeparator();
                menu.AddItem("删除", "Del", delegate { DeleteRow(row); });
            }
            else
            {
                menu.AddItem("启用选中 (" + rows.Count + ")", "", delegate { SetRowsEnabled(SelectedRows, true); });
                menu.AddItem("停用选中 (" + rows.Count + ")", "", delegate { SetRowsEnabled(SelectedRows, false); });
                menu.AddSeparator();
                menu.AddItem("复制选中 (" + rows.Count + ")", "Ctrl+C", delegate { CopySelectedRows(); });
                menu.AddItem("剪切选中 (" + rows.Count + ")", "Ctrl+X", delegate { CutSelectedRows(); });
                menu.AddItem("粘贴", "Ctrl+V", delegate { PasteRows(); }, HasRowClipboard);
                menu.AddSeparator();
                menu.AddItem("删除选中 (" + rows.Count + ")", "Del", delegate { DeleteSelected(); });
            }
            menu.AddSeparator();
            if (_list.Items.Count > 1)
                menu.AddItem("全选", "Ctrl+A", delegate { _list.SelectAll(); });
            menu.AddItem("新增快捷键", "Ctrl+N", delegate { AddMapping(); });
            menu.ShowAt(Cursor.Position);
        }

        /// <summary>
        /// Right-click on the blank part of the shortcut list: the same commands
        /// that make sense without a row, so the empty area is not a dead end.
        /// </summary>
        private void ShowEmptyListMenu()
        {
            MPopupMenu menu = new MPopupMenu();
            menu.AddItem("新增快捷键", "Ctrl+N", delegate { AddMapping(); });
            if (_list.Items.Count > 0)
                menu.AddItem("全选", "Ctrl+A", delegate { _list.SelectAll(); });
            // Paste needs no selection: the rows land at the end of the list.
            menu.AddItem("粘贴", "Ctrl+V", delegate { PasteRows(); }, HasRowClipboard);
            menu.AddSeparator();
            menu.AddItem("搜索快捷键…", "Ctrl+F", delegate { _search.Inner.Focus(); });
            menu.AddItem("刷新列表", "F5", delegate { RefreshList(); ShowStatus("列表已刷新", MInfoSeverity.Info); });
            menu.ShowAt(Cursor.Position);
        }

        private void ToggleRow(MappingListItem row)
        {
            if (row == null) return;
            KeyMapping mapping = row.Mapping;
            PushMappingUndo(mapping.Enabled ? "停用快捷键" : "启用快捷键");
            mapping.Enabled = !mapping.Enabled;
            SaveAndApply();
            RefreshList();
            ShowStatus(mapping.TriggerText + (mapping.Enabled ? " 已启用" : " 已停用"), MInfoSeverity.Info);
        }

        private void DeleteSelected()
        {
            List<MappingListItem> rows = SelectedRows;
            if (rows.Count == 0) return;
            if (rows.Count == 1)
            {
                DeleteRow(rows[0]);
                return;
            }

            string message = "确定要删除选中的 " + rows.Count + " 条快捷键吗？";
            if (!MDialog.Confirm(this, "删除快捷键", message, "删除", "取消", MDialogKind.Warning)) return;
            PushMappingUndo("删除快捷键");
            foreach (MappingListItem row in rows)
            {
                if (row != null && row.Mapping != null) MappingList.Remove(row.Mapping);
            }
            SaveAndApply();
            RefreshList();
            ShowStatus("已删除 " + rows.Count + " 条快捷键", MInfoSeverity.Info);
        }

        private void DeleteRow(MappingListItem row)
        {
            if (row == null) return;
            KeyMapping mapping = row.Mapping;
            if (!MDialog.Confirm(this, "删除快捷键", "确定要删除快捷键 " + mapping.TriggerText + " 吗？",
                "删除", "取消", MDialogKind.Warning)) return;
            PushMappingUndo("删除快捷键");
            MappingList.Remove(mapping);
            SaveAndApply();
            RefreshList();
            ShowStatus("已删除：" + mapping.TriggerText, MInfoSeverity.Info);
        }

        /// <summary>
        /// The list already moved the rows; mirror the same block move onto the
        /// mapping order so the saved order matches what is on screen. Rows that
        /// the current filter hides stay where they are relative to their anchors.
        /// </summary>
        private void ReorderMappings(MListReorderEventArgs args)
        {
            if (args == null || args.Count <= 0) return;
            List<MappingListItem> rows = new List<MappingListItem>();
            foreach (MListItem item in _list.Items)
            {
                MappingListItem row = item as MappingListItem;
                if (row != null) rows.Add(row);
            }
            if (args.To < 0 || args.To + args.Count > rows.Count) return;

            List<KeyMapping> block = new List<KeyMapping>();
            for (int i = 0; i < args.Count; i++)
            {
                KeyMapping mapping = rows[args.To + i].Mapping;
                if (mapping != null && !block.Contains(mapping)) block.Add(mapping);
            }
            if (block.Count == 0) return;

            int nextRow = args.To + args.Count;
            KeyMapping anchor = nextRow < rows.Count ? rows[nextRow].Mapping : null;

            PushMappingUndo("调整快捷键顺序");
            List<KeyMapping> mappings = MappingList;
            foreach (KeyMapping mapping in block) mappings.Remove(mapping);
            int insertAt = anchor == null ? mappings.Count : mappings.IndexOf(anchor);
            if (insertAt < 0) insertAt = mappings.Count;
            mappings.InsertRange(insertAt, block);

            SaveAndApply();
            RefreshList();
            ShowStatus("已调整 " + block.Count + " 条快捷键的顺序", MInfoSeverity.Success);
        }

        private void AddMapping()
        {
            KeyMapping draft = new KeyMapping();
            draft.Caps = true;
            draft.KeyCode = Keys.None;
            draft.Action = "input.keys";
            // Leave the combination empty so the editor opens with no modifier
            // chip checked and no key captured: the user picks their own.
            draft.Param = "";
            draft.Note = "";
            AddMappingWithDraft(draft);
        }

        /// <summary>
        /// Shared by the list page and the keyboard preview: both hand over a
        /// prefilled draft so the editor opens on the key and layer the user is
        /// looking at instead of an empty one.
        /// </summary>
        private void AddMappingWithDraft(KeyMapping draft)
        {
            // Snapshot before the dialog so an undo also re-enables a shortcut that
            // this edit had to disable because of a conflict.
            MappingSnapshot before = CaptureMappings("新增快捷键");
            // The editor is a normal dialog: pausing the hotkey engine here made the
            // app's own shortcuts dead while it was open, which is unnecessary.
            using (MappingEditorForm dialog = new MappingEditorForm(draft, true))
            {
                DialogResult result = dialog.ShowDialog(this);
                if (result != DialogResult.OK || dialog.Result == null) return;

                KeyMapping candidate = dialog.Result;
                KeyMapping conflict = FindConflict(candidate, null);
                if (conflict != null)
                {
                    string message = "快捷键 " + candidate.TriggerText + " 已被“" + conflict.ActionText + "”使用。\r\n\r\n是否停用原来的快捷键并继续？";
                    if (!MDialog.Confirm(this, "快捷键冲突", message, "停用原快捷键", "取消")) return;
                    conflict.Enabled = false;
                }
                PushMappingUndo(before);
                MappingList.Add(candidate);
                SaveAndApply();
                RefreshList();
                ShowStatus("已新增：" + candidate.TriggerText, MInfoSeverity.Success);
            }
        }

        private void EditMapping(KeyMapping mapping)
        {
            MappingSnapshot before = CaptureMappings("编辑快捷键");
            using (MappingEditorForm dialog = new MappingEditorForm(mapping, false))
            {
                DialogResult result = dialog.ShowDialog(this);
                if (result != DialogResult.OK || dialog.Result == null) return;

                KeyMapping candidate = dialog.Result;
                KeyMapping conflict = FindConflict(candidate, mapping.Id);
                if (conflict != null)
                {
                    string message = "快捷键 " + candidate.TriggerText + " 已被“" + conflict.ActionText + "”使用。\r\n\r\n是否停用原来的快捷键并继续？";
                    if (!MDialog.Confirm(this, "快捷键冲突", message, "停用原快捷键", "取消")) return;
                    conflict.Enabled = false;
                }
                PushMappingUndo(before);
                // Copy the whole candidate: copying fields one by one is how the
                // note (and, earlier, the macro restore flag) stopped being saved.
                mapping.CopyFrom(candidate);
                SaveAndApply();
                RefreshList();
                ShowStatus("已更新：" + mapping.TriggerText, MInfoSeverity.Success);
            }
        }

        private KeyMapping FindConflict(KeyMapping candidate, string ignoreId)
        {
            foreach (KeyMapping m in MappingList)
            {
                if (m == null || !m.Enabled) continue;
                // A mapping without a main key can never trigger, so it cannot be
                // the conflict partner either.
                if (m.Key == 0) continue;
                if (ignoreId != null && m.Id == ignoreId) continue;
                if (m.Caps != candidate.Caps || m.Ctrl != candidate.Ctrl || m.Alt != candidate.Alt
                    || m.Shift != candidate.Shift || m.Win != candidate.Win) continue;
                if ((m.Key & (int)Keys.KeyCode) == (candidate.Key & (int)Keys.KeyCode)) return m;
            }
            return null;
        }

        private void ShowMoreMenu()
        {
            MPopupMenu menu = new MPopupMenu();
            menu.AddItem("新增快捷键", "Ctrl+N", delegate { AddMapping(); });
            menu.AddSeparator();
            menu.AddItem(_keyboardExpanded ? "收起键盘" : "展开键盘", "", delegate { ToggleKeyboard(); });
            menu.AddItem("恢复默认预设", "", delegate { ResetDefaults(); });
            menu.AddSeparator();
            menu.AddItem("撤回", "Ctrl+Z", delegate { UndoMappings(); });
            menu.AddItem("重做", "Ctrl+Y", delegate { RedoMappings(); });
            menu.AddSeparator();
            menu.AddItem("导入配置…", "", delegate { ImportSettings(); });
            menu.AddItem("导出配置…", "", delegate { ExportSettings(); });
            menu.AddItem("配置文件…", "", delegate { OpenConfigFolder(); });
            menu.ShowBelow(_moreButton, true);
        }

        private void ResetDefaults()
        {
            if (!MDialog.Confirm(this, "恢复默认", "恢复为默认快捷键？当前的自定义会被覆盖。",
                "恢复", "取消", MDialogKind.Warning)) return;
            PushMappingUndo("恢复默认快捷键");
            ReplaceMappingList(ActionCatalog.CreateDefaultMappings());
            SaveAndApply();
            RefreshList();
            ShowStatus("已恢复默认快捷键", MInfoSeverity.Success);
        }

        // ---- pages ---------------------------------------------------------
        private void SwitchPage(string page)
        {
            if (page != "settings" && page != "about" && page != "plugins" && page != "macros") page = "keys";
            _currentPage = page;
            _pageKeys.Visible = page == "keys";
            _pageSettings.Visible = page == "settings";
            _pageMacros.Visible = page == "macros";
            _pagePlugins.Visible = page == "plugins";
            _pageAbout.Visible = page == "about";
            _navKeys.Selected = page == "keys";
            _navSettings.Selected = page == "settings";
            _navMacros.Selected = page == "macros";
            _navPlugins.Selected = page == "plugins";
            _navAbout.Selected = page == "about";

            if (page == "keys")
            {
                _titleLabel.Text = "快捷键";
                _subtitleLabel.Text = "点选键位筛选列表，点选修饰键进入主键标明状态；默认全局预设，支持根据程序自动切换预设。";
                // Coming back from another page used to leave focus on the sidebar,
                // which killed Del/Enter/arrows until the user clicked a row. The
                // list is the page's working surface, so hand it the keyboard.
                if (_list != null) _list.Focus();
            }
            else if (page == "settings")
            {
                _titleLabel.Text = "设置";
                _subtitleLabel.Text = "外观、行为与启动选项。";
            }
            else if (page == "macros")
            {
                _titleLabel.Text = "宏动作";
                _subtitleLabel.Text = "把多个动作组合成宏动作，宏动作可以相互嵌套。";
                RefreshMacros();
            }
            else if (page == "plugins")
            {
                _titleLabel.Text = "插件";
                _subtitleLabel.Text = "支持 .cs 与 .zip：直接拖进窗口导入，动作立即可用。";
                RefreshPlugins();
            }
            else
            {
                _titleLabel.Text = "关于";
                _subtitleLabel.Text = "版本信息与功能说明。";
            }

            // The header selector belongs to the shortcuts page only; the title
            // width reserve depends on it, so re-run the content layout once the
            // shell has real dimensions.
            UpdatePresetBarVisibility();
            if (_shellReady) LayoutContent();
        }

        // ---- engine / config plumbing --------------------------------------
        private void SetEnabled(bool enabled)
        {
            if (_syncing) return;
            _settings.Enabled = enabled;
            if (_engine != null)
            {
                _engine.UpdateSettings(_settings);
                if (enabled) _engine.Start();
                else _engine.Stop();
            }
            SaveQuiet();
            UpdateStatusUi();
            ShowStatus(enabled ? "快捷键已启用" : "快捷键已暂停",
                enabled ? MInfoSeverity.Success : MInfoSeverity.Info);
        }

        private void SaveAndApply()
        {
            SaveQuiet();
            RestartEngine();
            UpdateStatusUi();
        }

        private void SaveQuiet()
        {
            try { ConfigStore.Save(_settings); }
            catch (Exception ex)
            {
                ConfigStore.Log("保存配置失败：" + ex);
                ShowStatus("保存失败：" + ex.Message, MInfoSeverity.Error);
            }
        }

        private void RestartEngine()
        {
            if (_engine == null) return;
            _engine.UpdateSettings(_settings);
            _engine.Stop();
            // Keys held by the previous backend have been released through it by
            // Stop(); only now is it safe to switch the sink.
            RunOnInputAndWait(delegate { InputPipeline.Apply(_settings); });
            if (_settings.Enabled) _engine.Start();
            UpdateStatusUi();
        }

        private void UpdateStatusUi()
        {
            bool running = _engine != null && _engine.IsRunning;
            bool enabled = _settings != null && _settings.Enabled;

            // Nowhere left to reflect the state but the logo: dim the mark while the
            // service is paused so the rail still reads as "off" without a switch.
            if (_logo != null) _logo.Dimmed = !enabled;

            if (_engine == null)
            {
                _statusLabel.Text = "● 正在启动";
                _statusLabel.ForeColor = Theme.TextTertiary;
            }
            else if (running)
            {
                _statusLabel.Text = "● 运行中";
                _statusLabel.ForeColor = Theme.Success;
            }
            else if (enabled)
            {
                _statusLabel.Text = "● 启动失败";
                _statusLabel.ForeColor = Theme.Danger;
            }
            else
            {
                _statusLabel.Text = "● 已暂停";
                _statusLabel.ForeColor = Theme.Warning;
            }
            if (_tray != null) _tray.Text = "CapsLock Lite - " + _statusLabel.Text.Replace("● ", "");
            if (_trayToggleItem != null) _trayToggleItem.Text = enabled ? "暂停快捷键" : "启用快捷键";
        }

        private void PauseEngine() { if (_engine != null) _engine.PauseForDialog(); }
        private void ResumeEngine() { if (_engine != null) _engine.ResumeAfterDialog(); UpdateStatusUi(); }

        // ---- IAppHost ------------------------------------------------------
        public void ShowStatus(string message, bool error)
        {
            ShowStatus(message, error ? MInfoSeverity.Error : MInfoSeverity.Success);
        }

        public void ShowStatus(string message, MInfoSeverity severity)
        {
            if (string.IsNullOrEmpty(message) || _infoBar == null || IsDisposed) return;
            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action<string, MInfoSeverity>(ShowStatus),
                        new object[] { message, severity });
                }
                catch { }
                return;
            }
            _infoBar.Message = message;
            _infoBar.Severity = severity;
            _infoBar.Visible = true;
            _infoTimer.Stop();
            _infoTimer.Start();
        }

        public void OpenCalculator()
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "calc.exe";
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception ex)
            {
                try
                {
                    ProcessStartInfo uri = new ProcessStartInfo();
                    uri.FileName = "ms-calculator:";
                    uri.UseShellExecute = true;
                    Process.Start(uri);
                }
                catch
                {
                    ShowStatus("无法打开计算器：" + ex.Message, MInfoSeverity.Error);
                }
            }
        }

        public void OpenHelp()
        {
            try { Process.Start(HelpUrl); }
            catch (Exception ex) { ShowStatus("打开文档失败：" + ex.Message, MInfoSeverity.Error); }
        }

        public void ShowEscapeResult(string escaped, string original)
        {
            using (EscapeResultForm dialog = new EscapeResultForm(escaped, original))
                dialog.ShowDialog(this);
        }

        public void ReloadFromDisk()
        {
            _settings = ConfigStore.LoadOrCreate();
            PauseEngine();
            try
            {
                ConfigStore.Normalize(_settings);
                if (_engine != null) _engine.UpdateSettings(_settings);
                SyncStartup();
            }
            finally { ResumeEngine(); }
            MacroLibrary.RefreshCatalog(_settings.Macros);
            SyncControlsFromSettings();
            // The reload replaces every preset object too; the selector must not
            // keep pointing at a list that is no longer part of the settings.
            ResetPresetState();
            RestartEngine();
            RefreshList();
            // The reload replaces every mapping object, so old undo snapshots would
            // resurrect entries that no longer exist in the file.
            ClearMappingHistory();
            if (_currentPage == "macros") RefreshMacros();
            if (_currentPage == "plugins") RefreshPlugins();
            ShowStatus("配置已重新加载", MInfoSeverity.Success);
        }

        private void SyncStartup()
        {
            if (_settings.RunAtStartup)
            {
                if (!StartupManager.Apply(true, _settings.StartMinimized))
                {
                    _settings.RunAtStartup = false;
                    SaveQuiet();
                    ShowStatus("系统拒绝开机启动设置，已自动关闭该选项。", MInfoSeverity.Error);
                }
                return;
            }
            if (StartupManager.IsRegistered())
                StartupManager.Apply(false, _settings.StartMinimized);
        }

        private void ExportSettings()
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "导出配置";
                dialog.Filter = "JSON 配置|*.json|所有文件|*.*";
                dialog.FileName = "CapsLockLite-settings.json";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ConfigStore.Export(_settings, dialog.FileName);
                    ShowStatus("已导出到 " + dialog.FileName, MInfoSeverity.Success);
                }
                catch (Exception ex) { ShowStatus("导出失败：" + ex.Message, MInfoSeverity.Error); }
            }
        }

        private void ImportSettings()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "导入配置";
                dialog.Filter = "JSON 配置|*.json|所有文件|*.*";
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    AppSettings imported = ConfigStore.Import(dialog.FileName);
                    int launchActions = 0;
                    int pluginActions = 0;
                    int mappingCount = 0;
                    int presetCount = imported.Presets == null ? 0 : imported.Presets.Count;
                    List<List<KeyMapping>> importedLists = ShortcutSets.All(imported);
                    for (int l = 0; l < importedLists.Count; l++)
                    {
                        List<KeyMapping> list = importedLists[l];
                        mappingCount += list.Count;
                        foreach (KeyMapping mapping in list)
                        {
                            if (mapping == null || mapping.Action == null) continue;
                            if (mapping.Action.StartsWith("launch.", StringComparison.OrdinalIgnoreCase))
                                launchActions++;
                            if (ActionCatalog.IsPluginActionId(mapping.Action))
                                pluginActions++;
                        }
                    }

                    string summary = "将导入 " + mappingCount + " 条快捷键"
                        + (presetCount > 0 ? "，以及 " + presetCount + " 个程序预设（含绑定程序）" : "")
                        + "。\r\n\r\n"
                        + "其中会调用外部程序或网址的动作：" + launchActions + " 条\r\n"
                        + "依赖插件的动作：" + pluginActions + " 条\r\n\r\n"
                        + "为避免配置本身成为执行入口，导入后快捷键会保持暂停，"
                        + "开机启动会被关闭；插件动作要等你确认信任后才会执行。\r\n\r\n"
                        + "是否继续导入？";
                    if (!MDialog.Confirm(this, "导入配置", summary, "导入", "取消", MDialogKind.Warning)) return;

                    imported.Enabled = false;
                    imported.RunAtStartup = false;

                    AppSettings previous = _settings;
                    try
                    {
                        ConfigStore.Save(imported);
                    }
                    catch
                    {
                        _settings = previous;
                        throw;
                    }
                    _settings = imported;
                    // The selector points at a preset from the previous settings;
                    // fall back to 全局 before the list is rebuilt.
                    ResetPresetState();
                    // Undo history belongs to the previous configuration, not to the
                    // imported one; mixing them would let 撤回 restore foreign rows.
                    ClearMappingHistory();
                    if (_engine != null) _engine.UpdateSettings(_settings);
                    PauseEngine();
                    try
                    {
                        SyncStartup();
                    }
                    finally { ResumeEngine(); }
                    SyncControlsFromSettings();
                    RestartEngine();
                    RefreshList();
                    if (_currentPage == "plugins") RefreshPlugins();
                    ShowStatus("配置已导入并暂停；请检查快捷键后再启用。", MInfoSeverity.Warning);
                }
                catch (Exception ex) { ShowStatus("导入失败：" + ex.Message, MInfoSeverity.Error); }
            }
        }

        private void OpenConfigFolder()
        {
            using (ConfigFolderForm dialog = new ConfigFolderForm(ConfigStore.EnsureDirectory()))
            {
                // The Caps layer keeps working while this window is open: opening a
                // configuration page must not take the user's shortcuts away.
                dialog.ShowDialog(this);
                if (dialog.Changed) ReloadFromDisk();
            }
        }

        private void ToggleDarkMode(bool dark)
        {
            if (_settings.DarkMode == dark) return;
            _settings.DarkMode = dark;
            SaveQuiet();
            ApplyTheme();
        }

        // ---- theme / dpi rebuild --------------------------------------------
        private void ApplyTheme()
        {
            if (IsDisposed) return;
            Theme.Apply(_settings.DarkMode);
            RebuildUi();
            ShowStatus(_settings.DarkMode ? "已切换到深色模式" : "已切换到浅色模式", MInfoSeverity.Info);
        }

        /// <summary>
        /// Disposes and rebuilds the whole control tree. Palette and DPI are
        /// static theme state, so every self-drawn control has to be recreated
        /// with freshly measured sizes after either one changes.
        /// </summary>
        private void RebuildUi()
        {
            if (IsDisposed) return;
            string page = _currentPage;
            int section = _settingsSection;
            SuspendLayout();

            BackColor = Theme.Background;
            ForeColor = Theme.TextPrimary;

            while (Controls.Count > 0)
            {
                Control child = Controls[0];
                Controls.RemoveAt(0);
                child.Dispose();
            }

            _statusLabel = null;
            _logo = null;
            _tagline = null;
            _search = null;
            _list = null;
            _infoBar = null;
            _sidebar = null;
            _divider = null;
            _content = null;

            // The rebuild replaces the whole tree, so the shell has to be laid
            // out again at the real window size before pages are switched and the
            // list is filled: with a stale _shellReady the list would measure its
            // scroll offset against the 150x150 placeholder and cut the first row.
            _shellReady = false;
            BuildShell();
            LayoutShell();
            _settingsSection = section;
            RebuildSettingsSection();
            SwitchPage(page);
            RefreshList();
            SyncControlsFromSettings();
            UpdateStatusUi();
            if (_trayMenu != null)
            {
                _trayMenu.BackColor = Theme.Surface;
                _trayMenu.ForeColor = Theme.TextPrimary;
            }
            LayoutShell();
            ResumeLayout(true);
            Native.SetDarkTitleBar(Handle, _settings.DarkMode);
            Native.SetRoundedCorners(Handle, true);
        }

        // ---- shortcuts ------------------------------------------------------
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData & Keys.Control) == Keys.Control)
            {
                Keys key = keyData & Keys.KeyCode;
                if (key == Keys.N)
                {
                    if (_currentPage == "macros") NewMacro();
                    else AddMappingForKey();
                    return true;
                }
                if (key == Keys.F)
                {
                    if (_currentPage == "macros")
                    {
                        if (_macroSearch != null && _macroSearch.Inner != null) _macroSearch.Inner.Focus();
                        else SwitchPage("macros");
                        return true;
                    }
                    SwitchPage("keys");
                    if (_search != null && _search.Inner != null) _search.Inner.Focus();
                    return true;
                }
                if (key == Keys.D)
                {
                    if (_currentPage == "macros" && _macroList != null && _macroList.Focused)
                    {
                        DuplicateSelectedMacros();
                        return true;
                    }
                    if (_currentPage != "macros") ToggleDarkMode(!_settings.DarkMode);
                    return true;
                }
                if (key == Keys.E)
                {
                    if (_currentPage == "macros") EditSelectedMacro();
                    else EditSelectedKeyMapping();
                    return true;
                }
                if (key == Keys.R)
                {
                    if (_currentPage == "macros") RunSelectedMacro();
                    return true;
                }
                if (key == Keys.X || key == Keys.C || key == Keys.V)
                {
                    // Two clipboards live on this page, and the focused surface decides
                    // which one the shortcut means: rows in the list move through the
                    // row clipboard, a chord selected on the board goes through the key
                    // clipboard. Typing into the search box keeps normal text editing.
                    bool typing = _search != null && _search.ContainsFocus;
                    if (_currentPage == "keys" && !typing)
                    {
                        if (_list != null && _list.Focused && _list.SelectedCount > 0)
                        {
                            if (key == Keys.X) CutSelectedRows();
                            else if (key == Keys.C) CopySelectedRows();
                            else PasteRows();
                            return true;
                        }
                        if (HasKeyboardFilter && _selectedKey != Keys.None)
                        {
                            if (key == Keys.X) CutKeyBindings();
                            else if (key == Keys.C) CopyKeyBindings();
                            else PasteKeyBindings();
                            return true;
                        }
                        if (key == Keys.V && _list != null && _list.Focused)
                        {
                            PasteRows();
                            return true;
                        }
                    }
                }
                if (key == Keys.Z || key == Keys.Y)
                {
                    // Undo covers every shortcut edit on the page, including the
                    // keyboard and clipboard commands, so it is handled here rather
                    // than by the list (which only knows about its own rows).
                    bool typing = _search != null && _search.ContainsFocus;
                    if (_currentPage == "keys" && !typing)
                    {
                        if (key == Keys.Y || (keyData & Keys.Shift) == Keys.Shift) RedoMappings();
                        else UndoMappings();
                        return true;
                    }
                }
            }
            if (keyData == Keys.Delete)
            {
                // Del is a page-level command like Ctrl+X/C/V: it works even when
                // focus moved off the list, and with no row selected it removes the
                // shortcuts on the selected chord.
                bool typing = _search != null && _search.ContainsFocus;
                if (_currentPage == "keys" && !typing)
                {
                    if (_list != null && _list.SelectedCount > 0)
                    {
                        DeleteSelected();
                        return true;
                    }
                    if (HasBindingTarget)
                    {
                        DeleteKeyBindings();
                        return true;
                    }
                }
            }
            if (keyData == Keys.F5)
            {
                if (_currentPage == "macros") { RefreshMacros(); ShowStatus("列表已刷新", MInfoSeverity.Info); return true; }
                ReloadFromDisk();
                return true;
            }
            if (keyData == Keys.Escape && _currentPage == "macros")
            {
                if (_macroSearch != null && _macroSearch.Text.Length > 0)
                {
                    _macroSearch.Text = "";
                    RefreshMacros();
                    return true;
                }
            }
            if (keyData == Keys.Escape)
            {
                // Esc cancels a key drag before unwinding anything else.
                if (_keyboard != null && _keyboard.IsDragging)
                {
                    _keyboard.CancelDrag();
                    return true;
                }
                // Esc unwinds the filters first, then the search box.
                if (_selectedKey != Keys.None)
                {
                    ClearKeyboardSelection();
                    return true;
                }
                if (HasKeyboardFilter)
                {
                    ClearKeyboardFilter();
                    return true;
                }
                if (_search != null && _search.Text.Length > 0)
                {
                    _search.Text = "";
                    RefreshList();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- lifecycle ------------------------------------------------------
        /// <summary>
        /// The very first show is skipped when the app starts in the tray. The
        /// window handle, hotkey engine and tray icon are all created, but the
        /// window is never mapped, so nothing flashes on screen and disappears
        /// again a moment later.
        /// </summary>
        protected override void SetVisibleCore(bool value)
        {
            if (value && _startMinimized && !_initialShowHandled
                && string.IsNullOrEmpty(_demo))
            {
                _initialShowHandled = true;
                if (!IsHandleCreated) CreateControl();
                EnsureRuntime();
                base.SetVisibleCore(false);
                ShowTrayBalloon();
                return;
            }
            base.SetVisibleCore(value);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            EnsureRuntime();
        }

        /// <summary>
        /// Creates the hotkey engine, the executor and the startup wiring exactly
        /// once. Form.Load does not run when the first show is suppressed, so
        /// this is also called from SetVisibleCore.
        /// </summary>
        private void EnsureRuntime()
        {
            if (_runtimeStarted) return;
            _runtimeStarted = true;

            _engine = new HotkeyManager(_settings, Handle);
            // These callbacks come from the hook thread now, so everything they touch
            // is handed to the executor's own thread first.
            _engine.ActionTriggered += delegate(KeyMapping mapping)
            {
                ActionExecutor executor = _executor;
                if (executor != null) executor.Enqueue(mapping);
            };
            _engine.HoldStarted += delegate(KeyMapping mapping)
            {
                ActionExecutor executor = _executor;
                if (executor != null) executor.Post(delegate { executor.BeginKeyHold(mapping); });
            };
            _engine.HoldRepeated += delegate(KeyMapping mapping)
            {
                ActionExecutor executor = _executor;
                if (executor != null) executor.Post(delegate { executor.RepeatKeyHold(mapping); });
            };
            _engine.HoldEnded += delegate(KeyMapping mapping)
            {
                ActionExecutor executor = _executor;
                if (executor != null) executor.Post(delegate { executor.EndKeyHold(mapping); });
            };
            _engine.UiPost = delegate(Action work)
            {
                if (IsDisposed) return;
                if (InvokeRequired) BeginInvoke(work);
                else work();
            };
            _engine.StatusChanged += delegate(string message, MInfoSeverity severity)
            {
                if (IsDisposed) return;
                if (InvokeRequired)
                    BeginInvoke(new Action<string, MInfoSeverity>(ShowStatus),
                        new object[] { message, severity });
                else
                    ShowStatus(message, severity);
            };
            // Injection and macro scheduling get their own thread as well: the actions
            // send input, wait for delivery and pace the steps, none of which should
            // ever depend on the window being responsive.
            _uiHost = new UiHost(this, this);
            _inputThread = new DispatcherThread("CapsLockLite.Input");
            DispatcherThread input = _inputThread;
            ActionExecutor createdExecutor = null;
            MacroRunner createdRunner = null;
            input.Send(delegate
            {
                createdExecutor = new ActionExecutor(_uiHost, input.Anchor);
                createdRunner = new MacroRunner(createdExecutor, _uiHost, delegate { return _settings.Macros; });
                createdRunner.StateChanged += delegate { OnMacroStateChanged(); };
                createdExecutor.MacroStartHook = StartMacroFromAction;
                // A deferred mouse action wakes the macro runner the moment it is done,
                // so a click step costs no extra timer tick.
                createdExecutor.Idle += delegate
                {
                    MacroRunner current = createdRunner;
                    if (current != null) current.Wake();
                };
            });
            _executor = createdExecutor;
            _macroRunner = createdRunner;

            MacroLibrary.RefreshCatalog(_settings.Macros);
            SyncStartup();
            RunOnInputAndWait(delegate { InputPipeline.Apply(_settings); });
            if (_settings.Enabled) _engine.Start();
            UpdateStatusUi();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.SetDarkTitleBar(Handle, _settings.DarkMode);
            Native.SetRoundedCorners(Handle, true);
            if (_engine != null) _engine.UpdateOwner(Handle);
        }

        /// <summary>
        /// The constructor runs before the window exists, so DeviceDpi there is
        /// usually the primary monitor's. Re-check once the window sits on its
        /// real monitor and rebuild at the correct scale if they differ.
        /// </summary>
        /// <summary>Runs work on the input thread (injection + macro scheduling).</summary>
        private void RunOnInput(Action work)
        {
            if (work == null) return;
            DispatcherThread input = _inputThread;
            if (input == null || input.IsOwnThread) work();
            else input.Post(work);
        }

        /// <summary>Runs work on the input thread and waits for it (state switches).</summary>
        private void RunOnInputAndWait(Action work)
        {
            if (work == null) return;
            DispatcherThread input = _inputThread;
            if (input == null || input.IsOwnThread) work();
            else input.Send(work);
        }

        /// <summary>Runs work on the UI thread from anywhere.</summary>
        private void RunOnUi(Action work)
        {
            if (work == null || IsDisposed) return;
            if (InvokeRequired) BeginInvoke(work);
            else work();
        }

        private void EnsureDpi()
        {
            int dpi = DeviceDpi;
            if (dpi <= 0 || dpi == _uiDpi) return;
            _uiDpi = dpi;
            Theme.SetDpi(dpi);
            // The window was laid out with the wrong scale, so its current client
            // size carries no meaning; restore the design size at the new scale.
            ClientSize = StartupClientSize();
            MinimumSize = new Size(Theme.S(880), Theme.S(680));
            RebuildUi();
        }

        /// <summary>
        /// WM_DPICHANGED is the only reliable signal that the window moved to a
        /// monitor with a different scale; the manifest asks for PerMonitorV2 so
        /// the system sends it. Windows supplies the suggested window rect in
        /// physical pixels for the new DPI, which we apply before re-measuring.
        /// </summary>
        private void OnDpiChanged(IntPtr wParam, IntPtr lParam)
        {
            int dpi = (int)(wParam.ToInt64() & 0xFFFF);
            if (dpi <= 0) dpi = DeviceDpi;

            // Only a normal window takes the suggested rect; a maximized or
            // minimized window is resized by the system and would end up in a
            // contradictory state if we forced a restored rectangle onto it.
            if (lParam != IntPtr.Zero && WindowState == FormWindowState.Normal)
            {
                Native.RECT suggested = (Native.RECT)Marshal.PtrToStructure(
                    lParam, typeof(Native.RECT));
                if (suggested.Width > 0 && suggested.Height > 0)
                {
                    Native.SetWindowPos(Handle, IntPtr.Zero, suggested.Left, suggested.Top,
                        suggested.Width, suggested.Height,
                        Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                }
            }

            if (dpi == _uiDpi) return;
            ConfigStore.Log("DPI 变化：" + _uiDpi + " → " + dpi);
            _uiDpi = dpi;
            Theme.SetDpi(dpi);
            MinimumSize = new Size(Theme.S(880), Theme.S(680));
            ScheduleDpiRebuild();
        }

        /// <summary>
        /// Rebuilding inside WndProc would dispose the very controls whose
        /// messages are still being dispatched, so defer one message-loop pass.
        /// </summary>
        private void ScheduleDpiRebuild()
        {
            if (_dpiRebuildScheduled || IsDisposed || !IsHandleCreated) return;
            _dpiRebuildScheduled = true;
            BeginInvoke(new Action(delegate
            {
                _dpiRebuildScheduled = false;
                if (IsDisposed) return;
                RebuildUi();
            }));
        }

        private void MainFormShown(object sender, EventArgs e)
        {
            _everShown = true;
            EnsureDpi();
            // A maximized window is restored as maximized, but the stored size stays
            // the last normal one so un-maximizing lands where the user left it.
            if (_settings.WindowMaximized && string.IsNullOrEmpty(_demo))
                WindowState = FormWindowState.Maximized;
            HandleDemo();
            _windowSizeReady = string.IsNullOrEmpty(_demo);
        }

        // ---- window size -----------------------------------------------------
        /// <summary>
        /// Room for the board by default; on a small desktop the window shrinks
        /// instead of opening larger than the work area. A size the user left
        /// behind wins over both.
        /// </summary>
        private Size StartupClientSize()
        {
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            int width = _settings.WindowWidth;
            int height = _settings.WindowHeight;
            if (width <= 0 || height <= 0)
            {
                width = Math.Min(Theme.S(DefaultClientWidth), (int)Math.Round(work.Width * 0.82));
                height = Math.Min(Theme.S(DefaultClientHeight), (int)Math.Round(work.Height * 0.82));
            }
            int maxWidth = Math.Max(Theme.S(640), work.Width - Theme.S(24));
            int maxHeight = Math.Max(Theme.S(480), work.Height - Theme.S(24));
            width = Math.Max(Theme.S(720), Math.Min(width, maxWidth));
            height = Math.Max(Theme.S(500), Math.Min(height, maxHeight));
            return new Size(width, height);
        }

        private void ScheduleWindowSizeSave()
        {
            if (!_windowSizeReady || IsDisposed) return;
            if (_windowSaveTimer == null)
            {
                _windowSaveTimer = new Timer();
                _windowSaveTimer.Interval = 700;
                _windowSaveTimer.Tick += delegate
                {
                    _windowSaveTimer.Stop();
                    CaptureWindowState();
                };
            }
            _windowSaveTimer.Stop();
            _windowSaveTimer.Start();
        }

        /// <summary>Stores the size the user left the window at (not the zoomed one).</summary>
        private void CaptureWindowState()
        {
            if (!_windowSizeReady || IsDisposed) return;
            if (WindowState == FormWindowState.Minimized) return;

            bool maximized = WindowState == FormWindowState.Maximized;
            bool changed = _settings.WindowMaximized != maximized;
            _settings.WindowMaximized = maximized;

            if (!maximized)
            {
                if (_settings.WindowWidth != ClientSize.Width
                    || _settings.WindowHeight != ClientSize.Height)
                {
                    _settings.WindowWidth = ClientSize.Width;
                    _settings.WindowHeight = ClientSize.Height;
                    changed = true;
                }
            }
            if (!changed) return;
            SaveQuiet();
        }

        private void HandleDemo()
        {
            if (string.IsNullOrEmpty(_demo)) return;
            // Demo hooks are developer-only and are never reachable from the
            // release command line. The old "import:" demo could silently
            // compile and run a plugin from an argument, so it is not kept.
            if (_demo == "editor") BeginInvoke(new Action(AddMapping));
            else if (_demo == "editortext") BeginInvoke(new Action(delegate { DemoEditor("input.text", "示例文本"); }));
            else if (_demo == "editorpath") BeginInvoke(new Action(delegate { DemoEditor("launch.program", "C:\\Windows\\notepad.exe"); }));
            else if (_demo == "config") BeginInvoke(new Action(OpenConfigFolder));
            else if (_demo == "textfile") BeginInvoke(new Action(delegate
            {
                using (TextFileForm dialog = new TextFileForm(ConfigStore.ConfigPath, true))
                    dialog.ShowDialog(this);
            }));
            else if (_demo == "escape") BeginInvoke(new Action(delegate
            {
                ShowEscapeResult("第一行\\n第二行\\t制表符", "第一行" + Environment.NewLine + "第二行	制表符");
            }));
            else if (_demo == "actionpicker") BeginInvoke(new Action(delegate
            {
                using (ActionPickerForm picker = new ActionPickerForm(null))
                    picker.ShowDialog(this);
            }));
            else if (_demo == "min") BeginInvoke(new Action(delegate
            {
                Size = MinimumSize;
            }));
            else if (_demo == "keycaps") BeginInvoke(new Action(delegate
            {
                _filterCaps = true;
                RefreshList();
            }));
            else if (_demo == "keycombo") BeginInvoke(new Action(delegate
            {
                _filterCaps = true;
                _filterAlt = true;
                RefreshList();
            }));
            else if (_demo == "keykey") BeginInvoke(new Action(delegate
            {
                _selectedKey = Keys.E;
                RefreshList();
            }));
            else if (_demo == "keycollapsed") BeginInvoke(new Action(delegate
            {
                _keyboardProgress = 0f;
                _keyboardFrom = 0f;
                _keyboardTarget = 0f;
                _keyboardExpanded = false;
                LayoutContent();
            }));
            else if (_demo == "keymid") BeginInvoke(new Action(delegate
            {
                _keyboardProgress = 0.55f;
                _keyboardFrom = 0.55f;
                _keyboardTarget = 0.55f;
                LayoutContent();
            }));
            else if (_demo == "mousecollapsed") BeginInvoke(new Action(delegate
            {
                _mouseProgress = 1f;
                if (_mousePad != null) _mousePad.CollapseProgress = 1f;
                _mousePadCollapsed = true;
                LayoutContent();
            }));
            else if (_demo == "mousemid") BeginInvoke(new Action(delegate
            {
                _mouseProgress = 0.55f;
                if (_mousePad != null) _mousePad.CollapseProgress = 0.55f;
                LayoutContent();
            }));
            else if (_demo == "mousetoggle") BeginInvoke(new Action(delegate
            {
                StartMouseAnimation(_mouseProgress > 0.5f ? 0f : 1f);
            }));
            else if (_demo == "keymenu") BeginInvoke(new Action(ShowMoreMenu));
            else if (_demo == "keytoggle") BeginInvoke(new Action(ToggleKeyboard));
            else if (_demo == "keylarge") BeginInvoke(new Action(delegate
            {
                ClientSize = new Size(Theme.S(1280), Theme.S(820));
            }));
            else if (_demo == "keyfocus") BeginInvoke(new Action(delegate
            {
                Activate();
                Timer focusTimer = new Timer();
                focusTimer.Interval = 500;
                focusTimer.Tick += delegate(object sender, EventArgs args)
                {
                    focusTimer.Stop();
                    focusTimer.Dispose();
                    if (_keyboard != null) _keyboard.Focus();
                };
                focusTimer.Start();
            }));
            else if (_demo == "keysmall") BeginInvoke(new Action(delegate
            {
                Size = MinimumSize;
                _filterCaps = true;
                RefreshList();
            }));
            else if (_demo == "darklive") BeginInvoke(new Action(delegate { ToggleDarkMode(true); }));
            else if (_demo == "plugins") BeginInvoke(new Action(delegate { SwitchPage("plugins"); }));
            else if (_demo.StartsWith("plugin-settings", StringComparison.OrdinalIgnoreCase))
            {
                BeginInvoke(new Action(delegate
                {
                    string pluginId = _demo.IndexOf(':') >= 0
                        ? _demo.Substring(_demo.IndexOf(':') + 1)
                        : "com.capslocklite.crosshair";
                    SwitchPage("plugins");
                    SelectPluginById(pluginId);
                    OpenPluginSettings();
                }));
            }
            else if (_demo == "plugin-editor") BeginInvoke(new Action(delegate
            {
                DemoEditor("plugin.com.capslocklite.screenshot.fullscreen", "");
            }));
            else if (_demo == "dpi150") BeginInvoke(new Action(delegate { DemoDpi(144); }));
            else if (_demo == "settings") BeginInvoke(new Action(delegate { SwitchPage("settings"); }));
            else if (_demo == "settings1")
                BeginInvoke(new Action(delegate { SwitchPage("settings"); _segments.SelectedIndex = 1; }));
            else if (_demo == "settings2")
                BeginInvoke(new Action(delegate { SwitchPage("settings"); _segments.SelectedIndex = 2; }));
            else if (_demo == "about") BeginInvoke(new Action(delegate { SwitchPage("about"); }));
        }

        /// <summary>
        /// Screenshot/diagnostic hook: rebuild the UI at another scale without
        /// touching display settings. Exercises exactly the path a real
        /// WM_DPICHANGED takes, including the measured re-layout.
        /// </summary>
        private void DemoDpi(int dpi)
        {
            _uiDpi = dpi;
            Theme.SetDpi(dpi);
            ClientSize = StartupClientSize();
            MinimumSize = new Size(Theme.S(880), Theme.S(680));
            RebuildUi();
            ShowStatus("已按 " + dpi + " DPI 重建界面", MInfoSeverity.Info);
        }

        private void DemoEditor(string action, string param)
        {
            KeyMapping draft = new KeyMapping();
            draft.Caps = true;
            draft.KeyCode = Keys.K;
            draft.Action = action;
            draft.Param = param;
            using (MappingEditorForm dialog = new MappingEditorForm(draft, true))
                dialog.ShowDialog(this);
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason != CloseReason.WindowsShutDown
                && e.CloseReason != CloseReason.TaskManagerClosing)
            {
                e.Cancel = true;
                HideToTray();
            }
        }

        private void HideToTray()
        {
            // A hidden window leaves the taskbar on its own. Toggling
            // ShowInTaskbar here would recreate the window handle, and a
            // maximized window is mapped again by that recreation: it reappears
            // with a broken layout and the next close cannot get rid of it.
            Hide();
            ShowTrayBalloon();
        }

        /// <summary>One-time hint that the app keeps running in the tray.</summary>
        private void ShowTrayBalloon()
        {
            if (_balloonShown) return;
            _balloonShown = true;
            try
            {
                _tray.BalloonTipTitle = "CapsLock Lite 正在后台运行";
                _tray.BalloonTipText = "按住 Caps Lock 再按 E / D / S / F 即可移动光标。";
                _tray.BalloonTipIcon = ToolTipIcon.Info;
                _tray.ShowBalloonTip(3000);
            }
            catch { }
        }

        /// <summary>Brings the window back from the tray; also a built-in action.</summary>
        public void ShowMainWindow()
        {
            // A window coming back from the tray must look exactly like it was
            // left, including maximized, before it becomes visible.
            if (!_everShown && _settings.WindowMaximized && WindowState == FormWindowState.Normal)
                WindowState = FormWindowState.Maximized;
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized)
                WindowState = _settings.WindowMaximized ? FormWindowState.Maximized : FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        /// <summary>Releases every held key on the input thread.</summary>
        private void ReleaseAllHolds()
        {
            PointerPin.Release();
            ActionExecutor executor = _executor;
            if (executor == null) return;
            RunOnInputAndWait(delegate { executor.EndKeyHold(null); });
        }

        private void ExitApplication()
        {
            _exiting = true;
            ReleaseAllHolds();
            if (_tray != null) _tray.Visible = false;
            Close();
        }

        /// <summary>
        /// Closing a plugin's assembly is impossible in .NET, so a removed
        /// plugin can only be fully gone after the process restarts. Spawn the
        /// replacement first (it waits for this pid) and then close normally so
        /// Dispose still tears the engine and plugins down.
        /// </summary>
        internal void StartPluginRestart()
        {
            _exiting = true;
            ReleaseAllHolds();
            if (_tray != null) _tray.Visible = false;
            ShowStatus("插件已卸载，正在重启 CapsLock Lite…", MInfoSeverity.Info);
            Program.RestartApplication();
            Close();
        }

        private void CreateTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Renderer = new MMenuRenderer();
            _trayMenu.ShowImageMargin = false;
            _trayMenu.Font = Theme.Body;
            _trayMenu.BackColor = Theme.Surface;
            _trayMenu.ForeColor = Theme.TextPrimary;

            ToolStripMenuItem open = new ToolStripMenuItem("打开设置");
            open.Font = Theme.BodyStrong;
            open.Click += delegate { ShowMainWindow(); };
            _trayMenu.Items.Add(open);
            _trayMenu.Items.Add(new ToolStripSeparator());

            _trayToggleItem = new ToolStripMenuItem("暂停快捷键");
            _trayToggleItem.Click += delegate { SetEnabled(!_settings.Enabled); };
            _trayMenu.Items.Add(_trayToggleItem);

            ToolStripMenuItem reload = new ToolStripMenuItem("重新加载配置");
            reload.Click += delegate { ReloadFromDisk(); };
            _trayMenu.Items.Add(reload);
            _trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exit = new ToolStripMenuItem("退出");
            exit.Click += delegate { ExitApplication(); };
            _trayMenu.Items.Add(exit);

            _tray = new NotifyIcon();
            _tray.Icon = _appIcon;
            _tray.Text = "CapsLock Lite";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _trayMenu;
            _tray.DoubleClick += delegate { ShowMainWindow(); };
        }

        protected override void WndProc(ref Message m)
        {
            if (Native.WM_ACTIVATE_INSTANCE != 0 && m.Msg == Native.WM_ACTIVATE_INSTANCE)
            {
                // Another copy of the app was started: come back through the
                // normal WinForms path instead of being poked with ShowWindow
                // from the outside.
                if (!_exiting) ShowMainWindow();
                return;
            }
            if (m.Msg == Native.WM_HOTKEY && _engine != null)
            {
                int id = m.WParam.ToInt32();
                if (_engine.HandleHotkeyMessage(id)) return;
            }
            if (m.Msg == Native.WM_DPICHANGED && !_exiting)
            {
                // Let framework bookkeeping run first, then re-measure our own
                // layout: AutoScaleMode.None means WinForms will not do it for us.
                base.WndProc(ref m);
                OnDpiChanged(m.WParam, m.LParam);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeKeyboardAnimation();
                DisposeMouseAnimation();
                if (_windowSaveTimer != null) { _windowSaveTimer.Dispose(); _windowSaveTimer = null; }
                if (_infoTimer != null) { _infoTimer.Dispose(); _infoTimer = null; }
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                    _tray = null;
                }
                // The pointer must never stay pinned after the app is gone.
                PointerPin.Release();
                // The log writer is a background thread: flush what it still holds.
                ConfigStore.FlushLog(1500);
                DispatcherThread input = _inputThread;
                _inputThread = null;
                if (input != null)
                {
                    MacroRunner runner = _macroRunner;
                    ActionExecutor executor = _executor;
                    _macroRunner = null;
                    _executor = null;
                    input.Send(delegate
                    {
                        if (runner != null) runner.Dispose();
                        if (executor != null) executor.Dispose();
                    });
                    input.Dispose();
                }
                else
                {
                    if (_macroRunner != null) { _macroRunner.Dispose(); _macroRunner = null; }
                    if (_executor != null) { _executor.Dispose(); _executor = null; }
                }
                if (_engine != null) { _engine.Dispose(); _engine = null; }
                PluginRuntime.Shutdown();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>Loads the original application logo from the embedded resources.</summary>
    internal sealed class MLogo : Control
    {
        private readonly Image _light;
        private readonly Image _dark;
        private Image _lightDim;
        private Image _darkDim;
        private bool _dimmed;

        /// <summary>True while the service is paused; the mark is painted greyscale.</summary>
        public bool Dimmed
        {
            get { return _dimmed; }
            set
            {
                if (_dimmed == value) return;
                _dimmed = value;
                Invalidate();
            }
        }

        public MLogo()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(Theme.S(38), Theme.S(38));
            _light = Load("CapsLockLite.logo.png");
            _dark = Load("CapsLockLite.logo-dark.png");
        }

        private static Image Load(string name)
        {
            try
            {
                using (Stream stream = typeof(MLogo).Assembly.GetManifestResourceStream(name))
                {
                    if (stream == null) return null;
                    using (Image image = Image.FromStream(stream))
                        return new Bitmap(image);
                }
            }
            catch { return null; }
        }

        private bool _hover;
        private bool _pressed;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Prepare(g);
            float radius = Theme.SF(9f);
            bool dark = Theme.IsDark;
            // Pressing pushes the cap down and drops the cast shadow.
            float lift = _pressed ? Theme.SF(2f) : 0f;
            float tileHeight = Height - Theme.SF(3f);

            // Cast shadow under the cap: this is what makes the mark read as a
            // physical key instead of a flat icon.
            if (!_pressed)
            {
                RectangleF shadow = new RectangleF(Theme.SF(1f), Theme.SF(2f),
                    Width - Theme.SF(2f), tileHeight);
                Theme.FillRound(g, shadow, radius,
                    dark ? Color.FromArgb(130, Color.Black) : Color.FromArgb(34, Color.Black));
            }

            RectangleF tile = new RectangleF(0f, lift, Width, tileHeight);
            Image image = Theme.IsDark && _dark != null ? _dark : _light;
            GraphicsState state = g.Save();
            using (GraphicsPath path = Theme.Rounded(tile, radius))
            {
                g.SetClip(path);
                if (image != null)
                {
                    if (_dimmed) image = DimImage(image, dark);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(image, tile);
                }
                else
                {
                    using (SolidBrush fill = new SolidBrush(_dimmed ? Theme.TextTertiary : Theme.TextPrimary))
                        g.FillRectangle(fill, tile);
                }

                // Top gloss plus a whisper of hover light give the cap curvature.
                // The gradient spans the whole tile and fades to transparent in a
                // three-stop blend: a partial-height brush leaves a 1px seam where
                // its tile wrap restarts, which showed up as a grey line across
                // the logo. (WrapMode.Clamp is not supported by LinearGradientBrush.)
                float glossHeight = tile.Height * 0.55f;
                using (LinearGradientBrush gloss = new LinearGradientBrush(
                    new PointF(0f, tile.Y), new PointF(0f, tile.Bottom),
                    Color.FromArgb(_pressed ? 22 : 62, Color.White), Color.FromArgb(0, Color.White)))
                {
                    ColorBlend blends = new ColorBlend(3);
                    blends.Colors = new Color[]
                    {
                        Color.FromArgb(_pressed ? 22 : 62, Color.White),
                        Color.FromArgb(0, Color.White),
                        Color.FromArgb(0, Color.White)
                    };
                    blends.Positions = new float[] { 0f, glossHeight / tile.Height, 1f };
                    gloss.InterpolationColors = blends;
                    g.FillRectangle(gloss, tile);
                }
                if (_hover && !_pressed)
                {
                    using (SolidBrush hover = new SolidBrush(Color.FromArgb(16, Color.White)))
                        g.FillRectangle(hover, tile);
                }
                if (_pressed)
                {
                    using (SolidBrush sunk = new SolidBrush(Color.FromArgb(34, Color.Black)))
                        g.FillRectangle(sunk, tile);
                }
            }
            g.Restore(state);

            // Rim light on the top edge sells the bevel in both themes.
            Theme.StrokeRound(g, new RectangleF(tile.X + Theme.SF(0.5f), tile.Y + Theme.SF(0.5f),
                tile.Width - Theme.SF(1f), tile.Height - Theme.SF(1f)), radius,
                Color.FromArgb(dark ? 60 : 34, Color.White), 1f);

            if (image == null)
            {
                using (Font f = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Point))
                    TextRenderer.DrawText(g, "C+", f, new Rectangle(0, 0, Width, (int)tileHeight), Theme.Background,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (_pressed) { _pressed = false; Invalidate(); }
            base.OnMouseUp(e);
        }

        /// <summary>Greyscale copy of the mark, cached per theme and size.</summary>
        private Image DimImage(Image source, bool dark)
        {
            Image cached = dark ? _darkDim : _lightDim;
            if (cached != null && cached.Width == source.Width && cached.Height == source.Height)
                return cached;

            Bitmap result = new Bitmap(source.Width, source.Height);
            using (Graphics g = Graphics.FromImage(result))
            using (ImageAttributes attributes = new ImageAttributes())
            {
                // Luminance matrix plus a little transparency: the mark keeps its
                // silhouette in both themes but stops competing with live content.
                ColorMatrix matrix = new ColorMatrix(new float[][]
                {
                    new float[] { 0.299f, 0.299f, 0.299f, 0f, 0f },
                    new float[] { 0.587f, 0.587f, 0.587f, 0f, 0f },
                    new float[] { 0.114f, 0.114f, 0.114f, 0f, 0f },
                    new float[] { 0f, 0f, 0f, 0.55f, 0f },
                    new float[] { 0f, 0f, 0f, 0f, 1f }
                });
                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }

            if (dark) _darkDim = result; else _lightDim = result;
            return result;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // The logo bitmaps are per instance and every theme/DPI change
                // rebuilds the control tree, so they have to be released here.
                if (_light != null) _light.Dispose();
                if (_dark != null) _dark.Dispose();
                if (_lightDim != null) _lightDim.Dispose();
                if (_darkDim != null) _darkDim.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
