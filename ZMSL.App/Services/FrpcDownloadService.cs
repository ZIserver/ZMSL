using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using ZMSL.Shared.DTOs;

namespace ZMSL.App.Services;

public class FrpcDownloadService
{
    private readonly ApiService _apiService;
    private readonly ILogger<FrpcDownloadService> _logger;
    private static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    public event EventHandler<FrpcDownloadProgressEventArgs>? ProgressChanged;
    public event EventHandler<FrpcDownloadStateChangedEventArgs>? StateChanged;

    public bool IsDownloading { get; private set; }
    public bool IsDownloadComplete { get; private set; }

    public FrpcDownloadService(ApiService apiService, ILogger<FrpcDownloadService> logger)
    {
        _apiService = apiService;
        _logger = logger;
    }

    public async Task<bool> CheckAndDownloadAsync()
    {
        var frpcPath = GetFrpcPath();
        
        if (File.Exists(frpcPath))
        {
            _logger.LogInformation("frpc.exe 已存在：{Path}", frpcPath);
            return true;
        }

        _logger.LogWarning("frpc.exe 不存在，需要下载");
        return await DownloadFrpcAsync();
    }

    private async Task<bool> DownloadFrpcAsync()
    {
        if (IsDownloading)
        {
            _logger.LogWarning("正在下载中，无法重复启动");
            return false;
        }

        try
        {
            IsDownloading = true;
            IsDownloadComplete = false;
            StateChanged?.Invoke(this, new FrpcDownloadStateChangedEventArgs 
            { 
                IsDownloading = true,
                Message = "正在获取下载链接..."
            });

            var result = await _apiService.GetAsync<ApiResponse<string>>("frp/frpc/download-url");
            if (result == null || !result.Success || string.IsNullOrEmpty(result.Data))
            {
                throw new Exception("无法获取下载链接");
            }

            var downloadUrl = result.Data;
            _logger.LogInformation("获取到下载链接：{Url}", downloadUrl);

            var tempZipPath = Path.Combine(Path.GetTempPath(), $"frpc_{Guid.NewGuid()}.zip");
            var extractPath = Path.Combine(Path.GetTempPath(), $"frpc_extract_{Guid.NewGuid()}");
            
            try
            {
                await DownloadFileMultiThreadedAsync(downloadUrl, tempZipPath);
                
                StateChanged?.Invoke(this, new FrpcDownloadStateChangedEventArgs 
                { 
                    IsDownloading = true,
                    Message = "正在解压文件..."
                });

                await ExtractAndInstallFrpcAsync(tempZipPath, extractPath);
                
                IsDownloadComplete = true;
                StateChanged?.Invoke(this, new FrpcDownloadStateChangedEventArgs 
                { 
                    IsDownloading = false,
                    IsComplete = true,
                    Message = "下载完成"
                });

                _logger.LogInformation("frpc 下载并安装完成");
                return true;
            }
            finally
            {
                Cleanup(tempZipPath, extractPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "frpc 下载失败");
            StateChanged?.Invoke(this, new FrpcDownloadStateChangedEventArgs 
            { 
                IsDownloading = false,
                IsComplete = false,
                Message = $"下载失败：{ex.Message}"
            });
            return false;
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private async Task DownloadFileMultiThreadedAsync(string url, string filePath)
    {
        const int threadCount = 4;
        const long minSizeForMultiThread = 1024 * 1024;

        _logger.LogInformation("开始多线程下载：{Url}", url);

        var request = new HttpRequestMessage(HttpMethod.Head, url);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var fileSize = response.Content.Headers.ContentLength ?? -1L;
        
        if (fileSize == -1L || fileSize < minSizeForMultiThread)
        {
            _logger.LogInformation("文件较小或不支持范围请求，使用单线程下载");
            await DownloadSingleThreadAsync(url, filePath);
            return;
        }

        _logger.LogInformation("文件大小：{Size} bytes，使用 {Threads} 个线程下载", fileSize, threadCount);

        var downloadTasks = new List<Task>();
        var segmentSize = fileSize / threadCount;
        var tempFiles = new List<string>();

        for (int i = 0; i < threadCount; i++)
        {
            long start = i * segmentSize;
            long end = (i == threadCount - 1) ? fileSize - 1 : start + segmentSize - 1;
            string tempFile = $"{filePath}.part{i}";
            tempFiles.Add(tempFile);

            downloadTasks.Add(DownloadSegmentAsync(url, filePath, tempFile, start, end, i, threadCount));
        }

        await Task.WhenAll(downloadTasks);

        await MergeSegmentsAsync(tempFiles, filePath);
    }

    private async Task DownloadSegmentAsync(string url, string outputFile, string tempFile, long start, long end, int segmentIndex, int totalSegments)
    {
        using var client = new HttpClient();
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(start, end);
        
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        long totalRead = 0;
        long segmentSize = end - start + 1;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            totalRead += bytesRead;

            var progress = (double)totalRead / segmentSize * 100 / totalSegments + 
                          (double)segmentIndex / totalSegments * 100;
            
            ProgressChanged?.Invoke(this, new FrpcDownloadProgressEventArgs 
            { 
                Progress = progress,
                DownloadedBytes = totalRead,
                TotalBytes = segmentSize
            });
        }

        _logger.LogInformation("段 {Index} 下载完成：{Bytes} bytes", segmentIndex, totalRead);
    }

    private async Task DownloadSingleThreadAsync(string url, string filePath)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead);
            totalRead += bytesRead;

            if (totalBytes > 0)
            {
                var progress = (double)totalRead / totalBytes * 100;
                ProgressChanged?.Invoke(this, new FrpcDownloadProgressEventArgs 
                { 
                    Progress = progress,
                    DownloadedBytes = totalRead,
                    TotalBytes = totalBytes
                });
            }
        }
    }

    private async Task MergeSegmentsAsync(List<string> tempFiles, string outputFile)
    {
        _logger.LogInformation("开始合并 {Count} 个分段", tempFiles.Count);
        
        await using var outputStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        
        foreach (var tempFile in tempFiles)
        {
            await using var inputStream = new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.None, 8192, true);
            await inputStream.CopyToAsync(outputStream);
        }

        _logger.LogInformation("文件合并完成：{Path}", outputFile);
    }

    private async Task ExtractAndInstallFrpcAsync(string zipPath, string extractPath)
    {
        Directory.CreateDirectory(extractPath);
        ZipFile.ExtractToDirectory(zipPath, extractPath);

        var frpcExe = Directory.GetFiles(extractPath, "frpc.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (frpcExe == null)
        {
            throw new Exception("压缩包中未找到 frpc.exe");
        }

        var targetDir = Path.GetDirectoryName(GetFrpcPath())!;
        Directory.CreateDirectory(targetDir);
        
        var targetPath = GetFrpcPath();
        File.Copy(frpcExe, targetPath, true);
        
        _logger.LogInformation("frpc.exe 已安装到：{Path}", targetPath);
    }

    private void Cleanup(params string[] paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理临时文件失败：{Path}", path);
            }
        }
    }

    private string GetFrpcPath()
    {
        var appPath = AppContext.BaseDirectory;
        return Path.Combine(appPath, "frpc", "frpc.exe");
    }
}

public class FrpcDownloadProgressEventArgs : EventArgs
{
    public double Progress { get; set; }
    public long DownloadedBytes { get; set; }
    public long TotalBytes { get; set; }
}

public class FrpcDownloadStateChangedEventArgs : EventArgs
{
    public bool IsDownloading { get; set; }
    public bool IsComplete { get; set; }
    public string Message { get; set; } = "";
}
