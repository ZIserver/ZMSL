using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZMSL.Shared.DTOs;

namespace ZMSL.App.ViewModels;

public partial class ServerCoreViewModel : ObservableObject
{
    private readonly Services.ServerDownloadService _downloadService;
    private readonly Services.DatabaseService _db;

    [ObservableProperty]
    public partial List<ServerCoreGroupDto> Groups { get; set; } = new();

    [ObservableProperty]
    public partial ServerCoreGroupDto? SelectedGroup { get; set; }

    [ObservableProperty]
    public partial List<ServerCoreFileDto> Files { get; set; } = new();

    [ObservableProperty]
    public partial ServerCoreFileDto? SelectedFile { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial double DownloadProgress { get; set; }

    [ObservableProperty]
    public partial string DownloadStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SearchKeyword { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SearchMcVersion { get; set; } = string.Empty;

    private CancellationTokenSource? _downloadCts;

    public ServerCoreViewModel(Services.ServerDownloadService downloadService, Services.DatabaseService db)
    {
        _downloadService = downloadService;
        _db = db;
        _downloadService.DownloadProgress += OnDownloadProgress;
    }

    [RelayCommand]
    private async Task LoadGroupsAsync()
    {
        IsLoading = true;
        try
        {
            // 旧API已移除，此功能暂时禁用
            // Groups = await _downloadService.GetGroupsAsync();
            Groups = new List<ServerCoreGroupDto>();
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载分组失败: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadFilesAsync()
    {
        if (SelectedGroup == null) return;
        
        IsLoading = true;
        try
        {
            // 旧API已移除，此功能暂时禁用
            // Files = await _downloadService.GetFilesAsync(SelectedGroup.Id);
            Files = new List<ServerCoreFileDto>();
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载文件失败: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        IsLoading = true;
        try
        {
            // 旧API已移除，此功能暂时禁用
            // Files = await _downloadService.SearchAsync(
            //     string.IsNullOrWhiteSpace(SearchKeyword) ? null : SearchKeyword,
            //     string.IsNullOrWhiteSpace(SearchMcVersion) ? null : SearchMcVersion);
            Files = new List<ServerCoreFileDto>();
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"搜索失败: {ex.Message}");
            Files = new List<ServerCoreFileDto>();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (SelectedFile == null) return;

        IsDownloading = true;
        DownloadProgress = 0;
        DownloadStatus = "准备下载...";
        _downloadCts = new CancellationTokenSource();

        try
        {
            var settings = await _db.GetSettingsAsync();
            var targetFolder = Path.Combine(settings.DefaultServerPath, "Downloads");
            
            // 旧API签名已改变，此功能暂时禁用
            // 需要提供: serverName (string), version (string), targetFolder (string), build (optional), cancellationToken
            // var path = await _downloadService.DownloadServerCoreAsync(SelectedFile, targetFolder, _downloadCts.Token);
            DownloadStatus = "此功能暂时禁用，请使用创建服务器向导";
            await Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            DownloadStatus = "下载已取消";
        }
        catch (Exception ex)
        {
            DownloadStatus = $"下载失败: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    private void OnDownloadProgress(object? sender, Services.DownloadProgressEventArgs e)
    {
        DownloadProgress = e.Progress;
        DownloadStatus = $"下载中: {FormatBytes(e.DownloadedBytes)} / {FormatBytes(e.TotalBytes)} ({e.Progress:F1}%)";
    }

    partial void OnSelectedGroupChanged(ServerCoreGroupDto? value)
    {
        // 移除自动加载，由UI层控制
    }

    private static string FormatBytes(long bytes)
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
