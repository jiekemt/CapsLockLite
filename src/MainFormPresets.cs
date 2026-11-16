using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CapsLockLite
{
    // Shortcut presets: the global list (settings.Mappings) plus one list per
    // bound program. This part owns the page-level selector, the preset actions
    // and the "which list am I editing" answer that every other shortcut method
    // asks for through MappingList.
    internal sealed partial class MainForm
    {
        private const string PresetGlobalName = "全局（所有程序）";

        private ShortcutPreset _activePreset;
        private MButton _presetButton;
        private MButton _presetNewButton;
        private Label _presetHint;
        private Timer _presetWatch;

        /// <summary>
        /// The list edits apply to: the preset the user is looking at, or the
        /// global list when 全局 is selected. The engine picks its own list per
        /// foreground program; this property is only about the editor.
        /// </summary>
        private List<KeyMapping> MappingList
        {
            get
            {
                if (_activePreset == null) return _settings.Mappings;
                if (_activePreset.Mappings == null) _activePreset.Mappings = new List<KeyMapping>();
                return _activePreset.Mappings;
            }
        }

        private void ReplaceMappingList(List<KeyMapping> replacement)
        {
            List<KeyMapping> target = MappingList;
            target.Clear();
            if (replacement != null) target.AddRange(replacement);
        }

        /// <summary>Bound count across the global list and every preset.</summary>
        private int BoundCountFor(Macro macro)
        {
            int bound = 0;
            List<List<KeyMapping>> lists = ShortcutSets.All(_settings);
            for (int i = 0; i < lists.Count; i++) bound += MacroLibrary.BoundCount(lists[i], macro);
            return bound;
        }

        // ---- preset bar ------------------------------------------------------
        // Hosted by the content header (top right) so the page body starts with
        // the search row, exactly like every other page.
        private void BuildPresetBar(Control host)
        {
            _presetButton = new MButton();
            _presetButton.Kind = MButtonKind.Secondary;
            _presetButton.Text = "预设：全局";
            _presetButton.Click += delegate { ShowPresetMenu(); };
            host.Controls.Add(_presetButton);

            _presetNewButton = new MButton();
            _presetNewButton.Kind = MButtonKind.Subtle;
            _presetNewButton.Text = "新建预设";
            _presetNewButton.Click += delegate { CreatePreset(); };
            host.Controls.Add(_presetNewButton);

            _presetHint = Theme.MakeLabel("", Theme.Caption, Theme.TextTertiary, 0, 0, Theme.S(300),
                Theme.LineHeight(Theme.Caption));
            _presetHint.AutoEllipsis = true;
            host.Controls.Add(_presetHint);

            // A slow foreground watcher makes the feature visible: the hint tells
            // which preset the app in front would use right now.
            _presetWatch = new Timer();
            _presetWatch.Interval = 1500;
            _presetWatch.Tick += delegate { UpdatePresetHint(); };
            _presetWatch.Start();
            FormClosed += delegate
            {
                if (_presetWatch != null) _presetWatch.Dispose();
            };

            UpdatePresetBar();
        }

        /// <summary>
        /// Lays the selector out in the content header, right aligned: 预设 button,
        /// 新建预设 next to it, foreground hint underneath. The layout gives the
        /// title/subtitle a smaller width on this page (LayoutContent reserves it).
        /// </summary>
        private void LayoutPresetBar(int width, int pad)
        {
            if (_presetButton == null || _presetButton.IsDisposed) return;
            int ctrl = Theme.S(Theme.CtrlHeight);
            int buttonWidth = Theme.S(200);
            int newWidth = Theme.S(88);
            int right = Math.Max(pad, width - pad);
            int rowY = Theme.S(26);
            _presetNewButton.SetBounds(right - newWidth, rowY, newWidth, ctrl);
            _presetButton.SetBounds(right - newWidth - Theme.S(8) - buttonWidth, rowY, buttonWidth, ctrl);
            int hintWidth = Theme.S(300);
            // Never hand a label a hard-coded height: anything shorter than the
            // measured line clips the descenders of g/p/q/y (CapsLockLite.exe).
            _presetHint.SetBounds(right - hintWidth, rowY + ctrl + Theme.S(6), hintWidth,
                Theme.LineHeight(Theme.Caption));
            _presetHint.TextAlign = ContentAlignment.MiddleRight;
            UpdatePresetBar();
        }

        /// <summary>The selector is a shortcuts-page control; other pages reclaim the space.</summary>
        private void UpdatePresetBarVisibility()
        {
            bool visible = _currentPage == "keys";
            if (_presetButton != null) _presetButton.Visible = visible;
            if (_presetNewButton != null) _presetNewButton.Visible = visible;
            if (_presetHint != null) _presetHint.Visible = visible;
        }

        private void UpdatePresetBar()
        {
            if (_presetButton == null) return;
            _presetButton.Text = _activePreset == null
                ? "预设：全局"
                : "预设：" + (_activePreset.Name.Length > 0 ? _activePreset.Name : "未命名");
            UpdatePresetHint();
        }

        private void UpdatePresetHint()
        {
            if (_presetHint == null || _presetButton == null || _presetButton.IsDisposed) return;
            string foreground = ForegroundApp.CurrentProcessName();
            string text;
            if (foreground.Length == 0)
            {
                text = _activePreset != null && !_activePreset.Bound
                    ? "这个预设还没绑定程序"
                    : "识别前台程序，自动切换到对应预设";
            }
            else
            {
                ShortcutPreset match = ShortcutSets.FindByProcess(_settings, foreground);
                text = "前台 " + foreground + ".exe · 使用" + (match == null ? "全局快捷键" : "预设「" + match.Name + "」");
            }
            if (_presetHint.Text != text) _presetHint.Text = text;
        }

        // ---- preset menu / actions ------------------------------------------
        private void ShowPresetMenu()
        {
            List<ShortcutPreset> presets = _settings.Presets;
            if (presets == null) return;

            MPopupMenu menu = new MPopupMenu();
            menu.AddItem(PresetGlobalName, "默认", _activePreset == null,
                delegate { SelectPreset(null); });
            if (presets.Count > 0) menu.AddSeparator();
            for (int i = 0; i < presets.Count; i++)
            {
                ShortcutPreset preset = presets[i];
                if (preset == null) continue;
                ShortcutPreset captured = preset;
                string name = preset.Name.Length > 0 ? preset.Name : "未命名预设";
                if (!preset.Bound) name += "（未绑定程序）";
                menu.AddItem(name, preset.Bound ? preset.Process + ".exe" : "",
                    ReferenceEquals(preset, _activePreset), delegate { SelectPreset(captured); });
            }

            menu.AddSeparator();
            //menu.AddItem("新建预设（复制全局快捷键）", "", delegate { CreatePreset(); });
            if (_activePreset != null)
            {
                menu.AddItem("绑定 / 更换程序…", "", delegate { BindPreset(_activePreset); });
                menu.AddItem("删除预设", "", delegate { DeletePreset(_activePreset); });
            }
            menu.FormClosed += delegate
            {
                // The selector is a menu button, not a mode switch: hand focus back
                // to the list so no outline lingers on the header button.
                if (!IsDisposed && _list != null && !_list.IsDisposed && _currentPage == "keys")
                    _list.Focus();
            };
            menu.ShowBelow(_presetButton, false);
        }

        private void SelectPreset(ShortcutPreset preset)
        {
            if (ReferenceEquals(preset, _activePreset))
            {
                UpdatePresetBar();
                return;
            }
            _activePreset = preset;
            // Undo snapshots point at the previous preset's rows; keeping them
            // would let 撤回 restore rows the user is not looking at.
            ClearMappingHistory();
            _selectedKey = Keys.None;
            RefreshList();
            RefreshKeyboard();
            UpdatePresetBar();
            if (preset != null && !preset.Bound)
                ShowStatus("这个预设还没绑定程序，点「预设」菜单里的「绑定 / 更换程序…」", MInfoSeverity.Info);
        }

        private void CreatePreset()
        {
            if (_settings.Presets == null) _settings.Presets = new List<ShortcutPreset>();
            if (_settings.Presets.Count >= 50)
            {
                ShowStatus("预设最多 50 个，请先删除不用的预设", MInfoSeverity.Warning);
                return;
            }

            ShortcutPreset preset = new ShortcutPreset();
            preset.Name = "新预设";
            // Rule: a new preset starts as a copy of the global shortcuts and with
            // no program bound, so the user chooses which app it belongs to.
            for (int i = 0; i < _settings.Mappings.Count; i++)
            {
                KeyMapping mapping = _settings.Mappings[i];
                if (mapping != null) preset.Mappings.Add(mapping.Clone());
            }
            _settings.Presets.Add(preset);
            _activePreset = preset;
            ClearMappingHistory();
            _selectedKey = Keys.None;

            using (ProgramPickerForm picker = new ProgramPickerForm(_settings, preset.Id))
            {
                if (picker.ShowDialog(this) == DialogResult.OK && picker.ProcessName.Length > 0)
                {
                    preset.Process = picker.ProcessName;
                    preset.Name = picker.ProcessName;
                }
            }
            SaveAndApply();
            RefreshList();
            RefreshKeyboard();
            UpdatePresetBar();
            if (preset.Bound)
                ShowStatus("已新建预设「" + preset.Name + "」，快捷键复制自全局；打开该程序时自动切换",
                    MInfoSeverity.Success);
            else
                ShowStatus("已新建预设，快捷键复制自全局；还没有绑定程序，可随时在预设菜单里绑定",
                    MInfoSeverity.Info);
        }

        private void BindPreset(ShortcutPreset preset)
        {
            if (preset == null) return;
            using (ProgramPickerForm picker = new ProgramPickerForm(_settings, preset.Id))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                ApplyPresetProcess(preset, picker.ProcessName);
            }
        }

        private void ApplyPresetProcess(ShortcutPreset preset, string process)
        {
            if (preset == null) return;
            process = ConfigStore.NormalizeProcessName(process);
            if (process.Length == 0) return;

            ShortcutPreset owner = ShortcutSets.FindByProcess(_settings, process);
            if (owner != null && !ReferenceEquals(owner, preset))
            {
                string ask = "「" + process + ".exe」已经绑定在预设「" + owner.Name + "」上。\r\n\r\n"
                    + "要把绑定改到「" + preset.Name + "」吗？原预设的快捷键会保留，只是不再绑定这个程序。";
                if (!MDialog.Confirm(this, "更换绑定程序", ask, "改绑", "取消")) return;
                owner.Process = "";
            }

            bool defaultName = preset.Process.Length == 0
                || string.Equals(preset.Name, preset.Process, StringComparison.OrdinalIgnoreCase)
                || preset.Name == "新预设" || preset.Name == "未命名预设";
            string previous = preset.Process;
            preset.Process = process;
            if (defaultName) preset.Name = process;

            SaveAndApply();
            RefreshList();
            UpdatePresetBar();
            ShowStatus((previous.Length > 0 ? "已更换绑定：" : "已绑定：") + process + ".exe",
                MInfoSeverity.Success);
        }

        private void DeletePreset(ShortcutPreset preset)
        {
            if (preset == null || _settings.Presets == null) return;
            string name = preset.Name.Length > 0 ? preset.Name : "未命名预设";
            string message = "确定要删除预设「" + name + "」吗？\r\n\r\n"
                + "它的 " + preset.Mappings.Count + " 条快捷键会一起删除，全局快捷键不受影响。";
            if (!MDialog.Confirm(this, "删除预设", message, "删除", "取消", MDialogKind.Warning)) return;
            _settings.Presets.Remove(preset);
            if (ReferenceEquals(preset, _activePreset))
            {
                _activePreset = null;
                ClearMappingHistory();
            }
            SaveAndApply();
            RefreshList();
            RefreshKeyboard();
            UpdatePresetBar();
            ShowStatus("已删除预设「" + name + "」", MInfoSeverity.Info);
        }

        /// <summary>
        /// Import replaces the whole settings object, presets included. The
        /// selector has to fall back to 全局 or it would keep pointing at a preset
        /// object that is no longer in the new configuration.
        /// </summary>
        private void ResetPresetState()
        {
            _activePreset = null;
            if (_presetButton != null) UpdatePresetBar();
        }
    }
}
