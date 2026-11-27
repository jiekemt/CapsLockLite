# 打包规则

以后生成发布包或源码包时，**不要包含 `preview/` 文件夹及其中任何截图文件**。

## 发布包

包含：

- `CapsLockLite.exe`
- `CapsLockLite-SHA256.txt`（exe 的 SHA-256）
- `interception.dll`（64 位；可选的驱动注入后端用，放在 exe 同目录，见下文「第三方组件」）
- `interception-LICENSE-LGPL-3.0.txt`（上面那个 DLL 的许可文本，必须同包分发）
- `README.md`
- `LICENSE`
- `PLUGIN.md`（插件开发指南，随程序分发）
- `screenshot-pro.cs`（示例插件，拖进窗口即可导入）

## 代码签名与校验

`build.ps1` 在设置 `SIGN_CERT_SHA1` 时调用 `sign.ps1` 做 Authenticode 签名（`SIGNTOOL` 可指定 signtool 路径，`SIGN_TIMESTAMP_URL` 可指定时间戳服务器）。没有证书时至少保留 `SHA256SUMS.txt`；用户应核对 exe/zip 的 SHA-256。

## 源码包

包含：

- `src/`
- `assets/`（构建所需图标、Logo、图标脚本）
- `samples/`（示例插件）
- `third_party/interception/`（`x64/interception.dll`、许可文本与来源说明，见下文「第三方组件」）
- `app.manifest`
- `build.ps1`
- `package.ps1`
- `sign.ps1`
- `README.md`
- `README.en.md`
- `SOURCE.md`
- `PLUGIN.md`（插件开发指南）
- `PACKAGING.md`（本文件）
- `CONTRIBUTING.md`
- `CHANGELOG.md`
- `SECURITY.md`
- `CODE_OF_CONDUCT.md`
- `docs/images/`（README 使用的界面截图）
- `LICENSE`
- `.gitignore`

同样不包含 `preview/`。

## 第三方组件（interception.dll）

「设置 → 行为 → 注入方式 → 驱动注入」需要 64 位的 `interception.dll`。它放在仓库的 `third_party/interception/` 下，打包时随发布包发到 exe 旁边、随源码包整个目录带上。

规则：

- **只分发用户态 DLL，不分发内核驱动**：`keyboard.sys` / `mouse.sys` 和 `install-interception.exe` 一律不进包，由用户自行安装（README「注入方式（可选驱动）」写了步骤）。把驱动打进一个普通应用包会触发杀软，也需要管理员安装，不是本程序的职责。
- 该 DLL 来自 [oblitum/Interception](https://github.com/oblitum/Interception) v1.0.1 的 `library/x64/interception.dll`，**未做任何修改**；SHA-256 记录在 `third_party/interception/README.txt` 里，升级时同步更新该文件。
- 许可：非商业用途 **LGPL 3.0**，商业用途需向上游作者单独授权。因此许可文本必须随包分发（`interception-LICENSE-LGPL-3.0.txt`），源码包里同时保留来源说明。
- 缺这个 DLL 时 `package.ps1` 直接报错退出，不允许打出一个“驱动注入永远回退”的包——这正是 1.26.0 首版发布包出现的问题。
- 32 位版本（`library/x86`）不打包：程序在 64 位系统上以 64 位运行。

## 插件包

包含：

- `screenshot-pro.cs`（可直接导入的截图插件源文件）
- `PLUGIN.md`（插件编写规范）
- `README.txt`（导入步骤与依赖说明）

解压后把 `screenshot-pro.cs` 拖进「插件」页导入即可，不需要编译；也可以直接把整个 zip 拖进软件窗口，软件会自动取出其中的 `.cs` 并导入。

## 使用

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

脚本会先构建，再分别生成 `dist` 下的发布包、源码包和插件包，并在打包后校验三个压缩包内都不存在 `preview/` 路径。

## 版本归档

**旧版本的打包文件不删除。** 打包脚本会把不是当前版本的产物移动到：

```text
dist\archive\vX.Y.Z\
```

同一版本的三类包（发布包、源码包、插件包）和对应的 `SHA256SUMS.txt` 一起归档，按版本号分文件夹存放；`dist` 根目录只保留当前版本的 exe、三个压缩包和校验文件，避免使用者误运行旧构建。每个版本的 exe 都能从归档的发布包中取回，所以不需要单独保留 exe。
