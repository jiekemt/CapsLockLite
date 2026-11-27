using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapsLockLite
{
    /// <summary>
    /// Macro page: a list of user macros plus manual run/stop, import and export.
    /// Macros are ordinary actions under "macro.&lt;id&gt;", so the same rows can be
    /// bound on the shortcuts page or nested inside another macro.
    /// </summary>
    internal sealed partial class MainForm
    {
        private MNavItem _navMacros;
        private Panel _pageMacros;
        private MSearchBox _macroSearch;
        private MButton _macroNewButton;
        private MButton _macroImportButton;
        private MButton _macroRunButton;
        private MButton _macroStopButton;
        private MIconButton _macroMoreButton;
        private MCard _macroListCard;
        private MListView _macroList;
        private Label _macroCountLabel;
        private Label _macroHintLabel;
        private MacroRunner _macroRunner;

        private const string MacroHintFull = "Ctrl/Shift 多选 · 拖动排序 · 双击编辑 · 右键更多";
        private const string MacroHintMedium = "拖动排序 · 双击编辑 · 右键更多";
        private const string MacroHintShort = "双击编辑 · 右键更多";

        private Panel BuildMacrosPage()
        {
            Panel page = new Panel();
            page.BackColor = Theme.Background;
            page.Visible = false;
            page.Tag = "macros";

            _macroSearch = new MSearchBox();
            _macroSearch.CueText = "搜索宏、备注或步骤里的动作…";
            _macroSearch.SearchTextChanged += delegate { RefreshMacros(); };
            page.Controls.Add(_macroSearch);

            _macroNewButton = MakeMacroButton("新建宏", MButtonKind.Primary, delegate { NewMacro(); });
            _macroImportButton = MakeMacroButton("导入…", MButtonKind.Secondary, delegate { ImportMacros(); });
            _macroRunButton = MakeMacroButton("运行", MButtonKind.Primary, delegate { RunSelectedMacro(); });
            _macroStopButton = MakeMacroButton("结束", MButtonKind.Secondary, delegate { StopMacro(); });
            page.Controls.Add(_macroNewButton);
            page.Controls.Add(_macroImportButton);
            page.Controls.Add(_macroRunButton);
            page.Controls.Add(_macroStopButton);

            _macroMoreButton = new MIconButton();
            _macroMoreButton.Glyph = "\uE712";
            _macroMoreButton.Click += delegate { ShowMacroMoreMenu(); };
            page.Controls.Add(_macroMoreButton);

            _macroListCard = new MCard();
            _macroListCard.Padding = new Padding(1);
            // Square corners: the list is full-bleed, so a rounded card would show a
            // mismatched corner ring at all four corners (same as the shortcut list).
            _macroListCard.Radius = 0;
            page.Controls.Add(_macroListCard);

            _macroList = new MListView();
            _macroList.RowHeight = Theme.S(58);
            _macroList.HeaderHeight = Theme.S(38);
            _macroList.MultiSelect = true;
            _macroList.AllowReorder = true;
            _macroList.EmptyText = "还没有宏动作\r\n点“新建宏”，把多个动作编写成脚本";
            _macroList.HeaderPainter = delegate(Graphics g, Rectangle r)
            {
                Theme.Prepare(g);
                using (SolidBrush b = new SolidBrush(Theme.Surface))
                    g.FillRectangle(b, r);
                TextRenderer.DrawText(g, "宏名称", Theme.CaptionStrong,
                    new Rectangle(Theme.S(18), r.Y, Theme.S(200), r.Height), Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "动作数", Theme.CaptionStrong,
                    new Rectangle(r.Right - Theme.S(208), r.Y, Theme.S(58), r.Height), Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "使用", Theme.CaptionStrong,
                    new Rectangle(r.Right - Theme.S(150), r.Y, Theme.S(134), r.Height), Theme.TextSecondary,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            };
            _macroList.SelectionChanged += delegate { UpdateMacroButtons(); UpdateMacroCount(); };
            _macroList.ItemActivated += delegate { EditSelectedMacro(); };
            _macroList.ItemContextRequested += delegate { ShowMacroMenu(); };
            _macroList.EmptyContextRequested += delegate { ShowEmptyMacroMenu(); };
            _macroList.ItemDeleteRequested += delegate { DeleteSelectedMacros(); };
            _macroList.ItemsReordered += delegate(object sender, MListReorderEventArgs e) { ReorderMacros(e); };
            _macroListCard.Controls.Add(_macroList);

            _macroCountLabel = Theme.MakeLabel("共 0 个宏", Theme.Caption, Theme.TextTertiary,
                0, 0, Theme.S(240), Theme.S(18));
            page.Controls.Add(_macroCountLabel);

            _macroHintLabel = Theme.MakeLabel(MacroHintFull, Theme.Caption, Theme.TextTertiary,
                0, 0, Theme.S(380), Theme.S(18));
            _macroHintLabel.TextAlign = ContentAlignment.MiddleRight;
            _macroHintLabel.AutoEllipsis = true;
            page.Controls.Add(_macroHintLabel);

            return page;
        }

        private MButton MakeMacroButton(string text, MButtonKind kind, EventHandler click)
        {
            MButton button = new MButton();
            button.Text = text;
            button.Kind = kind;
            button.Font = Theme.BodyStrong;
            button.Click += click;
            return button;
        }

        private void LayoutMacrosPage()
        {
            if (_pageMacros == null || _macroList == null) return;
            int w = _pageMacros.ClientSize.Width;
            int h = _pageMacros.ClientSize.Height;
            if (w <= 0 || h <= 0) return;

            int ctrl = Theme.S(Theme.CtrlHeight);
            int gap = Theme.S(8);
            int right = w;

            _macroStopButton.SetBounds(right - Theme.S(72), 0, Theme.S(72), ctrl);
            right -= Theme.S(72) + gap;
            _macroRunButton.SetBounds(right - Theme.S(72), 0, Theme.S(72), ctrl);
            right -= Theme.S(72) + gap;
            _macroMoreButton.SetBounds(right - ctrl, 0, ctrl, ctrl);
            right -= ctrl + gap;

            _macroNewButton.SetBounds(0, 0, Theme.S(96), ctrl);
            int importX = Theme.S(96) + gap;
            _macroImportButton.SetBounds(importX, 0, Theme.S(80), ctrl);
            int searchLeft = importX + Theme.S(80) + Theme.S(12);
            _macroSearch.SetBounds(searchLeft, 0,
                Math.Max(Theme.S(120), right - gap - searchLeft), ctrl);

            int footer = Theme.S(22);
            int cardTop = ctrl + Theme.S(12);
            _macroListCard.SetBounds(0, cardTop, w,
                Math.Max(Theme.S(80), h - cardTop - footer - Theme.S(8)));
            _macroList.SetBounds(1, 1,
                Math.Max(1, _macroListCard.ClientSize.Width - 2),
                Math.Max(1, _macroListCard.ClientSize.Height - 2));

            _macroCountLabel.SetBounds(0, h - footer, Math.Max(Theme.S(80), w / 2), footer);
            int hintWidth = Math.Max(Theme.S(80), w - w / 2);
            _macroHintLabel.SetBounds(w / 2, h - footer, hintWidth, footer);
            if (Theme.Measure(MacroHintFull, Theme.Caption).Width <= hintWidth)
                _macroHintLabel.Text = MacroHintFull;
            else if (Theme.Measure(MacroHintMedium, Theme.Caption).Width <= hintWidth)
                _macroHintLabel.Text = MacroHintMedium;
            else
                _macroHintLabel.Text = MacroHintShort;
        }

        /// <summary>Rebuilds the rows, keeping the selection by macro id.</summary>
        private void RefreshMacros()
        {
            if (_macroList == null) return;
            MacroLibrary.RefreshCatalog(_settings.Macros);

            string query = _macroSearch == null ? "" : (_macroSearch.Text ?? "").Trim();
            List<string> keep = new List<string>();
            string leadId = null;
            foreach (MListItem item in _macroList.SelectedItems)
            {
                MacroListItem row = item as MacroListItem;
                if (row != null && row.Macro != null) keep.Add(row.Macro.Id);
            }
            MacroListItem current = _macroList.SelectedItem as MacroListItem;
            if (current != null && current.Macro != null) leadId = current.Macro.Id;

            List<MListItem> rows = new List<MListItem>();
            List<int> restore = new List<int>();
            int lead = -1;
            for (int i = 0; i < _settings.Macros.Count; i++)
            {
                Macro macro = _settings.Macros[i];
                if (macro == null) continue;
                if (query.Length > 0 && !MacroMatches(macro, query)) continue;
                bool running = _macroRunner != null && _macroRunner.IsRunning
                    && string.Equals(_macroRunner.RunningName, macro.Name, StringComparison.CurrentCulture);
                rows.Add(new MacroListItem(macro, BoundCountFor(macro), running));
                if (leadId != null && leadId == macro.Id) lead = rows.Count - 1;
                if (leadId != null && keep.Contains(macro.Id)) restore.Add(rows.Count - 1);
            }

            _macroList.SetItems(rows);
            if (rows.Count > 0)
            {
                if (lead < 0) lead = 0;
                if (restore.Count == 0) restore.Add(lead);
                _macroList.ApplySelection(restore, lead);
            }
            UpdateMacroCount();
            UpdateMacroButtons();
        }

        private static bool MacroMatches(Macro macro, string query)
        {
            if (macro.Name != null && macro.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)
                return true;
            if (macro.Note != null && macro.Note.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)
                return true;
            for (int i = 0; i < macro.Steps.Count; i++)
            {
                string text = ActionCatalog.Describe(macro.Steps[i].Action, macro.Steps[i].Param);
                if (text != null && text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private Macro SelectedMacro
        {
            get
            {
                if (_macroList == null) return null;
                MacroListItem row = _macroList.SelectedItem as MacroListItem;
                return row == null ? null : row.Macro;
            }
        }

        private List<Macro> SelectedMacros
        {
            get
            {
                List<Macro> list = new List<Macro>();
                if (_macroList == null) return list;
                foreach (MListItem item in _macroList.SelectedItems)
                {
                    MacroListItem row = item as MacroListItem;
                    if (row != null && row.Macro != null) list.Add(row.Macro);
                }
                return list;
            }
        }

        private void UpdateMacroCount()
        {
            if (_macroCountLabel == null) return;
            int total = _settings.Macros.Count;
            int selected = _macroList == null ? 0 : _macroList.SelectedCount;
            _macroCountLabel.Text = selected > 1
                ? "已选 " + selected + " 条 · 共 " + total + " 个宏"
                : "共 " + total + " 个宏";
        }

        private void UpdateMacroButtons()
        {
            if (_macroRunButton == null) return;
            bool running = _macroRunner != null && _macroRunner.IsRunning;
            int selected = _macroList == null ? 0 : _macroList.SelectedCount;
            _macroRunButton.Enabled = selected == 1 && !running;
            _macroStopButton.Enabled = running;
            if (_macroStopButton.Enabled && _macroList != null) _macroList.Invalidate();
        }

        private void OnMacroStateChanged()
        {
            // Fired from the input thread (where the runner lives).
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(OnMacroStateChanged)); }
                catch (InvalidOperationException) { }
                return;
            }
            if (_currentPage == "macros") RefreshMacros();
            else UpdateMacroButtons();
        }

        private void NewMacro()
        {
            Macro draft = new Macro();
            draft.Name = MacroLibrary.UniqueName(_settings.Macros, "新建宏");
            MacroEditorForm editor = new MacroEditorForm(draft, _settings.Macros, true);
            if (editor.ShowDialog(this) != DialogResult.OK || editor.Result == null) return;
            Macro created = editor.Result;
            created.Id = Guid.NewGuid().ToString("N");
            _settings.Macros.Add(created);
            MacroLibrary.RefreshCatalog(_settings.Macros);
            SaveAndApply();
            RefreshMacros();
            ShowStatus("已创建宏“" + created.Name + "”，可在动作里选择它。", MInfoSeverity.Success);
            SelectMacro(created.Id);
        }

        private void EditSelectedMacro()
        {
            Macro macro = SelectedMacro;
            if (macro == null) return;
            if (_macroList.SelectedCount > 1)
            {
                ShowStatus("已选中 " + _macroList.SelectedCount + " 条；编辑一次只处理一条", MInfoSeverity.Info);
                return;
            }
            MacroEditorForm editor = new MacroEditorForm(macro, _settings.Macros);
            if (editor.ShowDialog(this) != DialogResult.OK || editor.Result == null) return;
            Macro edited = editor.Result;
            macro.Name = edited.Name;
            macro.Note = edited.Note;
            macro.Steps = edited.Steps;
            // The editor owns the whole macro; copying only the visible fields used to
            // drop the restore-cursor option when an existing macro was edited.
            macro.RestoreCursor = edited.RestoreCursor;
            MacroLibrary.RefreshCatalog(_settings.Macros);
            SaveAndApply();
            RefreshMacros();
            RefreshList();
            ShowStatus("已保存宏“" + macro.Name + "”", MInfoSeverity.Success);
            SelectMacro(macro.Id);
        }

        private void SelectMacro(string id)
        {
            if (_macroList == null) return;
            for (int i = 0; i < _macroList.Items.Count; i++)
            {
                MacroListItem row = _macroList.Items[i] as MacroListItem;
                if (row != null && row.Macro != null && row.Macro.Id == id)
                {
                    _macroList.SelectedIndex = i;
                    return;
                }
            }
        }

        private void RunSelectedMacro()
        {
            Macro macro = SelectedMacro;
            if (macro == null) return;
            if (_macroRunner == null)
            {
                ShowStatus("宏引擎尚未就绪", MInfoSeverity.Error);
                return;
            }
            if (_macroList.SelectedCount > 1)
            {
                ShowStatus("请先选中一个宏再运行", MInfoSeverity.Info);
                return;
            }
            // One macro at a time: running a second one would either stop the first
            // halfway or stack two runs; ask the user to end the current one instead.
            if (_macroRunner.IsRunning)
            {
                ShowStatus("宏“" + (_macroRunner.RunningName ?? "当前宏") + "”正在运行，先结束当前宏再运行下一个宏。", MInfoSeverity.Info);
                return;
            }
            // The runner lives on the input thread: start it there and report back.
            MacroRunner runner = _macroRunner;
            string actionId = macro.ActionId;
            string name = macro.Name;
            RunOnInput(delegate
            {
                string error;
                if (!runner.Start(actionId, out error))
                {
                    string failure = error;
                    RunOnUi(delegate { ShowStatus(failure, MInfoSeverity.Error); });
                    return;
                }
                RunOnUi(delegate
                {
                    ShowStatus("正在运行宏“" + name + "”", MInfoSeverity.Info);
                    RefreshMacros();
                });
            });
        }

        private void StopMacro()
        {
            MacroRunner runner = _macroRunner;
            if (runner == null || !runner.IsRunning) return;
            string name = runner.RunningName;
            RunOnInput(delegate { runner.Stop(); });
            ShowStatus("已结束宏“" + (name ?? "当前宏") + "”", MInfoSeverity.Info);
            RefreshMacros();
        }

        private void DeleteSelectedMacros()
        {
            List<Macro> picked = SelectedMacros;
            if (picked.Count == 0) return;
            int bound = 0;
            for (int i = 0; i < picked.Count; i++) bound += BoundCountFor(picked[i]);
            string message = picked.Count == 1
                ? "确定要删除宏“" + picked[0].Name + "”吗？"
                : "确定要删除选中的 " + picked.Count + " 个宏吗？";
            if (bound > 0)
                message += "\r\n\r\n有 " + bound + " 个快捷键绑定了这些宏，删除后它们会显示为“宏动作不存在”。";
            if (!MDialog.Confirm(this, "删除宏", message, "删除", "取消", MDialogKind.Warning)) return;
            for (int i = 0; i < picked.Count; i++) _settings.Macros.Remove(picked[i]);
            MacroLibrary.RefreshCatalog(_settings.Macros);
            SaveAndApply();
            RefreshMacros();
            RefreshList();
            ShowStatus(picked.Count == 1 ? "已删除宏" : "已删除 " + picked.Count + " 个宏", MInfoSeverity.Info);
        }

        private void DuplicateSelectedMacros()
        {
            List<Macro> picked = SelectedMacros;
            if (picked.Count == 0) return;
            string lastId = null;
            for (int i = 0; i < picked.Count; i++)
            {
                Macro copy = picked[i].Clone();
                copy.Id = Guid.NewGuid().ToString("N");
                copy.Name = MacroLibrary.UniqueName(_settings.Macros, picked[i].Name + " 副本");
                _settings.Macros.Add(copy);
                lastId = copy.Id;
            }
            MacroLibrary.RefreshCatalog(_settings.Macros);
            SaveAndApply();
            RefreshMacros();
            if (lastId != null) SelectMacro(lastId);
            ShowStatus("已创建 " + picked.Count + " 个副本", MInfoSeverity.Success);
        }

        private void ExportMacros(bool all)
        {
            List<Macro> list = all ? new List<Macro>(_settings.Macros) : SelectedMacros;
            list.RemoveAll(delegate(Macro m) { return m == null; });
            if (list.Count == 0)
            {
                ShowStatus(all ? "还没有可导出的宏" : "请先选中要导出的宏", MInfoSeverity.Info);
                return;
            }
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Title = "导出宏";
            dialog.Filter = MacroFileStore.Filter;
            dialog.DefaultExt = MacroFileStore.Extension;
            dialog.FileName = list.Count == 1
                ? SafeFileName(list[0].Name) + MacroFileStore.Extension
                : "宏动作" + MacroFileStore.Extension;
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                MacroFileStore.Save(dialog.FileName, list);
                ShowStatus("已导出 " + list.Count + " 个宏到 " + System.IO.Path.GetFileName(dialog.FileName),
                    MInfoSeverity.Success);
            }
            catch (Exception ex)
            {
                MDialog.Tell(this, "导出失败", ex.Message, MDialogKind.Error);
            }
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "宏动作";
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            string result = name;
            for (int i = 0; i < invalid.Length; i++) result = result.Replace(invalid[i], '_');
            return result.Length > 60 ? result.Substring(0, 60) : result;
        }

        private void ImportMacros()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "导入宏";
            dialog.Filter = MacroFileStore.Filter;
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            List<Macro> loaded;
            try
            {
                loaded = MacroFileStore.Load(dialog.FileName);
            }
            catch (Exception ex)
            {
                MDialog.Tell(this, "导入失败", "这个文件不是有效的宏文件。\r\n\r\n" + ex.Message, MDialogKind.Error);
                return;
            }
            if (loaded.Count == 0)
            {
                MDialog.Tell(this, "没有宏", "文件里没有任何宏动作。", MDialogKind.Info);
                return;
            }
            if (!MDialog.Confirm(this, "导入宏",
                "将导入 " + loaded.Count + " 个宏，并自动重命名以避免与现有宏重名。",
                "导入", "取消", MDialogKind.Info))
                return;

            string lastId = null;
            for (int i = 0; i < loaded.Count; i++)
            {
                Macro macro = loaded[i];
                macro.Id = Guid.NewGuid().ToString("N");
                macro.Name = MacroLibrary.UniqueName(_settings.Macros, macro.Name);
                macro.ImportedFrom = System.IO.Path.GetFileName(dialog.FileName);
                _settings.Macros.Add(macro);
                lastId = macro.Id;
            }
            MacroLibrary.RefreshCatalog(_settings.Macros);
            SaveAndApply();
            RefreshMacros();
            if (lastId != null) SelectMacro(lastId);
            ShowStatus("已导入 " + loaded.Count + " 个宏", MInfoSeverity.Success);
        }

        private void ReorderMacros(MListReorderEventArgs e)
        {
            // The list only shows filtered rows, so rebuild the settings order
            // from the visible order while keeping hidden macros in place.
            List<Macro> visible = new List<Macro>();
            for (int i = 0; i < _macroList.Items.Count; i++)
            {
                MacroListItem row = _macroList.Items[i] as MacroListItem;
                if (row != null && row.Macro != null) visible.Add(row.Macro);
            }
            List<Macro> next = new List<Macro>(_settings.Macros.Count);
            int cursor = 0;
            for (int i = 0; i < _settings.Macros.Count; i++)
            {
                Macro macro = _settings.Macros[i];
                if (macro == null) continue;
                if (visible.Contains(macro)) next.Add(visible[cursor++]);
                else next.Add(macro);
            }
            _settings.Macros = next;
            SaveQuiet();
            RefreshMacros();
            ShowStatus("已调整 " + e.Count + " 个宏的顺序", MInfoSeverity.Success);
        }

        private void ShowMacroMenu()
        {
            List<Macro> picked = SelectedMacros;
            if (picked.Count == 0) return;
            MPopupMenu menu = new MPopupMenu();
            bool single = picked.Count == 1;
            bool running = _macroRunner != null && _macroRunner.IsRunning;
            menu.AddItem("编辑…", "Enter", delegate { EditSelectedMacro(); }, single);
            menu.AddItem("运行", "Ctrl+R", delegate { RunSelectedMacro(); }, single && !running);
            menu.AddSeparator();
            menu.AddItem("复制副本", "Ctrl+D", delegate { DuplicateSelectedMacros(); });
            menu.AddItem(single ? "导出…" : "导出选中 (" + picked.Count + ")…", null,
                delegate { ExportMacros(false); });
            menu.AddItem(single ? "删除…" : "删除选中 (" + picked.Count + ")…", "Del",
                delegate { DeleteSelectedMacros(); });
            menu.AddSeparator();
            menu.AddItem("全选", "Ctrl+A", delegate { _macroList.SelectAll(); }, _macroList.Items.Count > 0);
            //menu.AddItem("新建宏", "Ctrl+N", delegate { NewMacro(); });
            menu.ShowAt(Cursor.Position);
        }

        private void ShowEmptyMacroMenu()
        {
            MPopupMenu menu = new MPopupMenu();
            menu.AddItem("新建宏", "Ctrl+N", delegate { NewMacro(); });
            menu.AddItem("导入宏…", null, delegate { ImportMacros(); });
            menu.AddItem("导出全部…", null, delegate { ExportMacros(true); }, _settings.Macros.Count > 0);
            menu.AddSeparator();
            menu.AddItem("全选", "Ctrl+A", delegate { _macroList.SelectAll(); }, _macroList.Items.Count > 0);
            menu.AddItem("刷新列表", "F5",
                delegate { RefreshMacros(); ShowStatus("列表已刷新", MInfoSeverity.Info); });
            menu.ShowAt(Cursor.Position);
        }

        private void ShowMacroMoreMenu()
        {
            MPopupMenu menu = new MPopupMenu();
            menu.AddItem("新建宏", "Ctrl+N", delegate { NewMacro(); });
            menu.AddItem("导入宏…", null, delegate { ImportMacros(); });
            menu.AddItem("导出全部…", null, delegate { ExportMacros(true); }, _settings.Macros.Count > 0);
            menu.AddSeparator();
            menu.AddItem("运行选中", "Ctrl+R", delegate { RunSelectedMacro(); }, SelectedMacro != null);
            menu.AddItem("结束运行", null, delegate { StopMacro(); },
                _macroRunner != null && _macroRunner.IsRunning);
            menu.AddSeparator();
            menu.AddItem("刷新列表", "F5", delegate { RefreshMacros(); });
            // Open below the button after the click message finishes; showing a
            // popup at Cursor.Position from inside OnMouseUp made the first click
            // land back on the menu and dismiss it (users saw "needs double click").
            BeginInvoke(new Action(delegate { menu.ShowBelow(_macroMoreButton, true); }));
        }

        /// <summary>
        /// Called by the executor, so this already runs on the input thread: the runner
        /// is started here, and only the UI feedback is marshalled back.
        /// </summary>
        private bool StartMacroFromAction(string actionId)
        {
            MacroRunner runner = _macroRunner;
            if (runner == null) return false;
            // Already running one: ignore the new trigger instead of stopping the
            // current run or stacking a second one on top of it.
            if (runner.IsRunning)
            {
                string busy = runner.RunningName ?? "当前宏";
                RunOnUi(delegate
                {
                    ShowStatus("宏“" + busy + "”正在运行，结束后才能触发下一个宏。", MInfoSeverity.Info);
                });
                return false;
            }
            string error;
            if (runner.Start(actionId, out error))
            {
                string started = runner.RunningName;
                RunOnUi(delegate
                {
                    if (_currentPage == "macros") RefreshMacros();
                    else ShowStatus("正在运行宏“" + started + "”", MInfoSeverity.Info);
                });
                return true;
            }
            string failure = error;
            RunOnUi(delegate { ShowStatus(failure, MInfoSeverity.Error); });
            return false;
        }
    }
}
