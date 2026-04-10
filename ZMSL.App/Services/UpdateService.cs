using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using ZMSL.Shared.DTOs;

namespace ZMSL.App.Services;

public class UpdateService
{
    private readonly ApiService _api;
    private readonly string _updateFolder;
    private readonly string _pendingUpdateFile;
    private CancellationTokenSource? _downloadCts;

    public event EventHandler<UpdateCheckResult>? UpdateCheckCompleted;
    public event EventHandler<double>? DownloadProgressChanged;
    public event EventHandler<UpdateDownloadResult>? DownloadCompleted;

    public bool IsDownloading { get; private set; }
    public bool HasPendingUpdate => File.Exists(_pendingUpdateFile);

    public UpdateService(ApiService api)
    {
        _api = api;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _updateFolder = Path.Combine(appData, "ZMSL", "Updates");
        // 更新包现在是 exe 安装程序
        _pendingUpdateFile = Path.Combine(_updateFolder, "update_installer.exe");
        
        Directory.CreateDirectory(_updateFolder);
    }

    public string CurrentVersion
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }
    }

    /// <summary>
    /// 检查更新
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        var result = new UpdateCheckResult();

        try
        {
            var response = await _api.CheckUpdateAsync(CurrentVersion);
            if (response.Success && response.Data != null)
            {
                result.Success = true;
                result.HasUpdate = response.Data.HasUpdate;
                result.LatestVersion = response.Data.Version;
                result.Changelog = response.Data.Changelog;
                result.DownloadUrl = response.Data.DownloadUrl;
                result.FileSize = response.Data.FileSize ?? 0;
                result.FileHash = response.Data.FileHash;
                result.ForceUpdate = response.Data.ForceUpdate;
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = response.Message ?? "检查更新失败";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        UpdateCheckCompleted?.Invoke(this, result);
        return result;
    }

    /// <summary>
    /// 后台下载更新
    /// </summary>
    public async Task<UpdateDownloadResult> DownloadUpdateAsync(string downloadUrl, string? expectedHash = null)
    {
        var result = new UpdateDownloadResult();

        if (IsDownloading)
        {
            result.Success = false;
            result.ErrorMessage = "已有下载任务进行中";
            return result;
        }

        IsDownloading = true;
        _downloadCts = new CancellationTokenSource();

        try
        {
            // 清理旧的更新文件
            if (File.Exists(_pendingUpdateFile))
            {
                File.Delete(_pendingUpdateFile);
            }

            var tempFile = _pendingUpdateFile + ".tmp";
            var progress = new Progress<double>(p => DownloadProgressChanged?.Invoke(this, p));

            var success = await _api.DownloadFileWithProgressAsync(downloadUrl, tempFile, progress, _downloadCts.Token);

            if (success)
            {
                // 验证文件哈希
                if (!string.IsNullOrEmpty(expectedHash))
                {
                    var actualHash = await ComputeFileHashAsync(tempFile);
                    if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(tempFile);
                        result.Success = false;
                        result.ErrorMessage = "文件校验失败";
                        return result;
                    }
                }

                File.Move(tempFile, _pendingUpdateFile, true);
                result.Success = true;
                result.FilePath = _pendingUpdateFile;
            }
            else
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                result.Success = false;
                result.ErrorMessage = "下载失败";
            }
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "下载已取消";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            IsDownloading = false;
            _downloadCts?.Dispose();
            _downloadCts = null;
        }

        DownloadCompleted?.Invoke(this, result);
        return result;
    }

    /// <summary>
    /// 取消下载
    /// </summary>
    public void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    /// <summary>
    /// 应用更新 - 打开安装包并退出当前程序
    /// 安装包安装完成后会自动重新启动应用
    /// </summary>
    public bool ApplyUpdate()
    {
        if (!HasPendingUpdate) return false;

        try
        {
            var currentExePath = Environment.ProcessPath;
            var appFolder = Path.GetDirectoryName(currentExePath);

            // 记录当前程序路径到临时文件，供安装完成后重启使用
            var restartInfoFile = Path.Combine(Path.GetTempPath(), "zmsl_restart_after_install.txt");
            File.WriteAllText(restartInfoFile, currentExePath);

            // 启动安装包
            Process.Start(new ProcessStartInfo
            {
                FileName = _pendingUpdateFile,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(_pendingUpdateFile)
            });

            // 等待一小段时间确保安装程序启动
            Thread.Sleep(1000);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 清理待安装的更新
    /// </summary>
    public void ClearPendingUpdate()
    {
        try
        {
            if (File.Exists(_pendingUpdateFile))
            {
                File.Delete(_pendingUpdateFile);
            }
        }
        catch { }
    }

    private static async Task<string> ComputeFileHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}

public class UpdateCheckResult
{
    public bool Success { get; set; }
    public bool HasUpdate { get; set; }
    public string? LatestVersion { get; set; }
    public string? Changelog { get; set; }
    public string? DownloadUrl { get; set; }
    public long FileSize { get; set; }
    public string? FileHash { get; set; }
    public bool ForceUpdate { get; set; }
    public string? ErrorMessage { get; set; }
}

public class UpdateDownloadResult
{
    public bool Success { get; set; }
    public string? FilePath { get; set; }
    public string? ErrorMessage { get; set; }
}
