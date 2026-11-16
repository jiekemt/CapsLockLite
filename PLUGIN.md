# CapsLock Lite 插件开发指南

> 适用版本：CapsLock Lite **1.4.6** 及以上
> 契约版本：**1**（`PluginRuntime.ApiVersion`）
> 阅读本文件即可完成插件开发，**不需要阅读 CapsLock Lite 的源码**。

---

## 1. 插件是什么

插件是**一个 C# 源文件**，用来向 CapsLock Lite 添加若干条「可执行动作」。动作进入软件后与内置动作地位相同：

- 可以在「快捷键」页被绑定为 `Caps + 键` 或 `Caps + 修饰键 + 键`（只支持 Caps 层，不注册系统级全局热键）；
- 可以设置「按一次 / 按住连发」；
- 可以带一个参数（文本、文件夹、次数等），使用软件自带的输入界面；
- 修改插件后点「重新加载」即可生效，**不需要重启软件**。

一个插件文件 = 一个插件 = 任意多条动作。

### 插件做不到的事

| 限制 | 说明 |
| --- | --- |
| 不能新增导航页 | 插件不能往左侧导航加条目；需要配置界面时实现可选的 `IPluginWithSettings`（见 4.5），宿主会给它一个二级页面 |
| 不能修改内置动作 | 插件动作强制带 `plugin.` 前缀，不会覆盖内置 ID |
| 不能拆成多个文件 | 一个 `.cs` 文件即一个插件；目录里每个**已信任**的 `.cs` 都会被当成独立插件单独编译 |
| 不能使用 C# 6+ 语法 | 宿主按 `langversion:5` 编译（无字符串插值、无 `?.`、无 `nameof`） |

---

## 2. 三十秒上手

1. 新建文件 `hello.cs`（UTF-8 编码）。
2. 粘贴第 12 节的「最小模板」。
3. 打开 CapsLock Lite →「插件」页 →「启用插件」并阅读风险提示 →「导入插件…」→ 选择 `hello.cs`。
4. 确认框会显示文件名与 SHA-256；确认信任后，列表里出现该插件，状态「已加载」。
5. 去「快捷键」页 →「新增」→「执行动作」→ 选到「示例 · 打招呼」→ 绑定一个键 → 保存。
6. 按住 `Caps Lock` 再按该键，状态条会显示插件发出的消息。

---

## 3. 运行模型

### 3.1 文件位置

| 项目 | 值 |
| --- | --- |
| 根目录 | `<配置目录>\plugins\` |
| 每个插件的目录 | `<配置目录>\plugins\<插件 Id>\` |
| 信任库 | `<配置目录>\plugins\trusted.txt`（每行一个 SHA-256） |
| 配置目录（默认） | `%APPDATA%\CapsLockLite` |
| 配置目录（AppData 不可写时） | `%LOCALAPPDATA%\CapsLockLite`，再退到当前用户临时目录 |
| 文件匹配 | `*.cs`，**递归**扫描子目录（跳过 `.build`、`.import`、`settings`、重解析点） |

每个插件一个文件夹，源码与它自己的数据文件放在一起：

```text
plugins\
  trusted.txt             ← 已信任插件的 SHA-256
  com.example.crosshair\
    crosshair.cs          ← 插件源文件
    crosshair.ini         ← 插件自己写的配置（插件决定）
    settings.json         ← 宿主存 context.Get/Set 的值（如果用了设置页的存储）
```

「插件」页会显示根目录，「打开目录」可直接打开它。

导入方式有三种：

1. 「插件」页 →「导入插件…」，选择 `.cs` 或 `.zip`；
2. 把 `.cs` 或 `.zip` **直接拖进软件窗口**任意位置；
3. 手工把 `.cs` 放进 `plugins\<插件 Id>\`，选中「未信任」的行并点「信任」。

规则：

- **默认禁用、显式信任**：启动时不会加载任何未在 `trusted.txt` 中记录哈希的 `.cs`；文件被修改后哈希不匹配，会自动停止加载。
- **目录权限检查**：配置/插件目录必须只允许当前用户（以及 SYSTEM/Administrators）写入；如果 ACL 允许 Everyone、Users、Authenticated Users 等宽泛主体写入，宿主会拒绝加载插件。
- **导入只编译、不执行**：导入时只编译并检查是否包含唯一一个 `IPlugin` 类型，不会调用构造函数、`Initialize` 或 `CreateActions`；确认信任后才实例化。
- **信任前不建最终目录**：候选文件先放在 `plugins\.pending\<hash>\`，信任并加载成功后才移入 `plugins\<插件 Id>\`。
- **自动建目录**：加载成功后按插件 `Id` 创建 `plugins\<插件 Id>\`；同名文件覆盖，更新插件需要重新信任新哈希。
- **旧文件自动归位**：直接放在 `plugins\` 根下、且已信任的 `.cs` 加载成功后自动移动到它自己的目录。
- `.zip` 包：扫描包内所有 `.cs`（忽略 `__MACOSX`），逐个编译识别；包内没有 `.cs` 会被拒绝。
- 单个源文件上限 512 KB，压缩包最多扫描 500 个条目、解压总量上限 16 MB；包内路径会被压平，只保留文件名。
- 一次导入多个插件时，各自独立成败；拒绝信任时所有待启用文件都会被删除。

### 3.2 加载与编译

插件在以下时机加载：**插件已启用且启动时**、**信任/导入后**、**点击「重新加载」时**。每次加载都会重新编译所有**已信任**插件：

1. 用 .NET Framework 自带的 `csc.exe` 直接编译（不经过 `cmd.exe`），等价于：

   ```
   csc /target:library /langversion:5 /optimize+
       /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll
       /r:"<CapsLockLite.exe 的完整路径>"
       "<插件文件>"
   ```

   也就是说：**插件直接对着正在运行的宿主编译**，宿主 API 有变化会在编译期暴露，而不是运行期崩溃。编译超过 30 秒会被终止。

2. 编译产物放在 `plugins\.build\`，加载进内存后立即删除；编译失败时的诊断文字会被软件读取并保存。

3. 软件通过反射在编译结果里寻找**唯一一个** `public` 且实现 `CapsLockLite.IPlugin` 的类，创建实例并调用 `CreateActions()`。这一步只在文件已信任后发生。

4. 动作注册进动作目录，随即出现在快捷键编辑器的「执行动作」列表中。

> 旧程序集无法从 .NET 进程卸载；反复「重新加载」会累积少量内存，重启软件可彻底释放。卸载已加载插件时宿主会退出，以确保其代码停止。

### 3.3 执行

触发快捷键后：键盘钩子 → 动作队列（每 15ms 泵一次，每个动作有 25ms 防抖）→ 专用插件 STA 线程上的 `IPluginAction.Execute`。

- `Execute` 在**专用 STA 线程**上调用，不在主界面/键盘钩子线程；可以直接使用 WinForms 和剪贴板，但不要依赖主窗口控件。
- 单次执行建议控制在 **200ms 以内**；耗时操作可自行开线程，`PluginContext.Report`/`Error` 可跨线程调用，软件会自动切回界面线程。
- 抛出的异常会被宿主捕获：状态条显示 `插件动作失败：…`，并写入日志，**不会导致软件退出**。
- 实现可选的 `IPluginLifecycle` 时，宿主会在重载、禁用、卸载前调用 `Shutdown()`；未实现时，后台线程可能运行到进程退出，卸载已加载插件会要求退出程序。

---

## 4. 接口契约

全部类型位于命名空间 `CapsLockLite`，随宿主编译。

### 4.1 `IPlugin` — 插件入口

```csharp
public interface IPlugin
{
    string Id { get; }            // 全局唯一，小写点分，见第 5 节
    string Name { get; }          // 显示名，不能为空
    string Version { get; }       // 版本号，建议语义化版本
    string Author { get; }        // 作者，可为空
    string Description { get; }   // 一句话说明，可为空
    IPluginAction[] CreateActions();   // 每次加载调用一次
}
```

要求：

- 实现类必须是 `public`、非抽象、有公共无参构造函数；
- 一个文件里**只能有一个** `IPlugin` 实现；
- `CreateActions` 不得返回 `null`，也不得返回空数组。

### 4.2 `IPluginAction` — 一条动作

```csharp
public interface IPluginAction
{
    string Id { get; }                  // 插件内唯一，小写
    string Category { get; }            // 动作选择器里的分组
    string Name { get; }                // 动作名，不能为空
    string Description { get; }         // 编辑器里显示的一行说明
    ActionParamKind ParamKind { get; }  // 需要哪种参数输入
    string DefaultParam { get; }        // 初次选择时的默认参数
    bool Repeatable { get; }            // 是否允许「按住连发」
    void Execute(PluginContext context);
}
```

`CreateActions` 返回的实例会被**长期复用**（每次触发都调用同一实例），所以动作类可以持有状态，但要注意线程安全。

### 4.3 `PluginContext` — 每次触发的上下文

```csharp
public sealed class PluginContext
{
    public string PluginId { get; }   // 你的插件 Id
    public string ActionId { get; }   // 完整动作 Id：plugin.<插件 Id>.<动作 Id>
    public string Param { get; }      // 用户配置的参数，非 null，可能为空串

    public void Report(string message);  // 状态条：普通消息
    public void Error(string message);   // 状态条：错误样式
    public void Log(string message);     // 追加到 error.log
}
```

- `Report` / `Error` 会覆盖状态条上的上一条消息（约 4 秒后自动隐藏）。
- 三个方法都可以在后台线程调用。

### 4.4 `ActionParamKind` — 参数类型

| 枚举值 | 编辑器控件 | 值语义 |
| --- | --- | --- |
| `None` | 无 | 参数恒为空串 |
| `Count` | 数字框（1–100） | 十进制整数 |
| `Path` | 单行输入 + 浏览文件 | 文件 / 程序路径 |
| `Folder` | 单行输入 + 浏览文件夹 | 文件夹路径 |
| `Url` | 单行输入 | 网址（可省略协议） |
| `Text` | 多行输入 | 任意文本 |
| `KeyCombo` | 内联组合键编辑（修饰键开关 + 按键捕获 + 预览） | 形如 `Ctrl + Shift + ←` |

参数**始终以字符串传入**，请按 `ParamKind` 自行解析；`Param` 不会为 `null`。

### 4.5 可选：插件设置页

> 一个插件通常只该暴露 1–2 个动作，其余配置放进它自己的设置页——例如「屏幕准心」只有
> 一个开关动作，样式、颜色、尺寸、显示器都在设置页里。

需要设置界面的插件**额外**实现 `IPluginWithSettings`。不要改 `IPlugin`：给已有接口加成员会让
所有旧插件编译失败，而可选接口对不实现它的插件完全无影响。

```csharp
public interface IPluginWithSettings
{
    IPluginSettingsPage CreateSettingsPage();
}

public interface IPluginSettingsPage
{
    string Title { get; }                                  // 页面标题，如「准心」
    Control CreateView(IPluginSettingsContext context);    // 返回自己的界面
}

public interface IPluginSettingsContext
{
    string Get(string key, string defaultValue);
    void Set(string key, string value);
    bool Save();
    void Report(string message);
    void Error(string message);
    void Log(string message);
    bool DarkMode { get; }
    Color AccentColor { get; }
    int Dpi { get; }
}
```

宿主行为：

- 「插件」页选中带设置页的插件后，「设置」按钮可用；点击后在插件页内切换到二级页面，
  顶部有「← 返回」和页面标题。
- 每次打开调用一次 `CreateSettingsPage()`，再调用 `CreateView(context)`；离开时宿主销毁
  返回的控件；页面若实现 `IDisposable`，宿主也会调用 `Dispose()`。
- 内容放在宿主提供的滚动容器里：**宽度由宿主设置，高度请自己设置**（例如
  `view.Height = ...`），超出时滚动，滚动条与主界面同一套自绘样式。
- 离开页面时，如果通过 `Set` 改过值，宿主会自动 `Save()` 一次。
- 主题或 DPI 变化会重建界面并关闭已打开的设置页，重新打开即可。
- `CreateSettingsPage` / `CreateView` 抛出的异常会被宿主捕获并在状态条提示，页面不会打开，
  主程序不受影响。

编写建议：

1. **不要**通过反射或遍历控件去改宿主界面——宿主一改就坏；只用本节的公开接口。
2. 用 `context.DarkMode` / `context.AccentColor` / `context.Dpi` 让页面与宿主观感一致。
3. 持久化二选一：
   - 宿主存储（推荐）：`context.Get/Set/Save`，宿主写入
     `plugins\<插件 Id>\settings.json`，与插件源码同目录；
   - 自己写文件：适合需要在**宿主启动时**就恢复状态的插件（例如常驻覆盖层）。实现
     `IPluginHosted`（见 4.6）拿到 `DataDirectory`，把文件写在那里。
4. 交互复杂（实时预览、拖拽）时，逻辑放在自己的控件里；宿主只负责承载与滚动。

最小示例：

```csharp
public sealed class MyPlugin : IPlugin, IPluginWithSettings
{
    public IPluginSettingsPage CreateSettingsPage() { return new MySettingsPage(); }
}

internal sealed class MySettingsPage : IPluginSettingsPage
{
    public string Title { get { return "我的插件"; } }

    public Control CreateView(IPluginSettingsContext context)
    {
        Panel view = new Panel();
        view.Height = context.Dpi * 60 / 96;        // 高度由插件决定

        CheckBox box = new CheckBox();
        box.Text = "启用某某功能";
        box.Checked = context.Get("enabled", "1") == "1";
        box.CheckedChanged += delegate
        {
            context.Set("enabled", box.Checked ? "1" : "0");
            context.Save();
            context.Report("已" + (box.Checked ? "启用" : "停用") + "某某功能");
        };
        view.Controls.Add(box);
        return view;
    }
}
```

---

### 4.6 可选：插件自己的数据目录

插件文件位于 `plugins\<插件 Id>\`。如果插件需要在**宿主启动时**（设置页还没打开）读取自己的配置，
或想把数据文件放在源码旁边，实现可选的 `IPluginHosted`：

```csharp
public interface IPluginHosted
{
    void Initialize(PluginHostInfo host);
}

public sealed class PluginHostInfo
{
    public string PluginId { get; }        // com.example.crosshair
    public string PluginFile { get; }      // …\plugins\com.example.crosshair\crosshair.cs
    public string DataDirectory { get; }   // …\plugins\com.example.crosshair（一定存在）
    public string PluginsRoot { get; }     // …\plugins
    public int Dpi { get; }
    public bool DarkMode { get; }
    public Color AccentColor { get; }
}
```

调用时机：实例创建之后、`CreateActions()` 之前，每次加载调用一次。

> 导入阶段只编译检查元数据，**不会**创建插件实例或调用 `Initialize`；只有你确认信任、宿主真正加载插件时才会调用。
> 此时 `DataDirectory` 指向最终的 `plugins\<插件 Id>\`，可以在 `Initialize` 里读写自己的配置。

示例（准心插件）：

```csharp
public sealed class CrosshairPlugin : IPlugin, IPluginHosted
{
    public void Initialize(PluginHostInfo host)
    {
        CrosshairConfig.DataDirectory = host.DataDirectory;
    }
}
```

这样 `crosshair.ini` 与 `crosshair.cs` 就在同一个目录，迁移或备份只需要拷一个文件夹。
旧版本把配置写在配置目录根下也没关系：插件可以在新位置缺失时读取一次旧文件，再保存到新位置。

### 4.7 可选：停止/清理

插件如果启动了自己的线程、定时器、键盘钩子或窗口，建议额外实现 `IPluginLifecycle`：

```csharp
public interface IPluginLifecycle
{
    void Shutdown();
}
```

宿主在重载、禁用、卸载插件前调用 `Shutdown()`；如果插件同时实现 `IDisposable`，宿主也会调用 `Dispose()`。
这是**尽力而为**的约定：插件在 `Shutdown` 之外启动的代码仍可能运行到进程退出，所以卸载一个已加载的插件时，
宿主会退出程序，重新启动后才算彻底停止。请不要在 `Shutdown` 里做长时间阻塞操作。

---

## 5. 命名与校验规则（不满足则整个插件不加载）

### 5.1 插件 Id

- 正则：`^[a-z0-9][a-z0-9_-]*(\.[a-z0-9][a-z0-9_-]*)+$`
- 长度 ≤ 64，至少两段，例如 `com.example.screenshot`
- 在所有已加载插件中必须唯一

### 5.2 动作 Id

- 正则：`^[a-z0-9][a-z0-9._-]*$`
- 长度 ≤ 48
- 同一插件内唯一

### 5.3 完整动作 Id（软件自动拼接，你不要手写）

```
plugin.<插件 Id>.<动作 Id>
例如：plugin.com.example.screenshot.fullscreen
```

- 完整 Id 长度 ≤ 120；
- 与其它动作（含内置动作、其它插件动作）冲突时，**后加载的插件失败**。

### 5.4 其它硬性要求

| 检查 | 失败后果 |
| --- | --- |
| 编译 0 错误 | 插件标记「失败」，不影响其它插件 |
| 恰好一个 public `IPlugin` 实现 | 同上 |
| `Name` 非空（插件与动作） | 同上 |
| `CreateActions` 非 null、非空 | 同上 |
| 动作实例非 null | 同上 |

失败原因可在「插件」页双击该行查看，含 `文件(行,列): CSxxxx 说明`。

---

## 6. 触发与连发

- 插件动作可以绑定为 **Caps 层组合键**（必须勾选 Caps Lock，只支持 Caps 层，不注册系统级全局热键）。
- `Repeatable = false`：触发器只能是「按一次」。
- `Repeatable = true`：可选「按住连发」——按住约 450ms 后开始，每 33ms 触发一次（每秒 30 发）；受 25ms 同 id 节流限制。
- 连发只对「按住主键」有效；松开主键或 Caps Lock 立即停止。

---

## 7. 线程、异常与日志

| 事项 | 约定 |
| --- | --- |
| 执行线程 | 专用插件 STA 线程（有消息循环），可直接操作剪贴板、`System.Windows.Forms`；不是主界面/键盘钩子线程 |
| 时长预算 | 建议 < 200ms；长任务请 `ThreadPool` / `Task`，完成后用 `context.Report` |
| 异常 | 宿主捕获 → 状态条 + `error.log`；同一动作异常不会影响其它快捷键 |
| 日志 | `context.Log("…")` 写入 `<配置目录>\error.log`，前缀带插件 Id |
| 模态对话框 | 会阻塞插件动作队列（不影响主界面/钩子）；交互动作应自行防重入 |

### 7.1 需要界面交互的动作（进阶）

契约不提供任何 UI 接口，但 `Execute` 运行在宿主的**专用插件 STA 线程（带消息循环）**上，可以直接创建并显示自己的窗体。因此「框选区域」这类交互完全可以在插件内完成，**不需要修改插件规则**。

做法与约束：

1. **先定格，再显示窗口。** 例如范围截图：先用 `CopyFromScreen` 把屏幕冻结到位图，再显示全屏覆盖窗体；否则覆盖层会把自己截进去。
2. **用模态 `ShowDialog()` 等用户操作**，返回后立即销毁窗口；不要留下长期打开的窗体。
3. **自己防重入。** 选择期间宿主的热键仍然有效，用静态标志拒绝重复进入（示例里 `Shot.Busy` 让其它截图动作直接返回）。
4. **自己处理 DPI。** 宿主进程是 PerMonitorV2，`Screen`、`CopyFromScreen` 与窗体 `Bounds` 都是物理像素；按虚拟桌面矩形建窗即可与截图一一对应。
5. **界面自己画。** 宿主的主题与控件是内部实现，插件拿不到；需要统一观感就自己写颜色与字体。
6. **`Repeatable = false`。** 交互型动作不应支持按住连发。
7. **不要把无窗口的长耗时计算塞进 `Execute`。** 模态交互阻塞动作队列是预期行为，纯计算请自行开线程。

完整示例：`samples/plugins/screenshot-pro.cs`——全屏 / 活动窗口 / 自定义范围，范围选择带暗化预览、放大镜、手柄缩放与尺寸角标。

---

## 8. 插件缺失、卸载与配置保留

这是插件机制里最需要理解的一条：

- 快捷键配置里保存的是**完整动作 Id**（`plugin.xxx.yyy`）。
- 如果对应的 `.cs` 被删除、编译失败或改名，软件**不会**把快捷键改成「不执行任何操作」，而是：
  - 保留动作 Id、参数、触发类型；
  - 列表里显示为 `插件动作未加载：<完整 Id>`；
  - 触发时状态条提示 `插件动作未加载：…`。
- 插件恢复后，原快捷键**自动恢复可用**，参数不丢。

因此：卸载插件不会破坏用户的快捷键配置；已加载插件被卸载时程序会退出，重新启动后完成清理。

---

## 9. 安全模型（重要）

- 插件是**未沙箱化的本机代码**，与本软件同进程、同权限运行，可以读写文件、访问网络、调用任意 API。
- 插件子系统默认**禁用**；只有用户在「插件」页启用，并逐个信任文件（按 SHA-256）后才会加载。
- 导入时只编译并检查元数据，**不会**实例化插件；被修改过的已信任文件哈希不匹配，会自动停止加载。
- 信任一个插件意味着你允许它以当前用户权限执行任意代码。**只信任你已阅读并确认来源的插件文件**。
- 若以管理员身份运行 CapsLock Lite，插件也会获得管理员权限；不要在高权限会话中加载未审查的插件。
- 卸载已加载插件时宿主会退出，以确保其后台线程/钩子停止；这是 .NET 程序集无法单独卸载的补偿措施。

---

## 10. 兼容性

- 契约版本：`PluginRuntime.ApiVersion = 1`。
- 宿主新增能力时优先使用**可选接口**（例如 `IPluginWithSettings`）：不实现它的插件完全不受
  影响，只有实现了的插件才需要跟进。
- 插件每次加载都会针对**当前版本**的宿主编译：
  - 宿主新增接口成员、删除枚举值、改签名 → 插件会得到**明确的编译错误**，而不是运行期崩溃；
  - 因此升级 CapsLock Lite 后，请打开「插件」页确认状态，必要时更新插件源码。
- 插件不会因为宿主的小版本升级而失效，除非它用到了被改动的 API。

---

## 11. 完整示例：截图插件

完整源码见仓库 `samples/plugins/screenshot.cs`，导入并信任后即可用。核心结构：

```csharp
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using CapsLockLite;

namespace CapsLockLite.Samples
{
    public sealed class ScreenshotPlugin : IPlugin
    {
        public string Id { get { return "com.capslocklite.screenshot"; } }
        public string Name { get { return "截图"; } }
        public string Version { get { return "1.0.0"; } }
        public string Author { get { return "CapsLock Lite 示例"; } }
        public string Description { get { return "把全部屏幕或当前前台窗口保存为 PNG，并复制文件路径。"; } }

        public IPluginAction[] CreateActions()
        {
            return new IPluginAction[] { new FullScreenAction(), new ActiveWindowAction() };
        }
    }

    internal sealed class FullScreenAction : IPluginAction
    {
        public string Id { get { return "fullscreen"; } }
        public string Category { get { return "截图"; } }
        public string Name { get { return "全屏截图"; } }
        public string Description { get { return "截取所有屏幕；保存文件夹留空时存到图片库下的 CapsLockLite。"; } }
        public ActionParamKind ParamKind { get { return ActionParamKind.Folder; } }
        public string DefaultParam { get { return string.Empty; } }
        public bool Repeatable { get { return false; } }

        public void Execute(PluginContext context)
        {
            System.Threading.Thread.Sleep(260);              // 等按键松开
            Rectangle bounds = SystemInformation.VirtualScreen;
            using (Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);

                string folder = string.IsNullOrEmpty(context.Param)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CapsLockLite")
                    : Environment.ExpandEnvironmentVariables(context.Param.Trim());
                Directory.CreateDirectory(folder);

                string path = Path.Combine(folder,
                    "全屏-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
                bitmap.Save(path, ImageFormat.Png);

                try { Clipboard.SetText(path); } catch { }
                context.Report("截图已保存：" + path);
                context.Log("saved " + path);
            }
        }
    }
}
```

要点：

1. `Id` 是插件全局标识，动作 `Id` 在其内部唯一；`plugin.com.capslocklite.screenshot.fullscreen` 由宿主拼接。
2. 参数用 `Folder`，编辑器会自动给出「浏览文件夹」按钮；留空时插件自己回落到图片库。
3. `Thread.Sleep(260)` 是为了等组合键松开，否则可能把按键提示截进去。
4. 出错直接抛异常即可，宿主会显示并记录；也可用 `context.Error("…")` 自行处理。
5. 成功后用 `context.Report` 汇报，用 `Clipboard.SetText` 复制路径（插件动作运行在专用 STA 线程，可以直接用）。

> 需要交互（框选、确认框）的完整写法见第 7.1 节，以及 `samples/plugins/screenshot-pro.cs`。

---

## 12. 最小模板（复制即用）

```csharp
using CapsLockLite;

public sealed class MyPlugin : IPlugin
{
    public string Id { get { return "com.example.hello"; } }
    public string Name { get { return "示例插件"; } }
    public string Version { get { return "1.0.0"; } }
    public string Author { get { return "你的名字"; } }
    public string Description { get { return "演示用插件。"; } }

    public IPluginAction[] CreateActions()
    {
        return new IPluginAction[] { new HelloAction() };
    }
}

public sealed class HelloAction : IPluginAction
{
    public string Id { get { return "hello"; } }
    public string Category { get { return "示例"; } }
    public string Name { get { return "打招呼"; } }
    public string Description { get { return "在状态条显示一条消息。"; } }
    public ActionParamKind ParamKind { get { return ActionParamKind.Text; } }
    public string DefaultParam { get { return "你好"; } }
    public bool Repeatable { get { return false; } }

    public void Execute(PluginContext context)
    {
        context.Report("你好，来自插件：" + context.Param);
    }
}
```

---

## 13. 常见问题

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| `CS0246 找不到类型 IPlugin` | 缺少 `using CapsLockLite;` | 加上，或写全名 `CapsLockLite.IPlugin` |
| `CS0016 未能写入输出文件` | 编译临时目录不可写 | 软件已把编译临时目录指向 `plugins\.build`；若仍失败，检查该目录权限 |
| `CS0117 枚举中不存在…` | 用了不存在的参数类型 | 对照第 4.4 节的取值 |
| `CS0738 未实现接口成员` | 少写/写错成员 | 对照第 4 节逐个补齐，注意属性是**只读** `get` |
| 插件页显示「失败」 | 编译或校验未通过 | 双击该行看完整原因 |
| 快捷键显示「插件动作未加载」 | 插件被删/编译失败 | 恢复插件并重载；参数仍在 |
| 触发后无反应、无提示 | 动作未保存或绑定冲突 | 检查快捷键是否启用、是否与其它项重复 |
| 中文乱码 | 文件编码不是 UTF-8 | 另存为 UTF-8（建议带 BOM） |

---

## 14. 提交前自查清单

- [ ] 文件名以 `.cs` 结尾，UTF-8 编码
- [ ] 只有一个 `public` 的 `IPlugin` 实现，且有公共无参构造函数
- [ ] 插件 `Id` 符合小写点分规则，且全机唯一
- [ ] 每个动作 `Id` 小写、插件内唯一，`Name` 非空
- [ ] `CreateActions` 返回非空数组
- [ ] 只用 C# 5 语法（无插值字符串、无 `?.`、无 `nameof`）
- [ ] `Execute` 不弹模态对话框，耗时操作已放到后台线程
- [ ] 出错用 `context.Error` 或抛异常，不吞掉错误
- [ ] 导入后「插件」页显示「已加载」，并能绑定、能触发
