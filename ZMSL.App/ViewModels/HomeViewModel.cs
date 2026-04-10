using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using ZMSL.Shared.DTOs;

namespace ZMSL.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly Services.ApiService _apiService;
    private readonly Services.AuthService _authService;
    private readonly Services.ServerManagerService _serverManager;
    private readonly Services.LinuxNodeService _nodeService;
    private readonly Services.DatabaseService _db;
    private readonly Services.FrpService _frpService;

    [ObservableProperty]
    public partial List<AnnouncementDto> Announcements { get; set; } = new();

    [ObservableProperty]
    public partial List<AdvertisementDto> Advertisements { get; set; } = new();

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string WelcomeMessage { get; set; } = "欢迎使用 ZMSL 我的世界服务器启动器";

    [ObservableProperty]
    public partial int ServerCount { get; set; }

    [ObservableProperty]
    public partial int RunningServerCount { get; set; }

    [ObservableProperty]
    public partial string FrpStatusText { get; set; } = "未连接";

    [ObservableProperty]
    public partial string FrpTrafficDisplay { get; set; } = "— / —";

    public HomeViewModel(Services.ApiService apiService, Services.AuthService authService, Services.ServerManagerService serverManager, Services.LinuxNodeService nodeService, Services.DatabaseService db, Services.FrpService frpService)
    {
        _apiService = apiService;
        _authService = authService;
        _serverManager = serverManager;
        _nodeService = nodeService;
        _db = db;
        _frpService = frpService;

        _authService.LoginStateChanged += async (s, e) => 
        { 
            UpdateWelcome(); 
            UpdateFrpTrafficDisplay();
            await LoadDataAsync();
        };
        _frpService.StatusChanged += (s, e) => UpdateFrpStatus(e.IsConnected);
        UpdateWelcome();
        UpdateFrpStatus(_frpService.IsConnected);
        UpdateFrpTrafficDisplay();
    }

    private void UpdateFrpStatus(bool isConnected)
    {
        // 确保在 UI 线程上执行属性更新
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcher != null)
        {
            dispatcher.TryEnqueue(() =>
            {
                FrpStatusText = isConnected ? "已连接" : "未连接";
            });
        }
        else
        {
            // 如果无法获取当前线程的 DispatcherQueue，尝试获取主窗口的
            if (App.MainWindow != null)
            {
                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    FrpStatusText = isConnected ? "已连接" : "未连接";
                });
            }
        }
    }

    private void UpdateFrpTrafficDisplay()
    {
        var user = _authService.CurrentUser;
        long quota = user?.TrafficQuota ?? 0;
        long used = user?.TrafficUsed ?? 0;

        string trafficDisplay = (user != null && quota > 0)
            ? $"{FormatTraffic(used)} / {FormatTraffic(quota)}"
            : "— / —";

        // 确保在 UI 线程上执行属性更新
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcher != null)
        {
            dispatcher.TryEnqueue(() =>
            {
                FrpTrafficDisplay = trafficDisplay;
            });
        }
        else
        {
            // 如果无法获取当前线程的 DispatcherQueue，尝试获取主窗口的
            if (App.MainWindow != null)
            {
                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    FrpTrafficDisplay = trafficDisplay;
                });
            }
        }
    }

    private static string FormatTraffic(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }

    private void UpdateWelcome()
    {
        if (_authService.IsLoggedIn && _authService.CurrentUser != null)
        {
            WelcomeMessage = $"欢迎回来, {_authService.CurrentUser.Username}!";
        }
        else
        {
            WelcomeMessage = "欢迎使用 ZMSL 我的世界服务器启动器";
        }
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            // 刷新用户信息以获取最新流量
            await _authService.RefreshUserInfoAsync();

            // 服务器统计 与 公告/广告 并行加载
            var statsTask = LoadServerStatsAsync();
            var adsTask = LoadAnnouncementsAndAdsAsync();

            await Task.WhenAll(statsTask, adsTask);

            var (serverCount, localRunningCount) = await statsTask;
            var (announcements, advertisements) = await adsTask;

            ServerCount = serverCount;
            RunningServerCount = localRunningCount;
            if (announcements != null) Announcements = announcements;
            if (advertisements != null) Advertisements = advertisements;
            UpdateFrpStatus(_frpService.IsConnected);
            UpdateFrpTrafficDisplay();
            IsLoading = false;

            // 远程运行数后台再补
            _ = LoadRemoteRunningCountAsync(localRunningCount);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HomePage] 加载数据异常：{ex.Message}");
            IsLoading = false;
        }
    }

    /// <summary>
    /// 加载服务器数量统计（本地 + 数据库中的远程数量，不调节点 API）
    /// </summary>
    private async Task<(int ServerCount, int LocalRunningCount)> LoadServerStatsAsync()
    {
        var servers = await _serverManager.GetServersAsync();
        int localServerCount = servers.Count;
        int localRunningCount = servers.Count(s => _serverManager.IsServerRunning(s.Id));
        int remoteServerCount = await _db.ExecuteWithLockAsync(async db =>
            await db.RemoteServers.CountAsync());
        return (localServerCount + remoteServerCount, localRunningCount);
    }

    /// <summary>
    /// 加载公告和广告，返回 (公告列表, 广告列表)
    /// </summary>
    private async Task<(List<AnnouncementDto>? Announcements, List<AdvertisementDto>? Advertisements)> LoadAnnouncementsAndAdsAsync()
    {
        List<AnnouncementDto>? announcements = null;
        List<AdvertisementDto>? advertisements = null;

        try
        {
            var announcementTask = _apiService.GetAnnouncementsAsync();
            var adTask = _apiService.GetAdvertisementsAsync();
            await Task.WhenAll(announcementTask, adTask);

            var announcementResult = await announcementTask;
            if (announcementResult.Success && announcementResult.Data != null)
            {
                announcements = announcementResult.Data;
                System.Diagnostics.Debug.WriteLine($"[HomePage] 加载公告成功：{announcements.Count} 条");
            }

            var adResult = await adTask;
            if (adResult.Success && adResult.Data != null)
            {
                advertisements = adResult.Data;
                System.Diagnostics.Debug.WriteLine($"[HomePage] 加载广告成功：{advertisements.Count} 条");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HomePage] 加载公告/广告异常：{ex.Message}");
        }

        return (announcements, advertisements);
    }

    private async Task LoadRemoteRunningCountAsync(int localRunningCount)
    {
        try
        {
            var nodes = await _nodeService.GetNodesAsync();
            int remoteRunningCount = 0;

            foreach (var node in nodes)
            {
                var remoteServers = await _nodeService.GetLocalRemoteServersAsync(node.Id);
                foreach (var remoteServer in remoteServers)
                {
                    try
                    {
                        var status = await _nodeService.GetServerStatusAsync(node, remoteServer.RemoteServerId);
                        if (status?.Running == true)
                            remoteRunningCount++;
                    }
                    catch { /* 忽略 */ }
                }
            }

            RunningServerCount = localRunningCount + remoteRunningCount;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[HomePage] 加载远程运行数异常：{ex.Message}");
        }
    }
}
