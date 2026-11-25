# CapsLock Lite 源码说明

版本：1.25.2  
目标框架：.NET Framework 4.8  
界面：Windows Forms（全部自绘控件）  
构建方式：系统自带 `csc.exe`，不依赖 Visual Studio / NuGet

## 构建

在 Windows 10/11 上运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

输出：

```text
dist\CapsLockLite.exe
```

`build.ps1` 会优先使用 `CSC` 环境变量指定的编译器；未设置时自动查找：

- `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`
- `%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe`

如果系统没有 .NET Framework 4.8，请先安装运行库/开发包。

## 自检

```powershell
.\dist\CapsLockLite.exe --selftest
```

结果写入 `dist\selftest.log`。自检覆盖默认映射、配置导入导出、组合键解析、Caps 层状态机、重复组合检测、插件编译与注册、压缩包导入与识别、键盘钩子安装。

## 目录

```text
src/                   C# 源码
assets/                图标、Logo 与图标生成脚本
samples/plugins/       示例插件（截图）
app.manifest           DPI / Windows 版本清单
build.ps1              构建脚本
README.md              用户文档
PLUGIN.md              插件开发指南（对外契约）
LICENSE                MIT License
```

主要文件：

| 文件 | 作用 |
| --- | --- |
| `Program.cs` | 入口、单实例、DPI、异常处理 |
| `AppInfo.cs` | 用户可见版本号的唯一来源（`AppInfo.Version`） |
| `MainForm.cs` | 主界面、托盘、设置页、配置导入导出 |
| `MainFormKeyboard.cs` | 快捷键页的键盘区：修饰键筛选、键位选择与拖动搬运、收起动画、右键菜单 |
| `MousePad.cs` | 快捷键页右侧的鼠标卡片：直线多边形外框、五个热区、与键帽同源的状态/计数/右键菜单，以及与键盘之间的双向拖动 |
| `KeyboardPreview.cs` | 键盘布局模型（104 键、键位单位）、筛选规则、键帽文字适配、自绘键盘控件（含拖动浮层）与区块标题 |
| `MPopupMenu.cs` | 自绘弹层菜单（`⋯` 菜单与下拉框共用） |
| `MainFormPlugins.cs` | 插件页：导入、重载、卸载、详情、设置页 |
| `MappingEditorForm.cs` | 快捷键编辑、触发类型、动作参数 |
| `HotkeyManager.cs` | 低层键盘/鼠标钩子、Caps 层状态机、长按连发 |
| `ActionExecutor.cs` | 动作队列与具体动作 |
| `ActionCatalog.cs` | 动作目录（类目顺序、类型判定）、默认可连发能力、默认键位、插件动作注册 |
| `PluginApi.cs` | 插件对外契约：`IPlugin` / `IPluginAction` / `PluginContext` / `ActionParamKind` / 可选设置页接口 |
| `PluginRuntime.cs` | 插件目录、编译、校验、注册与执行 |
| `PluginSettings.cs` | 插件设置页的存储（每插件一个 JSON）与上下文实现 |
| `MScrollPanel.cs` | 插件设置页的滚动容器（自绘滚动条） |
| `Models.cs` | `AppSettings` / `KeyMapping` / `ShortcutPreset` 数据模型 |
| `ConfigStore.cs` | JSON 配置、日志、可写目录探测、字段归一化 |
| `ModernControls.cs` / `Theme.cs` | 自绘控件、浅色/深色主题 |
| `ModernInputs.cs` / `ModernDialog.cs` | 圆角输入框、数字框、多行文本、只读文本视图，以及替代系统提示框的提示/确认对话框 |
| `MouseKeys.cs` | 鼠标触发的伪键码（`0x0E01`–`0x0E05`），超出键盘钩子上报范围，不会与真实按键冲突 |
| `MouseHook.cs` | 低层鼠标钩子（`WH_MOUSE_LL`），只翻译左/右/中键与垂直滚轮，其余事件原样放行 |
| `MousePoint.cs` | 「鼠标点击」参数（按钮 + 虚拟屏幕坐标）、抓取坐标时的窗口移屏与按键确认 |
| `ModernList.cs` / `ListItems.cs` / `ListSelection.cs` | 自绘虚拟列表（多选、拖动排序、行内开关、右键菜单事件）、各页面的行与选择模型 |
| `KeyText.cs` | 键名格式化与解析（`Caps + Alt + E`）；方向键存为 `↑ ↓ ← →`，解析同时认字形与 `UP/DOWN/LEFT/RIGHT` 英文名；小键盘存为 `Num 7`，`TryParseNumpad` 同时认 `Num 7` / `Num7` / `NumPad7` / `小键盘 7` |
| `MappingConflicts.cs` | 重复组合键检测 |
| `MainFormSettings.cs` | 设置页 |
| `MainFormMacros.cs` | 宏动作页：列表、新建/编辑、导入导出、运行与结束 |
| `MainFormHistory.cs` | 快捷键页的撤回/重做快照栈（`Ctrl+Z` / `Ctrl+Y`，键盘与列表共用） |
| `MainFormPresets.cs` | 快捷键页的程序预设：预设选择器（挂在内容区右上角）、切换、新建/复制全局、绑定与解绑、删除、前台程序提示 |
| `ShortcutPresets.cs` | 前台程序识别（`ForegroundApp`）与「当前用哪张快捷键表」的判定（`ShortcutSets`） |
| `ProgramPickerForm.cs` | 绑定程序对话框：运行中的窗口列表 + 搜索 + 浏览 exe |
| `MacroEditorForm.cs` | 宏编辑页与步骤编辑框（含剪切/复制/粘贴、拖动排序、步骤命名、撤回/重做、执行后返回鼠标原位置） |
| `Macros.cs` | 宏文件（`.clmacro`）读写、宏库工具（命名、绑定计数、嵌套校验） |
| `MacroRunner.cs` | 宏执行器：帧栈、每步延迟与次数、嵌套保护 |
| `Models.cs` / `ConfigStore.cs` 中的 `Macro` / `MacroStep` | 宏与步骤的数据模型，以及配置归一化 |
| `ConfigDialogs.cs` / `ActionPickerForm.cs` | 配置文本 / 导入导出对话框、动作选择器（原生·插件·宏筛选） |
| `Native.cs` | Win32 P/Invoke、SendInput、窗口工具 |
| `StartupManager.cs` | 注册表 / 启动文件夹开机启动 |
| `SelfTest.cs` | 无界面自检 |

## 维护要点

### 触发类型

每个 `KeyMapping` 有：

```csharp
public string TriggerMode { get; set; } // "once" 或 "hold"
```

- `once`：按下单发，按住不重复。
- `hold`：按住约 450ms 后每 33ms（每秒 30 发）重复一次，松开主键或 Caps Lock 停止。`input.keys` 走物理长按：`HotkeyManager` 抛 `HoldStarted`/`HoldRepeated`/`HoldEnded`，`ActionExecutor.BeginKeyHold` 只发 keydown（先按 Ctrl/Alt/Shift/Win 修饰，再按主键），`RepeatKeyHold` 只补 keydown，`EndKeyHold` 才发唯一一次 keyup——两条路径共用 `Native.SendKeyState`（单条 SendInput，不带 KEYEVENTF_KEYUP 或带）。其他可连发动作仍走 `ActionTriggered` 重复执行。节奏不能交给 `System.Windows.Forms.Timer` 的 `Interval`：系统时钟粒度约 15.6ms，`Interval=33` 实际会落到 46.8ms（每秒 21 发）。连发阶段用 `timeBeginPeriod(1)` 提高时钟精度，定时器只负责唤醒（`Interval=1`），是否到期由 `Stopwatch` 累加 33ms 判定，退出连发时 `timeEndPeriod(1)` 还原。
- `ActionDef.Repeatable` 表示动作是否具备连发能力；不适用的动作在编辑器里置灰。
- 归一化逻辑在 `ConfigStore.Normalize`：可连发动作缺省为 `hold`，不可连发动作强制为 `once`；滚轮主键没有抬起事件，`NormalizeMappings` 也会把它强制成 `once`，编辑页在 `UpdateModeState` 里同样会置灰“按住连发”。

### 快捷键页的键盘区

键盘区是快捷键页的主体，不再是独立页面：工具栏（搜索 / 新增 / ⋯）→ 键盘区 → 列表。

- 布局在 `KeyboardLayout.Create()` 里用「键位单位」描述（1 = 一个标准键宽，整块键盘 22.5 × 6 单位），控件按自身宽度等比缩放，104 个键的相对位置只有这一份数据；改布局只改这里。
- `MKeyboardView` 只负责绘制与命中：外部通过 `SetMappings` / `SetFilter` / `SetSelectedKey` 喂数据，事件（`SelectionChanged` / `KeyActivated` / `ModifierToggleRequested` / `KeyContextRequested`）回到 `MainForm`。控件里不要直接读 `_settings`。
- 筛选语义是「精确组合」：选中后只有五个布尔值与所选完全一致的映射才命中，所以只选 Caps 不会点亮 `Caps + Alt` 的键，每个键位同一时刻至多一条动作。键盘用 `KeyboardFilter.Exact`，列表用 `KeyboardFilter.Matches`（空选仍显示全部）——两处语义不同是有意的，别合并。
- 五个修饰键就是筛选开关（没有额外的复选框行）：键位命中只看 `KeyMapping.Key & Keys.KeyCode`（`\` 键额外接受 `OemBackslash`，兼容 ISO 布局）。选中的修饰键画斜线遮罩，与「该键位有快捷键」的实色高亮区分，两者不要共用一个画法。点选修饰键同样是一次新的选择：`ToggleKeyboardFilter` 会先把 `_selectedKey` 清空并去掉键帽选中环，再刷新列表，这样「点修饰键」和「点另一个主键」的手感一致。
- 空选不是空白：`Recompute` 同时统计 `_capAllCounts`（该键所有组合的数量，供总览）与 `_capCounts` / `_capEnabled`（当前精确组合的命中数与启用数）。总览给用到的键加一层浅强调底色，所有键（含 Space）统一在右下角画 `DrawCountBadge` **正圆**小徽标写数量（`_unit < 34` 时徽标隐藏，避免压住 1u 键的字母，只保留底色）。这个状态可以点选主键把列表筛到该键；双击不进入编辑，两次点击都执行切换（`ApplyLeftClick` 只在已选修饰键时才保护第二次点击），右键直接返回，编辑仍要先选修饰键。
- 停用（`Enabled == false`）的组合键：`_capDisabled` 为真时画灰底 + `DrawDisabledMask` 的**反向**斜线（修饰键是 `/`，停用是 `\`），动作名也跟着变灰；不要复用 `DrawModifierMask`，也不要用实色强调，否则和「启用」分不开。
- 键盘区可以收起/展开：`KeyboardContentHeight` 算自然高度，`LayoutKeyboard` 按 `_keyboardProgress` 决定可见高度；展开收起由 `_keyboardTimer` 以 15ms 步进、220ms ease-out 驱动，`LayoutContent()` 每帧重排。键盘始终按完整高度放在会被裁剪的 `_keyboardHost` 里——收缩容器而不是压缩控件，键帽才不会被拉扁。头部箭头随进度旋转。
- 键盘只响应鼠标：键位不接受键盘输入，也不抢焦点（`TabStop = false`，`OnMouseDown` 不再 `Focus()`），所以不会和动作编辑器的按键捕获打架；随该功能一起删掉了 `Native.DetachInputMethod`。
- 键位拖动搬运：仅当已选修饰键、且该键在当前组合下有动作时，按下 `ArmDrag`（4px 阈值），超过后 `Capture = true`。卡片位置在 `OnMouseMove` 里直接等于指针（`_dragRender = _dragPoint`，不做插值）——任何平滑都会读成「不跟手」；重绘只失效「旧卡片 ∪ 新卡片 ∪ 源键 ∪ 目标键」的包围盒（`InvalidateDrag`/`DragDirtyBounds`），配合 `ControlStyles.Opaque` 避免整块键盘每帧重画。卡片就是源键的 `TopFace`：尺寸、圆角与键帽完全一致（`DragGhostRect` 直接返回该矩形），位置为「指针 − `_dragGrab`」（grab 以 face 为原点），不按文字宽度放大；键名行不画、但仍占高度，动作名用该键自己的 `KeyActionLayout`（`DrawActionKey(..., hideSymbol: true)`）在原位置、原字号绘制。绘制分三层：源键帽压 `Background` 170 蒙层、目标键 42 透明 `Accent` 填充 + 2px 边框、指针处画带 3px 阴影的卡片。松手要先把 `_dragFrom/_dragTo` 存成局部变量，再 `EndDrag()`，再 `RaiseKeyDrag(from, to)`——`EndDrag` 会清空字段，先读字段只能拿到 -1（表现为「拖了没反应」）。`OnMouseCaptureChanged` 捕获丢失按落位处理，`Esc` 由 `MainForm.ProcessCmdKey` 调 `CancelDrag()`。业务侧 `MoveKeyBindings` 只改 `KeyCode`；目标已有同组合时 `MDialog.Confirm` 确认后覆盖，然后 `SaveAndApply + RefreshList` 并提示搬运条数。
- 键位剪贴板：`MainFormKeyboard` 里的 `_keyClipboard` 保存 `KeyMapping.Clone()`（应用内剪贴板，不碰系统剪贴板）。`Ctrl+X/C/V` 由 `MainForm.ProcessCmdKey` 转到 `CutKeyBindings/CopyKeyBindings/PasteKeyBindings`，仅在快捷键页、已选修饰键与键位、且焦点不在搜索框时生效；右键菜单在「编辑/停用/删除」后追加「剪切 / 复制 / 粘贴到 X」（`AddItem(text, shortcut, click, enabled)`，无内容或空剪贴板时置灰）。粘贴时逐条克隆并重新分配 `Id`、改 `KeyCode`，同时把修饰键改写成**当前筛选的组合**（`Caps = true`，`Ctrl/Alt/Shift/Win` 取 `_filter*`），所以能在 Caps 层复制、切到 Caps + Alt 再粘贴；`!_filterCaps` 时拒绝并提示。目标已有同组合先确认覆盖，然后 `SaveAndApply + RefreshList`，状态栏用落位后的 `TriggerText`。
- `Del` 和剪贴板一样是页面级命令：`MainForm.ProcessCmdKey` 在快捷键页（焦点不在搜索框）处理 `Keys.Delete`，有选中行走 `DeleteSelected()`，否则 `HasBindingTarget` 为真时走 `DeleteKeyBindings()` 删除当前键位上的全部组合并确认。`SwitchPage("keys")` 结尾会把焦点交回 `_list`——早期切页后焦点留在侧栏，`Del`/`Enter`/方向键全都不响应，只能先点一下列表。
- 快捷键页的撤回是**整页快照**：`MainFormHistory.cs`（`partial MainForm`）保存 `List<KeyMapping>` 深拷贝，`PushMappingUndo(label)` 在每个改动入口（新增、编辑、删除、启停、排序、搬运、剪切、粘贴、恢复默认、导入、重载）落栈，`Ctrl+Z` / `Ctrl+Y`（`Ctrl+Shift+Z` 同为重做）由 `ProcessCmdKey` 统一转给 `UndoMappings`/`RedoMappings`，列表与键盘共用一条历史。撤回时把快照克隆回 `MappingList`（当前预设的列表，见下一条）再 `SaveAndApply + RefreshList`，栈上限 50 条；导入、`ReloadFromDisk` 与切换预设会 `ClearMappingHistory()`，避免旧快照把另一张表的行复活。
- 程序预设就是「另一张快捷键表」：`AppSettings.Mappings` 仍是全局表，`Presets` 里每项是一个 `ShortcutPreset { Id, Name, Process, Mappings }`。`ShortcutSets.Active(settings)` 用前台窗口的进程名（`ForegroundApp`：`GetForegroundWindow` + `GetWindowThreadProcessId` + `Process.ProcessName`，按窗口句柄缓存）挑表：命中的预设表替换全局表，未命中或未绑定都回落到全局表；`HotkeyManager.FindCapsMapping` 走 `ShortcutSets.Active`，冲突统计走 `ShortcutSets.All`（全局 + 每个预设），宏的「绑定数」也按全部表累加。页面上的 `_activePreset` 只决定**正在编辑哪张表**（`MappingList`），与自动切换无关。进程名比较必须两侧都过 `ConfigStore.NormalizeProcessName`（去目录、去 `.exe`）且忽略大小写，`code.exe`、`Code.EXE`、完整路径才等价。
- 预设的归一化在 `ConfigStore.NormalizePresetList`：每项补齐 Id、名字裁剪到 60（空名字回落到进程名或「未命名预设」）、`Mappings` 复用 `NormalizeMappings`，同一个程序只允许一个预设——后来者保留快捷键但把 `Process` 清空（「解绑而不是丢表」）。`ConfigVersion` 因此升到 6，但没有旧数据迁移：老配置只是多一个空的 `Presets`。导入/导出整份 `AppSettings`，预设与绑定关系自然随行。
- 绑定程序的入口只有两个：新建预设时立刻弹 `ProgramPickerForm`（复制全局表、`Process` 留空，取消就保留一个未绑定预设），以及预设菜单里的「绑定 / 更换程序…」。对话框列出有主窗口的运行中进程（排除自己），并标注「已绑定「X」」；也接受「浏览…」选 exe。改绑到已被别的预设占用的程序要先确认，确认后旧预设解绑。对话框底部的页脚按钮**不能再用固定 `S(88)`**：宽度逐项 `Math.Max(S(88), Theme.Measure(文字, Theme.BodyStrong).Width + S(30))`，再按 `gap = S(8)` 从右往左排（`okLeft = right - okWidth`、`cancelLeft = okLeft - gap - cancelWidth`、`browseLeft = cancelLeft - gap - browseWidth`）；状态标签宽度由 `browseLeft` 反推（`browseLeft - S(12) - S(24)`，最小 `S(120)`）。固定宽度会让「浏览…」在字体稍宽时被 `EndEllipsis` 裁成残缺笔画，状态标签再按固定 `S(360)` 摆还会盖住按钮文字。`MacroEditorForm` 与 `MappingEditorForm` 里的「浏览…」按钮同样改成按实测宽度摆放。`MButton` 的 `Subtle` 样式现在填父级底色（`ParentSurface()` 向上找第一个不透明背景）而不是留透明：ClearType 画在透明控件上会留下彩色边纹、字形显得比同类细。
- 键帽上的动作名由 `KeyActionText.Layout` 逐键计算（104 个键都算，不是只算已有映射的键）：先用 `KeyLabel.Plan` 得到标签行，再按该键实际宽高从 `BuildLadder` 的像素字号阶梯（0.30u → 0.11u，不低于 6px）里挑最大的能放下整串的字号；整串放不下就用同一字号截断到最宽前缀 + `…`（`Ellipsize`），连 6px 动作行都排不下才整条不画。数字键的标签就是「符号 + 空格 + 数字」一个字符串，交给同一套居中标签绘制，动作名另起一行；多词键名（Page Up）仍可拆两行，必要时缩成一行。`EnsureLayouts` 只在尺寸/数据变化时重跑，绘制只读结果。
- 键帽文字的纵向排版按**实测行高**（`TextRenderer.MeasureText("国Ag", font).Height`）逐行累加，不能用 `Font.Height`（雅黑行框比 em 大 40%，用它排两块文字必然重叠或截断）。标签行与动作行之间不留额外间距：1u 键的总高度只差 1px，留了就会把本来能显示的动作名挤掉。动作名已预量宽度，绘制时不再用 `EndEllipsis`——GDI 对居中文本的省略号会切字。两行式键名（`Page Up`、`Page Down`、`Num Lock`）的行框同样必须等于实测行高：`MKeyboardView.TwoLineRowHeights` 返回两行各自的行高，整块在键面内居中；早期按「可用高度 × 0.42 / 0.58」切分，第一行只有 14px 左右，`g`、`q`、`p`、`y`、`j` 的下伸部分会被行框裁掉（`Page` 的 g 钩子缺一块就是这么来的），而 `TextFormatFlags.Top` 会在行框底部硬裁。
- 双击键位直接进入编辑：`KeyActivated` → `ActivateSelectedKey`，单条组合直接打开 `MappingEditorForm`，多条先弹 `MPopupMenu` 选择，键上没有组合时直接走 `AddMappingForKey`（新建到该键位）。双击窗口是 300ms 的短窗（`MKeyboardView.QuickDoubleClickMs`），第一次点击在 `OnMouseUp` 的 `ApplyLeftClick` 里记下「键帽 + 时间戳」，`OnMouseDoubleClick` 用 `IsQuickSecondClick` 只认这个标记（系统的 `WM_LBUTTONDBLCLK` 窗口约 500ms，太长）；`ApplyLeftClick` 的「第二次点击不切掉选中」也用同一个窗口。在选中修饰键的状态下，按在「有绑定」的键帽上会先 `ArmDrag`（按下不立刻算点击，松手没移动才算），因此 `ArmDrag` 绝不能清掉这份点击记录，否则第二次按下就接不上双击；记录只在拖动真正完成时（`OnMouseUp` 的 moved 分支、`OnMouseCaptureChanged` 补完 drop）清空。激活前 `ActivateCap` 会重新落一次选中：双击的第一下、或者拖动后残留的选中都可能已经被切掉，若不重设，`ActivateSelectedKey` 会拿着 `Keys.None` 直接返回（这就是「拖动搬移后双击没反应」的根因）。空选态（未选修饰键）双击仍是两次普通点选，不进入编辑。
- 编辑复用列表的 `MappingListItem`、`ToggleRow`、`DeleteRow`，新增走 `AddMappingWithDraft`（按当前修饰键与选中键位预填）。`RefreshList()` 末尾调用 `RefreshKeyboard()` + `UpdateKeyboardSummary()`，所有改动路径自动同步，不要在别处再写一遍。
- 键帽字号是**像素字号**、随键宽缩放（`KeyLabel.CreateFonts`，7.5/6/5 的老方案已废弃）：布局单位是像素，而点值字体会按系统 DPI 放大，在 120 dpi 上比键帽大 25%，必然截断。`KeyLabel.Plan` 按「完整 → 小一号 → 再小一号 → 空格换行 → 省略号」选择，自检 `KeyboardLabelAudit` 用同一套规则遍历 560–1600 像素页宽（每 20px 一档），任何一个键名落到省略号都算失败。
- 动作名有两条自检线：`KeyboardLabelAudit.ActionProblems` 按页宽扫描全部 104 个键的合成布局，`MKeyboardView.LayoutProblems` 再按控件的真实像素矩形复核一遍。只测其中一条会漏掉「审计按单位算、绘制按取整后的矩形算」这类偏差。
- 截图核对用 `--demo=keycaps`（Caps 筛选）、`keycombo`（Caps+Alt）、`keykey`（单键）、`keycollapsed`（收起）、`keymid`（动画中间态）、`keysmall`（最小窗口）、`keylarge`（大窗口）、`keymenu`（展开 ⋯ 菜单）；`--demo=min` 只缩窗口。这些入口只在 `DEBUG` 构建中编译（`build.ps1` 默认不定义），发布版解析不到 `--demo`，也不存在 `--demo=import`。

### 鼠标卡片

- 快捷键页的列表右侧是 200px 宽的鼠标卡片（`MMousePad`），与列表同高；列表相应收窄 200px。卡片可折叠成 30px 窄条，收起后列表恢复全宽；页面宽度不足 860 逻辑像素时卡片整体让位，不让列表被挤窄。
- 外框是 12 个顶点的直线多边形（`BodyOutline`，150×210 单位空间），左/右键的热区直接复用同一轮廓、整体内缩 2 单位，滚轮列在 57–93 之间按 2 单位缝分三格。几何在 `EnsureGeometry` 里按卡片尺寸等比缩放并缓存，命中测试用 `GraphicsPath.IsVisible`（斜肩不能按矩形判定）。
- 状态与键帽同源：`ComputeState` 统计该热区在当前修饰键组合下的总数/精确数/启用数；配色、文字色、停用斜线、悬停提亮、计数徽标统一走 `KeyVisual`（`KeyVisual.cs`），键帽的 `DrawKey` 与鼠标卡片调用同一组函数，因此两边的状态画法不会漂移。徽标直径/字号/内缩直接取键盘的键位单位（`BadgeUnit`），与键帽徽标分毫不差。选中环用 `TextPrimary`，拖放目标用 `Accent` 2px 环。
- 交互与键帽一致：点选筛选（第二次点选取消，双击的第二下保持选中）、双击编辑、右键菜单走同一个 `ShowKeyboardMenu`、Del/Enter/Ctrl+X/C/V 仍按 `_selectedKey` 生效。
- 3D 与文字：热区多边形当「格子」，可见键面 = 格子按 `min(格子高*0.10, max(1.5, 键位单位*0.06))` 削掉底部；阴影带 = 键面下移一个深度，只有键面被描边，所以阴影落在键面之外。标题/动作名直接复用键盘的 `KeyLabel.CreateFonts` + `KeyLabel.Plan` + `KeyActionText.Layout`（`KeyVisual.DrawLabel` / `DrawActionLayout`），字体阶梯、字重、两行/省略规则与键帽完全相同；动作名只在选中修饰键后绘制。
- 跨区拖动：拖动源在指针离开自己的范围后不再绘制浮卡（子控件只能画在自己范围内），改为每帧抛 `DragPointerMoved(title, action, screen, outside)`，页面把它转给指针所在的另一块界面（`SetExternalDrag`），由那一侧画同一张浮卡（阴影 + 面板 + `Accent` 2 边框 + 强调色标题/动作）。两个方向的落点仍然都调用 `MoveKeyBindings`。
- 双向拖动：键盘拖动时 `MKeyboardView.OnMouseMove` 发现指针离开板面就通过 `ExternalDropTarget` 问鼠标卡片落点，命中后 `SetExternalDrop` 高亮并抛 `ExternalTargetChanged`；松开走 `KeyDragDroppedExternal`。反向由 `MMousePad` 自己捕获鼠标、用 `ExternalDropTarget` 问 `KeyAtScreen`，键盘用 `SetExternalDropKey` 高亮。两个方向最终都调用同一个 `MoveKeyBindings(from, to)`，所以冲突确认、撤销快照、自动选中目标键的行为完全一致。
- 键盘的导航键区（PrtSc/Ins/Del 与方向键）在 15.25u 起排：左侧与主键区、右侧与数字小键盘都留 0.25u，之前从 15.5u 起排会贴着数字小键盘。

### 快捷键列表

`MListView` 是自绘列表（行自己画，滚动条画在控件内部）。交互事件：`ItemActivated`（左键双击 / Enter）、`ItemToggleRequested`（`Space` / 行尾开关，`ToggleHotspotIndex` 指出被点的那一行）、`ItemDeleteRequested`（`Del`）、`ItemContextRequested`（右键）、`EmptyContextRequested`（右键落在空白处）、`ItemsReordered`（拖动结束，参数是 `MListReorderEventArgs`）。右键先选中该行再抛 `ItemContextRequested`，由 `MainForm.ShowListMenu` 弹出与键盘同一套 `MPopupMenu`（编辑… / 停用·启用 / 删除 / 新增快捷键，多选时换成「…选中 (n)」一组）。

- 多选由 `ListSelection.cs` 的 `MListSelection` 维护：Ctrl 点选切换、Shift 连选、Ctrl+A 全选、方向键配合 Shift 扩展；`MultiSelect` 与 `AllowReorder` 默认关闭，只有快捷键页与插件页打开，`ActionPickerForm` / `ConfigDialogs` 这类选择框仍是单选。
- 拖动排序：按在选中行上移动超过 4px 才进入拖动；整块跟随指针（15ms Timer + 插值），其余行动画让位，插入位置用强调色横线标出，靠近上下边缘自动滚动。**拖动只存在于左键按住期间**：`StartDrag` 设 `Capture`，`CommitDrag` / `CancelDrag` 释放；动画帧里还会查 `Control.MouseButtons`，鼠标被别的窗口抢走或松开事件丢失时立刻落位，`OnMouseCaptureChanged` 与失焦也提交。松手点（`OnMouseUp` 的 `e.Location`）就是落点。纯算法放在 `MListReorder`（`TargetStart` / `MoveIndex` / `Move<T>`），动画与命中都在 `MListView`。
- 行尾开关只作用于该行（`ToggleHotspotIndex`），不会把多选里的其它行一起翻掉；批量启停走右键菜单。
- 右键是菜单手势，绝不能顺手切换开关或开始拖动滚动条：`OnMouseDown` 里右键分支必须在开关命中之前返回。
- 右键落在空白处（`IndexAt` 返回负数、且不在滚动条区域）时不改选择，只抛 `EmptyContextRequested`；快捷键页弹「新增快捷键 / 全选 / 搜索快捷键… / 刷新列表」，插件页弹「导入插件… / 重新加载插件 / 打开插件目录 / 全选」。
- **WinForms 的 `OnMouseDoubleClick` 对右键双击同样会触发**（`WM_RBUTTONDBLCLK` → `MouseDown(Clicks=2)` + `MouseDoubleClick(Button=Right)`），所以每个双击处理器都要先判 `e.Button == MouseButtons.Left`，否则右键双击会直接打开编辑页。

### 输入控件与对话框

界面里不再直接用原生 `TextBox` / `NumericUpDown` / `MessageBox`：原生输入框是方角、系统滚动条带箭头，原生数字框带系统上下箭头，和自绘界面放在一起非常割裂。

- `ModernInputs.cs`：`MTextBox`（圆角外壳 + 内嵌无边框 `TextBox`，聚焦时强调色描边）、`MTextArea`（多行，内嵌 `ScrollBars.None`，右侧自绘 6px 圆角滚动条，用 `EM_GETFIRSTVISIBLELINE` / `EM_LINECOUNT` / `EM_LINESCROLL` 同步滚动）、`MNumberBox`（完全自绘的数字框，右侧自绘上下箭头，支持滚轮、方向键与按住连续步进）、`MTextView`（只读换行文本 + 自绘滚动条，插件详情与 JSON 预览用）。
- `ModernDialog.cs`：`MDialog.Tell` / `MDialog.Confirm` 替代 `MessageBox`，固定对话框、在 owner 居中、`Enter` 确认 `Esc` 取消、`Native.SetDarkTitleBar` + `SetRoundedCorners`；图标按 `MDialogKind` 画成着色圆底 + 字形。程序启动早期的异常提示仍用原生 `MessageBox`（那时主题和 win32 状态还没准备好）。
- 构造顺序有坑：`Height` 要在 `Controls.Add(_inner)` 之后再设，否则 `OnSizeChanged` 会在 `_inner` 还是 null 时被触发（`MTextArea` 就是这么崩过一次）；布局方法开头加 `if (_inner == null) return;` 兜底。
- `MTextView` 内部有 10px 内边距，不要把它塞进 20px 高的条里（会让正文被压成一条线）；对话框正文改由 `MTextLayout.Wrap` 预先换行、`OnPaint` 逐行 `TextRenderer.DrawText` 画。
- 对话框是平面板式（不套内嵌卡片）：`BackColor` 用 `Theme.Surface`，图标圆底 `Theme.S(36)` 在左、正文在右（间距 `Theme.S(16)`），正文与操作行之间一条 1px `Theme.Stroke` 细线（左右内缩 `Theme.S(24)`），按钮行在细线下 `Theme.S(14)`、右对齐（次要左、主操作右）。短对话框里再套一张卡片会像贴错位置的面板，已经明确弃用。
- 宿主容器与子控件的安全边距：`MappingEditorForm.ParamHostHeight(ActionParamKind.Text)` 必须比 `_area` 的高度至少多 `Theme.S(4)`（现在 `_area` 是 `Theme.S(96)`、宿主是 `Theme.S(100)`，`y = Theme.S(2)`）。子控件超出宿主矩形时 WinForms 会直接裁掉，圆角底边会缺一条。


### 弹层菜单

`MPopupMenu` 是自绘的置顶小窗体（圆角 + 1px 描边 + CS_DROPSHADOW），`⋯` 菜单、`MComboBox` 的下拉与右键菜单都用它；不要再给这些入口加 `ContextMenuStrip`，原生控件的方角与系统间距和界面割裂。菜单项 34px 高、左右 14px 内边距，选中项左侧有 3px 强调色竖条，快捷键右对齐用 `Theme.Caption`。

圆角只画描边是不够的：窗口本身仍是矩形，未填色的四角会把底色露成直角。`MPopupMenu` 在 `OnResize` 里把 `Region` 设成同一段圆角路径（窗口被真正裁掉），描边则用「整块底色 + 内缩 1px 的圆角填充」画成，边缘和裁剪曲线重合。区域裁剪没有抗锯齿，四角是台阶状，这是 WinForms 自绘弹层的正常代价。

### 内置动作

`ActionCatalog.BuildActions()` 是唯一清单，id 形如 `system.show`。新增一个动作要同步三处：`ActionCatalog`（定义）、`ActionExecutor.Execute` 的 switch（实现）、需要宿主能力时再加到 `IAppHost`。`system.show`（打开主界面）就是通过 `IAppHost.ShowMainWindow` 实现的，托盘菜单的「打开设置」复用同一个方法。

快捷键列表的「类型」列由 `ListItems.MappingListItem` 现查 `ActionCatalog.Get(mapping.Action)`：`IsMacro` → 「宏」，`IsPlugin` → 「插件」，其余（含 `Missing` 的插件占位）算「原生」。不要在映射数据里另存类型字段，否则插件卸载后旧行会显示错误来源。

清单顺序就是动作选择器的默认顺序：`启动与输入`、`鼠标`、`其他` 三类排在最前（`none` 也放在最前面定义），其余按光标 / 选择 / 删除 / 剪贴板 / 窗口 / 系统的顺序。动作选择器顶部有 `MSegmented` 做的「全部 / 原生 / 插件 / 宏」筛选，插件动作的分类在 `ActionPickerForm.RefreshList` 里统一加「插件 · 」前缀。打开对话框时按当前动作选中一行；之后改筛选或搜索只保留仍然可见的选中项，找不到就清空选择，不自动跳第一行（焦点交给列表，↓ 再选）。筛选与搜索、冲突检测都不需要改动作数据。

`ActionCatalog.Describe` 对 `Missing` 的动作要按类型分支：宏（`IsMacro`）显示「宏动作不存在」，插件才显示「插件动作未加载：id」，否则删掉的宏会在快捷键列表里被误报成插件问题。

单键动作已合并进 `input.keys`（1.14.1）：行首/行尾、翻页、选中到行首/行尾/页首/页尾、Backspace/Delete、Ctrl+Backspace/Delete、Enter、撤销/重做、标签页切换、复制/剪切/粘贴都不再是独立动作。`ActionCatalog.KeySendReplacements` 保存旧 id → 组合键的对照表，`MigrateKeySendAction` / `MigrateKeySendStep` 在 `ConfigStore.Normalize` 与宏归一化里把旧配置和旧 `.clmacro` 改写成 `input.keys`，用户原来的按键不变。保留独立动作的只有两类：需要多步组合的（清空当前行、删除全部、删除到行首/行尾、选中当前单词/行）和本身不是按键的（窗口、系统、启动、鼠标）。

1.14.4 又退役了两个动作：`edit.insert.line.below`（向下插入一行，原本是「行尾 + 回车」两步）与 `system.reload`（重新加载，原本由程序自己重载配置）。它们都进入 `KeySendReplacements`（→ `Enter` / `F5`，注释分别为「回车」「刷新」），旧配置在归一化时自动降级成普通按键发送，不会变成空动作。`CreateDefaultMappings` 同时换成一份按用户实际使用习惯整理的 57 条清单：`Add(...)` 增加了 `triggerMode` 参数，每条默认项自带 `hold` / `once` 与注释，`SelfTest.CheckDefaults` 看住条数、重复组合、已退役动作和三种注释（含 `Caps+Alt+Enter` 被修正为普通回车）。

`ActionExecutor.Enqueue` 入队后立刻把一次 `Drain` Post 到 UI 消息循环（`Control.BeginInvoke`），单发动作的延迟从「等一个 15ms 的 `WM_TIMER`」降到约 1ms；15ms 定时器保留作兜底（宿主不是 `Control` 或句柄还没建好时）。执行仍在 UI 线程，仍受 25ms 同 id 节流约束。

`mouse.click` 是第一个带自定义参数编辑器的普通动作：参数串是 `L;x;y[;CASW]`（按钮;虚拟屏 X;虚拟屏 Y;可选修饰键，按 Ctrl、Alt、Shift、Win 顺序拼字母），由 `MousePoint.cs` 的 `MouseClickPoint` 解析、`MMousePointField` 编辑（行首标签是「点击设置」；第一行四个修饰键开关 + 第二行分段按钮与「抓取坐标」 + 第三行两个数字框），参数行的标签用 `AutoSize`，编辑控件列的起点按标签实测宽度后移（'点击设置' 四个字在 125%/150% 缩放下也不裁字）；快捷键编辑页与宏步骤页共用同一个控件。旧数据里可能带第 4 段（历史「返回原位」标记），解析时忽略没有 `C/A/S/W` 字母的段。执行时 `ActionExecutor.ClickAt` 先按下勾选的修饰键再移动光标（让目标窗口的 hover 状态看到修饰键）、点击，最后在 `finally` 里逆序松开，即使移动失败也不会把修饰键留在按下状态。抓取坐标时 `MousePointCapture` 把窗口移到屏幕外（不 Hide，避免模态对话框的 Show/Hide 状态被破坏），由软件已有的键盘钩子等待 CapsLock（Esc 取消），确认后把当前光标位置写回数字框并恢复窗口。

### 主题

`Theme.Apply(bool dark)` 切换静态调色板。切换后 `MainForm.ApplyTheme()` 调用 `RebuildUi()` 销毁并重建 UI，保证所有自绘控件立即更新。新增控件时不要硬编码颜色，统一使用 `Theme.*`。

### 多显示器与 DPI

- 进程声明 PerMonitorV2；缩放因子只放在 `Theme.SetDpi` / `Theme.S`，**子窗口构造时不要再调用 `Theme.SetDpi`**，否则会把全局缩放改写回主屏值。
- `MainForm` 处理 `WM_DPICHANGED`：先应用系统给出的建议窗口矩形，再 `Theme.SetDpi` 并 `RebuildUi()`；重建推迟到下一次消息循环，避免在 `WndProc` 里销毁正在派发消息的控件。
- 构造期的 `DeviceDpi` 通常是主屏的，`EnsureDpi()` 在窗口首次显示时校正一次。
- `--demo=dpi150` 可以在不改显示设置的前提下按 144 DPI 重建界面，用于截图核对。

### 重复组合键

`MappingConflicts.FindShadowed` 按「Caps 层 + 修饰键 + 主键」判定重复：同签名只有列表首条生效，后面的重复项永远不会触发。列表会给这些行标「重复」，`HotkeyManager.ReportConflicts` 在启动时汇总提示（同一条提示不重复弹）并写日志。1.8.0 起不再注册系统级全局热键；`ConfigStore.Normalize` 会把历史 `Caps=false` 的映射补齐 Caps 层，避免升级后静默失效。

### 键盘钩子

- `HotkeyManager` 安装 `WH_KEYBOARD_LL`。
- 被 Caps 层消费的键会加入 `_swallowedKeys`，其系统自动重复被抑制。
- Caps Lock 按下时，先按住的修饰键已经发给目标窗口了，Caps 层必须把它们收回来：`SuppressHeldModifiers` 对每个「已送达」的修饰键补一次 keyup 并记进 `_suppressedModifiers`，于是 Caps+Alt+Q 这类绑定发送 F4 时不会变成 Alt+F4 关掉目标程序；用户松开该修饰键时对应的 keyup 被吞掉（不会补第二次 up），松开 Caps 时 `RestoreSuppressedModifiers` 把仍按住的修饰键补一次 keydown 还给目标。Caps 按下之后才按的修饰键本来就被吞掉（`_swallowedModifiers`），不需要回收与恢复。
- 长按连发由 `_repeatTimer` 主动驱动，不依赖系统自动重复；主键或 Caps Lock 松开时停止。
- 多键叠加按「最后按下为准」，但**每个按下的键都保持按住**：`_chordEntries` 记录 Caps 层里仍按着的主键（键、映射、是否已在目标上按住）。`DeliverChord` 启动新组合时只调 `StopRepeatOnly()`（停连发，不触发 `HoldEnded`），旧键的 `HeldOnTarget` 保持不变；只有用户的物理 keyup 才 `ReleaseChordHold` 补一次 `HoldEnded`。`ActionExecutor` 因此维护 `List<HeldKey> _holds`（可同时按住多个键，`EndKeyHold(mapping)` 只松开对应映射，`EndKeyHold(null)` 全松），修饰键只在没有其它 hold 需要时才发送 keyup。松开最新的键时 `ResumeHeldChord` 只把连发交回仍按着的最新主键，不会重复 `HoldStarted`。Caps 松开、`ResetState`、退出应用都会调用 `ReleaseAllChords` 把所有 hold 放掉，避免键卡在目标程序里。
- 模拟按键带 `KEYEVENTF_INJECTED` 标记，钩子会忽略注入事件，避免自触发。

### 鼠标触发

- 五种鼠标触发（左键、右键、中键、滚轮上滑、滚轮下滑）复用 `KeyMapping.Key`：`MouseKeys` 提供 `0x0E01`–`0x0E05` 五个伪键码。键盘钩子上报的是 0–255 的虚拟键码，所以这些值不会被真实按键命中，`ConfigStore` 的配置格式与 `ConfigVersion` 都不用改，`MappingConflicts` 照常按 `Key & 0xFFFF` 判重。
- `MouseHook` 只在 Caps 层存在的期间安装：`OnHookKey` 的 Caps down 分支调 `EnsureMouseHook` 挂上 `WH_MOUSE_LL`，Caps up 和 `Stop`/`ResetState` 时卸载，所以平时的鼠标输入完全不经过本程序。它只翻译 `WM_LBUTTONDOWN/UP`、`WM_RBUTTONDOWN/UP`、`WM_MBUTTONDOWN/UP`、`WM_MOUSEWHEEL`；鼠标移动、横向滚轮、X 键一律放行。`KeyboardHook` 安装失败仍然中止启动；`MouseHook` 失败只写一次日志并继续（键盘是核心功能，鼠标触发可以没有）。
- `HotkeyManager.OnHookMouse` 只在 `_capsDown` 时介入，而且**只在找到绑定动作时才吞事件**：没有绑定的鼠标键照常点击、滚动，不会因为按住 Caps 就把鼠标弄死。被吞的按下会记进 `_swallowedMouseButtons`，对应的抬起也一并吞掉，避免目标程序收到没有配对的 button-up。
- 鼠标键复用键盘的 `_downKeys` / `_chordEntries` / `DeliverChord`，所以「按住连发」「多键叠加」「目标程序里保持按住」的行为与键盘完全一致。滚轮没有抬起事件，`HandleWheel` 按 120 单位累计后一次触发（精度触摸板会发很多小数 delta），单条消息最多补 3 次，方向改变或停顿 800ms 会重置累计；滚轮永远不会进入 `_repeatTimer`，编辑页在 `UpdateModeState` 里把「按住连发」置灰并在保存时强制 `once`。
- 录制鼠标键由 `MKeyCapture.AllowMouse` 控制（默认关闭，`MKeyComboField` 的「发送按键」参数仍然是纯键盘捕获）。获得焦点时 `ArmMouse` 安装一个临时 `MouseHook`，录到第一个按下/滚轮后 `_mouseArmed=false` 但保留钩子：之后的点击原样放行，对话框按钮可以正常点；再点一下主键框（`OnMouseDown`）重新进入录制。失去焦点或 `AllowMouse=false` 时卸载钩子。
- 「鼠标点击」动作通过 `mouse_event` 发合成点击时把 `dwExtraInfo` 设成 `Native.SyntheticMarker`，`MouseHook` 在回调里直接放行带该标记的事件，避免动作自己的点击又被当成 `Caps + 鼠标左键` 触发。
- 键盘页不渲染鼠标触发：`KeyboardPreview.Recompute` 用 `IndexOfKey` 找键帽，伪键码找不到对应键帽就自然跳过，不需要额外的黑名单。

### 插件

- 对外契约只在 `PluginApi.cs` 里，全部类型为 `public`；改这里等于改 ABI，必须同步 `PLUGIN.md`。
- 插件在运行时用 .NET Framework 的 `csc.exe` 编译（引用宿主 exe），编译产物落在 `plugins\.build\` 并在加载后删除；编译进程的 `TEMP` 被指向该目录，避免受限环境写系统临时目录失败。
- 编译或校验失败只让**该文件**失败，其余插件继续加载；错误文字会显示在插件页详情里。
- 动作注册进 `ActionCatalog`，ID 为 `plugin.<插件 Id>.<动作 Id>`；重载时先 `UnregisterPluginActions()` 再注册。
- 未加载的插件动作由 `ActionDef.ForMissing` 提供占位定义：`ConfigStore.Normalize` 对 `Missing` 的动作**不**归一化参数与触发类型，避免插件临时缺失时改写用户配置。
- 导入统一走 `PluginRuntime.ImportPaths`：`.cs` 以及 `.zip` 内的每个 `.cs` 都先在 `plugins\.import\` 里**只编译并检查元数据**（不实例化、不调用 `Initialize`/`CreateActions`），通过后复制到 `plugins\.pending\<sha256>\`；用户确认信任后才写入 `trusted.txt` 并加载，失败/拒绝信任时删除待启用文件。导入对话框与窗口拖放共用这个入口。
- 信任模型：没有总开关，信任是唯一闸门；`Reload` 只加载 `trusted.txt` 中记录 SHA-256 的文件，文件被修改后哈希不匹配会自动降级为「未信任」并且不执行。`PluginRuntime.UntrustFile(path)`/`UntrustHashes(hashes)` 删除信任记录并重载：插件页右键「取消信任…」走前者，卸载插件时 `RemovePlugin` 先收集被删文件的哈希、删除成功后调 `UntrustHashes`，所以再次导入同一文件需要重走信任流程。`trusted.txt` 与插件目录都在每用户配置目录下；`ConfigStore.IsConfigDirectoryPrivate` 会检查 ACL，发现 Everyone/Users/Authenticated Users 等可写时拒绝加载插件。
- 目录结构：每个插件一个文件夹。`Reload` 使用自定义递归扫描，跳过 `.build` / `.import` / `settings` 目录和重解析点；本来在 `plugins\` 根下或 `.pending\` 中的已信任文件在**加载成功后**由 `MigrateIntoFolder` 移入自己的目录，失败则保持原样并记日志。
- 插件动作通过 `PluginDispatcher` 在专用 STA 线程执行（有消息循环，支持 WinForms/剪贴板）；`PluginRuntime.Dispatch` 只入队，不阻塞键盘钩子/UI 线程。重载/卸载前调用可选的 `IPluginLifecycle.Shutdown()`，并尝试 `IDisposable.Dispose()`；卸载已加载插件时 UI 会退出程序，确保代码停止。
- 编译使用系统 `csc.exe` 直接进程（不经过 `cmd.exe`），超时 30 秒；忽略 `CSC` 环境变量覆盖。
- 「卸载」会删除插件源文件与它自己的目录（`plugins\<id>\`），且只在该目录确实位于插件根下、名字等于插件 Id 时才递归删除；否则只删源文件。
- `IPluginHosted.Initialize(PluginHostInfo)` 在实例创建后、`CreateActions()` 前调用，把 `DataDirectory` 交给插件；插件自己的配置文件因此与源码同目录。
- 拖放：WinForms 里每个子控件各自有窗口，只在窗体上设 `AllowDrop` 收不到落在子控件上的文件；`AttachDropTargets` 递归给所有子控件挂 `DragEnter`/`DragDrop`，并在每次 `BuildShell()` 之后重新挂（控件树会被重建）。
- 压缩包只取文件名（防路径穿越），单文件上限 512 KB、最多扫描 500 条；同名文件覆盖，同名候选跳过。
- 新增 `ActionParamKind` 要同步三处：`MappingEditorForm.BuildParamArea`、`ActionCatalog.Describe`、`PLUGIN.md` 的参数表。
- 插件设置页走**可选接口**：`IPluginWithSettings` / `IPluginSettingsPage` / `IPluginSettingsContext`。不要给 `IPlugin` 加成员，否则所有旧插件编译失败。
- 宿主侧：`MainFormPlugins.OpenPluginSettings` 建二级页面，用 `MScrollPanel` 承载（自绘滚动条；不要用 `AutoScroll`，否则会露出系统滚动条）；`ClosePluginSettings` 负责保存与销毁视图；重载、导入、卸载前必须先关闭设置页。列表行的双击 / `Enter`（`ItemActivated`）直接进设置页，没有设置页时给状态提示；插件详情、信任、卸载只放在右键菜单里（`ShowPluginMenu`），信任在无可信任文件时保持可见但置灰（`MPopupMenu.AddItem(..., bool enabled)`）。
- 设置持久化在 `PluginSettingsStore`（`plugins\<插件 Id>\settings.json`），只在 `Set` 过之后才写盘；`PluginRuntime.DataDirectoryOf` 负责定位插件实际所在目录。

### 宏动作

宏动作把多个内置动作、插件动作以及其他宏串成一段顺序脚本。它是普通动作的一种：ID 为 `macro.<宏 Id>`，由 `ActionCatalog.RegisterMacros` 注册，因此绑定、动作选择器、冲突检测、快捷键列表都无需特判。

- 数据模型见 `Models.cs`：`Macro { Id, Name, Note, List<MacroStep> Steps }`，`MacroStep { Action, Param, Note, DelayMs = 50, Count = 1 }`；`MacroStep.Note` 是用户在步骤编辑框里起的名字（可空，归一化时 trim 并截到 60 字），只影响列表显示，不参与执行。`ConfigStore.NormalizeMacroList` 负责修复外部文件（空 Id / 重名 Id / 空名字 / 空动作 / 越界的延迟与次数），配置版本因此升到 4。
- 宏文件是纯 JSON（`.clmacro`），一个文件可放多个宏；读写都在 `Macros.cs` 的 `MacroFileStore`，写入用 `JsonPretty` + 无 BOM 的 UTF-8。文件不含任何 `.cs` 代码，因此导入不需要信任流程。
- 执行器 `MacroRunner` 用**帧栈**而不是展开：每个 `Frame` 记录当前宏、步号、这一步剩余次数与下一次可执行的时刻。这样「宏里嵌套宏、嵌套的宏又被重复 999 次」也只占常数内存。每一步执行完等 `DelayMs`，`Count` 是这一步重复几次；嵌套宏跑完后父帧再等自己的 `DelayMs`。
- 安全阀：启动前 `MacroLibrary.ValidateNesting` 递归检查循环与深度（`MaxNesting = 8`）；运行时帧栈最多 16 层、整次运行最多 10000 步，超限自动停止并给状态提示。
- `ActionExecutor` 的默认分支先看 `ActionCatalog.IsMacroActionId`，命中就调用 `MacroStartHook`（由 `MainForm` 指向 `StartMacroFromAction`），不会落到插件分发；`ExecuteAction(action, param)` 让运行器复用同一套动作实现。
- 单实例运行：`MacroRunner.Start` 在 `IsRunning` 为真时直接返回 false（错误文本「宏“X”正在运行，结束后才能触发下一个宏。」），不再先 `Stop` 当前运行；`MainFormMacros.StartMacroFromAction`（快捷键触发）与 `RunSelectedMacro`（页面「运行」）都先判断并给状态提示，想换宏必须点「结束运行」。嵌套宏不经过这条路径——运行器在 `OnTick` 里自己 push `Frame`，所以子宏照常执行。
- 编辑页 `MacroEditorForm` 复用 `MListView` 的多选与拖动排序：`Ctrl+X/C/V` 针对步骤列表做剪切/复制/粘贴（也可从行右键菜单触发），剪贴板是进程内的 `List<MacroStep>` 深拷贝；工具栏只留「添加动作… / 编辑… / 删除」，`ProcessCmdKey` 让 `Esc` 取消、`Ctrl+S` 保存。**列表只负责显示顺序**：`ItemsReordered` 与保存前都会调用 `SyncStepsFromList()`，用 `MacroLibrary.ApplyStepOrder()` 把行顺序写回 `_draft.Steps`（列表没覆盖到的步骤按原顺序保留，绝不丢步），否则拖完再保存会回到旧顺序。
- 步骤编辑有独立的撤回栈（`StepSnapshot { List<MacroStep> Steps; string Label }`，上限 50）：每个改动入口先 `PushStepUndo(label)` 再改，`Ctrl+Z` / `Ctrl+Y` 走 `UndoSteps`/`RedoSteps`，右键菜单同样有「撤回 / 重做」；焦点在名称或备注输入框时 `ProcessCmdKey` 直接放行，让文本框保留自己的撤消。撤回只回滚步骤，不碰宏名、备注与运行状态。
- 步骤编辑框 `MacroStepForm` 的高度是量出来的：命名行、动作行、参数行、按 `MTextLayout.Wrap` 计算行数的说明、时间行依次递推，换动作时重新跑 `LayoutRows`；命名行复用同一套「标签 + 控件」布局（标签 `S(60)`、输入框从 `S(80)` 起），并排在卡片最上方——它只是给这一步取个显示名，`MacroStep.Note` 归一化时截到 60 字，`MacroStepListItem` 有名字时动作列只画这个名字（`BodyStrong`），名字取代动作描述而不是叠成两行，参数列仍显示参数；参数行与「编辑快捷键」动作卡片同构（说明列 `S(16)`、控件从 `S(80)` 起、右侧留 `S(16)`，路径/文件夹带 `浏览…`，组合键与快捷键页共用同一个内联 `MKeyComboField`（四个修饰键开关 + 按键捕获 + 实时预览），不再弹二级对话框，文本用 `MTextArea` 96 高），插件说明再长也不会压住参数控件。
- `MNumberBox` 点入后第一次按键替换旧值、后续数字追加（早期实现每按一位就 `CommitEdit`，两位数字只剩最后一位）；`Step` 用 `Increment` 步进，等待框 25ms、次数框 1；Enter 在框内只提交并让出焦点（`IsInputKey` 放行 Enter，`Owner.ActiveControl = null`），窗体层 `ProcessCmdKey` 必须把焦点在 `MNumberBox` 上的 Enter 原样放行，否则对话框会直接关闭。
- 页面 `MainFormMacros`：列表按 Id 保持选择，运行中的宏显示「运行中」胶囊，`运行 / 结束` 两个按钮由 `MacroRunner.StateChanged` 驱动。导入/导出走 `SaveFileDialog`/`OpenFileDialog`，导出单个或全部。

### 状态提示

`IAppHost.ShowStatus` 只有 `(string, MInfoSeverity)` 与 `(string, bool error)` 两个重载，严重度由调用方给出，不再按中文关键字猜（插件用英文上报也不会被误判成成功）。`HotkeyManager.StatusChanged` 同样带严重度。

### 配置

默认位置：

```text
%APPDATA%\CapsLockLite\settings.json
```

不可写时依次回退到 `%LOCALAPPDATA%\CapsLockLite`、当前用户临时目录；不再使用 exe 同目录或 `CAPSLOCKLITE_DATA_DIR` 重定向。日志为同目录 `error.log`。

`ConfigStore.Save` 使用随机临时文件 + `File.Replace`（不支持时退回复制），避免崩溃时留下半截配置。

`ConfigStore.Normalize` 不再丢弃没有主键的映射（保留并显示“未设置”），`MappingConflicts` 也会跳过它们。

窗口尺寸：

- `WindowWidth` / `WindowHeight` / `WindowMaximized` 记住用户拖动后的窗口。默认值是 `min(1160×780, 工作区 82%)`，避免在 1366×768 这类屏幕上开到桌面外；读数超出合理范围时按「没设置」处理。
- 只有用户真的改过尺寸才写盘：`Resize` 后 700ms 防抖再 `SaveQuiet`；最小化时跳过，最大化时只记 `WindowMaximized`、保留上一次的还原尺寸。`--demo=`（仅 DEBUG 构建）运行时不写入，免得截图污染配置。
- `ConfigVersion` 升到 3（v2 的 F1 迁移仍保留）：`MigrateToVersion2` 把「没有被改过的默认 Caps+F1 → `system.help`」换成 `system.show`，用户自己改过的 F1 保持原样。以后改默认键位都走这个开关，不要去改 `Normalize` 里的默认值。

### 开机启动

优先写：

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

失败时尝试启动文件夹 `CapsLock Lite.cmd`。如果运行在沙盒、受限启动器或安全软件拦截环境中，系统会拒绝这些写入；程序会关闭开关并提示，而不会假装成功。

### 系统计算器

`system.calc` 直接启动 `calc.exe`，失败时尝试 `ms-calculator:`，不再包含自制计算器。

### 页头与侧栏

「启用」总开关是全局状态，挂在左侧栏顶部的 Logo 上（`MLogo`，同时是品牌标记）：`_logo.Click` 调 `SetEnabled(!_settings.Enabled)`，停用只改 `_logo.Dimmed = true`，由 `MLogo` 用 `ColorMatrix`（0.299/0.587/0.114 亮度，整体 alpha 0.55）缓存浅色版和深色版各一份灰化位图；切换主题会重建控件树，`UpdateStatusUi` 里要回填 `Dimmed`，否则暂停状态能看见而 Logo 还亮着。不要逐页放到内容区右上角，也不要再加单独的开关控件。快捷键页的右上角是预设控件：`BuildPresetBar(_content)` 把控件挂到内容区，`LayoutPresetBar(w, pad)` 右对齐排布（预设按钮 200、新建预设 88、下方 300 宽右对齐的前台提示），`LayoutContent` 只在快捷键页为它预留标题宽度。`SwitchPage` 负责显隐（`UpdatePresetBarVisibility`）并在 `_shellReady` 之后重跑 `LayoutContent`：构造期内容面板还是 150×150 的占位尺寸，提前排版会让列表按小视口算出错误的滚动位置（表现为首行被页头切掉一半）。主题切换走 `RebuildUi` 时同理：重建控件树前先把 `_shellReady` 置为 false，`BuildShell()` 之后立刻 `LayoutShell()`，再 `SwitchPage` / `RefreshList`；否则旧的 `_shellReady = true` 会让新页面按占位尺寸排一次版，切完主题列表首行就被页头切掉（同一个坑，1.18.1 修）。侧栏分页按钮不写死坐标：`PositionSidebarFooter` 里 `navTop = Math.Max(S(96), _statusLabel.Bottom + S(14))`，然后 `_navKeys/_navSettings/_navMacros/_navPlugins/_navAbout` 依次排在 `navTop + i * S(44)`，x 仍是 `S(12)`；状态指示的文字一变长，分页按钮就整体下移，不会再贴着「● 运行中」。`MLogo` 的立体感全在 `OnPaint`：投影（`_pressed` 时去掉）、`Theme.Rounded` 裁剪后画 Logo、全高 `LinearGradientBrush` 白色高光（`ColorBlend` 三段：62 白 → 0 白，0.55 处收尾）、hover 的白色 16 罩层、按下时的黑色 34 罩层、`StrokeRound` 内描边；高光必须填满整个 tile。只填上半截时 Brush 的平铺接缝会在分界处画出一条 1px 灰线（1.18.1 的 Logo 中线就是它），而 `LinearGradientBrush.WrapMode = Clamp` 会直接抛「参数无效」，只能靠全高渐变 + 多段 `ColorBlend` 解决。按下同时把图形整体下移 `S(2)`（`lift`），指针进/出/下/上只改 `_hover/_pressed`，不 `Focus()`、不写配置。`MButton` 画文字要带 `SingleLine | EndEllipsis | NoPrefix`，左右各留 `S(8)` 内边距，长预设名才会省略而不是被边框裁掉；焦点环只在 `Focused && Enabled && ShowFocusCues` 时画，鼠标点完不留高亮。预设菜单是非模态的 `MPopupMenu`，关闭后焦点会回落到触发按钮，所以在 `ShowPresetMenu` 里挂 `menu.FormClosed` 把焦点交给 `_list`，否则按钮上会一直留一圈高亮。侧栏品牌区：Logo 在 `(S(22), S(22))`、`S(38)` 见方，标题在 `(S(71), S(28))`，副标题 `_tagline` 在 `(S(71), S(44))` 且 `AutoSize`；运行状态指示不占页脚，`_statusLabel.Location = new Point(_tagline.Left, _tagline.Bottom + S(4))`，在 `PositionSidebarFooter` 里定位。侧栏页脚只剩版本号：`versionY = h - S(36)`、左边缘 `S(24)`，定位前用 `TextRenderer.MeasureText` 实测宽度——`MakeLabel` 给的是统一占位宽（版本号原来占 `S(160)`），不实测会把它顶出栏外。页脚里所有 Label 都按实测宽度摆放；本条的教训是：任何「紧跟另一个 Label」的控件都必须先量窄被跟随者。

## 发布

1. 更新 `src\AssemblyInfo.cs` 和界面版本号。
2. 运行 `build.ps1`。
3. 运行 `--selftest`。
4. 运行 `package.ps1` 生成发布包/源码包；**不要打包 `preview\` 或任何截图**。
5. 如需重建图标：

```powershell
python assets\make_final_icon.py
```

图标脚本需要 Pillow，并以 `assets\logo-option-1.png` 为源图。

`input.keys` 从 1.14.2 起支持可选注释：`KeyMapping.Note`（最长 40 字，归一化时裁剪，非 `input.keys` 的行会清空）。`ActionCatalog.Describe(KeyMapping)` 是列表行的唯一入口——有注释显示注释，没有注释显示组合串，都没有才显示「发送按键」；`ActionCatalog.Describe(id, param)` 对 `input.keys` 也返回裸组合串，供宏步骤等没有注释的场合使用。旧动作迁移时会把动作名写进注释（`KeySendPreset.Note`，如 `clipboard.copy → 复制`、`move.up → 上移`），这样十几个迁移过来的行仍然一眼能认。

快捷键编辑页的「发送按键」参数改用 `MKeyComboField`（`ModernInputs.cs`）：四个 `MChipToggle` 修饰键 + `MKeyCapture` 主键 + 实时预览，全部内联在参数区，`SendKeysDialog` 已随之删除（1.14.5 起宏步骤编辑框的「组合键」参数也换成了同一个控件）；下面一行的「注释」只对 `input.keys` 显示。`ParamHostHeight(KeyCombo)` 因此是 78，带注释时 118。`MKeyCapture` 的 `IsInputKey` 把 Tab、Esc 与 ↑↓←→ 都声明为输入键，并在 `ProcessCmdKey` 里先一步把 Esc 和方向键记进 `KeyCode`——否则对话框会先拿 Esc 当取消、方向键会被当成焦点导航，这五个键就永远录不进主键框（对话框侧的 Escape 分支用 `MKeyCapture.AnyFocused(this)` 判断焦点是否真在捕获框里，在才放行）。方向键 `move.up/down/left/right` 也已从 `ActionCatalog` 和 `ActionExecutor` 删除，迁移表把它们映射到 `Up/Down/Left/Right`；依赖它们的默认项（上移 ×3/×10/×30、下移 ×10/×30、左移 ×5、右移 ×5）一并删除，`CreateDefaultMappings` 里只保留 `Caps + E/D/S/F` 四条单发。

注释的作用范围由 `ActionCatalog.SupportsNote(action)` 决定：只有 `input.keys` 与 `mouse.click` 返回 true——它们的参数本身是裸输入（组合键、坐标），没有名字可显示；`ConfigStore.Normalize` 会清掉其他动作残留的 `Note`。`Describe(KeyMapping)` 对有注释的行一律先返回注释，再按旧规则回退。

编辑快捷键的保存路径改成了整体回写：`KeyMapping.CopyFrom(other)` 复制全部字段，`MainForm.EditMapping` 不再逐字段赋值。此前 `Note` 就是在逐字段复制里被漏掉的（同样的坑还漏过宏的 `RestoreCursor`），以后新增字段只需要改 `CopyFrom`。自检里有「copy-back keeps the note」与「normalise keeps notes only where they are shown」两条看住它。鼠标点击的编辑页参数区高度 146，带注释时 186（`ParamHostHeight`；控件本身 112，多出的是注释行与间距）。
