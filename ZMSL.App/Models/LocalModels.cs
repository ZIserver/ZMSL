using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace ZMSL.App.Models;

/// <summary>
/// 服务器创建模式
/// </summary>
public enum CreateMode
{
    Beginner,  // 小白模式
    Advanced   // 高手模式
}

/// <summary>
/// 节点平台类型
/// </summary>
public enum NodePlatform
{
    Linux,     // Linux 节点
    Windows    // Windows 节点
}

/// <summary>
/// 本地服务器实例
/// </summary>
public class LocalServer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CoreType { get; set; } = string.Empty; // vanilla, paper, forge, fabric
    public string CoreVersion { get; set; } = string.Empty;
    public string MinecraftVersion { get; set; } = string.Empty;
    public string ServerPath { get; set; } = string.Empty;
    public string JarFileName { get; set; } = string.Empty;
    public string? JavaPath { get; set; }
    public string JvmArgs { get; set; } = string.Empty;
    public int MinMemoryMB { get; set; } = 1024;
    public int MaxMemoryMB { get; set; } = 2048;
    public int Port { get; set; } = 25565;
    public bool AutoAcceptEula { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastStartedAt { get; set; }
    
    // 新增属性
    public CreateMode Mode { get; set; } = CreateMode.Beginner;
    public int PlayerCapacity { get; set; } = 10; // 玩家人数容量
    public bool UseLatestPurpur { get; set; } = true; // 是否使用Purpur最新版本
    
    // 第三方登录配置
    public bool EnableAuthlib { get; set; } = false; // 是否启用authlib-injector
    public string? AuthlibUrl { get; set; } // 验证服务器地址，如 littleskin.cn
    public bool AuthlibDownloaded { get; set; } = false; // authlib-injector是否已下载

    // 英文别名（用于群组服配置等不支持中文的场景）
    public string EnglishAlias { get; set; } = string.Empty;

    // 自定义启动命令
    public string? StartupCommand { get; set; }

    // Forge/NeoForge 安装状态
    public bool ForgeInstalled { get; set; } = true;
    public string? ForgeVersion { get; set; }

    // 自定义图标路径
    public string? IconPath { get; set; }

    // 临时属性，不映射到数据库
    [NotMapped]
    public string? ImportSourcePath { get; set; }
}

/// <summary>
/// 本地下载记录
/// </summary>
public class DownloadRecord
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty; // core, modpack
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // Pending, Downloading, Completed, Failed
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// 本地FRP隧道配置
/// </summary>
public class LocalFrpTunnel
{
    public int Id { get; set; }
    public int RemoteTunnelId { get; set; }
    public int LocalServerId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NodeHost { get; set; } = string.Empty;
    public int NodePort { get; set; }
    public string Token { get; set; } = string.Empty;
    public string Protocol { get; set; } = "tcp";
    public int LocalPort { get; set; } = 25565;
    public int RemotePort { get; set; }
    public string ConnectAddress { get; set; } = string.Empty;
    public bool EnableProxyProtocol { get; set; } = false; // 是否启用 Proxy Protocol v2
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 应用设置
/// </summary>
public class AppSettings
{
    public int Id { get; set; } = 1;
    public string? DefaultJavaPath { get; set; }
    public string DefaultServerPath { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "http://localhost:5000";
    public string? UserToken { get; set; }
    public string? MeFrpToken { get; set; }
    public string? StarryFrpToken { get; set; }
    public string Theme { get; set; } = "Default"; // Default, Light, Dark
    public bool AutoCheckUpdate { get; set; } = true;
    public bool MinimizeToTray { get; set; } = false;

    // 自动备份相关设置
    public bool EnableAutoBackup { get; set; } = true;
    /// <summary>
    /// 备份间隔(单位:分钟)
    /// </summary>
    public int BackupIntervalMinutes { get; set; } = 60;
    /// <summary>
    /// 备份根目录,默认:文档/ZMSL/backups
    /// </summary>
    public string? BackupDirectory { get; set; }
    /// <summary>
    /// 每个服务器保留的备份份数
    /// </summary>
    public int BackupRetentionCount { get; set; } = 7;

    // 日志分析 AI 配置
    public string? LogAnalysisApiUrl { get; set; }
    public string? LogAnalysisApiKey { get; set; }
    public string? LogAnalysisModel { get; set; }
    public bool UseCustomLogAnalysisConfig { get; set; } = false;

    // 启动与运行设置
    public bool AutoStartLastServer { get; set; } = false; // 启动时自动运行上次服务器
    public bool AutoRestartAppOnCrash { get; set; } = false; // 软件崩溃自动重启
    public bool AutoRestartServerOnCrash { get; set; } = false; // 服务器崩溃自动重启
    public bool StartOnBoot { get; set; } = false; // 开机自启

    // 下载设置
    public int DownloadThreads { get; set; } = 8; // 下载线程数
    public bool ForceMultiThread { get; set; } = false; // 强制多线程下载

    // 控制台设置
    public string ConsoleEncoding { get; set; } = "UTF-8"; // 控制台编码: ANSI, UTF-8, GB18030
    public int ConsoleFontSize { get; set; } = 12; // 控制台字体大小

    // UI 设置
    public bool UseCardView { get; set; } = true; // 默认使用卡片视图
    public bool EnableMicaEffect { get; set; } = true; // 启用云母/毛玻璃效果
    public bool UseCustomBackground { get; set; } = false; // 使用自定义背景图
    public string? BackgroundImagePath { get; set; } // 背景图片路径
    public int MicaIntensity { get; set; } = 0; // 云母效果强度 0-2

    // 镜像源设置
    public string DownloadMirrorSource { get; set; } = "MSL"; // MSL 或 ZSync
}

/// <summary>
/// 服务器备份记录
/// </summary>
public class ServerBackup
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 节点配置（支持 Linux 和 Windows）
/// </summary>
public class LinuxNode
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 8080;
    public string Token { get; set; } = string.Empty;
    public bool IsOnline { get; set; } = false;
    public NodePlatform Platform { get; set; } = NodePlatform.Linux; // 节点平台类型
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastConnectedAt { get; set; }
    
    /// <summary>
    /// 获取平台显示名称
    /// </summary>
    public string PlatformDisplayName => Platform == NodePlatform.Windows ? "Windows" : "Linux";
}

/// <summary>
/// 本地Java安装信息
/// </summary>
public class JavaInfo
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Source { get; set; } = string.Empty; // JAVA_HOME, PATH, Local, FullScan, Downloaded
    public DateTime DetectedAt { get; set; } = DateTime.Now;
    public bool IsValid { get; set; } = true;
}

/// <summary>
/// 远程服务器（节点上的服务器）
/// </summary>
public class RemoteServer
{
    public int Id { get; set; }
    public int NodeId { get; set; }
    public string RemoteServerId { get; set; } = string.Empty; // 节点上的服务器ID
    public string Name { get; set; } = string.Empty;
    public string CoreType { get; set; } = string.Empty;
    public string MinecraftVersion { get; set; } = string.Empty;
    public string JarFileName { get; set; } = string.Empty;
    public string? JavaPath { get; set; }
    public string JvmArgs { get; set; } = string.Empty;
    public int MinMemoryMB { get; set; } = 1024;
    public int MaxMemoryMB { get; set; } = 2048;
    public int Port { get; set; } = 25565;
    public bool AutoRestart { get; set; } = false;
    public bool IsRunning { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 统一的服务器视图模型（用于列表显示）
/// </summary>
public partial class ServerViewModel : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "Local" 或 "Remote"
    public string Location { get; set; } = string.Empty; // "本地" 或 节点名称
    public string CoreType { get; set; } = string.Empty;
    public string MinecraftVersion { get; set; } = string.Empty;
    public bool IsRunning { get; set; } = false;
    public int NodeId { get; set; } = 0; // 远程服务器的节点ID
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int MinMemoryMB { get; set; }
    public int MaxMemoryMB { get; set; }
    public int Port { get; set; } // Port number
    
    // 选中状态
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial bool IsSelected { get; set; }

    // 原始对象引用
    public LocalServer? LocalServer { get; set; }
    public RemoteServer? RemoteServer { get; set; }
}
