# 架构说明

## 进程与信任边界

```text
Flutter Windows GUI（Material 3 + Riverpod）
                    |
      随机 Session Key 鉴权的 NDJSON/TCP v1
                    |
OixNodeHost.exe（C# / .NET Framework 4.8）
                    |
             mihomo-oix.exe
```

Flutter 负责窗口、主题、自适应导航、动画、托盘和用户交互。它不读取 `credentials.dat`，也不直接管理 Core 或 Provider。

`OixNodeHost.exe` 保留原有稳定业务逻辑，负责 DPAPI、配置、节点刷新、端口映射、Provider 服务、回滚和 Core 生命周期。HostBridge 只监听随机回环端口，并要求每个请求携带当前会话的 Session Key。

Session Key 由 Flutter 使用安全随机数生成，通过 Host 标准输入传递。命令行只包含父进程 PID。Host 返回的设置只包含 `tokenConfigured`，不会把 Access Token 传回 Flutter。

## 前端层

- `HostLauncher`：定位同目录的 `OixNodeHost.exe`，启动 Host，传入父进程 PID，并验证协议版本。
- `HostClient`：为普通请求建立短连接，为 `events.subscribe` 保持状态流连接。
- Riverpod providers：管理 Host 生命周期、状态快照、设置、节点过滤、日志、导航和本地主题偏好。
- `AdaptiveShell`：在 `<600`、`600–1023` 和 `>=1024` 三档宽度之间切换导航布局。
- `window_manager` 与 `tray_manager`：实现无边框标题栏、关闭到托盘、显示、刷新、重启 Core 和退出。

退出到托盘只隐藏窗口。托盘“退出”会调用 `host.shutdown`，销毁托盘和窗口；如果 GUI 崩溃，Host 每两秒检查父 PID 并自行退出。

## Host IPC v1

HostBridge 支持以下方法：

| 方法 | 用途 |
|---|---|
| `snapshot.get` | 获取健康状态、节点和 Provider 地址 |
| `settings.get` | 获取不含 Token 的设置 |
| `settings.save` | 验证并保存设置，可更新 Token |
| `events.subscribe` | 订阅状态快照事件 |
| `actions.refresh` | 刷新 Provider 与节点 |
| `actions.restart` | 重启 Core |
| `actions.openDataFolder` | 打开运行数据目录 |
| `actions.openProvider` | 打开本地 Provider 地址 |
| `logs.get` | 获取脱敏日志尾部 |
| `host.shutdown` | 请求 Host 正常退出 |

协议使用每行一个 JSON 对象的 NDJSON 格式。每次请求都带 `id`、`auth` 和 `method`；参数存在时使用 `params`。单条请求上限为 1 MiB。

## 后端组件

- `CoreSupervisor`：通过公开的 `-oix-token`、`-oix-provider-name` 参数启动官方核心；执行配置预检并用 Job Object 管理生命周期。
- `CoreClient`：使用标准 Controller API 刷新 Provider、从 `/providers/proxies` 的 `oixCloud` Provider 读取节点、设置 `/oix/options`，并热加载 `/configs?force=true`。全局 `/proxies` 不作为节点来源。
- `RuntimeConfigBuilder`：从零生成助手专用最小 YAML；为每个稳定端口生成隐藏 `select` 组，通过 `use: [oixCloud]` 引入 Provider，再让 listener 引用该组。运行配置带 schema 标记。
- `NodeMapper`：保存节点名到本地端口的映射，支持保留期和最久未使用回收。
- `ProviderServer`：只绑定回环地址，发布只读 Provider，并对管理操作鉴权。
- `CredentialStore`：使用 DPAPI `CurrentUser` 加密 Token 与 Controller Secret。

## 刷新事务

```text
官方核心依据 Token 刷新 oixCloud Provider
                    |
            读取并过滤节点
                    |
      分配稳定端口与隐藏路由组
                    |
          生成 runtime.next.yaml
                    |
           mihomo-oix -t 预检
                    |
       备份 runtime.last-good.yaml
                    |
        PUT /configs?force=true 热加载
                    |
          验证所有本地监听端口
              /             \
           成功              失败
            |                 |
     发布新节点快照       恢复 last-good
```

配置、监听或节点验证失败时，HTTP Provider 继续返回上一份成功快照。连续空节点保护用于区分临时网络故障和真实空账户。

## 打包

`build.ps1` 先编译 C# Host、旧 WinForms 回退和 C# 自测，再构建 Flutter Windows Release。最终 Release 目录会加入 `OixNodeHost.exe`、`core\mihomo-oix.exe` 和托盘图标。

`installer/OixNodeHelper-Flutter.iss` 把整个 Flutter Release 目录打包为当前用户安装程序。简体中文翻译文件随仓库保存，避免依赖 Inno Setup 安装目录中的可选语言资源。

## 明确不做

- 不读取或修改 FlClash 数据目录。
- 不接收账户密码，只接收用户从官网复制的 Token。
- 不分析、解密或逆向 OixCloud 私有组件和通信协议。
- 不提取、记录或展示代理服务器地址/IP。
- 不向局域网或公网开放本地服务。
