# CapsLock Lite 源码说明

版本：1.26.1  
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
third_party/interception/  可选驱动后端的客户端库（x64 interception.dll）、许可与来源说明
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
| `Native.cs` | Win32 P/Invoke、SendInput 批量结构、窗口工具 |
| `InputSink.cs` | 注入抽象：`IInputSink`、系统注入实现 `SendInputSink`、选择与回退路由 `InputPipeline` |
| `InterceptionSink.cs` | 可选的驱动注入后端（Interception 过滤驱动，动态查找 `third_party/interception/x64/interception.dll`，发布时随包放在 exe 旁） |
| `SyntheticLedger.cs` | 「本程序注入的按键」账本：驱动注入没有 `dwExtraInfo` 标记，靠它避免 Caps 层吃掉自己的输出 |
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
- 两条剪贴板，按焦点区分（`MainForm.ProcessCmdKey`）：列表有焦点且有选中行 → 行剪贴板（`CopySelectedRows` / `CutSelectedRows` / `PasteRows`，纯逻辑在 `MappingClipboard`）；否则有键位选中 → 键位剪贴板（`CopyKeyBindings` 那一套）。切换时不要改这条优先级，否则「先选键位再复制整条组合」会失效。行粘贴生成新 id、插在最后选中行之后（无选中时用剪切位置，再退化为末尾），`MappingClipboard.InsertIndex` 负责这个位置规则，自检 `row paste lands right after the anchor` 锁住。
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
- 系统注入的按键带 `Native.SyntheticMarker`（`dwExtraInfo`），钩子凭标记放行，避免自触发；驱动注入没有这个标记，改由 `SyntheticLedger` 认领（见「注入方式与驱动后端」）。

### 鼠标触发

- 五种鼠标触发（左键、右键、中键、滚轮上滑、滚轮下滑）复用 `KeyMapping.Key`：`MouseKeys` 提供 `0x0E01`–`0x0E05` 五个伪键码。键盘钩子上报的是 0–255 的虚拟键码，所以这些值不会被真实按键命中，`ConfigStore` 的配置格式与 `ConfigVersion` 都不用改，`MappingConflicts` 照常按 `Key & 0xFFFF` 判重。
- `MouseHook` 只在 Caps 层存在的期间安装：`OnHookKey` 的 Caps down 分支调 `EnsureMouseHook` 挂上 `WH_MOUSE_LL`，Caps up 和 `Stop`/`ResetState` 时卸载，所以平时的鼠标输入完全不经过本程序。它只翻译 `WM_LBUTTONDOWN/UP`、`WM_RBUTTONDOWN/UP`、`WM_MBUTTONDOWN/UP`、`WM_MOUSEWHEEL`；鼠标移动、横向滚轮、X 键一律放行。`KeyboardHook` 安装失败仍然中止启动；`MouseHook` 失败只写一次日志并继续（键盘是核心功能，鼠标触发可以没有）。
- `HotkeyManager.OnHookMouse` 只在 `_capsDown` 时介入，而且**只在找到绑定动作时才吞事件**：没有绑定的鼠标键照常点击、滚动，不会因为按住 Caps 就把鼠标弄死。被吞的按下会记进 `_swallowedMouseButtons`，对应的抬起也一并吞掉，避免目标程序收到没有配对的 button-up。
- 鼠标键复用键盘的 `_downKeys` / `_chordEntries` / `DeliverChord`，所以「按住连发」「多键叠加」「目标程序里保持按住」的行为与键盘完全一致。滚轮没有抬起事件，`HandleWheel` 按 120 单位累计后一次触发（精度触摸板会发很多小数 delta），单条消息最多补 3 次，方向改变或停顿 800ms 会重置累计；滚轮永远不会进入 `_repeatTimer`，编辑页在 `UpdateModeState` 里把「按住连发」置灰并在保存时强制 `once`。
- 录制鼠标键由 `MKeyCapture.AllowMouse` 控制（默认关闭，`MKeyComboField` 的「发送按键」参数仍然是纯键盘捕获）。获得焦点时 `ArmMouse` 安装一个临时 `MouseHook`，录到第一个按下/滚轮后 `_mouseArmed=false` 但保留钩子：之后的点击原样放行，对话框按钮可以正常点；再点一下主键框（`OnMouseDown`）重新进入录制。失去焦点或 `AllowMouse=false` 时卸载钩子。
- 「鼠标点击」动作通过 `mouse_event` 发合成点击时把 `dwExtraInfo` 设成 `Native.SyntheticMarker`，`MouseHook` 在回调里直接放行带该标记的事件，避免动作自己的点击又被当成 `Caps + 鼠标左键` 触发。
- 键盘页不渲染鼠标触发：`KeyboardPreview.Recompute` 用 `IndexOfKey` 找键帽，伪键码找不到对应键帽就自然跳过，不需要额外的黑名单。

### 注入方式与驱动后端

- 所有合成输入只走 `InputPipeline`（`SendStrokes` / `SendCombo` / `SendKeyState` / `Tap` / `SendText` / `MouseButton` / `MouseWheel`），不再直接调 `Native.SendInput`：`ActionExecutor` 的每个动作、`HotkeyManager` 的短按与修饰键回收都经过它。新增发键路径时必须走这里，否则驱动后端会漏掉那条路径。
- `InputPipeline.Apply` 按 `AppSettings.InputBackend` 选后端：`system` 恒用 `SendInput`；`driver` 每次强制探测驱动，失败时明确报错；`auto` 只在探测成功时切驱动，失败视为正常情况。`MainForm.RestartEngine` 在 `_engine.Stop()` 之后才切换后端，保证旧后端按住的键由旧后端松开；`EnsureRuntime` 在启动前应用一次。
- 驱动后端拒绝一次发送（键没有扫描码、驱动被卸载）时，`InputPipeline` 会先 `SyntheticLedger.Forget` 掉这批声明，再回退到 `SendInput` 重发，快捷键不会因为驱动掉线而丢失。
- 驱动按**扫描码**发送，扩展键必须带 `E0`（`Native.IsExtendedKey`，与 SendInput 的 `KEYEVENTF_EXTENDEDKEY` 共用一张表）。漏掉 E0 的后果不是报错而是**静默变成别的键或消失**：`Win` 键漏了 E0 时 `Win + Space`（短按切换输入法）直接失效，这就是一次真实回归。新增「发送按键」可用键时务必确认它是否需要 E0；`Pause` 需要 `E1`，此模型表达不了，`ToKeyStroke` 直接返回 false 让管线回退到 SendInput。
- `InterceptionSink` 只做「发送」，不做拦截：捕获始终由 `WH_KEYBOARD_LL` / `WH_MOUSE_LL` 负责，Caps 层状态机完全不受注入方式影响。它按扫描码发送（`MapVirtualKey(vk, 0)` + 扩展键的 `E0` 位），没有 Unicode 通道，所以文本仍由 `SendInput` 打；`interception.dll` 缺失、位数不符或驱动未安装时探测失败，错误文字显示在状态栏。
- **三个线程各管一段**（`DispatcherThread.cs`：STA + 自己的消息循环 + 锚点 `Control`）：①**钩子线程**装 `WH_KEYBOARD_LL`/`WH_MOUSE_LL`，只跑 Caps 层状态机（回调运行在安装它的线程上，系统要等它返回才放行事件；以前装在 UI 线程，UI 一忙注入就被压住——实测 UI 忙 50ms 时按键要 65–78ms 才到目标）；②**输入线程**跑 `ActionExecutor` 与 `MacroRunner`，串行注入、等送达、驱动步骤节奏；③**UI 线程**只做展示。钩子回调不发送输入也不等待；需要发键的（修饰键回收、短按动作）直接调 `InputPipeline`（不等待，不会自锁）；需要 UI 的（坐标抓取、状态栏、宏状态）经 `HotkeyManager.UiPost` / `UiHost` 派回主窗；`ActionExecutor` 用 `Post`（自己的锚点）接收按住类事件。新增回调或动作时先问一句：它必须在哪条线程上跑？

- **桌面注入只剩一条线程干活，鼠标动作靠“等送达 + 下一拍”拆成两半**：驱动把键盘与鼠标写到两条独立的设备栈，跨设备顺序本来没有保证；更关键的是本程序的低级键盘钩子由 UI 线程服务，UI 线程一忙，注入的键盘笔画就出不去。QPC 实测（目标为独立进程）：UI 线程空闲时键盘 3.3 ms、鼠标 2.8 ms；UI 线程忙 50 ms 时键盘 65–78 ms、鼠标仍 1–3 ms——这就是「后面的点击反而先到」的全部原因（早期用 `Environment.TickCount` 量出的「键盘 16–31 ms」是 15.6 ms 计时粒度的假象，驱动本身没有这段延迟）。
- 因此**不要**把动作交给第二个线程执行（试过：修饰键顺序对了，但步骤之间互相超车，宏还会在自己的最后一次点击落地前就宣布结束、并先把光标还原）。现在的规矩是：
  - 所有注入仍在**同一条线程**（UI 线程）上按步骤串行发出，`SendInput` 的批次顺序天然保留；
  - 带修饰键的鼠标动作拆成两拍：先在当前这一拍按下修饰键，鼠标事件交给 `ActionExecutor` 的 `_pending` 队列；泵定时器（有待办时把间隔降到 1 ms）在**账本里没有未认领笔画**时才发鼠标事件 —— 也就是修饰键真的到了目标之后。队列上限 `DeliveryWindowMs` 超时兜底，钩子停掉也不会卡死；
  - 修饰键必须**覆盖整个点击**，不只是“先到”：点击抬起与修饰键抬起走两条设备栈，谁先到不确定；`HoldModifiersAfterClick` 用 `InputPipeline.WaitMouseState` 等按键状态真的变成抬起，再补 `ModifierHoldMs` 的兜底，然后才松开修饰键。目标端顺序因此稳定为 `Ctrl↓ → 点击↓(Ctrl) → 点击↑(Ctrl) → Ctrl↑`。
  - `Busy` 同时覆盖「队列非空」与「自己还有笔画未被认领」，`MacroRunner` 用它当步骤栅栏，**并且宏结束前也要等它**（否则光标会先还原、最后一次点击才落地——这是一次真实故障），延时从这一步真正结束的时刻开始计算；
  - 新增注入路径时：不要另起线程，也不要用固定 sleep 代替「等账本清空」。
- **鼠标声明只在鼠标钩子在位时才记**（`SyntheticLedger.MouseClaimsActive`）：Caps 松开后鼠标钩子会卸载，此时没有任何东西能认领鼠标笔画；留着声明会在 2 秒内把用户自己的点击认领成「本程序发的」，也会让送达栅栏一直等到超时。
- `SyntheticLedger` 是驱动后端的配套：驱动注入发生在 `dwExtraInfo` 之下，钩子看到的事件与真实硬件无异。每次发送前记一条声明（FIFO 匹配、约 2 秒过期）用于识别「自己发的」；另按「当前按住的键」记一条按住记录，释放时只把它标成「已发出释放」，等这条释放真的被消费掉才清掉（另加 1 秒兜底，防止注入丢失后永久认领）。没有定时尾巴：曾经用 150ms 尾巴接住仍在队列里的重复，结果把用户「快速再按一次同一个键」也一起吃掉了。
- 两条铁律，改动这块时不要破：**抬起永不认领**——本程序发出的每个抬起都已登记，没登记过的抬起就是用户在松手，必须进入状态机结束 hold（曾经在这里把物理抬起当成了自己的事件，结果松掉主键后动作一直重复到松 Caps）；**重复的下压不当作新组合键**——按住记录只用来标记「这个键是我按住的」，由 `OnHookKey` 在 `_swallowedKeys` 之后调用 `IsHeldRepeat` 吞掉，且只在该键不是用户仍按着（`wasDown`）时生效。过期与回退（`Forget`）都要配对清理按住记录，否则一次失败的发送会永久吃掉那个真实按键。
- 自检覆盖：`a physical key-up is never claimed by a held key`、`a held key is reported, not booked, on its repeats`、`the physical key-up still ends a driver hold`、`a repeat left in flight does not restart the hold`。

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
