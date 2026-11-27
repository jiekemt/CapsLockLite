# 驱动注入：安装与排查

CapsLock Lite 默认用 Windows 的 `SendInput` 发送按键，桌面程序都能收到。但**游戏**常用 Raw Input / DirectInput 直接读设备，系统注入的事件会被忽略，表现为「按了 Caps 层快捷键，游戏里没反应」。这时可以换成 **Interception 键鼠过滤驱动** 注入：按键从内核驱动层发出，与真实硬件无法区分。

> 不需要玩游戏的话，**保持默认的「系统注入」即可**，不必安装驱动。

## 1. 什么时候需要装

- 游戏或反作弊软件只认原始设备输入（Raw Input / DirectInput），Caps 层快捷键在里面无效；
- 其他桌面程序一般**不需要**，`SendInput` 就够了。

## 2. 准备

| 项目 | 要求 |
| --- | --- |
| 系统 | Windows 10 / 11，**64 位** |
| 权限 | 安装驱动需要**管理员**权限 |
| 内核设置 | 若开启了「内核隔离 → 内存完整性」，可能拒绝加载未签名驱动，需要临时关闭并重启 |
| 用户态库 | 发布包已附带 64 位 `interception.dll`；从源码构建时它在 `third_party/interception/x64/` |

驱动本身（`keyboard.sys` / `mouse.sys` 与安装器）**不随本程序分发**，需要从上游获取：

- Interception 项目主页：<https://github.com/oblitum/Interception>
- 在它的 Releases 页面下载压缩包（例如 `Interception.zip`）。

## 3. 安装步骤

1. 解压下载的 Interception 压缩包，得到 `install-interception.exe`（通常在 `Interception\command line installer\` 下）。
2. 在开始菜单搜索「命令提示符」→ 右键 → **以管理员身份运行**。
3. 用命令行进入解压目录，执行：

   ```bat
   install-interception.exe /install
   ```

4. **重启电脑**（驱动需要重启后才会加载）。
5. 把 `interception.dll`（64 位）放在 `CapsLockLite.exe` **同一个文件夹**里。发布包已经放好了；从源码构建时从 `third_party/interception/x64/` 复制过去。

## 4. 在本程序里启用

1. 打开 CapsLock Lite → **设置 → 行为 → 注入方式**。
2. 选「**驱动注入（Interception）**」。默认的「自动」在检测到驱动时也会优先使用驱动。
3. 状态栏应显示「注入方式：驱动（Interception）」。
4. 想确认的话，退出再启动一次，然后看 `%APPDATA%\CapsLockLite\error.log` 第一行附近：

   ```text
   输入注入方式：Interception
   ```

## 5. 装完不生效？按这个顺序排查

| 现象 | 原因 / 处理 |
| --- | --- |
| 状态栏提示「驱动注入不可用，已回退到系统注入」并给出原因 | 驱动没装好或 `interception.dll` 不在 exe 旁边。重装驱动并确认 dll 位置，然后重启程序。 |
| 「行为」里选了驱动，但日志仍写 `输入注入方式：SendInput` | 同上；也可能是 32 位系统或 dll 架构不对（必须是 64 位）。 |
| 安装时报「未能安装驱动」/ 蓝屏 | 内存完整性（内核隔离）或杀毒软件拦了未签名驱动。关闭内核隔离后重启再装；仍失败就保持默认系统注入。 |
| 装好后游戏仍收不到 | 确认游戏是前台窗口、`注入方式` 确实是驱动，且驱动已重启生效；个别反作弊会直接拦注入，这种情况本程序也无能为力。 |
| 装完某些设备行为异常（触摸板/多鼠标） | Interception 会接管全部鼠标键盘设备，属于上游驱动的行为；不需要时按下一节卸载。 |

## 6. 卸载 / 回退

1. 「注入方式」改回「系统注入（SendInput）」。
2. 管理员命令行执行：

   ```bat
   install-interception.exe /uninstall
   ```

3. 重启电脑。之后 `interception.dll` 留着也无害（检测不到驱动会自动回退）。

## 7. 许可与来源

随包附带的 `interception.dll` 来自 [Interception](https://github.com/oblitum/Interception) v1.0.1，**未做任何修改**，按 LGPL 3.0 授权（许可文本随包：`interception-LICENSE-LGPL-3.0.txt`）。商业使用需向上游作者单独取得授权。内核驱动与安装器由用户自行从上游获取并安装，本程序不再分发。
