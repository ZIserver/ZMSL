## ZMSL.App 项目概览

- **项目名称**: 智穗MC 开服器桌面客户端（ZMSL.App）
- **主要用途**: 为 Minecraft 服务器提供一站式本地启动器与管理工具，集成服务器创建/启动、Java 环境管理、服务端核心下载、FRP 内网穿透、账号登录与用量统计、自动更新等功能。
- **运行平台**: Windows 桌面（.NET 8 + WinUI 3）。

## 技术栈与整体架构

- **技术栈**:
  - **UI**: WinUI 3（XAML + 代码隐藏）
  - **模式**: MVVM（CommunityToolkit.Mvvm）
  - **数据访问**: Entity Framework Core + Sqlite 本地数据库
  - **网络通信**: HttpClient/IHttpClientFactory 调用后端 API（ZMSL.Api）与第三方 MSL API
  - **系统集成**: Windows 通知（AppNotification）、文件/文件夹选择器、进程管理（java、frpc、服务器 Jar）

- **目录结构（逻辑分层）**:
  - **Converters/**: XAML 绑定用的值转换器（如布尔 → 可见性、核心类型 → 插件按钮可见性）。
  - **Models/**: 本地持久化数据模型（服务器、下载记录、FRP 隧道、应用设置等）。
  - **Services/**: 业务服务层，封装 API 调用、本地数据库、服务器进程、FRP、Java 管理、更新等逻辑。
  - **ViewModels/**: MVVM 的视图模型，承上启下连接 Views 与 Services。
  - **Views/**: WinUI 页面与对话框（首页、登录、服务器管理、FRP、设置、创建服务器向导等）。
  - **App.xaml/App.xaml.cs**: 应用入口、依赖注入、全局异常与更新检查。
  - **MainWindow.xaml/.cs**: 主窗口框架（NavigationView 导航、状态栏、主题切换、登录强制控制）。

## 启动与运行流程

- **应用启动 [App](file:///d:/Desktop/ZMSL/ZMSL.App/App.xaml.cs)**:
  - 初始化 XAML 资源与 DI 容器（IServiceProvider）。
  - 注册 **DatabaseService、ApiService、AuthService、ServerDownloadService、ServerManagerService、FrpService、UpdateService、JavaManagerService** 等单例服务。
  - 配置命名 HttpClient `"ZmslApi"`，指向 `https://msl.v2.zhsdev.top/api/`，忽略证书验证，用于访问自建 ZMSL 后端。
  - 注册 Windows 通知系统，并挂接全局异常处理。
  - `OnLaunched` 中创建并激活 **MainWindow**，随后后台异步检查版本更新并尝试静默下载更新包，完成后通过 Windows 通知提示用户重启应用更新。

- **主窗口框架 [MainWindow](file:///d:/Desktop/ZMSL/ZMSL.App/MainWindow.xaml.cs)**:
  - 使用自定义标题栏 + Mica 背景，设置窗口大小（1280×800）、图标与标题“智穗MC开服器”。
  - 标题栏右侧提供：
    - **主题切换按钮**: 在亮/暗主题间切换，更新 `ThemeIcon` 与 `ThemeText`。
    - **用户按钮**: 显示当前用户名 / “未登录”，点击可查看用户信息并退出登录，未登录时跳转到登录页。
  - 主体使用 **NavigationView + Frame**：
    - 菜单项: 首页(Home)、我的服务器(MyServer)、FRP 穿透(Frp)。
    - 底部: 设置(Settings)。
  - 状态栏展示实时状态：
    - 文本状态（`StatusText`）。
    - 服务器运行状态（圆点 + 文本）。
    - FRP 连接状态（圆点 + 文本）。
    - 应用版本号文本。
  - **登录强制控制**:
    - 启动时检查 `AuthService.IsLoggedIn`：已登录则导航到 HomePage，未登录则导航 LoginPage。
    - NavigationView 切换时，除 Home/Settings 外均进行登录校验，未登录一律跳登录页面。

## 本地数据模型（Models/LocalModels.cs）

- **CreateMode**: 服务器创建模式枚举
  - **Beginner**: 小白模式。
  - **Advanced**: 高手模式。

- **LocalServer**: 本地服务器实例
  - **核心属性**: Id、Name、CoreType（vanilla/paper/forge/fabric…）、CoreVersion、MinecraftVersion、ServerPath、JarFileName。
  - **运行配置**: JavaPath、JvmArgs、MinMemoryMB/MaxMemoryMB、Port、AutoAcceptEula。
  - **元数据**: CreatedAt、LastStartedAt。
  - **拓展字段**: Mode（CreateMode）、PlayerCapacity（玩家容量）、UseLatestPurpur（是否使用 Purpur 最新版本，配合小白模式）。

- **DownloadRecord**: 本地下载记录
  - 记录核心下载/整合包下载的类型(Type)、名称/版本、URL、本地路径、状态(Pending/Downloading/Completed/Failed)、字节数与时间戳等。

- **LocalFrpTunnel**: 本地 FRP 隧道配置
  - RemoteTunnelId、LocalServerId、Name、NodeHost/NodePort、Token、Protocol、LocalPort/RemotePort、ConnectAddress 等，用于关联远端 FRP 隧道与本地服务器。

- **AppSettings**: 应用设置
  - DefaultJavaPath、DefaultServerPath、ApiBaseUrl、UserToken、Theme、AutoCheckUpdate、MinimizeToTray 等。
  - 通过 EF Core 在本地 Sqlite 中持久化，默认服务器目录位于“文档/ZMSL/Servers”。

## 数据库与设置管理（DatabaseService）

- **类型**: EF Core DbContext，数据库文件位于 LocalApplicationData/ZMSL/zmsl.db。
- **表**: Servers、Downloads、FrpTunnels、Settings。
- 应用启动时 `EnsureCreated()` 并执行自定义迁移逻辑：
  - 通过 `PRAGMA table_info(Servers)` 检查 Servers 表是否存在 `Mode/PlayerCapacity/UseLatestPurpur` 列，如缺失则使用 `ALTER TABLE` 在线添加列并写调试日志。
- 提供高层 API：
  - **GetSettingsAsync/SaveSettingsAsync**: 读取/保存单例 AppSettings（使用 HasData 初始化默认记录）。

## 后端 API 访问层（ApiService）

- **职责**: 与 ZMSL.Api 后端进行通信，统一处理 Token、超时与返回 DTO。
- **Token 管理**:
  - 启动时从 DatabaseService 读取 AppSettings.UserToken，并缓存到 `_token`。
  - 对外提供 `SetToken`，供 AuthService 登录/登出后刷新授权头。
- **主要 API 分组**:
  - **认证**: 登录、注册、获取当前用户信息（UserDto）。
  - **公告 & 广告**: 获取公告列表、广告列表，用于首页展示。
  - **服务端核心（旧版 API 仍保留调用封装）**: 查询核心分组、文件列表、搜索核心（配合 ServerCoreViewModel，现 UI 中部分功能标为暂时禁用）。
  - **FRP 相关**: 获取节点列表、个人隧道列表、创建/删除隧道、获取隧道配置、上报流量增量。
  - **通用下载**: 下载任意文件、带进度下载更新包。
  - **版本更新**: `CheckUpdateAsync`，与 UpdateService 协同完成版本检测。

## 认证与账号体系（AuthService & LoginViewModel）

- **AuthService**:
  - 依赖 ApiService 与 DatabaseService。
  - 启动时自动尝试从本地 AppSettings.UserToken 进行自动登录：
    - 若 Token 有效，则填充 CurrentUser 并触发 LoginStateChanged 事件。
    - 若 Token 无效，则清除本地 Token 并重置 ApiService Token。
  - 提供 **LoginAsync / RegisterAsync / Logout**，并对登录成功时将 Token 回写至 AppSettings。

- **LoginViewModel**:
  - 管理登录/注册表单字段、加载状态与错误/成功提示文案。
  - `LoginAsync` 调用 AuthService.LoginAsync，成功时触发 LoginSuccess 事件给 UI。
  - `RegisterAsync` 负责校验必填项、密码一致性与最小长度，再调用 AuthService 注册。
  - `ToggleMode` 在登录/注册模式间切换。

## 服务器管理模块（ServerManagerService & MyServerViewModel）

- **ServerManagerService**:
  - 持有 **DatabaseService**，维护运行中的服务器进程字典 `_runningServers` 与日志缓存 `_serverLogs`。
  - 提供服务器 CRUD：`GetServersAsync` / `CreateServerAsync` / `UpdateServerAsync` / `DeleteServerAsync`。
  - 启动逻辑：
    - 根据 LocalServer 信息拼装 jar 路径与 JVM 参数（含 GBK 编码设置以适配中文 Windows 控制台）。
    - 在服务器目录下启动 `java -jar xxx.jar nogui`，重定向标准输出/错误与输入，使用 GBK 编码读取日志。
    - 在启动前检查 eula.txt 是否已接受；如未接受，返回 `ServerStartResult.NeedEulaAccept`。
  - 运行控制：
    - `IsServerRunning`、`GetRunningServerIds`、`StopAllServersAsync`。
    - `StopServerAsync` 先发送 `stop` 命令，超时再 Kill 进程。
    - `SendCommandAsync` 将命令写入标准输入，实现控制台交互。
  - 事件：
    - `ServerOutput`: 输出每行日志（区分普通/错误），供 UI 控制台显示与缓存。
    - `ServerStatusChanged`: 通知服务器运行状态改变，供 UI 及 MainWindow 状态栏更新。

- **MyServerViewModel**:
  - 通过 ObservableCollection<LocalServer> 展示服务器列表，SelectedServer 对应当前选中服务器。
  - 控制台输出通过 ObservableCollection<string> 绑定，由 `OnServerOutput` 事件回调更新，并限制最大行数。
  - 提供命令：加载列表、创建服务器、删除服务器、启动/停止服务器、发送命令、保存服务器配置。
  - 在 `OnServerStatusChanged` 中同步 `IsServerRunning`，并通过 MainWindow.UpdateServerStatus 更新底部状态栏。

## 服务端核心下载与推荐（ServerDownloadService & ServerCoreViewModel）

- **ServerDownloadService**:
  - 封装对第三方 `https://api.mslmc.cn/v3/` 的访问，用于获取服务端核心分类、版本列表与下载地址。
  - 提供 `DownloadServerCoreAsync`：
    - 获取下载 URL → 确定 jar 文件名 → 建立 DownloadRecord → 流式下载到目标文件夹 → 更新下载进度事件 → 完成后标记记录状态。
  - 暴露 `DownloadProgress` 事件，用于 UI 进度条展示。
  - 提供 `GetDownloadHistoryAsync` 获取下载历史。
  - 提供 `GetRecommendedCores` 返回推荐核心列表（paper、purpur、forge、fabric、vanilla、velocity 等），用于 UI 引导用户选择合适核心类型。

- **ServerCoreViewModel**:
  - 原本用于通过 ZMSL.Api 的旧接口管理服务端核心分组/文件搜索，目前大量接口已注释为“旧 API 已移除，此功能暂时禁用”。
  - 保留 UI 状态字段与下载进度，但下载入口提示用户改用“创建服务器向导”。

## Java 环境智能管理（JavaManagerService）

- **目标**: 根据 MC 版本自动推荐并获取合适的 Java 版本，统一管理本地打包 Java（/java 目录）与系统已有 Java。

- **Java 版本推荐**:
  - `GetRecommendedJavaVersion(mcVersion)` 按 MC 版本范围返回推荐 JDK：
    - 1.16 以下 → Java 8。
    - 1.16.x → Java 16。
    - 1.17–1.20.4 → Java 17。
    - 1.20.5+ 与 1.21+ → Java 21。

- **Java 检测逻辑**:
  - 检查 `JAVA_HOME` 中的 java.exe，并解析 `java -version` 输出。
  - 尝试从 PATH 直接运行 `java -version` 并解析版本。
  - 扫描应用目录下的 `/java` 子目录，查找之前下载的 JDK。

- **Java 获取/下载**:
  - `GetOrDownloadJavaAsync(version)`：
    - 先从已检测的系统 Java 与本地打包目录找到指定版本。
    - 未找到则调用 `https://api.mslmc.cn/v3/download/jdk/{version}?os=windows&arch=...` 获取下载地址。
    - 下载 zip 至临时路径 → 解压至 `/java/jdk{version}` → 处理多一层目录结构 → 返回 java.exe 路径。

## FRP 内网穿透模块（FrpService & FrpViewModel）

- **FrpService**:
  - 依赖 ApiService、AuthService 与 DatabaseService。
  - 负责：
    - 通过 ApiService 获取可用 FRP 节点列表，并使用 Ping 测试延迟，按延迟排序。
    - 获取当前用户的隧道列表、创建/删除隧道。
    - 使用从后端获取的 FrpConfigDto 生成 frpc.toml 配置文件（保存在 LocalApplicationData/ZMSL/frp）。
    - 启动 `frpc.exe`（位于应用目录 /frpc/frpc.exe），重定向标准输出/错误并捆绑事件：
      - 输出行进入 AddLog → 触发 LogReceived 事件供 UI 展示。
      - 进程退出时触发 StatusChanged(IsConnected=false)。
    - 通过定时器调用 frpc Admin API (`http://127.0.0.1:7400/api/status`) 轮询实时流量（支持 TCP/UDP），计算增量：
      - 触发 TrafficUpdated 事件给 UI。
      - 调用 ApiService.ReportTrafficAsync 上报增量流量，用于用户配额统计。
    - 定期刷新用户信息，若 `TrafficUsed >= TrafficQuota`，则自动停止隧道并发送 Windows 通知提醒流量用尽。
  - 提供 `GetLogs/ ClearLogs / GetCurrentTrafficAsync` 等辅助接口。

- **FrpViewModel**:
  - 管理节点列表、隧道列表、当前连接状态与日志列表。
  - 在用户登录后自动加载节点与个人隧道。
  - 提供命令：创建隧道、删除隧道、启动/停止隧道、清空日志。
  - 启动隧道前会通过 ApiService 再次拉取用户最新流量数据，若流量不足则弹出对话框提示用户购买流量。
  - 在 `OnFrpStatusChanged` 中同步 UI 的 `IsConnected`、当前连接地址/名称、当前运行隧道，并调用 MainWindow.UpdateFrpStatus 更新状态栏。
  - 日志通过 `_frpService.LogReceived` 事件实时追加，并限制最大条数。

## 应用设置与全局配置（SettingsViewModel）

- 通过 DatabaseService 读取/保存 AppSettings，提供：
  - 默认服务器根目录（可通过 FolderPicker 选择）。
  - 默认 Java 路径（可通过 FileOpenPicker 指定，也可使用 ServerManagerService.DetectJava 自动检测结果）。
- UI 中提供保存按钮与状态消息（StatusMessage），提示保存成功/失败。

## 自动更新模块（UpdateService）

- 通过 ApiService.CheckUpdateAsync 上报当前版本号并获取最新版本信息（AppVersionDto）。
- 支持后台静默下载更新：
  - 下载到 LocalApplicationData/ZMSL/Updates/pending_update.zip.tmp，完成后校验 SHA256，校验通过则重命名为 pending_update.zip。
  - 通过事件暴露检查结果、下载进度与下载完成状态。
- 应用更新：
  - `ApplyUpdate()` 启动同目录下的 `ZMSL.Updater.exe`，传入更新文件路径、应用目录、当前进程 Id 与当前 exe 路径，由 Updater 负责真正的替换与重启逻辑。

## Converters 模块

- **BoolToVisibilityConverter**: 将 bool 变换为 Visible/Collapsed，可选 Invert 反转逻辑，广泛用于 UI 控制显示隐藏。
- **CoreTypeToPluginVisibilityConverter**: 根据核心类型字符串判断插件按钮是否可见：vanilla 不支持插件返回 Collapsed，其他类型 Visible。

## 视图层（Views）概览

> 视图代码与 XAML 主要负责布局与绑定，对应的业务逻辑集中在各 ViewModel 中。

- **HomePage**: 绑定 HomeViewModel，展示欢迎语、公告、广告与服务器数量/运行数量统计。
- **LoginPage**: 绑定 LoginViewModel，提供登录/注册切换与表单。
- **MyServerPage**: 绑定 MyServerViewModel，展示本地服务器列表、控制台输出与命令输入框。
- **FrpPage**: 绑定 FrpViewModel，展示节点列表、隧道列表、当前连接状态与日志。
- **SettingsPage**: 绑定 SettingsViewModel，配置默认路径、Java 路径与其他应用设置。
- **CreateServerWizard / BeginnerModePage / AdvancedModePage / CreateConfirmationPage**: 组成“创建服务器向导”，区分小白模式与高手模式，最后在确认页统一回写 LocalServer 配置并创建服务器记录。
- **ServerCorePage / ServerDetailPage / ServerPropertiesDialog / ServerSettingsDialog / PluginManagerPage** 等: 承载服务器核心浏览、服务器详情、属性/设置对话框、插件管理等功能，对应的逻辑与 ServerManagerService、ServerDownloadService、ApiService 协同。

## 与其他子项目的关系

- **ZMSL.Api-Java**: 本项目所有登录、公告/广告、服务端核心、FRP 隧道、版本更新相关接口均通过 ApiService 调用该后端服务。
- **ZMSL.Shared**: 引用共享项目中的 DTO（如 UserDto、AnnouncementDto、FrpNodeDto、TunnelDto、AppVersionDto 等）以保证前后端数据结构一致。
- **ZMSL.Updater**: 自动更新时由 UpdateService 启动的独立更新器进程，负责替换主程序文件。

---

本文件从 App 启动流程、窗口框架、服务层、视图模型到关键数据模型，对 **ZMSL.App** 的职责和结构进行了逐文件层面的整理，可作为后续重构、调试或功能扩展时的入口文档。