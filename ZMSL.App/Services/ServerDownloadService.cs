using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZMSL.App.Models;

namespace ZMSL.App.Services;

public class ServerDownloadService
{
    private readonly DatabaseService _db;
    private readonly HttpClient _apiClient;  // API 客户端
    private readonly HttpClient _downloadClient;  // 下载文件专用客户端
    private string _currentMirrorSource = "MSL";
    private bool _initialized = false;

    public event EventHandler<DownloadProgressEventArgs>? DownloadProgress;

    public ServerDownloadService(DatabaseService db, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        // API 客户端
        _apiClient = httpClientFactory.CreateClient();
        _apiClient.DefaultRequestHeaders.Add("User-Agent", "ZMSL-App/2.0.0");
        _apiClient.Timeout = TimeSpan.FromSeconds(10);
        
        // 文件下载客户端（长超时）
        _downloadClient = httpClientFactory.CreateClient();
        _downloadClient.Timeout = TimeSpan.FromHours(1);
    }

    /// <summary>
    /// 初始化镜像源设置（从数据库读取）
    /// </summary>
    private async Task InitializeMirrorSourceAsync()
    {
        if (_initialized) return;
        
        try
        {
            var settings = await _db.GetSettingsAsync();
            var mirrorSource = settings.DownloadMirrorSource ?? "MSL";
            SetMirrorSource(mirrorSource);
        }
        catch
        {
            // 如果读取失败，使用默认的 MSL
            SetMirrorSource("MSL");
        }
        
        _initialized = true;
    }

    /// <summary>
    /// 设置当前使用的镜像源
    /// </summary>
    public void SetMirrorSource(string sourceName)
    {
        _currentMirrorSource = sourceName;
        var config = MirrorSourceService.GetSourceConfig(sourceName);
        System.Diagnostics.Debug.WriteLine($"[ServerDownload] 切换镜像源到: {config.DisplayName}");
    }

    /// <summary>
    /// 获取当前镜像源的基础 URL
    /// </summary>
    private string GetCurrentBaseUrl()
    {
        var config = MirrorSourceService.GetSourceConfig(_currentMirrorSource);
        return config.BaseUrl;
    }

    // ============== MSL API 数据结构 ==============
    
    public class MslServerClassify
    {
        [JsonPropertyName("pluginsCore")]
        public List<string> PluginsCore { get; set; } = new();

        [JsonPropertyName("pluginsAndModsCore_Forge")]
        public List<string> PluginsAndModsCore_Forge { get; set; } = new();

        [JsonPropertyName("pluginsAndModsCore_Fabric")]
        public List<string> PluginsAndModsCore_Fabric { get; set; } = new();

        [JsonPropertyName("modsCore_Forge")]
        public List<string> ModsCore_Forge { get; set; } = new();

        [JsonPropertyName("modsCore_Fabric")]
        public List<string> ModsCore_Fabric { get; set; } = new();

        [JsonPropertyName("vanillaCore")]
        public List<string> VanillaCore { get; set; } = new();

        [JsonPropertyName("bedrockCore")]
        public List<string> BedrockCore { get; set; } = new();

        [JsonPropertyName("proxyCore")]
        public List<string> ProxyCore { get; set; } = new();
    }

    // ============== ZSync API 数据结构 ==============

    public class ZSyncServerClassify
    {
        [JsonPropertyName("pluginsCore")]
        public List<string> PluginsCore { get; set; } = new();

        [JsonPropertyName("mixedCore_Forge")]
        public List<string> MixedCore_Forge { get; set; } = new();

        [JsonPropertyName("mixedCore_Fabric")]
        public List<string> MixedCore_Fabric { get; set; } = new();

        [JsonPropertyName("modsCore_Forge")]
        public List<string> ModsCore_Forge { get; set; } = new();

        [JsonPropertyName("modsCore_Fabric")]
        public List<string> ModsCore_Fabric { get; set; } = new();

        [JsonPropertyName("vanillaCore")]
        public List<string> VanillaCore { get; set; } = new();

        [JsonPropertyName("bedrockCore")]
        public List<string> BedrockCore { get; set; } = new();

        [JsonPropertyName("proxyCore")]
        public List<string> ProxyCore { get; set; } = new();
    }

    public class MslApiResponse<T>
    {
        public int Code { get; set; }
        public string Message { get; set; } = "";
        public T? Data { get; set; }
    }

    public class ZSyncApiResponse<T>
    {
        public int Code { get; set; }
        public string Message { get; set; } = "";
        public T? Data { get; set; }
    }

    public class MslVersionList
    {
        public List<string> VersionList { get; set; } = new();
    }

    public class MslDownloadData
    {
        public string Url { get; set; } = "";
        public string? Sha256 { get; set; }
    }

    public class ServerCoreCategory
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public List<string> Cores { get; set; } = new();
    }

    public class ServerCoreItem
    {
        public string CoreName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
    }

    // ============== 公共方法 ==============

    /// <summary>
    /// 获取所有服务端分类
    /// </summary>
    public async Task<List<ServerCoreCategory>> GetServerCategoriesAsync()
    {
        // 首次使用时初始化镜像源设置
        await InitializeMirrorSourceAsync();
        
        try
        {
            if (_currentMirrorSource == "ZSync")
            {
                return await GetServerCategoriesFromZSyncAsync();
            }
            else
            {
                return await GetServerCategoriesFromMslAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ServerDownload] 获取分类失败: {ex.Message}");
        }
        return new List<ServerCoreCategory>();
    }

    private async Task<List<ServerCoreCategory>> GetServerCategoriesFromMslAsync()
    {
        var url = $"{GetCurrentBaseUrl()}query/server_classify";
        var response = await _apiClient.GetFromJsonAsync<MslApiResponse<MslServerClassify>>(url);
        if (response?.Code == 200 && response.Data != null)
        {
            var categories = new List<ServerCoreCategory>
            {
                new() { Name = "plugins", DisplayName = "插件端", Cores = response.Data.PluginsCore },
                new() { Name = "pluginsAndModsForge", DisplayName = "混合端(Forge)", Cores = response.Data.PluginsAndModsCore_Forge },
                new() { Name = "pluginsAndModsFabric", DisplayName = "混合端(Fabric)", Cores = response.Data.PluginsAndModsCore_Fabric },
                new() { Name = "modsForge", DisplayName = "模组端(Forge)", Cores = response.Data.ModsCore_Forge },
                new() { Name = "modsFabric", DisplayName = "模组端(Fabric)", Cores = response.Data.ModsCore_Fabric },
                new() { Name = "vanilla", DisplayName = "原版端", Cores = response.Data.VanillaCore },
                new() { Name = "bedrock", DisplayName = "基岩版", Cores = response.Data.BedrockCore },
                new() { Name = "proxy", DisplayName = "群组端", Cores = response.Data.ProxyCore }
            };
            return categories.Where(c => c.Cores.Count > 0).ToList();
        }
        return new List<ServerCoreCategory>();
    }

    private async Task<List<ServerCoreCategory>> GetServerCategoriesFromZSyncAsync()
    {
        var url = $"{GetCurrentBaseUrl()}query/server_classify";
        var response = await _apiClient.GetFromJsonAsync<ZSyncApiResponse<ZSyncServerClassify>>(url);
        if (response?.Code == 200 && response.Data != null)
        {
            var categories = new List<ServerCoreCategory>
            {
                new() { Name = "pluginsCore", DisplayName = "插件端", Cores = response.Data.PluginsCore },
                new() { Name = "mixedCore_Forge", DisplayName = "混合端(Forge)", Cores = response.Data.MixedCore_Forge },
                new() { Name = "mixedCore_Fabric", DisplayName = "混合端(Fabric)", Cores = response.Data.MixedCore_Fabric },
                new() { Name = "modsCore_Forge", DisplayName = "模组端(Forge)", Cores = response.Data.ModsCore_Forge },
                new() { Name = "modsCore_Fabric", DisplayName = "模组端(Fabric)", Cores = response.Data.ModsCore_Fabric },
                new() { Name = "vanillaCore", DisplayName = "原版端", Cores = response.Data.VanillaCore },
                new() { Name = "bedrockCore", DisplayName = "基岩版", Cores = response.Data.BedrockCore },
                new() { Name = "proxyCore", DisplayName = "群组端", Cores = response.Data.ProxyCore }
            };
            return categories.Where(c => c.Cores.Count > 0).ToList();
        }
        return new List<ServerCoreCategory>();
    }

    /// <summary>
    /// 获取指定服务端支持的MC版本列表
    /// </summary>
    public async Task<List<string>> GetAvailableVersionsAsync(string serverName)
    {
        // 首次使用时初始化镜像源设置
        await InitializeMirrorSourceAsync();
        
        try
        {
            var url = $"{GetCurrentBaseUrl()}query/available_versions/{Uri.EscapeDataString(serverName)}";
            
            if (_currentMirrorSource == "ZSync")
            {
                var response = await _apiClient.GetFromJsonAsync<ZSyncApiResponse<MslVersionList>>(url);
                if (response?.Code == 200 && response.Data != null)
                {
                    return response.Data.VersionList;
                }
            }
            else
            {
                var response = await _apiClient.GetFromJsonAsync<MslApiResponse<MslVersionList>>(url);
                if (response?.Code == 200 && response.Data != null)
                {
                    return response.Data.VersionList;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ServerDownload] 获取版本列表失败: {ex.Message}");
        }
        return new List<string>();
    }

    /// <summary>
    /// 获取服务端下载地址
    /// </summary>
    public async Task<MslDownloadData?> GetDownloadUrlAsync(string serverName, string version, string build = "latest")
    {
        // 首次使用时初始化镜像源设置
        await InitializeMirrorSourceAsync();
        
        try
        {
            if (_currentMirrorSource == "ZSync")
            {
                var url = $"{GetCurrentBaseUrl()}download/server/{Uri.EscapeDataString(serverName)}/{Uri.EscapeDataString(version)}";
                var response = await _apiClient.GetFromJsonAsync<ZSyncApiResponse<MslDownloadData>>(url);
                if (response?.Code == 200 && response.Data != null)
                {
                    return response.Data;
                }
            }
            else
            {
                var url = $"{GetCurrentBaseUrl()}download/server/{Uri.EscapeDataString(serverName)}/{Uri.EscapeDataString(version)}?build={build}";
                var response = await _apiClient.GetFromJsonAsync<MslApiResponse<MslDownloadData>>(url);
                if (response?.Code == 200 && response.Data != null)
                {
                    return response.Data;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ServerDownload] 获取下载地址失败: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// 下载服务端核心文件
    /// </summary>
    public async Task<string> DownloadServerCoreAsync(
        string serverName, 
        string version, 
        string targetFolder, 
        string? build = "latest",
        CancellationToken cancellationToken = default)
    {
        // 获取下载地址
        var downloadData = await GetDownloadUrlAsync(serverName, version, build ?? "latest");
        if (downloadData == null || string.IsNullOrEmpty(downloadData.Url))
        {
            throw new Exception("无法获取下载地址");
        }

        // 确定文件名
        var fileName = Path.GetFileName(new Uri(downloadData.Url).AbsolutePath);
        if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
        {
            fileName = $"{serverName}-{version}.jar";
        }

        Directory.CreateDirectory(targetFolder);
        var targetPath = Path.Combine(targetFolder, fileName);

        // 创建下载记录
        var record = new DownloadRecord
        {
            Type = "core",
            Name = $"{serverName} {version}",
            Version = version,
            Url = downloadData.Url,
            LocalPath = targetPath,
            Status = "Downloading"
        };
        _db.Downloads.Add(record);
        await _db.SaveChangesAsync().ConfigureAwait(false);

        try
        {
            // 先检查服务器是否支持Range请求
            using var headResponse = await _downloadClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, downloadData.Url), 
                cancellationToken).ConfigureAwait(false);
            
            var totalBytes = headResponse.Content.Headers.ContentLength ?? 0;
            var supportsRange = headResponse.Headers.AcceptRanges.Contains("bytes") && totalBytes > 0;
            
            record.TotalBytes = totalBytes;
            await _db.SaveChangesAsync().ConfigureAwait(false);

            // 将下载任务完全移至后台线程执行，避免阻塞 UI
            await Task.Run(async () =>
            {
                if (supportsRange && totalBytes > 1024 * 1024) // 大于1MB才使用多线程
                {
                    await DownloadWithMultiThreadAsync(downloadData.Url, targetPath, totalBytes, fileName, record, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await DownloadSingleThreadAsync(downloadData.Url, targetPath, totalBytes, fileName, record, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);

            record.Status = "Completed";
            record.CompletedAt = DateTime.Now;
            await _db.SaveChangesAsync().ConfigureAwait(false);

            System.Diagnostics.Debug.WriteLine($"[ServerDownload] 下载成功: {fileName}");
            return targetPath;
        }
        catch (Exception ex)
        {
            record.Status = "Failed";
            await _db.SaveChangesAsync();
            System.Diagnostics.Debug.WriteLine($"[ServerDownload] 下载失败: {ex.Message}");
            throw new Exception($"下载失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 单线程下载
    /// </summary>
    private async Task DownloadSingleThreadAsync(
        string url,
        string targetPath,
        long totalBytes,
        string fileName,
        DownloadRecord record,
        CancellationToken cancellationToken)
    {
        using var response = await _downloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        long downloadedBytes = 0;
        int bytesRead;
        var lastReportTime = DateTime.MinValue;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
            downloadedBytes += bytesRead;
            
            // 限制进度更新频率 (每100ms更新一次)
            var now = DateTime.Now;
            if ((now - lastReportTime).TotalMilliseconds > 100 || downloadedBytes == totalBytes)
            {
                lastReportTime = now;
                record.DownloadedBytes = downloadedBytes;

                DownloadProgress?.Invoke(this, new DownloadProgressEventArgs
                {
                    FileName = fileName,
                    TotalBytes = totalBytes,
                    DownloadedBytes = downloadedBytes,
                    Progress = totalBytes > 0 ? (double)downloadedBytes / totalBytes * 100 : 0
                });
            }
        }
    }

    /// <summary>
    /// 多线程分块下载
    /// </summary>
    private async Task DownloadWithMultiThreadAsync(
        string url,
        string targetPath,
        long totalBytes,
        string fileName,
        DownloadRecord record,
        CancellationToken cancellationToken)
    {
        const int threadCount = 4; // 使用4个线程
        var chunkSize = totalBytes / threadCount;
        var tasks = new List<Task>();
        var tempFiles = new List<string>();
        long totalDownloadedBytes = 0;
        
        System.Diagnostics.Debug.WriteLine($"[ServerDownload] 使用多线程下载: {threadCount}个线程, 文件大小: {totalBytes / 1024.0 / 1024.0:F2}MB");

        // 启动进度报告任务
        var progressTask = Task.Run(async () =>
        {
            var lastReportTime = DateTime.MinValue;
            while (Interlocked.Read(ref totalDownloadedBytes) < totalBytes && !cancellationToken.IsCancellationRequested)
            {
                var currentBytes = Interlocked.Read(ref totalDownloadedBytes);
                var now = DateTime.Now;
                
                if ((now - lastReportTime).TotalMilliseconds > 100)
                {
                    lastReportTime = now;
                    record.DownloadedBytes = currentBytes;

                    DownloadProgress?.Invoke(this, new DownloadProgressEventArgs
                    {
                        FileName = fileName,
                        TotalBytes = totalBytes,
                        DownloadedBytes = currentBytes,
                        Progress = totalBytes > 0 ? (double)currentBytes / totalBytes * 100 : 0
                    });
                }
                
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);

        // 创建多个下载任务
        for (int i = 0; i < threadCount; i++)
        {
            var index = i;
            var start = i * chunkSize;
            var end = (i == threadCount - 1) ? totalBytes - 1 : (i + 1) * chunkSize - 1;
            var tempFile = $"{targetPath}.part{i}";
            tempFiles.Add(tempFile);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);

                    using var response = await _downloadClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                        Interlocked.Add(ref totalDownloadedBytes, bytesRead);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ServerDownload] 线程{index}下载失败: {ex.Message}");
                    throw;
                }
            }, cancellationToken));
        }

        // 等待所有线程完成
        await Task.WhenAll(tasks).ConfigureAwait(false);

        // 确保进度任务结束
        record.DownloadedBytes = totalBytes;
        DownloadProgress?.Invoke(this, new DownloadProgressEventArgs
        {
            FileName = fileName,
            TotalBytes = totalBytes,
            DownloadedBytes = totalBytes,
            Progress = 100
        });

        // 合并文件
        System.Diagnostics.Debug.WriteLine($"[ServerDownload] 开始合并文件...");
        using (var outputStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            foreach (var tempFile in tempFiles)
            {
                using var inputStream = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                await inputStream.CopyToAsync(outputStream, cancellationToken).ConfigureAwait(false);
            }
        }

        // 删除临时文件
        foreach (var tempFile in tempFiles)
        {
            try
            {
                File.Delete(tempFile);
            }
            catch { }
        }

        System.Diagnostics.Debug.WriteLine($"[ServerDownload] 文件合并完成");
    }

    /// <summary>
    /// 获取下载历史记录
    /// </summary>
    public async Task<List<DownloadRecord>> GetDownloadHistoryAsync()
    {
        return await _db.Downloads.OrderByDescending(d => d.CreatedAt).ToListAsync();
    }
    
    /// <summary>
    /// 下载 Forge/NeoForge 安装器
    /// </summary>
    public async Task<string> DownloadForgeInstallerAsync(
        string coreName,
        string mcVersion,
        string targetFolder,
        string? build = "latest",
        CancellationToken cancellationToken = default)
    {
        var downloadData = await GetDownloadUrlAsync(coreName, mcVersion, build ?? "latest");
        if (downloadData == null || string.IsNullOrEmpty(downloadData.Url))
        {
            throw new Exception("无法获取Forge安装器下载地址");
        }

        var fileName = Path.GetFileName(new Uri(downloadData.Url).AbsolutePath);
        if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
        {
            fileName = $"{coreName}-{mcVersion}-installer.jar";
        }

        Directory.CreateDirectory(targetFolder);
        var targetPath = Path.Combine(targetFolder, fileName);

        var record = new DownloadRecord
        {
            Type = "forge-installer",
            Name = $"{coreName} {mcVersion} Installer",
            Version = mcVersion,
            Url = downloadData.Url,
            LocalPath = targetPath,
            Status = "Downloading"
        };
        _db.Downloads.Add(record);
        await _db.SaveChangesAsync().ConfigureAwait(false);

        try
        {
            using var headResponse = await _downloadClient.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, downloadData.Url),
                cancellationToken).ConfigureAwait(false);

            var totalBytes = headResponse.Content.Headers.ContentLength ?? 0;
            var supportsRange = headResponse.Headers.AcceptRanges.Contains("bytes") && totalBytes > 0;

            record.TotalBytes = totalBytes;
            await _db.SaveChangesAsync().ConfigureAwait(false);

            await Task.Run(async () =>
            {
                if (supportsRange && totalBytes > 1024 * 1024)
                {
                    await DownloadWithMultiThreadAsync(downloadData.Url, targetPath, totalBytes, fileName, record, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await DownloadSingleThreadAsync(downloadData.Url, targetPath, totalBytes, fileName, record, cancellationToken).ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);

            record.Status = "Completed";
            record.CompletedAt = DateTime.Now;
            await _db.SaveChangesAsync().ConfigureAwait(false);

            System.Diagnostics.Debug.WriteLine($"[ServerDownload] Forge安装器下载成功: {fileName}");
            return targetPath;
        }
        catch (Exception ex)
        {
            record.Status = "Failed";
            await _db.SaveChangesAsync();
            System.Diagnostics.Debug.WriteLine($"[ServerDownload] Forge安装器下载失败: {ex.Message}");
            throw new Exception($"下载失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 检查核心类型是否需要安装流程
    /// </summary>
    public static bool NeedsInstallProcess(string coreType)
    {
        return coreType.Equals("forge", StringComparison.OrdinalIgnoreCase) ||
               coreType.Equals("neoforge", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 检查文件名是否为安装器
    /// </summary>
    public static bool IsInstallerJar(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        return name.Contains("installer") && name.EndsWith(".jar");
    }
}

public class DownloadProgressEventArgs : EventArgs
{
    public string FileName { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public double Progress { get; set; }
}
