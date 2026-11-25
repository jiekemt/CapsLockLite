using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CapsLockLite
{
    internal sealed partial class MainForm
    {
        private const string PluginHintFull = "Ctrl/Shift 多选 · 拖动排序 · 双击设置 · 右键详情";
        private const string PluginHintMedium = "Ctrl/Shift 多选 · 拖动排序 · 双击设置";
        private const string PluginHintShort = "双击进设置页";

        private MNavItem _navPlugins;
        private Panel _pagePlugins;
        private MListView _pluginList;
        private MCard _pluginListCard;
        private Label _pluginDirLabel;
        private Label _pluginCountLabel;
        private Label _pluginHintLabel;
        private MButton _pluginImportButton;
        private MButton _pluginReloadButton;
        private MButton _pluginOpenButton;
        private MButton _pluginRemoveButton;
        private MButton _pluginSettingsButton;
        private MButton _pluginTrustButton;
        private ToolTip _pluginTip;

        // Second-level page provided by one plugin.
        private Panel _pluginSettingsHost;
        private MScrollPanel _pluginSettingsScroll;
        private MButton _pluginSettingsBack;
        private Label _pluginSettingsTitle;
        private Label _pluginSettingsSubtitle;
        private IPluginSettingsPage _pluginSettingsPage;
        private IPluginSettingsContext _pluginSettingsContext;
        private Control _pluginSettingsView;
        private string _pluginSettingsPluginId = "";
        private bool _pluginSettingsOpen;

        private Panel BuildPluginsPage()
        {
            Panel page = new Panel();
            page.BackColor = Theme.Background;

            // The page is rebuilt on theme/DPI changes; the previous plugin view
            // belonged to the old control tree and is gone.
            IDisposable stalePage = _pluginSettingsPage as IDisposable;
            if (stalePage != null)
            {
                try { stalePage.Dispose(); }
                catch { }
            }
            _pluginSettingsPage = null;
            _pluginSettingsContext = null;
            _pluginSettingsView = null;
            _pluginSettingsPluginId = "";
            _pluginSettingsOpen = false;

            if (_pluginTip != null)
            {
                _pluginTip.Dispose();
                _pluginTip = null;
            }

            _pluginDirLabel = Theme.MakeLine("", Theme.Caption, Theme.TextTertiary, 0, 0, Theme.S(200));
            page.Controls.Add(_pluginDirLabel);

            _pluginTip = new ToolTip();

            _pluginImportButton = MakePluginButton("导入插件…", MButtonKind.Primary,
                delegate { ImportPlugins(); });
            _pluginReloadButton = MakePluginButton("重新加载", MButtonKind.Secondary,
                delegate { ReloadPlugins(true); });
            _pluginOpenButton = MakePluginButton("打开目录", MButtonKind.Secondary,
                delegate { OpenPluginFolder(); });
            _pluginRemoveButton = MakePluginButton("卸载", MButtonKind.Danger,
                delegate { RemovePlugin(); });
            _pluginRemoveButton.Enabled = false;

            _pluginSettingsButton = MakePluginButton("设置", MButtonKind.Secondary,
                delegate { OpenPluginSettings(); });
            _pluginSettingsButton.Enabled = false;

            _pluginTrustButton = MakePluginButton("信任", MButtonKind.Secondary,
                delegate { TrustSelectedPlugin(); });
            _pluginTrustButton.Enabled = false;

            page.Controls.Add(_pluginImportButton);
            page.Controls.Add(_pluginReloadButton);
            page.Controls.Add(_pluginOpenButton);
            page.Controls.Add(_pluginRemoveButton);
            page.Controls.Add(_pluginSettingsButton);
            page.Controls.Add(_pluginTrustButton);

            _pluginListCard = new MCard();
            _pluginListCard.Padding = new Padding(1);
            // Square corners: the list is full-bleed, so a rounded card would show a
            // mismatched corner ring at all four corners (same as the shortcut list).
            _pluginListCard.Radius = 0;
            page.Controls.Add(_pluginListCard);

            _pluginList = new MListView();
            _pluginList.RowHeight = Theme.S(58);
            _pluginList.HeaderHeight = Theme.S(38);
            _pluginList.MultiSelect = true;
            _pluginList.AllowReorder = true;
            _pluginList.EmptyText = "还没有插件\r\n点“导入插件…”，或把 .cs / .zip 拖进窗口";
            _pluginList.HeaderPainter = delegate(Graphics g, Rectangle r)
            {
                Theme.Prepare(g);
                using (SolidBrush b = new SolidBrush(Theme.Surface))
                    g.FillRectangle(b, r);
                TextRenderer.DrawText(g, "插件", Theme.CaptionStrong,
                    new Rectangle(Theme.S(18), r.Y, Theme.S(200), r.Height), Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "动作", Theme.CaptionStrong,
                    new Rectangle(r.Right - Theme.S(168), r.Y, Theme.S(70), r.Height), Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "状态", Theme.CaptionStrong,
                    new Rectangle(r.Right - Theme.S(80), r.Y, Theme.S(60), r.Height), Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            };
            _pluginList.ItemActivated += delegate { OpenPluginSettings(); };
            _pluginList.SelectionChanged += delegate { UpdatePluginButtons(); UpdatePluginCount(); };
            _pluginList.ItemContextRequested += delegate { ShowPluginMenu(); };
            _pluginList.EmptyContextRequested += delegate { ShowEmptyPluginMenu(); };
            _pluginList.ItemDeleteRequested += delegate { RemovePlugin(); };
            _pluginList.ItemsReordered += delegate(object sender, MListReorderEventArgs e) { ReorderPlugins(e); };
            _pluginListCard.Controls.Add(_pluginList);

            _pluginCountLabel = Theme.MakeLabel("共 0 个插件", Theme.Caption, Theme.TextTertiary,
                0, 0, Theme.S(200), Theme.S(18));
            page.Controls.Add(_pluginCountLabel);

            _pluginHintLabel = Theme.MakeLabel(PluginHintFull, Theme.Caption, Theme.TextTertiary,
                0, 0, Theme.S(320), Theme.S(18));
            _pluginHintLabel.TextAlign = ContentAlignment.MiddleRight;
            _pluginHintLabel.AutoEllipsis = true;
            page.Controls.Add(_pluginHintLabel);

            // ---- second-level settings page (hidden until a plugin is opened) ----
            _pluginSettingsHost = new Panel();
            _pluginSettingsHost.BackColor = Theme.Background;
            _pluginSettingsHost.Visible = false;
            page.Controls.Add(_pluginSettingsHost);

            _pluginSettingsBack = MakePluginButton("← 返回", MButtonKind.Secondary,
                delegate { ClosePluginSettings(); });
            _pluginSettingsHost.Controls.Add(_pluginSettingsBack);

            _pluginSettingsTitle = Theme.MakeLabel("", Theme.Subtitle, Theme.TextPrimary,
                0, 0, Theme.S(300), Theme.S(24));
            _pluginSettingsHost.Controls.Add(_pluginSettingsTitle);

            _pluginSettingsSubtitle = Theme.MakeLabel("", Theme.Caption, Theme.TextTertiary,
                0, 0, Theme.S(400), Theme.S(18));
            _pluginSettingsHost.Controls.Add(_pluginSettingsSubtitle);

            _pluginSettingsScroll = new MScrollPanel();
            _pluginSettingsHost.Controls.Add(_pluginSettingsScroll);

            return page;
        }

        private MButton MakePluginButton(string text, MButtonKind kind, EventHandler click)
        {
            MButton button = new MButton();
            button.Text = text;
            button.Kind = kind;
            button.Font = Theme.BodyStrong;
            button.Click += click;
            return button;
        }

        private void LayoutPluginsPage()
        {
            if (_pagePlugins == null || _pluginList == null) return;
            int w = _pagePlugins.ClientSize.Width;
            int h = _pagePlugins.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            int ctrl = Theme.S(Theme.CtrlHeight);
            int gap = Theme.S(8);
            int right = w;

            int removeW = Theme.S(72);
            int settingsW = Theme.S(72);
            int openW = Theme.S(84);
            int reloadW = Theme.S(84);
            int importW = Theme.S(104);
            int trustW = Theme.S(64);

            _pluginRemoveButton.SetBounds(right - removeW, 0, removeW, ctrl);
            right -= removeW + gap;
            _pluginSettingsButton.SetBounds(right - settingsW, 0, settingsW, ctrl);
            right -= settingsW + gap;
            _pluginOpenButton.SetBounds(right - openW, 0, openW, ctrl);
            right -= openW + gap;
            _pluginReloadButton.SetBounds(right - reloadW, 0, reloadW, ctrl);
            right -= reloadW + gap;
            _pluginImportButton.SetBounds(right - importW, 0, importW, ctrl);
            right -= importW + gap;
            _pluginTrustButton.SetBounds(right - trustW, 0, trustW, ctrl);
            right -= trustW + gap;

            int dirLeft = 0;
            int dirWidth = Math.Max(Theme.S(60), right - dirLeft);
            _pluginDirLabel.SetBounds(dirLeft, 0, dirWidth, ctrl);
            if (_pluginTip != null) _pluginTip.SetToolTip(_pluginDirLabel, PluginRuntime.PluginDirectory);

            int footer = Theme.S(22);
            int cardTop = ctrl + Theme.S(12);
            _pluginListCard.SetBounds(0, cardTop, w,
                Math.Max(Theme.S(80), h - cardTop - footer - Theme.S(8)));
            _pluginList.SetBounds(1, 1,
                Math.Max(1, _pluginListCard.ClientSize.Width - 2),
                Math.Max(1, _pluginListCard.ClientSize.Height - 2));

            _pluginCountLabel.SetBounds(0, h - footer, Math.Max(Theme.S(80), w / 2), footer);
            int hintWidth = Math.Max(Theme.S(80), w - w / 2);
            _pluginHintLabel.SetBounds(w / 2, h - footer, hintWidth, footer);
            if (Theme.Measure(PluginHintFull, Theme.Caption).Width <= hintWidth)
                _pluginHintLabel.Text = PluginHintFull;
            else if (Theme.Measure(PluginHintMedium, Theme.Caption).Width <= hintWidth)
                _pluginHintLabel.Text = PluginHintMedium;
            else
                _pluginHintLabel.Text = PluginHintShort;

            if (_pluginSettingsHost != null)
            {
                _pluginSettingsHost.SetBounds(0, 0, w, h);
                _pluginSettingsBack.SetBounds(0, Theme.S(4), Theme.S(88), ctrl);
                int titleLeft = Theme.S(100);
                int titleWidth = Math.Max(Theme.S(120), w - titleLeft);
                _pluginSettingsTitle.SetBounds(titleLeft, Theme.S(0), titleWidth, Theme.S(26));
                _pluginSettingsSubtitle.SetBounds(titleLeft, Theme.S(28), titleWidth,
                    Theme.LineHeight(Theme.Caption));
                _pluginSettingsScroll.SetBounds(0, Theme.S(58), w,
                    Math.Max(Theme.S(80), h - Theme.S(58)));
                LayoutPluginSettingsView();
            }
        }

        private void LayoutPluginSettingsView()
        {
            if (_pluginSettingsView == null || _pluginSettingsScroll == null) return;
            // Width is owned by the scroll host; only make sure the plugin view has
            // a usable height when it forgot to set one.
            if (_pluginSettingsView.Height <= 0)
                _pluginSettingsView.Height = Theme.S(420);
        }

        private PluginStatus SelectedPlugin
        {
            get
            {
                if (_pluginList == null) return null;
                PluginListItem item = _pluginList.SelectedItem as PluginListItem;
                return item == null ? null : item.Plugin;
            }
        }

        /// <summary>Every selected plugin, in list order (never null).</summary>
        private List<PluginStatus> SelectedPlugins
        {
            get
            {
                List<PluginStatus> plugins = new List<PluginStatus>();
                if (_pluginList == null) return plugins;
                foreach (MListItem item in _pluginList.SelectedItems)
                {
                    PluginListItem row = item as PluginListItem;
                    if (row != null && row.Plugin != null) plugins.Add(row.Plugin);
                }
                return plugins;
            }
        }

        /// <summary>
        /// Plugin files in the order the user arranged them. Unknown files keep
        /// the loader's (path-sorted) order after the known ones, so importing or
        /// renaming a plugin never shuffles the rows the user already placed.
        /// </summary>
        private List<PluginStatus> OrderedPlugins()
        {
            List<PluginStatus> ordered = new List<PluginStatus>();
            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> wanted = _settings == null ? null : _settings.PluginOrder;
            if (wanted != null)
            {
                for (int i = 0; i < wanted.Count; i++)
                {
                    string name = wanted[i];
                    if (string.IsNullOrEmpty(name) || used.Contains(name)) continue;
                    for (int j = 0; j < PluginRuntime.Plugins.Count; j++)
                    {
                        PluginStatus plugin = PluginRuntime.Plugins[j];
                        if (plugin == null || !string.Equals(plugin.FileName, name,
                            StringComparison.OrdinalIgnoreCase)) continue;
                        ordered.Add(plugin);
                        used.Add(name);
                        break;
                    }
                }
            }
            for (int i = 0; i < PluginRuntime.Plugins.Count; i++)
            {
                PluginStatus plugin = PluginRuntime.Plugins[i];
                if (plugin != null && used.Add(plugin.FileName)) ordered.Add(plugin);
            }
            return ordered;
        }

        private void RefreshPlugins()
        {
            if (_pluginList == null) return;

            // Keep the selection across reload/import/delete/reorder by file path.
            HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            PluginStatus leadPlugin = SelectedPlugin;
            string leadPath = leadPlugin == null ? null : leadPlugin.FilePath;
            foreach (MListItem item in _pluginList.SelectedItems)
            {
                PluginListItem row = item as PluginListItem;
                if (row != null && row.Plugin != null) keep.Add(row.Plugin.FilePath);
            }

            List<PluginStatus> ordered = OrderedPlugins();
            List<MListItem> rows = new List<MListItem>();
            for (int i = 0; i < ordered.Count; i++) rows.Add(new PluginListItem(ordered[i]));
            _pluginList.SetItems(rows);

            List<int> restore = new List<int>();
            int lead = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                PluginListItem row = rows[i] as PluginListItem;
                if (row == null || row.Plugin == null) continue;
                if (keep.Contains(row.Plugin.FilePath)) restore.Add(i);
                if (leadPath != null && row.Plugin.FilePath == leadPath) lead = i;
            }
            if (restore.Count > 0)
            {
                if (lead < 0) lead = restore[0];
                _pluginList.ApplySelection(restore, lead);
            }
            else if (rows.Count > 0)
            {
                _pluginList.SelectedIndex = 0;
            }

            UpdatePluginCount();
            _pluginDirLabel.Text = "插件目录："
                + PathDisplay.Shorten(PathDisplay.Friendly(PluginRuntime.PluginDirectory), 56);
            UpdatePluginButtons();
        }

        private void UpdatePluginButtons()
        {
            if (_pluginRemoveButton == null) return;
            List<PluginStatus> selected = SelectedPlugins;
            PluginStatus plugin = SelectedPlugin;

            _pluginImportButton.Enabled = true;
            _pluginReloadButton.Enabled = true;
            _pluginOpenButton.Enabled = true;
            _pluginRemoveButton.Enabled = selected.Count > 0 && !_pluginSettingsOpen;
            if (_pluginSettingsButton != null)
                _pluginSettingsButton.Enabled = selected.Count == 1
                    && !_pluginSettingsOpen && plugin.HasSettings;
            if (_pluginTrustButton != null)
            {
                bool anyTrustable = false;
                for (int i = 0; i < selected.Count; i++)
                {
                    PluginStatus candidate = selected[i];
                    if (candidate != null && candidate.FileExists && !candidate.IsLoaded
                        && !candidate.Trusted) { anyTrustable = true; break; }
                }
                _pluginTrustButton.Enabled = anyTrustable && !_pluginSettingsOpen;
            }
        }

        /// <summary>
        /// The list already moved the rows; store that exact order so a reload
        /// puts every plugin back where the user left it.
        /// </summary>
        /// <summary>
        /// Footer counter: plugin total, or "已选 N 条" once several rows are
        /// picked. One place so every refresh and click agrees.
        /// </summary>
        private void UpdatePluginCount()
        {
            if (_pluginCountLabel == null) return;
            int total = _pluginList == null ? 0 : _pluginList.Items.Count;
            int picked = _pluginList == null ? 0 : _pluginList.SelectedCount;
            _pluginCountLabel.Text = picked > 1
                ? "已选 " + picked + " 条 · 共 " + total + " 个插件"
                : "共 " + total + " 个插件";
        }

        /// <summary>
        /// Row menu for the plugin list: details and (when the plugin has one)
        /// its settings page for a single row, trust and remove for the whole
        /// selection. Double-click / Enter opens the settings page instead.
        /// </summary>
        private void ShowPluginMenu()
        {
            if (_pluginSettingsOpen) return;
            List<PluginStatus> selected = SelectedPlugins;
            if (selected.Count == 0) return;

            int n = selected.Count;
            MPopupMenu menu = new MPopupMenu();
            if (n == 1)
            {
                menu.AddItem("插件详情…", "", delegate { ShowPluginDetails(); });
                if (selected[0].HasSettings)
                    menu.AddItem("插件设置…", "Enter", delegate { OpenPluginSettings(); });
            }

            bool anyTrustable = false;
            bool anyTrusted = false;
            for (int i = 0; i < selected.Count; i++)
            {
                PluginStatus candidate = selected[i];
                if (candidate == null) continue;
                if (candidate.FileExists && !candidate.IsLoaded && !candidate.Trusted) anyTrustable = true;
                if (candidate.Trusted) anyTrusted = true;
            }

            // Trust stays visible even when there is nothing left to trust, so the
            // menu keeps the same three actions the page documents.
            menu.AddItem(n == 1 ? "信任此插件…" : "信任选中 (" + n + ")…", "",
                delegate { TrustSelectedPlugin(); }, anyTrustable);
            menu.AddItem(n == 1 ? "取消信任…" : "取消信任选中 (" + n + ")…", "",
                delegate { UntrustSelectedPlugins(); }, anyTrusted);

            menu.AddItem(n == 1 ? "卸载插件…" : "卸载选中 (" + n + ")…", "Delete",
                delegate { RemovePlugin(); });
            menu.ShowAt(Cursor.Position);
        }

        /// <summary>
        /// Right-click on the blank part of the plugin list: page-level commands
        /// only, so an empty list still has a way forward.
        /// </summary>
        private void ShowEmptyPluginMenu()
        {
            if (_pluginSettingsOpen) return;

            MPopupMenu menu = new MPopupMenu();
            menu.AddItem("导入插件…", "", delegate { ImportPlugins(); }, true);
            menu.AddItem("重新加载插件", "F5", delegate { ReloadPlugins(true); }, true);
            menu.AddItem("打开插件目录", "", delegate { OpenPluginFolder(); }, true);
            if (_pluginList.Items.Count > 0)
            {
                menu.AddSeparator();
                menu.AddItem("全选", "Ctrl+A", delegate { _pluginList.SelectAll(); }, true);
            }
            menu.ShowAt(Cursor.Position);
        }

        private void ReorderPlugins(MListReorderEventArgs args)
        {
            if (args == null || args.Count <= 0) return;
            List<string> order = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MListItem item in _pluginList.Items)
            {
                PluginListItem row = item as PluginListItem;
                if (row == null || row.Plugin == null) continue;
                if (seen.Add(row.Plugin.FileName)) order.Add(row.Plugin.FileName);
            }
            _settings.PluginOrder = order;
            SaveQuiet();
            RefreshPlugins();
            ShowStatus("已调整 " + args.Count + " 个插件的顺序", MInfoSeverity.Success);
        }

        private void TrustSelectedPlugin()
        {
            List<PluginStatus> targets = new List<PluginStatus>();
            List<PluginStatus> selected = SelectedPlugins;
            for (int i = 0; i < selected.Count; i++)
            {
                PluginStatus candidate = selected[i];
                if (candidate != null && candidate.FileExists && !candidate.IsLoaded && !candidate.Trusted)
                    targets.Add(candidate);
            }
            if (targets.Count == 0) return;

            string question;
            if (targets.Count == 1)
            {
                PluginStatus plugin = targets[0];
                string hash;
                try { hash = PluginRuntime.ComputeHash(plugin.FilePath); }
                catch (Exception ex)
                {
                    ShowStatus("读取插件失败：" + ex.Message, MInfoSeverity.Error);
                    return;
                }
                question = "信任后，这个文件会在本进程中编译并以当前用户权限运行。\r\n\r\n"
                    + "文件：" + plugin.FilePath + "\r\n"
                    + "SHA-256：" + hash + "\r\n\r\n"
                    + "只在你已阅读源码并确认来源时继续。是否信任？";
            }
            else
            {
                StringBuilder names = new StringBuilder();
                for (int i = 0; i < targets.Count && i < 8; i++)
                    names.AppendLine("· " + targets[i].FileName);
                if (targets.Count > 8) names.AppendLine("… 共 " + targets.Count + " 个文件");
                question = "信任后，这 " + targets.Count + " 个文件会在本进程中编译并以当前用户权限运行。\r\n\r\n"
                    + names.ToString() + "\r\n"
                    + "只在你已阅读源码并确认来源时继续。是否信任？";
            }

            if (!MDialog.Confirm(this, "信任插件", question, "信任", "取消", MDialogKind.Warning)) return;

            bool all = true;
            PauseEngine();
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (!PluginRuntime.TrustFile(targets[i].FilePath)) all = false;
                }
            }
            finally { ResumeEngine(); }

            RefreshPlugins();
            RefreshList();
            string message = targets.Count == 1
                ? (all ? "插件已信任并重新加载。" : "插件信任失败，请查看日志。")
                : (all ? "已信任 " + targets.Count + " 个插件。" : "部分插件信任失败，请查看日志。");
            ShowStatus(message, all ? MInfoSeverity.Success : MInfoSeverity.Error);
        }

        /// <summary>
        /// Drops the trust record of the selected plugins. The files stay on disk
        /// but stop loading until the user trusts them again.
        /// </summary>
        private void UntrustSelectedPlugins()
        {
            List<PluginStatus> targets = new List<PluginStatus>();
            List<PluginStatus> selected = SelectedPlugins;
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i] != null && selected[i].Trusted) targets.Add(selected[i]);
            }
            if (targets.Count == 0) return;

            string question;
            if (targets.Count == 1)
            {
                PluginStatus plugin = targets[0];
                question = "取消信任后，“" + plugin.Name + "”的动作会立即卸载，"
                    + "已绑定它的快捷键会显示为“插件动作未加载”。\r\n\r\n"
                    + "插件文件仍保留在磁盘上，再次使用需要重新信任。是否继续？";
            }
            else
            {
                StringBuilder names = new StringBuilder();
                for (int i = 0; i < targets.Count && i < 8; i++)
                    names.AppendLine("· " + targets[i].Name);
                if (targets.Count > 8) names.AppendLine("… 共 " + targets.Count + " 个插件");
                question = "取消信任这 " + targets.Count + " 个插件？\r\n\r\n"
                    + names.ToString() + "\r\n"
                    + "它们的动作会立即卸载，文件仍保留在磁盘上，再次使用需要重新信任。";
            }
            if (!MDialog.Confirm(this, "取消信任", question, "取消信任", "返回", MDialogKind.Warning)) return;

            bool all = true;
            PauseEngine();
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (!PluginRuntime.UntrustFile(targets[i].FilePath)) all = false;
                }
            }
            finally { ResumeEngine(); }

            RefreshPlugins();
            RefreshList();
            string message = targets.Count == 1
                ? (all ? "已取消信任；该插件不会再加载。" : "取消信任失败，请查看日志。")
                : (all ? "已取消信任 " + targets.Count + " 个插件。" : "部分插件取消信任失败，请查看日志。");
            ShowStatus(message, all ? MInfoSeverity.Success : MInfoSeverity.Error);
        }

        private void ImportPlugins()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "导入插件";
                dialog.Filter = "插件文件|*.cs;*.zip|C# 插件源文件|*.cs|插件压缩包|*.zip|所有文件|*.*";
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                ImportPluginPaths(dialog.FileNames);
            }
        }

        /// <summary>
        /// Shared by the import dialog and by drag &amp; drop. Import only
        /// compiles and inspects the candidate; the user must explicitly trust
        /// the staged source before any plugin code is instantiated.
        /// </summary>
        private void ImportPluginPaths(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            if (_pluginSettingsOpen) ClosePluginSettings();

            IList<PluginImportResult> results;
            PauseEngine();
            try { results = PluginRuntime.ImportPaths(paths); }
            finally { ResumeEngine(); }

            int staged = 0;
            int failed = 0;
            StringBuilder details = new StringBuilder();
            StringBuilder trustList = new StringBuilder();

            for (int i = 0; i < results.Count; i++)
            {
                PluginImportResult result = results[i];
                if (result.Imported)
                {
                    staged++;
                    if (staged <= 6)
                    {
                        trustList.AppendLine("· " + result.DisplayName);
                        trustList.AppendLine("  SHA-256 " + result.Hash);
                    }
                }
                else
                {
                    failed++;
                    details.AppendLine("· " + result.DisplayName + "：" + result.Error);
                    ConfigStore.Log("导入插件失败：" + result.DisplayName + " - " + result.Error);
                }
            }

            if (staged > 6) trustList.AppendLine("……其余 " + (staged - 6) + " 个文件未展开");

            if (staged == 0)
            {
                ShowStatus("没有识别到可用插件", MInfoSeverity.Error);
                string text = "下面这些文件没有通过插件识别，未做任何改动：\r\n\r\n" + details.ToString().TrimEnd();
                if (text.Length > 1200) text = text.Substring(0, 1200) + "…";
                MDialog.Tell(this, "导入插件", text, MDialogKind.Warning);
                return;
            }

            string prompt = "即将信任并启用 " + staged + " 个插件文件。\r\n\r\n"
                + trustList.ToString()
                + "\r\n插件代码是本机代码，会以当前用户权限运行，没有沙箱。"
                + "只应信任你已阅读并确认来源的文件。\r\n\r\n是否信任这些文件？";
            if (!MDialog.Confirm(this, "确认信任插件", prompt, "信任并启用", "取消", MDialogKind.Warning))
            {
                PluginRuntime.DiscardImports(results);
                ShowStatus("已取消导入，未信任任何文件。", MInfoSeverity.Info);
                return;
            }

            bool trusted = true;
            PauseEngine();
            try { trusted = PluginRuntime.TrustImports(results); }
            finally { ResumeEngine(); }

            RefreshPlugins();
            RefreshList();
            SwitchPage("plugins");

            if (!trusted)
            {
                ShowStatus("插件信任库保存失败，未加载这些文件；请检查目录权限。", MInfoSeverity.Error);
                return;
            }
            if (failed > 0)
            {
                ShowStatus("已信任并加载 " + staged + " 个插件，" + failed + " 个未通过识别",
                    MInfoSeverity.Warning);
            }
            else
            {
                ShowStatus("已信任并加载 " + staged + " 个插件，现在可以在快捷键里绑定",
                    MInfoSeverity.Success);
            }
        }

        /// <summary>
        /// Own-drawn child controls each have their own window, so a drop lands on
        /// the child under the cursor. Every control must accept files itself.
        /// </summary>
        private void AttachDropTargets(Control root)
        {
            if (root == null) return;
            foreach (Control child in root.Controls)
            {
                child.AllowDrop = true;
                child.DragEnter += HandlePluginDragEnter;
                child.DragDrop += HandlePluginDragDrop;
                AttachDropTargets(child);
            }
        }

        private static bool IsPluginFileDrop(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return false;
            string[] files = data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return false;
            for (int i = 0; i < files.Length; i++)
            {
                string extension = Path.GetExtension(files[i]);
                if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void HandlePluginDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = IsPluginFileDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void HandlePluginDragDrop(object sender, DragEventArgs e)
        {
            if (!IsPluginFileDrop(e.Data)) return;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            ImportPluginPaths(files);
        }

        private void ReloadPlugins(bool showStatus)
        {
            if (_pluginSettingsOpen) ClosePluginSettings();

            PauseEngine();
            try { PluginRuntime.Reload(); }
            finally { ResumeEngine(); }
            RefreshPlugins();
            RefreshList();

            if (!showStatus) return;

            int loaded = 0;
            int failed = 0;
            for (int i = 0; i < PluginRuntime.Plugins.Count; i++)
            {
                if (PluginRuntime.Plugins[i].IsLoaded) loaded++;
                else failed++;
            }
            string message = "已重新加载：" + loaded + " 个插件可用";
            if (failed > 0) message += "，" + failed + " 个失败或未信任";
            if (loaded > 0)
                message += "；旧插件的后台线程可能仍在本次进程中，必要时请重启";
            ShowStatus(message, failed > 0 ? MInfoSeverity.Warning : MInfoSeverity.Success);
        }

        private void RemovePlugin()
        {
            if (_pluginSettingsOpen) ClosePluginSettings();

            List<PluginStatus> selected = SelectedPlugins;
            if (selected.Count == 0) return;

            bool wasLoaded = false;
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i] != null && selected[i].IsLoaded) { wasLoaded = true; break; }
            }

            string question;
            if (selected.Count == 1)
            {
                PluginStatus single = selected[0];
                question = "确定卸载插件“" + single.Name + "”？\r\n\r\n"
                    + "源文件和它的数据目录（settings.json 与插件自己写的配置文件）都会被删除；"
                    + "已经绑定它的快捷键会显示为“插件动作未加载”。";
            }
            else
            {
                StringBuilder names = new StringBuilder();
                for (int i = 0; i < selected.Count && i < 8; i++)
                    names.AppendLine("· " + selected[i].Name);
                if (selected.Count > 8) names.AppendLine("… 共 " + selected.Count + " 个插件");
                question = "确定卸载选中的 " + selected.Count + " 个插件？\r\n\r\n"
                    + names.ToString() + "\r\n"
                    + "它们的源文件和数据目录都会被删除；已经绑定它们的快捷键会显示为“插件动作未加载”。";
            }
            if (wasLoaded)
                question += "\r\n\r\n其中有插件当前已加载。为保证它的后台线程/钩子停止，"
                    + "卸载完成后程序会自动重启，无需手动重新打开。";
            if (!MDialog.Confirm(this, "卸载插件", question, "卸载", "取消", MDialogKind.Warning)) return;

            int removed = 0;
            int cleaned = 0;
            string failure = null;
            List<string> hashes = new List<string>();
            for (int i = 0; i < selected.Count; i++)
            {
                PluginStatus plugin = selected[i];
                if (plugin == null) continue;
                string hash = plugin.Hash;
                if (string.IsNullOrEmpty(hash))
                {
                    try { hash = PluginRuntime.ComputeHash(plugin.FilePath); }
                    catch { hash = null; }
                }
                if (!string.IsNullOrEmpty(hash)) hashes.Add(hash);
                try
                {
                    if (File.Exists(plugin.FilePath)) File.Delete(plugin.FilePath);
                    removed++;
                }
                catch (Exception ex)
                {
                    if (failure == null) failure = ex.Message;
                    continue;
                }

                // Leftover settings.json / ini made a reinstall inherit the old
                // configuration, which looked like the uninstall had failed.
                if (RemovePluginData(plugin)) cleaned++;
            }

            if (removed == 0)
            {
                ShowStatus("卸载失败：" + (failure == null ? "插件文件不存在。" : failure),
                    MInfoSeverity.Error);
                return;
            }

            // A removed plugin must not stay trusted: importing the same file
            // again has to go through the trust prompt once more.
            PluginRuntime.UntrustHashes(hashes);

            ReloadPlugins(false);

            if (wasLoaded)
            {
                // A managed assembly cannot be unloaded. Restarting is the only
                // way to guarantee that code from the removed plugin stops
                // running, and doing it automatically spares the user a manual
                // relaunch.
                StartPluginRestart();
                return;
            }

            string message = selected.Count == 1
                ? "已卸载：" + selected[0].Name
                : "已卸载 " + removed + " 个插件";
            if (cleaned > 0) message += "，并清除了 " + cleaned + " 个数据目录";
            if (hashes.Count > 0) message += "，并清除了信任记录";
            if (failure != null) message += "；部分文件无法删除：" + failure;
            ShowStatus(message, failure == null ? MInfoSeverity.Success : MInfoSeverity.Warning);
        }

        /// <summary>
        /// Deletes the plugin's own folder, but only when it really is
        /// "plugins\&lt;id&gt;". A plugin loaded from a custom folder must never be
        /// removed recursively.
        /// </summary>
        private static bool RemovePluginData(PluginStatus plugin)
        {
            string folder = plugin.DataDirectory;
            if (string.IsNullOrEmpty(folder)) return false;
            try
            {
                string full = Path.GetFullPath(folder).TrimEnd('\\');
                string root = Path.GetFullPath(PluginRuntime.PluginDirectory).TrimEnd('\\');
                if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return false;
                if (!full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return false;
                string leaf = Path.GetFileName(full);
                if (!string.Equals(leaf, plugin.Id, StringComparison.OrdinalIgnoreCase)) return false;
                if (!Directory.Exists(full)) return false;
                FileAttributes attributes = File.GetAttributes(full);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                Directory.Delete(full, true);
                return true;
            }
            catch (Exception ex)
            {
                ConfigStore.Log("删除插件数据目录失败：" + folder + " - " + ex.Message);
                return false;
            }
        }

        private void OpenPluginFolder()
        {
            try
            {
                Directory.CreateDirectory(PluginRuntime.PluginDirectory);
                Process.Start(PluginRuntime.PluginDirectory);
            }
            catch (Exception ex)
            {
                ShowStatus("打开插件目录失败：" + ex.Message, MInfoSeverity.Error);
            }
        }

        /// <summary>
        /// Opens the optional second-level page a plugin provides through
        /// <see cref="IPluginWithSettings"/>. The plugin builds its own view; the
        /// host only supplies the frame, storage and theme hints.
        /// </summary>
        private void OpenPluginSettings()
        {
            PluginStatus plugin = SelectedPlugin;
            if (plugin == null || _pluginSettingsOpen) return;

            IPluginWithSettings provider = plugin.Instance as IPluginWithSettings;
            if (provider == null)
            {
                ShowStatus("该插件没有提供设置页。", MInfoSeverity.Info);
                return;
            }

            IPluginSettingsPage page;
            Control view;
            PluginSettingsStore store = new PluginSettingsStore(plugin.Id);
            PluginSettingsContext context = new PluginSettingsContext(plugin.Id, store, this,
                delegate(string message) { ShowStatus(message, MInfoSeverity.Info); },
                delegate(string message) { ShowStatus(message, MInfoSeverity.Error); });

            try
            {
                page = provider.CreateSettingsPage();
                if (page == null)
                {
                    ShowStatus("该插件没有提供设置页。", MInfoSeverity.Info);
                    return;
                }
                view = page.CreateView(context);
            }
            catch (Exception ex)
            {
                ConfigStore.Log("打开插件设置失败：" + plugin.Id + " - " + ex);
                ShowStatus("打开插件设置失败：" + ex.Message, MInfoSeverity.Error);
                return;
            }

            if (view == null)
            {
                ShowStatus("插件没有返回设置界面。", MInfoSeverity.Error);
                return;
            }

            _pluginSettingsPluginId = plugin.Id;
            _pluginSettingsPage = page;
            _pluginSettingsContext = context;
            _pluginSettingsView = view;

            _pluginSettingsTitle.Text = string.IsNullOrEmpty(page.Title) ? plugin.Name : page.Title;
            _pluginSettingsSubtitle.Text = plugin.Name + " · 由插件提供的设置页";

            _pluginSettingsScroll.SetView(view);

            _pluginSettingsOpen = true;
            SetPluginListVisible(false);
            _pluginSettingsHost.Visible = true;
            _pluginSettingsHost.BringToFront();
            LayoutPluginSettingsView();
            UpdatePluginButtons();
            _pluginSettingsBack.Focus();
            ShowStatus("正在编辑「" + plugin.Name + "」的设置", MInfoSeverity.Info);
        }

        private void ClosePluginSettings()
        {
            SavePluginSettings();

            if (_pluginSettingsView != null)
            {
                _pluginSettingsScroll.SetView(null);
                _pluginSettingsView.Dispose();
                _pluginSettingsView = null;
            }

            IDisposable disposable = _pluginSettingsPage as IDisposable;
            if (disposable != null)
            {
                try { disposable.Dispose(); }
                catch { }
            }

            _pluginSettingsPage = null;
            _pluginSettingsContext = null;
            _pluginSettingsPluginId = "";
            _pluginSettingsOpen = false;

            if (_pluginSettingsHost != null) _pluginSettingsHost.Visible = false;
            SetPluginListVisible(true);
            UpdatePluginButtons();
        }

        private void SavePluginSettings()
        {
            if (_pluginSettingsContext == null) return;

            // Only write when the page actually changed something; opening and
            // closing a page must not create empty settings files.
            PluginSettingsContext concrete = _pluginSettingsContext as PluginSettingsContext;
            if (concrete != null && !concrete.HasChanges) return;

            try { _pluginSettingsContext.Save(); }
            catch (Exception ex) { ConfigStore.Log("保存插件设置失败：" + ex.Message); }
        }

        private void SelectPluginById(string id)
        {
            if (_pluginList == null) return;
            for (int i = 0; i < _pluginList.Items.Count; i++)
            {
                PluginListItem item = _pluginList.Items[i] as PluginListItem;
                if (item != null
                    && string.Equals(item.Plugin.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    _pluginList.SelectedIndex = i;
                    return;
                }
            }
        }

        private void SetPluginListVisible(bool visible)
        {
            if (_pluginListCard != null) _pluginListCard.Visible = visible;
            if (_pluginCountLabel != null) _pluginCountLabel.Visible = visible;
            if (_pluginHintLabel != null) _pluginHintLabel.Visible = visible;
            if (_pluginDirLabel != null) _pluginDirLabel.Visible = visible;
            if (_pluginImportButton != null) _pluginImportButton.Visible = visible;
            if (_pluginReloadButton != null) _pluginReloadButton.Visible = visible;
            if (_pluginOpenButton != null) _pluginOpenButton.Visible = visible;
            if (_pluginRemoveButton != null) _pluginRemoveButton.Visible = visible;
            if (_pluginSettingsButton != null) _pluginSettingsButton.Visible = visible;
            if (_pluginTrustButton != null) _pluginTrustButton.Visible = visible;
        }

        private void ShowPluginDetails()
        {
            PluginStatus plugin = SelectedPlugin;
            if (plugin == null) return;

            StringBuilder body = new StringBuilder();
            body.AppendLine("标识：" + plugin.Id);
            body.AppendLine("版本：" + plugin.Version);
            body.AppendLine("作者：" + (plugin.Author.Length > 0 ? plugin.Author : "（未填写）"));
            body.AppendLine("文件：" + plugin.FilePath);
            body.AppendLine("信任：" + (plugin.Trusted ? "是" : "否"));
            if (!string.IsNullOrEmpty(plugin.Hash)) body.AppendLine("SHA-256：" + plugin.Hash);
            body.AppendLine();
            body.AppendLine("说明：");
            body.AppendLine(plugin.Description.Length > 0 ? plugin.Description : "（未填写）");
            body.AppendLine("设置页：" + (plugin.HasSettings ? "由插件提供（点“设置”进入）" : "无"));

            if (!plugin.Trusted)
            {
                body.AppendLine();
                body.AppendLine("该文件尚未信任，不会被加载或执行。");
                body.AppendLine("请先阅读源码并确认来源，再点击“信任”。");
            }
            else if (plugin.IsLoaded)
            {
                body.AppendLine();
                body.AppendLine("动作（" + plugin.ActionCount + " 个）：");
                for (int i = 0; i < plugin.Actions.Count; i++)
                {
                    ActionDef def = plugin.Actions[i];
                    body.AppendLine("  · " + def.Category + " · " + def.Name + "    [" + def.Id + "]");
                }
            }
            else
            {
                body.AppendLine();
                body.AppendLine("加载失败：");
                body.AppendLine(plugin.Error);
            }

            string subtitle = plugin.IsLoaded
                ? "动作已经可以在快捷键编辑器的“执行动作”中选择。"
                : "该插件当前不可用，原因见下方。";

            using (TextReportForm dialog = new TextReportForm(
                plugin.Name + "  ·  插件详情", subtitle, body.ToString()))
            {
                dialog.ShowDialog(this);
            }
        }
    }
}
