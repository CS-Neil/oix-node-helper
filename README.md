# OixNodeHelper for Windows

OixNodeHelper 是一个 Windows 桌面与托盘应用，用来实现“只更新 OixCloud 节点，FlClash 的策略组和规则保持不变”。从 0.3.0 开始，默认界面是 Flutter Windows；原有 WinForms 界面继续保留为兼容回退。

节点只从 [OixCloud 官方 Token 页面](https://oixcloud.com/user/token) 对应的 managed subscription 获取。Access Token 交给官方 `mihomo-oix` 核心处理，助手再从核心公开的 `oixCloud` Provider 读取节点。助手不读取或修改 FlClash 的 `config.yaml`、数据库或 profile，也不实现 OixCloud 的私有订阅协议。

## 安装

推荐使用 Flutter 安装包：

```text
build\OixNodeHelper-Flutter-Setup.exe
```

安装器默认安装到 `%LOCALAPPDATA%\Programs\OixNodeHelper`，不需要管理员权限。安装包包含 Flutter GUI、`OixNodeHost.exe`、Windows x64 版 `mihomo-oix`、Flutter 运行库和 FlClash Provider 示例。

便携版位于：

```text
app\build\windows\x64\runner\Release
```

`build\OixNodeHelper.exe` 和 `build\OixNodeHelper-Setup.exe` 是旧 WinForms GUI 及其安装器，只用于兼容和故障回退。

## 首次使用

1. 在浏览器登录 `https://oixcloud.com/user/token`，复制 Access Token。
2. 启动 OixNodeHelper，打开“设置”，粘贴 Token。
3. 保持默认 Controller `http://127.0.0.1:6173`、Provider 端口 `6172`、节点端口 `7200-7299`。
4. 点击“保存并重启”，等待概览页显示 Core 正常运行。
5. 访问 `http://127.0.0.1:6172/health` 检查服务状态。
6. 把 `config\flclash-provider.yaml.example` 合并进自己长期维护的 FlClash 配置。

实际合并时，保留自己的 `proxy-groups` 和 `rules`；只增加 `proxy-providers.oixcloud-local`，并让相关策略组通过 `use: [oixcloud-local]` 引用它。

关闭主窗口会把程序隐藏到托盘，并继续运行 Host 和 Core。只有托盘菜单中的“退出”会关闭 GUI、Host 和 Core。

## 界面

Flutter GUI 使用 Material 3 和 Riverpod，提供概览、节点、设置、日志和关于页面。界面支持浅色、深色、跟随系统、纯黑背景、Windows 动态颜色和自适应导航：

- 窗口宽度小于 600 时使用底部导航。
- 600–1023 使用紧凑侧边导航。
- 1024 及以上使用展开侧边导航。

## 数据流

```text
Flutter Windows GUI
         |
鉴权 NDJSON/TCP（127.0.0.1，协议 v1）
         |
OixNodeHost.exe
         |
官方 mihomo-oix managed subscription
         |
助手专用核心（127.0.0.1:6173）
         |
稳定节点端口（127.0.0.1:7200-7299）
         |
本地 Provider（127.0.0.1:6172/clash）
         |
FlClash 中由用户维护的策略组与规则
```

## 可靠性与安全

- 候选配置先由官方核心执行 `-t` 验证，再通过 Controller API 热加载。
- 热加载后检查所有节点监听端口；失败时自动恢复上一份可用配置。
- 每个本地端口通过隐藏策略组绑定一个 Provider 节点；运行配置结构升级时按 schema 自动重建。
- 节点源连续返回空列表前保留旧快照，避免临时故障清空节点。
- 消失节点的端口默认保留 14 天，端口不足时才回收最久未使用的记录。
- Token 和 Controller Secret 由 Windows DPAPI `CurrentUser` 加密保存。
- Flutter 不直接读写 `credentials.dat`，IPC 也只返回 `tokenConfigured`。
- GUI 每次启动生成随机 Session Key，通过 Host 标准输入传递；Session Key 不进入命令行、日志或状态快照。
- Provider、Controller、IPC 和所有节点端口只监听 `127.0.0.1`。
- `/api/refresh` 要求 `Authorization: Bearer <Controller Secret>`；只读 Provider 无需鉴权。
- 日志对 Token 和 Secret 脱敏，不捕获或记录核心原始输出。
- GUI 异常退出时，Host 的父进程监控与 Windows Job Object 会清理 Host 管理的 Core。

Token 通过官方核心的 `-oix-token` 参数传入，因此同一 Windows 用户下、具有进程检查权限的软件仍可能读取核心命令行。不要在不可信的 Windows 账户或共享会话中运行。

## 本地接口

| 路径 | 用途 |
|---|---|
| `GET /health` | 阶段、核心状态、节点数、上次成功时间和错误 |
| `GET /clash` | Mihomo/Clash `proxy-provider` YAML |
| `GET /list` | Surge external proxy 列表 |
| `GET /api/nodes` | 节点名称与本机端口 JSON |
| `POST /api/refresh` | 带 Controller Secret 鉴权的异步刷新 |

运行数据位于 `%LOCALAPPDATA%\OixNodeHelper`。卸载默认保留该目录。

## 构建与测试

构建需要：

- .NET Framework 4.8 编译器。
- Flutter stable，启用 Windows desktop。
- Visual Studio 2022 的“Desktop development with C++”工作负载，包括 MSVC、CMake 和 Windows SDK。
- Windows 开发者模式，用于 Flutter 插件符号链接。
- Inno Setup 6；缺少时仍会生成便携版，但不会生成 Flutter 安装包。

运行 C# 自测：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\run-tests.ps1
```

运行 Flutter 测试和静态分析：

```powershell
cd app
flutter test
flutter analyze --no-pub
```

完整构建：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

`build.ps1` 会从 PATH、`FLUTTER_ROOT` 或 `%USERPROFILE%\develop\flutter` 查找 Flutter，并从 PATH、当前用户或系统级安装目录查找 Inno Setup。

主要输出：

```text
app\build\windows\x64\runner\Release\oix_node_helper.exe
build\OixNodeHelper-Flutter-Setup.exe
build\OixNodeHost.exe
build\OixNodeHelper.Tests.exe
```

## 当前边界

- 助手只调用官方核心公开的 CLI 和 Mihomo Controller API，不实现或逆向 OixCloud 私有协议。
- 节点名称是 Controller 公开的标识，也是稳定端口映射键；服务端重命名节点时会视为新节点。
- 安装包是本地构建，尚无商业代码签名证书，Windows SmartScreen 可能显示“未知发布者”。

## GitHub 发布与凭据安全

真实 Token 只能通过应用设置界面写入 Windows DPAPI 加密的运行时凭据文件。不要把 Token 放进源码、测试、示例配置、截图、日志、Issue、提交信息或构建脚本。

首次初始化 Git 仓库后，启用项目自带的提交前扫描：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\enable-git-hooks.ps1
```

随时可以扫描整个待发布工作区：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\scan-secrets.ps1 -All
```

每次推送前仍应人工检查 `git status --short` 和 `git diff --cached`。公开仓库还应在 GitHub 的安全设置中启用 Secret scanning 与 Push protection，并将 `Secret scan` 工作流设为默认分支的必需检查。完整步骤见 [`docs/RELEASE_CHECKLIST.md`](docs/RELEASE_CHECKLIST.md)。

如果真实 Token 曾进入提交、Issue 或 Release，必须立即在服务提供方撤销或轮换；仅删除文件或改写 Git 历史不能恢复该 Token 的安全性。
