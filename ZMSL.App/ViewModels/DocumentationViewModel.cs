using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;

namespace ZMSL.App.ViewModels
{
    public partial class DocumentationViewModel : ObservableObject
    {
        [ObservableProperty]
        public partial string CurrentDoc { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string SelectedCategory { get; set; } = "GettingStarted";

        private readonly Dictionary<string, string> _docs = new()
        {
            ["GettingStarted"] = @"# 快速入门

欢迎使用 **智穗MC开服器**！只需几步，即可开启您的 Minecraft 服务器之旅。

## 1. 首页概览
启动应用后，您将进入首页仪表盘。
- **状态概览**：顶部显示当前运行的服务器数量和系统资源占用（CPU/内存）。
- **快捷操作**：您可以直接点击""启动""按钮运行最近的服务器。
- **公告通知**：右侧展示最新的官方公告和更新信息。

## 2. 下载服务端
如果您还没有服务器核心，请前往 **服务端下载** 页面。
1. **选择类型**：支持 Vanilla (官方原版), Spigot, Paper (高性能), Forge/Fabric (模组) 等主流核心。
2. **选择版本**：从下拉菜单中选择您需要的 Minecraft 版本（如 1.20.4）。
3. **点击下载**：下载完成后，核心将自动保存到本地库中。
*(注：部分核心下载功能可能依赖网络环境，请耐心等待)*

## 3. 首次启动
1. 前往 **我的服务器** 页面。
2. 点击右上角的 **+ 创建服务器**。
3. 按照向导选择刚才下载的核心，输入服务器名称。
4. 创建成功后，点击列表中的 **启动** 按钮。
5. **EULA 协议**：首次启动会弹出 EULA 签署提示，请点击""同意""以继续。

## 4. 基础设置
在 **设置** 页面中，您可以：
- **Java 管理**：配置不同版本的 JDK 路径（推荐使用 JDK 17+ 以支持高版本 MC）。
- **下载源**：切换下载镜像源以提升下载速度。
- **外观**：切换深色/浅色主题。
",
            ["ServerManagement"] = @"# 服务器管理

在 **我的服务器** 页面，您可以对服务器进行全生命周期的管理。

## 核心功能

### 1. 实例列表
- 展示所有本地和远程服务器实例。
- **状态指示**：绿色圆点表示运行中，灰色表示停止。
- **快捷操作**：直接在卡片上进行 启动/停止/重启 操作。

### 2. 控制台 (Console)
点击任意服务器进入详情页，您将看到实时控制台。
- **日志查看**：实时显示服务器输出的日志信息。
- **指令发送**：在底部输入框发送 MC 指令（如 `/op player`, `/gamemode creative`）。
- **性能监控**：查看该实例实时的内存和 CPU 占用。

### 3. 配置管理
无需手动编辑文件，图形化修改 `server.properties`：
- **基础选项**：端口 (server-port)、最大人数 (max-players)、难度 (difficulty)。
- **高级选项**：正版验证 (online-mode)、视距 (view-distance)、PVP 开关等。
- **保存生效**：修改后点击保存，重启服务器即可生效。

### 4. 备份与恢复
- **创建备份**：一键打包当前服务器存档和配置。
- **自动备份**：在设置中开启定时备份策略。
- **恢复**：从历史备份列表中选择并还原，数据安全无忧。
",
            ["AdvancedFeatures"] = @"# 进阶功能

智穗MC开服器提供了一系列高级工具，助您成为专业的服主。

## 1. 日志分析 (Log Analysis)
服务器崩溃了？看不懂报错？
- 进入 **日志分析** 页面。
- 系统会自动读取最新的 `latest.log` 或崩溃报告。
- **智能诊断**：自动识别常见的 Java 报错、插件冲突或配置错误，并给出 **解决方案建议**。
- **历史查询**：支持查看过往的日志文件。

## 2. 节点管理 (Linux Nodes)
通过 **节点管理**，您可以统一管理多台远程 Linux 服务器。
- **添加节点**：输入远程服务器 IP、SSH 端口、用户名和密码/密钥。
- **一键部署**：系统会自动在远程服务器上部署守护进程。
- **远程控制**：像管理本地服务器一样，在本地客户端上 启动/停止/监控 远程服务器。

## 3. FRP 内网穿透
没有公网 IP？没关系！
- 进入 **FRP 穿透** 页面。
- **内置客户端**：无需额外下载 FRP 工具。
- **隧道配置**：设置本地端口（如 25565）映射到远程服务器端口。
- **一键连接**：点击连接后，即可生成公网地址，发给朋友即可联机。
*(注：需要您拥有或租赁一台具有公网 IP 的 FRP 服务端)*
",
            ["Community"] = @"# 社区与支持

加入智穗社区，与其他服主交流心得。

## 1. 玩家论坛
集成在客户端内的 **玩家论坛** 板块：
- **浏览帖子**：查看最新的开服教程、插件推荐、服务器宣传。
- **互动交流**：支持点赞、评论、收藏您感兴趣的内容。
- **发布内容**：分享您的经验或宣传您的服务器。

## 2. 个人中心
在论坛页面右上角进入 **个人中心**：
- **我的足迹**：查看我发布的帖子、评论历史。
- **收藏夹**：快速找到之前收藏的优质教程。
- **消息通知**：查看回复、点赞等互动提醒。

## 3. 关于我们
- **版本检查**：在设置中检查更新，确保使用最新功能。
- **开源协议**：本项目遵循开源协议，欢迎在 GitHub 上提交 Issue 或 PR。
- **联系作者**：通过论坛或关于页面的联系方式反馈问题。

---
*遇到问题？请先查阅 [常见问题] 或在论坛发帖求助。*
"
        };

        public DocumentationViewModel()
        {
            // 默认选中第一项
            SelectCategory("GettingStarted");
        }

        [RelayCommand]
        public void SelectCategory(string categoryKey)
        {
            if (_docs.TryGetValue(categoryKey, out var content))
            {
                SelectedCategory = categoryKey;
                CurrentDoc = content;
            }
        }
    }
}
