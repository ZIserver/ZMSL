namespace ZMSL.Shared.Models;

/// <summary>
/// 用户模型
/// </summary>
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string Role { get; set; } = "User"; // User, Admin
    public long TrafficQuota { get; set; } = 5368709120; // 5GB 默认配额
    public long TrafficUsed { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// FRP节点模型
/// </summary>
public class FrpNode
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 7000;
    public string Token { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public bool IsOnline { get; set; } = true;
    public int MaxConnections { get; set; } = 100;
    public int CurrentConnections { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 公告模型
/// </summary>
public class Announcement
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Type { get; set; } = "Info"; // Info, Warning, Important
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// 服务端核心分组
/// </summary>
public class ServerCoreGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 服务端核心文件
/// </summary>
public class ServerCoreFile
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string MinecraftVersion { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; } = 0;
    public string Sha256 { get; set; } = string.Empty;
    public int DownloadCount { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public ServerCoreGroup? Group { get; set; }
}

/// <summary>
/// 用户隧道
/// </summary>
public class UserTunnel
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int NodeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Protocol { get; set; } = "tcp"; // tcp, udp
    public int LocalPort { get; set; } = 25565;
    public int RemotePort { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public User? User { get; set; }
    public FrpNode? Node { get; set; }
}
