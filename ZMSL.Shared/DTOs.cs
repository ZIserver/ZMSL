namespace ZMSL.Shared.DTOs;

// ============== 认证相关 ==============

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequest
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Token { get; set; }
    public UserDto? User { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
    public int Level { get; set; } = 1;
    public int Experience { get; set; } = 0;
    public long? TrafficQuota { get; set; }
    public long? TrafficUsed { get; set; }
    public long? PurchasedTraffic { get; set; }
    public bool IsRealNameVerified { get; set; }
    public string? Nickname { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BetaKeyValidationRequest
{
    public string Key { get; set; } = string.Empty;
    public string Hwid { get; set; } = string.Empty;
}

// ============== API响应 ==============

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
}

public class ApiResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

// ============== 服务端核心 ==============

public class ServerCoreGroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public int FileCount { get; set; }
    
    // 用于UI显示
    public string FileCountText => $"{FileCount} 个版本";
}

public class ServerCoreFileDto
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string MinecraftVersion { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public int DownloadCount { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    
    // 格式化文件大小
    public string FileSizeText => FormatFileSize(FileSize);
    
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:F2} {sizes[order]}";
    }
}

public class UploadServerCoreRequest
{
    public int GroupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string MinecraftVersion { get; set; } = string.Empty;
}

// ============== FRP相关 ==============

public class FrpNodeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Region { get; set; } = string.Empty;
    public bool? IsOnline { get; set; }
    public int Latency { get; set; } // 延迟ms
    public string NodeGroup { get; set; } = "PAID"; // 节点分组: FREE-免费组, PAID-付费组
    
    // 用于UI显示
    public string NodeGroupText => NodeGroup == "FREE" ? "免费组" : "付费组";
    public bool IsFreeNode => NodeGroup == "FREE";
    public bool? RequiresRealName { get; set; }
}

public class CreateTunnelRequest
{
    public int NodeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Protocol { get; set; } = "tcp";
    public int LocalPort { get; set; } = 25565;
}

public class TunnelDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public string NodeHost { get; set; } = string.Empty;
    public string NodeDomain { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public int RemotePort { get; set; }
    public string ConnectAddress { get; set; } = string.Empty;
    public bool? IsActive { get; set; }
    public bool? RequiresRealName { get; set; }
    
    // 用于 UI 高亮当前运行的隧道
    public bool IsRunning { get; set; }
    
    // 客户端辅助属性：解析后的域名
    [System.Text.Json.Serialization.JsonIgnore]
    public string? ResolvedDomain { get; set; }
}

public class DnsRecordDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long TunnelId { get; set; }
    public string DomainName { get; set; } = string.Empty;
    public string SubDomain { get; set; } = string.Empty;
    public string Rr { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
}

public class FrpConfigDto
{
    public string ServerHost { get; set; } = string.Empty;
    public int ServerPort { get; set; }
    public string Token { get; set; } = string.Empty;
    public string ProxyName { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public int RemotePort { get; set; }
}

// ============== 公告相关 ==============

public class AnnouncementDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CreateAnnouncementRequest
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Type { get; set; } = "Info";
    public DateTime? ExpiresAt { get; set; }
}

// ============== 广告推广相关 ==============

public class AdvertisementDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}

// ============== 管理后台 ==============

public class AdminDashboardDto
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int TotalNodes { get; set; }
    public int OnlineNodes { get; set; }
    public int TotalTunnels { get; set; }
    public int ActiveTunnels { get; set; }
    public long TotalTrafficUsed { get; set; }
    public int TotalDownloads { get; set; }
}

public class UpdateUserRequest
{
    public string? Role { get; set; }
    public long? TrafficQuota { get; set; }
    public bool? IsActive { get; set; }
}

public class CreateNodeRequest
{
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 7000;
    public string Token { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public int MaxConnections { get; set; } = 100;
}

public class CreateGroupRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconUrl { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 0;
}

// ============== 插件管理 ==============

public class PluginFileDto
{
    public long? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? SupportedVersions { get; set; }  // 支持的MC版本，逗号分隔
    public string? FileName { get; set; }
    public string? DownloadUrl { get; set; }
    public long? FileSize { get; set; }
    public int? DownloadCount { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsOssPath { get; set; }
}

// ============== 应用版本更新 ==============

public class AppVersionDto
{
    public long? Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string? Changelog { get; set; }
    public string? DownloadUrl { get; set; }
    public long? FileSize { get; set; }
    public string? FileHash { get; set; }
    public bool ForceUpdate { get; set; }
    public string? MinVersion { get; set; }
    public bool? IsLatest { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public bool HasUpdate { get; set; }
}

// ============== 论坛相关 ==============

public class ForumCategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public int SortOrder { get; set; }
    public int? PostCount { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ForumPostDto
{
    public long Id { get; set; }
    public long? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public int? ViewCount { get; set; }
    public int? LikeCount { get; set; }
    public int? CommentCount { get; set; }
    public int? FavoriteCount { get; set; }
    public bool? IsPinned { get; set; }
    public bool? IsFeatured { get; set; }
    public bool? IsLocked { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool? IsLiked { get; set; }
    public bool? IsFavorited { get; set; }
}

public class ForumCommentDto
{
    public long Id { get; set; }
    public long PostId { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public long? ParentId { get; set; }
    public long? RootId { get; set; }
    public int? ReplyToUserId { get; set; }
    public string? ReplyToUsername { get; set; }
    public int Depth { get; set; }
    public string Content { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public int? LikeCount { get; set; }
    public bool? IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool? IsLiked { get; set; }
    public List<ForumCommentDto>? Replies { get; set; }
}

public class NotificationDto
{
    public long Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public long? TargetId { get; set; }
    public long? RelatedUserId { get; set; }
    public string? RelatedUsername { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreatePostRequest
{
    public long? CategoryId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Status { get; set; }
}

public class UpdatePostRequest
{
    public long? CategoryId { get; set; }
    public string? Title { get; set; }
    public string? Content { get; set; }
    public string? Status { get; set; }
}

public class CreateCommentRequest
{
    public long PostId { get; set; }
    public long? ParentId { get; set; }
    public string Content { get; set; } = string.Empty;
}

// ============== 服务端版本查询 ==============

public class ServerVersionListDto
{
    public List<string> VersionList { get; set; } = new();
}

public class ServerDownloadUrlDto
{
    public string Url { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

public class MslApiResponse<T>
{
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }

    public bool IsSuccess => Code == 200;
}

// ============== 鸣谢 ==============

public class AcknowledgmentDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? Link { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}
