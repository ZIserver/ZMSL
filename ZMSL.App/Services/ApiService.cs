using System.Net.Http.Json;
using ZMSL.Shared.DTOs;

namespace ZMSL.App.Services;

public class ApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DatabaseService _db;
    private string? _token;

    public ApiService(IHttpClientFactory httpClientFactory, DatabaseService db)
    {
        _httpClientFactory = httpClientFactory;
        _db = db;
        _ = LoadTokenAsync();
    }

    private async Task LoadTokenAsync()
    {
        var settings = await _db.GetSettingsAsync();
        _token = settings.UserToken;
    }

    public void SetToken(string? token)
    {
        _token = token;
    }

    private async Task EnsureTokenLoadedAsync()
    {
        if (string.IsNullOrEmpty(_token))
        {
            await LoadTokenAsync();
            if (!string.IsNullOrEmpty(_token))
            {
                LogService.Instance.Debug("Token reloaded from DB", "ApiService");
            }
        }
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("ZmslApi");
        // 添加User-Agent以避免Nginx 403错误
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ZMSL-App/2.0.0 (Windows; .NET 8.0)");
        
        if (!string.IsNullOrEmpty(_token))
        {
            client.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
            // LogService.Instance.Debug($"Token present: {_token.Substring(0, Math.Min(10, _token.Length))}...", "ApiService");
        }
        else
        {
            LogService.Instance.Debug("Warning: No Token available for request", "ApiService");
        }
        return client;
    }

    // 全局 JSON 选项
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase // 尝试使用驼峰命名
    };

    // ============== 认证 ==============
    
    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("auth/login", request, _jsonOptions);
        return await response.Content.ReadFromJsonAsync<LoginResponse>(_jsonOptions) ?? new LoginResponse { Success = false, Message = "请求失败" };
    }

    public async Task<ApiResponse> RegisterAsync(RegisterRequest request)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("auth/register", request, _jsonOptions);
        return await response.Content.ReadFromJsonAsync<ApiResponse>(_jsonOptions) ?? new ApiResponse { Success = false, Message = "请求失败" };
    }

    public async Task<ApiResponse<UserDto>> GetCurrentUserAsync()
    {
        try
        {
            await EnsureTokenLoadedAsync();
            var client = CreateClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await client.GetAsync("auth/me", cts.Token);
            return await response.Content.ReadFromJsonAsync<ApiResponse<UserDto>>(_jsonOptions, cancellationToken: cts.Token) ?? new ApiResponse<UserDto> { Success = false };
        }
        catch (OperationCanceledException)
        {
            return new ApiResponse<UserDto> { Success = false, Message = "获取用户信息超时" };
        }
        catch (Exception ex)
        {
            return new ApiResponse<UserDto> { Success = false, Message = ex.Message };
        }
    }

    // ============== 公告 ==============
    
    public async Task<ApiResponse<List<AnnouncementDto>>> GetAnnouncementsAsync()
    {
        var client = CreateClient();
        var response = await client.GetAsync("announcements");
        return await response.Content.ReadFromJsonAsync<ApiResponse<List<AnnouncementDto>>>(_jsonOptions) 
            ?? new ApiResponse<List<AnnouncementDto>> { Success = false };
    }

    // ============== Beta验证 ==============

    public async Task<ApiResponse<string>> ValidateBetaKeyAsync(string key, string hwid)
    {
        try
        {
            var client = CreateClient();
            var request = new BetaKeyValidationRequest { Key = key, Hwid = hwid };
            var response = await client.PostAsJsonAsync("beta/validate", request, _jsonOptions);
            
            // 记录详细的响应内容以便调试
            var content = await response.Content.ReadAsStringAsync();
            // System.Diagnostics.Debug.WriteLine($"[ApiService] Beta validation response: {content}");
            
            try 
            {
                return System.Text.Json.JsonSerializer.Deserialize<ApiResponse<string>>(content, _jsonOptions) 
                    ?? new ApiResponse<string> { Success = false, Message = "响应为空" };
            }
            catch (Exception ex)
            {
                return new ApiResponse<string> { Success = false, Message = $"解析失败: {ex.Message}" };
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"Beta key validation failed: {ex}", "ApiService");
            return new ApiResponse<string> { Success = false, Message = $"验证异常: {ex.Message}" };
        }
    }

    // ============== 广告 ==============
    
    public async Task<ApiResponse<List<AdvertisementDto>>> GetAdvertisementsAsync()
    {
        var client = CreateClient();
        var response = await client.GetAsync("advertisements");
        return await response.Content.ReadFromJsonAsync<ApiResponse<List<AdvertisementDto>>>(_jsonOptions) 
            ?? new ApiResponse<List<AdvertisementDto>> { Success = false };
    }

    // ============== 服务端核心 ==============
    
    public async Task<ApiResponse<List<ServerCoreGroupDto>>> GetServerCoreGroupsAsync()
    {
        try
        {
            var client = CreateClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await client.GetAsync("servercores/groups", cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new ApiResponse<List<ServerCoreGroupDto>> { Success = false, Message = $"HTTP {(int)response.StatusCode}" };
            return await response.Content.ReadFromJsonAsync<ApiResponse<List<ServerCoreGroupDto>>>(_jsonOptions, cancellationToken: cts.Token).ConfigureAwait(false) 
                ?? new ApiResponse<List<ServerCoreGroupDto>> { Success = false };
        }
        catch (OperationCanceledException)
        {
            return new ApiResponse<List<ServerCoreGroupDto>> { Success = false, Message = "请求超时" };
        }
        catch (Exception ex)
        {
            return new ApiResponse<List<ServerCoreGroupDto>> { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<List<ServerCoreFileDto>>> GetServerCoreFilesAsync(int groupId)
    {
        try
        {
            var client = CreateClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await client.GetAsync($"servercores/files?groupId={groupId}", cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new ApiResponse<List<ServerCoreFileDto>> { Success = false, Message = $"HTTP {(int)response.StatusCode}" };
            return await response.Content.ReadFromJsonAsync<ApiResponse<List<ServerCoreFileDto>>>(_jsonOptions, cancellationToken: cts.Token).ConfigureAwait(false) 
                ?? new ApiResponse<List<ServerCoreFileDto>> { Success = false };
        }
        catch (OperationCanceledException)
        {
            return new ApiResponse<List<ServerCoreFileDto>> { Success = false, Message = "请求超时" };
        }
        catch (Exception ex)
        {
            return new ApiResponse<List<ServerCoreFileDto>> { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<List<ServerCoreFileDto>>> SearchServerCoresAsync(string? keyword = null, string? mcVersion = null)
    {
        try
        {
            var client = CreateClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var query = new List<string>();
            if (!string.IsNullOrEmpty(keyword)) query.Add($"keyword={Uri.EscapeDataString(keyword)}");
            if (!string.IsNullOrEmpty(mcVersion)) query.Add($"mcVersion={Uri.EscapeDataString(mcVersion)}");
            var queryString = query.Count > 0 ? "?" + string.Join("&", query) : "";
            var response = await client.GetAsync($"servercores/search{queryString}", cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new ApiResponse<List<ServerCoreFileDto>> { Success = false, Message = $"HTTP {(int)response.StatusCode}" };
            return await response.Content.ReadFromJsonAsync<ApiResponse<List<ServerCoreFileDto>>>(_jsonOptions, cancellationToken: cts.Token).ConfigureAwait(false) 
                ?? new ApiResponse<List<ServerCoreFileDto>> { Success = false };
        }
        catch (OperationCanceledException)
        {
            return new ApiResponse<List<ServerCoreFileDto>> { Success = false, Message = "请求超时" };
        }
        catch (Exception ex)
        {
            return new ApiResponse<List<ServerCoreFileDto>> { Success = false, Message = ex.Message };
        }
    }

    // ============== FRP ==============
    
    public async Task<ApiResponse<List<FrpNodeDto>>> GetFrpNodesAsync()
    {
        await EnsureTokenLoadedAsync();
        var client = CreateClient();
        var response = await client.GetAsync("frp/nodes");
        return await response.Content.ReadFromJsonAsync<ApiResponse<List<FrpNodeDto>>>(_jsonOptions) 
            ?? new ApiResponse<List<FrpNodeDto>> { Success = false };
    }

    public async Task<ApiResponse<List<TunnelDto>>> GetMyTunnelsAsync()
    {
        await EnsureTokenLoadedAsync();
        var client = CreateClient();
        var response = await client.GetAsync("frp/tunnels");
        return await response.Content.ReadFromJsonAsync<ApiResponse<List<TunnelDto>>>(_jsonOptions) 
            ?? new ApiResponse<List<TunnelDto>> { Success = false };
    }

    public async Task<ApiResponse<List<DnsRecordDto>>> GetMyDnsRecordsAsync()
    {
        await EnsureTokenLoadedAsync();
        var client = CreateClient();
        var response = await client.GetAsync("dns/my-records");
        return await response.Content.ReadFromJsonAsync<ApiResponse<List<DnsRecordDto>>>(_jsonOptions) 
            ?? new ApiResponse<List<DnsRecordDto>> { Success = false };
    }

    public async Task<ApiResponse<TunnelDto>> CreateTunnelAsync(CreateTunnelRequest request)
    {
        await EnsureTokenLoadedAsync();
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("frp/tunnels", request, _jsonOptions);
        return await response.Content.ReadFromJsonAsync<ApiResponse<TunnelDto>>(_jsonOptions) 
            ?? new ApiResponse<TunnelDto> { Success = false };
    }

    public async Task<ApiResponse> DeleteTunnelAsync(int tunnelId)
    {
        await EnsureTokenLoadedAsync();
        var client = CreateClient();
        var response = await client.DeleteAsync($"frp/tunnels/{tunnelId}");
        return await response.Content.ReadFromJsonAsync<ApiResponse>(_jsonOptions) 
            ?? new ApiResponse { Success = false };
    }

    public async Task<ApiResponse<FrpConfigDto>> GetTunnelConfigAsync(int tunnelId)
    {
        await EnsureTokenLoadedAsync();
        var client = CreateClient();
        var response = await client.GetAsync($"frp/tunnels/{tunnelId}/config");
        return await response.Content.ReadFromJsonAsync<ApiResponse<FrpConfigDto>>(_jsonOptions) 
            ?? new ApiResponse<FrpConfigDto> { Success = false };
    }

    public async Task<ApiResponse> ReportTrafficAsync(int tunnelId, long trafficIn, long trafficOut)
    {
        await EnsureTokenLoadedAsync();
        var client = CreateClient();
        var response = await client.PostAsJsonAsync($"frp/tunnels/{tunnelId}/traffic", new 
        { 
            trafficIn, 
            trafficOut 
        }, _jsonOptions);
        return await response.Content.ReadFromJsonAsync<ApiResponse>(_jsonOptions) 
            ?? new ApiResponse { Success = false };
    }

    // ============== 插件管理 ==============
    
    public async Task<T?> GetAsync<T>(string endpoint)
    {
        var client = CreateClient();
        var response = await client.GetAsync(endpoint);
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
    }

    public async Task<bool> DownloadFileAsync(string url, string filePath)
    {
        try
        {
            var client = CreateClient();
            var response = await client.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var fileBytes = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(filePath, fileBytes);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    // ============== 版本更新 ==============

    public async Task<ApiResponse<AppVersionDto>> CheckUpdateAsync(string currentVersion)
    {
        try
        {
            var client = CreateClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await client.GetAsync($"versions/check?currentVersion={Uri.EscapeDataString(currentVersion)}", cts.Token);
            if (!response.IsSuccessStatusCode)
                return new ApiResponse<AppVersionDto> { Success = false, Message = $"HTTP {(int)response.StatusCode}" };
            return await response.Content.ReadFromJsonAsync<ApiResponse<AppVersionDto>>(_jsonOptions, cancellationToken: cts.Token)
                ?? new ApiResponse<AppVersionDto> { Success = false };
        }
        catch (OperationCanceledException)
        {
            return new ApiResponse<AppVersionDto> { Success = false, Message = "请求超时" };
        }
        catch (Exception ex)
        {
            return new ApiResponse<AppVersionDto> { Success = false, Message = ex.Message };
        }
    }

    public async Task<bool> DownloadFileWithProgressAsync(string url, string filePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = CreateClient();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return false;

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalRead += bytesRead;
                if (totalBytes > 0)
                {
                    progress?.Report((double)totalRead / totalBytes * 100);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    // ============== 论坛 API ==============

    public async Task<ApiResponse<List<ForumCategoryDto>>> GetForumCategoriesAsync()
    {
        try
        {
            var client = CreateClient();
            LogService.Instance.Debug("Requesting GET forum/categories", "ApiService");
            var response = await client.GetAsync("forum/categories");
            LogService.Instance.Debug($"Response status: {response.StatusCode}", "ApiService");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                LogService.Instance.Error($"Error content: {errorContent}", "ApiService");
                return new ApiResponse<List<ForumCategoryDto>> { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse<List<ForumCategoryDto>>>(_jsonOptions) 
                ?? new ApiResponse<List<ForumCategoryDto>> { Success = false };
        }
        catch (Exception ex)
        {
            LogService.Instance.Error("Exception in GetForumCategoriesAsync", "ApiService", ex);
            return new ApiResponse<List<ForumCategoryDto>> { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<PagedResult<ForumPostDto>>> GetForumPostsAsync(int page = 1, int pageSize = 20, long? categoryId = null)
    {
        try
        {
            var client = CreateClient();
            var url = $"forum/posts?page={page}&pageSize={pageSize}";
            if (categoryId.HasValue)
            {
                url += $"&categoryId={categoryId.Value}";
            }
            
            LogService.Instance.Debug($"Requesting GET {url}", "ApiService");
            var response = await client.GetAsync(url);
            LogService.Instance.Debug($"Response status: {response.StatusCode}", "ApiService");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                LogService.Instance.Error($"Error content: {errorContent}", "ApiService");
                return new ApiResponse<PagedResult<ForumPostDto>> { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<ForumPostDto>>>(_jsonOptions) 
                ?? new ApiResponse<PagedResult<ForumPostDto>> { Success = false };
        }
        catch (Exception ex)
        {
            LogService.Instance.Error("Exception in GetForumPostsAsync", "ApiService", ex);
            return new ApiResponse<PagedResult<ForumPostDto>> { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<ForumPostDto>> GetForumPostAsync(long id)
    {
        try
        {
            var client = CreateClient();
            var url = $"forum/posts/{id}";
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                LogService.Instance.Error($"Error content: {errorContent}", "ApiService");
                return new ApiResponse<ForumPostDto> { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse<ForumPostDto>>(_jsonOptions) 
                ?? new ApiResponse<ForumPostDto> { Success = false };
        }
        catch (Exception ex)
        {
            LogService.Instance.Error($"Exception in GetForumPostAsync for id {id}", "ApiService", ex);
            return new ApiResponse<ForumPostDto> { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<PagedResult<ForumCommentDto>>> GetForumCommentsAsync(long postId, int page = 1, int pageSize = 20)
    {
        var client = CreateClient();
        var response = await client.GetAsync($"forum/posts/{postId}/comments?page={page}&pageSize={pageSize}");
        return await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<ForumCommentDto>>>(_jsonOptions) 
            ?? new ApiResponse<PagedResult<ForumCommentDto>> { Success = false };
    }

    public async Task<ApiResponse<PagedResult<ForumPostDto>>> SearchForumPostsAsync(string keyword, int page = 1, int pageSize = 20)
    {
        var client = CreateClient();
        var response = await client.GetAsync($"forum/posts/search?keyword={Uri.EscapeDataString(keyword)}&page={page}&pageSize={pageSize}");
        return await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<ForumPostDto>>>(_jsonOptions) 
            ?? new ApiResponse<PagedResult<ForumPostDto>> { Success = false };
    }

    public async Task<ApiResponse<ForumPostDto>> CreateForumPostAsync(CreatePostRequest request)
    {
        try
        {
            await EnsureTokenLoadedAsync();
            var client = CreateClient();
            var response = await client.PostAsJsonAsync("forum/posts", request, _jsonOptions);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return new ApiResponse<ForumPostDto> { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse<ForumPostDto>>(_jsonOptions) 
                ?? new ApiResponse<ForumPostDto> { Success = false };
        }
        catch (Exception ex)
        {
            return new ApiResponse<ForumPostDto> { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<ForumPostDto>> UpdateForumPostAsync(long id, CreatePostRequest request)
    {
        try
        {
            await EnsureTokenLoadedAsync();
            var client = CreateClient();
            var response = await client.PutAsJsonAsync($"forum/posts/{id}", request, _jsonOptions);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return new ApiResponse<ForumPostDto> { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse<ForumPostDto>>(_jsonOptions) 
                ?? new ApiResponse<ForumPostDto> { Success = false };
        }
        catch (Exception ex)
        {
            return new ApiResponse<ForumPostDto> { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse> DeleteForumPostAsync(long id)
    {
        try
        {
            await EnsureTokenLoadedAsync();
            var client = CreateClient();
            var response = await client.DeleteAsync($"forum/posts/{id}");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return new ApiResponse { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse>(_jsonOptions) 
                ?? new ApiResponse { Success = false };
        }
        catch (Exception ex)
        {
            return new ApiResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<ForumCommentDto>> CreateForumCommentAsync(CreateCommentRequest request)
    {
        try
        {
            await EnsureTokenLoadedAsync();
            var client = CreateClient();
            var json = System.Text.Json.JsonSerializer.Serialize(request, _jsonOptions);
            LogService.Instance.Debug($"Creating comment for post {request.PostId}. Payload: {json}", "ApiService");
            
            var response = await client.PostAsJsonAsync("forum/comments", request, _jsonOptions);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                LogService.Instance.Error($"Create comment failed: {response.StatusCode} {errorContent}", "ApiService");
                return new ApiResponse<ForumCommentDto> { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse<ForumCommentDto>>(_jsonOptions) 
                ?? new ApiResponse<ForumCommentDto> { Success = false };
        }
        catch (Exception ex)
        {
            LogService.Instance.Error("Exception in CreateForumCommentAsync", "ApiService", ex);
            return new ApiResponse<ForumCommentDto> { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse> DeleteForumCommentAsync(long id)
    {
        try
        {
            await EnsureTokenLoadedAsync();
            var client = CreateClient();
            var response = await client.DeleteAsync($"forum/comments/{id}");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return new ApiResponse { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse>(_jsonOptions) 
                ?? new ApiResponse { Success = false };
        }
        catch (Exception ex)
        {
            return new ApiResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<Dictionary<string, bool>>> LikeForumPostAsync(long id)
    {
        try
        {
            await EnsureTokenLoadedAsync();
            var client = CreateClient();
            var response = await client.PostAsync($"forum/posts/{id}/like", null);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return new ApiResponse<Dictionary<string, bool>> { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse<Dictionary<string, bool>>>(_jsonOptions) 
                ?? new ApiResponse<Dictionary<string, bool>> { Success = false };
        }
        catch (Exception ex)
        {
            return new ApiResponse<Dictionary<string, bool>> { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<Dictionary<string, bool>>> LikeForumCommentAsync(long id)
    {
        try
        {
            await EnsureTokenLoadedAsync();
            var client = CreateClient();
            var response = await client.PostAsync($"forum/comments/{id}/like", null);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return new ApiResponse<Dictionary<string, bool>> { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse<Dictionary<string, bool>>>(_jsonOptions) 
                ?? new ApiResponse<Dictionary<string, bool>> { Success = false };
        }
        catch (Exception ex)
        {
            return new ApiResponse<Dictionary<string, bool>> { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<Dictionary<string, bool>>> FavoriteForumPostAsync(long id)
    {
        try
        {
            await EnsureTokenLoadedAsync();
            var client = CreateClient();
            var response = await client.PostAsync($"forum/posts/{id}/favorite", null);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return new ApiResponse<Dictionary<string, bool>> { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse<Dictionary<string, bool>>>(_jsonOptions) 
                ?? new ApiResponse<Dictionary<string, bool>> { Success = false };
        }
        catch (Exception ex)
        {
            return new ApiResponse<Dictionary<string, bool>> { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<PagedResult<ForumPostDto>>> GetMyForumPostsAsync(int page = 1, int pageSize = 20)
    {
        await EnsureTokenLoadedAsync();
        var client = CreateClient();
        var response = await client.GetAsync($"forum/my/posts?page={page}&pageSize={pageSize}");
        return await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<ForumPostDto>>>(_jsonOptions) 
            ?? new ApiResponse<PagedResult<ForumPostDto>> { Success = false };
    }

    public async Task<ApiResponse<PagedResult<ForumPostDto>>> GetMyForumFavoritesAsync(int page = 1, int pageSize = 20)
    {
        await EnsureTokenLoadedAsync();
        var client = CreateClient();
        var response = await client.GetAsync($"forum/my/favorites?page={page}&pageSize={pageSize}");
        return await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<ForumPostDto>>>(_jsonOptions) 
            ?? new ApiResponse<PagedResult<ForumPostDto>> { Success = false };
    }

    public async Task<ApiResponse<PagedResult<ForumCommentDto>>> GetMyForumCommentsAsync(int page = 1, int pageSize = 20)
    {
        await EnsureTokenLoadedAsync();
        var client = CreateClient();
        var response = await client.GetAsync($"forum/my/comments?page={page}&pageSize={pageSize}");
        return await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<ForumCommentDto>>>(_jsonOptions) 
            ?? new ApiResponse<PagedResult<ForumCommentDto>> { Success = false };
    }

    // ============== 通知 API ==============

    public async Task<ApiResponse<PagedResult<NotificationDto>>> GetNotificationsAsync(int page = 1, int pageSize = 20)
    {
        try
        {
            await EnsureTokenLoadedAsync();
            var client = CreateClient();
            var response = await client.GetAsync($"notifications?page={page}&pageSize={pageSize}");
            
            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                LogService.Instance.Error($"GetNotifications failed: {response.StatusCode} {content}", "ApiService");
                return new ApiResponse<PagedResult<NotificationDto>> { Success = false, Message = $"HTTP {response.StatusCode}" };
            }

            // var json = await response.Content.ReadAsStringAsync();
            // LogService.Instance.Debug($"GetNotifications response: {json}", "ApiService");

            return await response.Content.ReadFromJsonAsync<ApiResponse<PagedResult<NotificationDto>>>(_jsonOptions) 
                ?? new ApiResponse<PagedResult<NotificationDto>> { Success = false };
        }
        catch (Exception ex)
        {
            LogService.Instance.Error("Exception in GetNotificationsAsync", "ApiService", ex);
            return new ApiResponse<PagedResult<NotificationDto>> { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<long>> GetUnreadNotificationCountAsync()
    {
        await EnsureTokenLoadedAsync();
        var client = CreateClient();
        var response = await client.GetAsync("notifications/unread-count");
        return await response.Content.ReadFromJsonAsync<ApiResponse<long>>(_jsonOptions) 
            ?? new ApiResponse<long> { Success = false };
    }

    public async Task<ApiResponse> MarkAllNotificationsAsReadAsync()
    {
        await EnsureTokenLoadedAsync();
        var client = CreateClient();
        var response = await client.PostAsync("notifications/read-all", null);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            return new ApiResponse { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
        }

        return await response.Content.ReadFromJsonAsync<ApiResponse>(_jsonOptions) 
            ?? new ApiResponse { Success = false };
    }

    // ============== 抽奖 API ==============

    public async Task<ApiResponse<List<ZMSL.App.Models.Lottery>>> GetLotteriesAsync()
    {
        try
        {
            await EnsureTokenLoadedAsync();
            var client = CreateClient();
            var response = await client.GetAsync("lotteries");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return new ApiResponse<List<ZMSL.App.Models.Lottery>> { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse<List<ZMSL.App.Models.Lottery>>>(_jsonOptions)
                ?? new ApiResponse<List<ZMSL.App.Models.Lottery>> { Success = false };
        }
        catch (Exception ex)
        {
            return new ApiResponse<List<ZMSL.App.Models.Lottery>> { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse> JoinLotteryAsync(long id, string? code = null)
    {
        try
        {
            await EnsureTokenLoadedAsync();
            var client = CreateClient();
            var request = code != null ? new { code } : null;
            var response = await client.PostAsJsonAsync($"lotteries/{id}/join", request, _jsonOptions);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return new ApiResponse { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse>(_jsonOptions)
                ?? new ApiResponse { Success = false };
        }
        catch (Exception ex)
        {
            return new ApiResponse { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<List<ZMSL.App.Models.LotteryWinner>>> GetLotteryWinnersAsync(long id)
    {
        try
        {
            await EnsureTokenLoadedAsync();
            var client = CreateClient();
            var response = await client.GetAsync($"lotteries/{id}/winners");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return new ApiResponse<List<ZMSL.App.Models.LotteryWinner>> { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            return await response.Content.ReadFromJsonAsync<ApiResponse<List<ZMSL.App.Models.LotteryWinner>>>(_jsonOptions)
                ?? new ApiResponse<List<ZMSL.App.Models.LotteryWinner>> { Success = false };
        }
        catch (Exception ex)
        {
            return new ApiResponse<List<ZMSL.App.Models.LotteryWinner>> { Success = false, Message = ex.Message };
        }
    }

    // ============== 服务端下载源 ==============

    public async Task<ApiResponse<ServerVersionListDto>> GetAvailableVersionsAsync(string server)
    {
        try
        {
            var client = CreateClient();
            // 使用完整URL覆盖BaseAddress
            var response = await client.GetAsync($"https://api.mslmc.cn/v3/query/available_versions/{server}");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return new ApiResponse<ServerVersionListDto> { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            var mslResponse = await response.Content.ReadFromJsonAsync<MslApiResponse<ServerVersionListDto>>(_jsonOptions);
            
            if (mslResponse == null)
                return new ApiResponse<ServerVersionListDto> { Success = false, Message = "Response is empty" };

            return new ApiResponse<ServerVersionListDto>
            {
                Success = mslResponse.IsSuccess,
                Message = mslResponse.Message,
                Data = mslResponse.Data
            };
        }
        catch (Exception ex)
        {
            return new ApiResponse<ServerVersionListDto> { Success = false, Message = ex.Message };
        }
    }

    public async Task<ApiResponse<ServerDownloadUrlDto>> GetServerDownloadUrlAsync(string server, string version)
    {
        try
        {
            var client = CreateClient();
            // 使用完整URL覆盖BaseAddress
            var response = await client.GetAsync($"https://api.mslmc.cn/v3/download/server/{server}/{version}");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return new ApiResponse<ServerDownloadUrlDto> { Success = false, Message = $"HTTP {(int)response.StatusCode}: {errorContent}" };
            }

            var mslResponse = await response.Content.ReadFromJsonAsync<MslApiResponse<ServerDownloadUrlDto>>(_jsonOptions);
             
            if (mslResponse == null)
                return new ApiResponse<ServerDownloadUrlDto> { Success = false, Message = "Response is empty" };

            return new ApiResponse<ServerDownloadUrlDto>
            {
                Success = mslResponse.IsSuccess,
                Message = mslResponse.Message,
                Data = mslResponse.Data
            };
        }
        catch (Exception ex)
        {
            return new ApiResponse<ServerDownloadUrlDto> { Success = false, Message = ex.Message };
        }
    }

    // ============== 鸣谢 ==============

    public async Task<ApiResponse<List<AcknowledgmentDto>>> GetAcknowledgmentsAsync()
    {
        try
        {
            var client = CreateClient();
            var response = await client.GetAsync("acknowledgments");
            return await response.Content.ReadFromJsonAsync<ApiResponse<List<AcknowledgmentDto>>>(_jsonOptions) 
                ?? new ApiResponse<List<AcknowledgmentDto>> { Success = false };
        }
        catch (Exception ex)
        {
            return new ApiResponse<List<AcknowledgmentDto>> { Success = false, Message = ex.Message };
        }
    }
}
