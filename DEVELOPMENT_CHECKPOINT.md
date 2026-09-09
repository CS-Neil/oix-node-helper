# Flutter 重构开发断点

保存时间：2026-09-07（Asia/Shanghai）

## 目标

```text
Flutter Windows GUI（Material 3 + Riverpod + 自适应动画）
                    |
       本机鉴权 NDJSON/TCP 协议 v1
                    |
C# OixNodeHost.exe（保留稳定业务逻辑）
                    |
             mihomo-oix.exe
```

Flutter 是默认 GUI；旧 WinForms GUI 只作为 legacy fallback。

## 本轮完成

- 修复 `host_client.dart` 的最后一条 `use_null_aware_elements` lint。
- `flutter test` 全部通过；`flutter analyze --no-pub` 为 `No issues found`。
- 安装 Visual Studio Build Tools 2022 17.14.39，包含 Desktop development with C++、MSVC、CMake 和 Windows SDK 10.0.26100.0。
- 启用 Windows 开发者模式，解决 Flutter 插件符号链接失败。
- `build.ps1` 现在会从 PATH、`FLUTTER_ROOT` 和 `%USERPROFILE%\develop\flutter` 自动查找 Flutter。
- 完整 Flutter Windows Release 构建成功，未出现插件/API 编译兼容问题。
- 安装 Inno Setup 6.7.3；`build.ps1` 增加 PATH、用户级和系统级安装位置探测。
- 将 MIT 授权的 `ChineseSimplified.isl` 与许可证固定在 `installer/`，消除对 Inno Setup 可选语言文件的机器依赖。
- `build/OixNodeHelper-Flutter-Setup.exe` 编译成功。
- 最终安装包已在 `build/installer-smoke` 做静默安装验证；关键二进制与 Release SHA-256 一致，新 README 和翻译许可证均包含，卸载返回 0 且隔离目录成功删除。
- 退出检查点中占用默认端口的旧 WinForms 实例后，安装版成功建立 GUI → Host → Core 进程链。
- 强制终止 Flutter GUI 后，Host 与 Core 均由父进程监控/Job Object 自动清理。
- `README.md` 与 `ARCHITECTURE.md` 已改为 Flutter 默认架构，并明确旧 WinForms 是兼容回退。

## 当前构建环境

- Flutter 3.47.2 stable，Dart 3.13.2。
- Visual Studio Build Tools 2022 17.14.39。
- Windows SDK 10.0.26100.0。
- Inno Setup 6.7.3，用户级安装。
- Windows 开发者模式已启用。
- Flutter 尚未加入用户 PATH，但 `build.ps1` 可以自动发现 `%USERPROFILE%\develop\flutter\bin\flutter.bat`。
- 当前目录仍不是有效 Git 工作树，无法创建 checkpoint commit。

## 已验证命令

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\run-tests.ps1

cd app
%USERPROFILE%\develop\flutter\bin\flutter.bat test
%USERPROFILE%\develop\flutter\bin\flutter.bat analyze --no-pub
%USERPROFILE%\develop\flutter\bin\flutter.bat doctor -v

cd ..
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

`flutter doctor -v` 已通过 Windows 与 Visual Studio 检查。Android SDK 缺失与此 Windows-only 项目无关；Flutter/Dart 不在 PATH 的提示由构建脚本自动发现覆盖。

## 待完成

1. 在可操作原生 Windows 窗口的环境中完成视觉与交互 QA：
   - `<600`、`600–1023`、`>=1024` 三档布局。
   - 浅色、深色、跟随系统、纯黑主题。
   - Shared Axis 页面过渡与状态卡片动画。
   - 最小化、最大化、关闭到托盘、托盘显示/刷新/重启/退出。
   - 正常退出时 `host.shutdown`，以及 Host 异常退出后的 GUI 错误与恢复。
2. 如准备公开分发，为 `OixNodeHelper-Flutter-Setup.exe` 配置代码签名。

本次环境的 Computer Use 接口只暴露浏览器，没有可调用的原生应用控制方法，因此视觉和托盘点击 QA 无法自动完成。进程级启动、父子进程清理、安装和卸载已经验证。

## 重要约束

- Flutter 不直接读取或保存 `credentials.dat`。
- Session Key 和 Access Token 不得进入命令行、日志或状态快照。
- 不删除现有 C# Core/Provider/回滚逻辑；Flutter 只通过 HostBridge 控制它们。
- 关闭到托盘时不能关闭 Host；只有托盘“退出”调用 `host.shutdown`。
- 正常联调前确认旧 WinForms 实例没有占用默认 Provider 端口 6172。
- FlClash 只作为视觉和交互参考，不复制其 GPL Dart 组件或品牌素材。

## 2026-09-09：FlClash 节点 timeout 的定位结论

现场取证：`mihomo-oix.exe` 运行 8 小时后持有 11,140 个套接字、852 MB 内存、10,783 个句柄，其中约 9,400 个处于 `Bound`（已发起、从未握手成功、也从未关闭），增速约 2,000/小时；`FlClashCore.exe` 另持 4,573 个。系统内 `LocalPort >= 49152` 的占用达 16,046，而 Windows 临时端口池只有 16,384 个，另有约 760 个被 Hyper-V 保留——端口池已经耗尽。此时单个本地端口仍可用（7201/7210/7230/7250/7270 均返回 HTTP 204，0.5–1.3 s），但 FlClash 成批测速会全部 timeout。

机制：助手核心当时的 `dns: enable: false` 让它使用 Windows 系统解析器，而 FlClash 把系统 DNS 指向自己的 TUN 并启用 fake-ip，于是 `oixcloud.com` 解析成 198.18.0.12、`www.gstatic.com` 解析成 198.18.0.8。核心拨号这些假地址被 TUN 抓走，还原域名后重新进入 FlClash 规则链；规则链里没有按进程放行 mihomo-oix，命中的策略组又 `use:` 本地 provider，于是回到 `127.0.0.1:72xx`，闭环成立。

已落地的修复：核心改用 IP 字面量 DoH 自解析（普通 UDP 53 会被 `tun.dns-hijack` 拦截，所以不能用）；`CoreHealthMonitor` 监控套接字/句柄/内存/临时端口并在持续告急时重启核心；轮询默认从 60 s 提高到 300 s，并对已持久化的低值做一次性迁移；示例配置与 README 写明必须置顶的三条防回环规则。

闭环的最后一环仍在用户侧：FlClash profile 必须把 `PROCESS-NAME,mihomo-oix.exe,DIRECT` 放在规则最前面。
