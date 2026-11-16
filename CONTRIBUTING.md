# 参与贡献

感谢你对 CapsLock Lite 的关注。本文说明如何搭建开发环境、验证改动，以及提交 Issue / Pull Request 时需要注意的事项。

## 行为准则

参与本项目即表示你同意遵守 [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md)。

## 开发环境

- Windows 10 / 11（x64）。
- .NET Framework 4.8（Windows 10/11 自带）。
- 不需要 Visual Studio、MSBuild 或 NuGet。构建脚本直接调用系统自带的 `csc.exe`。
- Git（可选，用于提交补丁）。

### 构建

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

输出：`dist\CapsLockLite.exe`。

### 自检

```powershell
.\dist\CapsLockLite.exe --selftest
```

结果写入 `dist\selftest.log`，进程退出码非 0 表示失败。自检覆盖默认键位、配置读写、键位解析、Caps 层状态机、重复组合检测、插件编译与注册、压缩包导入、键盘钩子安装等。

> 提示：如果仓库放在授予 `Users` / `Authenticated Users` 写权限的目录（某些同步盘、沙盒工作区），自检里的「配置目录是否私有」会按设计失败。把仓库复制到普通用户目录再跑即可。

### 打包（可选）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

规则见 [PACKAGING.md](PACKAGING.md)。发布包、源码包都不包含 `preview\` 截图。

## 代码结构

源码全部在 `src\`，按功能拆成多个 `partial class MainForm` 文件和独立组件。完整说明见 [SOURCE.md](SOURCE.md)，其中包含各子系统的设计约束。开始改动前建议至少阅读与你改动相关的章节。

几个容易踩的约束：

- 目标语言是 **C# 5**（`/langversion:5`），不要使用字符串插值、`?.`、`nameof`、元组等新语法。
- 不引入第三方 NuGet 依赖；界面全部自绘，使用 `Theme.*` 设计令牌，不要硬编码颜色和尺寸。
- 新增内置动作要同步三处：`ActionCatalog`（定义）、`ActionExecutor.Execute`（实现）、需要宿主能力时再加到 `IAppHost`。
- 插件契约只在 `PluginApi.cs` 中定义；修改它等于修改插件 ABI，必须同步更新 `PLUGIN.md`。
- 快捷键只在 Caps 层生效，不注册系统级全局热键；这是产品边界，不要绕过。

## 提交 Issue

- 先搜索现有 Issue，避免重复。
- Bug 请使用 [Bug 报告模板](https://github.com/jiekemt/CapsLockLite/issues/new?template=bug_report.yml)，尽量附上：
  - CapsLock Lite 版本（侧栏底部或「帮助与关于」）；
  - Windows 版本与显示缩放比例；
  - 复现步骤、期望结果、实际结果；
  - 相关的 `%APPDATA%\CapsLockLite\error.log` 片段（注意先删除个人信息）。
- 功能建议请使用 [功能请求模板](https://github.com/jiekemt/CapsLockLite/issues/new?template=feature_request.yml)，说明使用场景，而不只是实现方式。
- 安全问题请不要公开提交 Issue，见 [SECURITY.md](SECURITY.md)。

## 提交 Pull Request

1. Fork 本仓库并从 `main` 创建分支，分支名建议 `fix/...`、`feat/...`、`docs/...`。
2. 保持改动聚焦：一个 PR 只做一件事，避免混入无关的格式化或重命名。
3. 提交前运行构建与自检，确保 `RESULT: PASS`。
4. 涉及界面的改动请附截图（浅色和深色各一张）；涉及交互的改动请说明键鼠操作路径。
5. 在 PR 描述中写清楚：改动内容、原因、验证方式、是否影响配置兼容性或插件 ABI。
6. 如果改动会改变用户可见行为，同步更新 `README.md` / `README.en.md` / `CHANGELOG.md`。

### 提交信息

使用简短的祈使句，推荐 Conventional Commits 前缀：

```text
fix: 修复 Caps + Alt 组合在部分程序中被识别为 Alt 单键
feat: 支持为宏步骤设置命名
docs: 补充插件信任模型说明
```

### 代码风格

- 4 空格缩进，大括号另起一行（与现有代码一致）。
- 保持文件编码为 UTF-8（无 BOM），换行符由 `.gitattributes` 统一。
- 注释用中文或英文均可，但同一文件内保持一致；面向用户的字符串目前是简体中文。
- 不要提交 `dist\`、`bin\`、`obj\`、日志或本地配置。

## 插件贡献

示例插件位于 `samples\plugins\`。插件是拥有当前用户全部权限的本机代码，提交前请确保：

- 不访问网络、不收集数据，除非在 PR 中明确说明并给出开关；
- 不写入插件目录以外的位置；
- 对文件、进程、注册表等敏感操作有明确的用户交互或错误处理；
- 在 `PLUGIN.md` 中补充必要的参数说明。

## 许可证

提交贡献即表示你同意以 [MIT License](LICENSE) 授权你的贡献。
