using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using ZMSL.Shared.DTOs;

namespace ZMSL.App.ViewModels;

public partial class FrpViewModel : ObservableObject
{
    private readonly Services.FrpService _frpService;
    private readonly Services.AuthService _authService;
    private readonly Services.ApiService _apiService;
    private readonly Services.FrpcDownloadService _downloadService;

    [ObservableProperty]
    public partial ObservableCollection<FrpNodeDto> Nodes { get; set; } = new();

    [ObservableProperty]
    public partial FrpNodeDto? SelectedNode { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<TunnelDto> Tunnels { get; set; } = new();

    [ObservableProperty]
    public partial TunnelDto? SelectedTunnel { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string CurrentConnectAddress { get; set; } = "";

    [ObservableProperty]
    public partial string CurrentTunnelName { get; set; } = "";

    [ObservableProperty]
    public partial int CurrentTunnelId { get; set; } = -1;

    [ObservableProperty]
    public partial string LatencyText { get; set; } = "— ms";

    [ObservableProperty]
    public partial string LatencyColor { get; set; } = "Gray";

    [ObservableProperty]
    public partial string TrafficIn { get; set; } = "0 B";

    [ObservableProperty]
    public partial ObservableCollection<string> Logs { get; set; } = new();

    [ObservableProperty]
    public partial bool IsLoggedIn { get; set; }

    [ObservableProperty]
    public partial bool IsFrpcInstalled { get; set; }

    [ObservableProperty]
    public partial double DownloadProgress { get; set; }

    [ObservableProperty]
    public partial string DownloadStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    // 创建隧道表单
    [ObservableProperty]
    public partial string TunnelName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TunnelProtocol { get; set; } = "tcp";

    [ObservableProperty]
    public partial int TunnelLocalPort { get; set; } = 25565;

    [ObservableProperty]
    public partial bool EnableProxyProtocol { get; set; }

    public FrpViewModel(Services.FrpService frpService, Services.AuthService authService, Services.ApiService apiService, Services.FrpcDownloadService downloadService)
    {
        _frpService = frpService;
        _authService = authService;
        _apiService = apiService;
        _downloadService = downloadService;

        _frpService.StatusChanged += OnFrpStatusChanged;
        _frpService.LogReceived += OnLogReceived;
        _frpService.TrafficUpdated += OnTrafficUpdated;
        _frpService.LatencyUpdated += OnLatencyUpdated;
        
        _downloadService.ProgressChanged += OnDownloadProgressChanged;
        _downloadService.StateChanged += OnDownloadStateChanged;
        
        _authService.LoginStateChanged += (s, e) => 
        {
            IsLoggedIn = _authService.IsLoggedIn;
            if (IsLoggedIn) _ = LoadDataAsync();
        };
        
        IsLoggedIn = _authService.IsLoggedIn;
        IsConnected = _frpService.IsConnected;
        IsFrpcInstalled = Services.FrpService.IsFrpcInstalled();
    }

    private void OnTrafficUpdated(object? sender, Services.TrafficEventArgs e)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            TrafficIn = FormatTraffic(e.TotalIn);
        });
    }

    private void OnLatencyUpdated(object? sender, int latency)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            if (latency >= 9999)
            {
                LatencyText = "超时";
                LatencyColor = "Red";
            }
            else
            {
                LatencyText = $"{latency} ms";
                LatencyColor = latency < 100 ? "#10B981" : (latency < 200 ? "#F59E0B" : "Red");
            }
        });
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            // 尝试恢复状态
            await _frpService.RestoreStateAsync();

            // 加载历史日志
            if (Logs.Count == 0)
            {
                var historyLogs = _frpService.GetLogs();
                foreach (var log in historyLogs)
                {
                    Logs.Add(log);
                }
            }
            
            // 同步连接状态
            IsConnected = _frpService.IsConnected;
            if (IsConnected)
            {
                CurrentTunnelId = _frpService.CurrentTunnelId ?? -1;
                CurrentTunnelName = _frpService.CurrentTunnelName;
                CurrentConnectAddress = _frpService.CurrentConnectAddress;
                
                System.Diagnostics.Debug.WriteLine($"[FrpViewModel] 恢复状态: ID={CurrentTunnelId}, Addr={CurrentConnectAddress}");
            }

            var nodes = await _frpService.GetNodesAsync();
            Nodes = new ObservableCollection<FrpNodeDto>(nodes);

            if (IsLoggedIn)
            {
                var tunnels = await _frpService.GetMyTunnelsAsync();
                
                // 加载 DNS 记录并匹配
                try 
                {
                    var dnsResult = await _apiService.GetMyDnsRecordsAsync();
                    if (dnsResult.Success && dnsResult.Data != null)
                    {
                        var dnsMap = dnsResult.Data.Where(d => d.TunnelId > 0)
                            .ToDictionary(d => d.TunnelId, d => d);
                        
                        foreach (var t in tunnels)
                        {
                            if (dnsMap.TryGetValue(t.Id, out var dns))
                            {
                                var prefix = dns.Rr.Replace("_minecraft._tcp.", "");
                                t.ResolvedDomain = $"{prefix}.{dns.DomainName}";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"加载DNS记录失败: {ex.Message}");
                }
                
                // 恢复运行状态
                if (IsConnected && CurrentTunnelId != -1)
                {
                    var runningTunnel = tunnels.FirstOrDefault(t => t.Id == CurrentTunnelId);
                    if (runningTunnel != null)
                    {
                        runningTunnel.IsRunning = true;
                        
                        // 始终从隧道列表恢复地址信息，优先使用域名
                        CurrentConnectAddress = !string.IsNullOrEmpty(runningTunnel.ResolvedDomain)
                            ? runningTunnel.ResolvedDomain
                            : (!string.IsNullOrEmpty(runningTunnel.NodeDomain)
                                ? $"{runningTunnel.NodeDomain}:{runningTunnel.RemotePort}"
                                : runningTunnel.ConnectAddress);
                        CurrentTunnelName = runningTunnel.Name;
                        System.Diagnostics.Debug.WriteLine($"[FrpViewModel] 已从列表恢复连接信息: {CurrentConnectAddress}");
                    }
                }
                
                Tunnels = new ObservableCollection<TunnelDto>(tunnels);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CreateTunnelAsync()
    {
        if (SelectedNode == null || string.IsNullOrWhiteSpace(TunnelName)) return;

        IsLoading = true;
        try
        {
            var result = await _frpService.CreateTunnelAsync(new CreateTunnelRequest
            {
                NodeId = SelectedNode.Id,
                Name = TunnelName,
                Protocol = TunnelProtocol,
                LocalPort = TunnelLocalPort
            });

            if (result.Success && result.Tunnel != null)
            {
                // 保存本地配置
                await _frpService.SaveLocalTunnelAsync(new Models.LocalFrpTunnel
                {
                    RemoteTunnelId = result.Tunnel.Id,
                    Name = result.Tunnel.Name,
                    NodeHost = SelectedNode.Host,
                    NodePort = SelectedNode.Port,
                    Protocol = result.Tunnel.Protocol,
                    LocalPort = result.Tunnel.LocalPort,
                    RemotePort = result.Tunnel.RemotePort,
                    ConnectAddress = result.Tunnel.ConnectAddress,
                    EnableProxyProtocol = EnableProxyProtocol
                });

                Tunnels.Add(result.Tunnel);
                TunnelName = string.Empty;
                EnableProxyProtocol = false;
                Logs.Add($"隧道创建成功: {result.Tunnel.ConnectAddress}");
            }
            else
            {
                Logs.Add($"创建失败: {result.Message}");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteTunnelAsync()
    {
        if (SelectedTunnel == null) return;

        var result = await _frpService.DeleteTunnelAsync(SelectedTunnel.Id);
        if (result.Success)
        {
            Tunnels.Remove(SelectedTunnel);
            Logs.Add("隧道已删除");
        }
        else
        {
            Logs.Add($"删除失败: {result.Message}");
        }
    }

    [RelayCommand]
    private async Task StartTunnelAsync()
    {
        if (SelectedTunnel == null) return;

        // 每次启动前从 API 获取最新流量信息（不使用缓存）
        //Logs.Add("正在检查流量配额...");
        var userResult = await _apiService.GetCurrentUserAsync();
        if (userResult.Success && userResult.Data != null)
        {
            var user = userResult.Data;

            // 检查实名认证
            if ((SelectedTunnel.RequiresRealName ?? false) && !user.IsRealNameVerified)
            {
                Logs.Add("❌ 启动失败：需要实名认证");
                App.MainWindow.DispatcherQueue.TryEnqueue(async () =>
                {
                    var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                    {
                        Title = "需要实名认证",
                        Content = "该节点需要实名认证才能使用。\n请前往官网用户中心进行认证。",
                        CloseButtonText = "关闭",
                        PrimaryButtonText = "前往认证",
                        XamlRoot = App.MainWindow.Content.XamlRoot
                    };
                    var result = await dialog.ShowAsync();
                    if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                    {
                        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://msl.v2.zhsdev.top/dashboard"));
                    }
                });
                return;
            }

            if ((user.TrafficUsed ?? 0) >= (user.TrafficQuota ?? 0))
            {
                Logs.Add("❌ 您的流量已用完，请购买流量后继续使用");
                
                // 显示提示对话框
                App.MainWindow.DispatcherQueue.TryEnqueue(async () =>
                {
                    var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                    {
                        Title = "流量已用完",
                        Content = $"您的流量已用完（已用 {FormatTraffic(user.TrafficUsed ?? 0)} / {FormatTraffic(user.TrafficQuota ?? 0)}）\n\n请充值后购买流量继续使用 FRP 穿透功能。",
                        CloseButtonText = "知道了",
                        XamlRoot = App.MainWindow.Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                });
                return;
            }
        }

        Logs.Add("正在启动隧道...");
        var success = await _frpService.StartTunnelAsync(SelectedTunnel.Id);
        if (!success)
        {
            Logs.Add("启动失败");
        }
    }

    private static string FormatTraffic(long bytes)
    {
        if (bytes >= 1073741824)
            return $"{bytes / 1073741824.0:F2} GB";
        if (bytes >= 1048576)
            return $"{bytes / 1048576.0:F2} MB";
        return $"{bytes / 1024.0:F2} KB";
    }

    [RelayCommand]
    private async Task StopTunnelAsync()
    {
        await _frpService.StopTunnelAsync();
        Logs.Add("隧道已停止");
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs.Clear();
        _frpService.ClearLogs();
    }

    [RelayCommand]
    private async Task DownloadFrpcAsync()
    {
        if (_downloadService.IsDownloading) return;

        IsDownloading = true;
        DownloadStatus = "正在准备下载...";
        
        try
        {
            var success = await _downloadService.CheckAndDownloadAsync();
            
            if (success)
            {
                IsFrpcInstalled = true;
                DownloadStatus = "下载完成！";
                Logs.Add("frpc 下载完成");
            }
            else
            {
                DownloadStatus = "下载失败";
                Logs.Add("frpc 下载失败");
            }
        }
        catch (Exception ex)
        {
            DownloadStatus = $"下载出错：{ex.Message}";
            Logs.Add($"frpc 下载出错：{ex.Message}");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private void OnDownloadProgressChanged(object? sender, Services.FrpcDownloadProgressEventArgs e)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            DownloadProgress = e.Progress;
        });
    }

    private void OnDownloadStateChanged(object? sender, Services.FrpcDownloadStateChangedEventArgs e)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            IsDownloading = e.IsDownloading;
            DownloadStatus = e.Message;
            if (e.IsComplete)
            {
                IsFrpcInstalled = true;
            }
        });
    }

    private void OnFrpStatusChanged(object? sender, Services.FrpStatusEventArgs e)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            IsConnected = e.IsConnected;
            
            // 更新所有隧道的 IsRunning 状态
            foreach (var tunnel in Tunnels)
            {
                tunnel.IsRunning = false;
            }
            
            if (e.IsConnected && SelectedTunnel != null)
            {
                CurrentConnectAddress = !string.IsNullOrEmpty(SelectedTunnel.ResolvedDomain)
                    ? SelectedTunnel.ResolvedDomain
                    : (!string.IsNullOrEmpty(SelectedTunnel.NodeDomain)
                        ? $"{SelectedTunnel.NodeDomain}:{SelectedTunnel.RemotePort}"
                        : SelectedTunnel.ConnectAddress);

                CurrentTunnelName = SelectedTunnel.Name;
                CurrentTunnelId = SelectedTunnel.Id;
                
                // 标记当前运行的隧道
                var running = Tunnels.FirstOrDefault(t => t.Id == SelectedTunnel.Id);
                if (running != null) running.IsRunning = true;
            }
            else if (!e.IsConnected)
            {
                CurrentConnectAddress = "";
                CurrentTunnelName = "";
                CurrentTunnelId = -1;
            }
            
            // 刷新列表显示
            OnPropertyChanged(nameof(Tunnels));
            
            (App.MainWindow as MainWindow)?.UpdateFrpStatus(e.IsConnected);
        });
    }

    private void OnLogReceived(object? sender, string log)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            // 日志已经AddLog加过时间戳，直接从服务层获取最新一条
            var logs = _frpService.GetLogs();
            if (logs.Count > 0)
            {
                var latestLog = logs[logs.Count - 1];
                if (!Logs.Contains(latestLog))
                {
                    Logs.Add(latestLog);
                }
            }
            while (Logs.Count > 500)
            {
                Logs.RemoveAt(0);
            }
        });
    }
}
