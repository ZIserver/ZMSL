using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.RegularExpressions;
using ZMSL.App.Models;
using ZMSL.App.Services;

namespace ZMSL.App.ViewModels;

public partial class MeFrpViewModel : ObservableObject
{
    private readonly MeFrpService _meFrpService;
    private readonly List<string> _generalLogs = new();
    private readonly DispatcherQueue _dispatcherQueue;
    private static readonly Regex AccessEndpointRegex = new(@"您可以使用\s*\[(?<endpoint>[^\[\]]+)\]\s*访问您的服务", RegexOptions.Compiled);

    [ObservableProperty]
    public partial bool IsLoggedIn { get; set; }

    [ObservableProperty]
    public partial bool IsInstalled { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool IsDownloading { get; set; }

    [ObservableProperty]
    public partial double DownloadProgress { get; set; }

    [ObservableProperty]
    public partial string DownloadStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TokenInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MeFrpUserInfo? UserInfo { get; set; }

    [ObservableProperty]
    public partial List<MeFrpProxy> Proxies { get; set; } = new();

    [ObservableProperty]
    public partial List<MeFrpNode> ProxyNodes { get; set; } = new();

    [ObservableProperty]
    public partial List<MeFrpNode> CreateNodes { get; set; } = new();

    [ObservableProperty]
    public partial MeFrpProxy? SelectedProxy { get; set; }

    [ObservableProperty]
    public partial MeFrpNode? SelectedCreateNode { get; set; }

    [ObservableProperty]
    public partial string SelectedNodeDetail { get; set; } = "请选择节点查看详情";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "请先安装并登录 MeFrp";

    [ObservableProperty]
    public partial List<string> Logs { get; set; } = new();

    [ObservableProperty]
    public partial int? RunningProxyId { get; set; }

    [ObservableProperty]
    public partial string RunningProxyName { get; set; } = "未启动";

    [ObservableProperty]
    public partial string LogTargetTitle { get; set; } = "请选择隧道查看日志";

    [ObservableProperty]
    public partial string CurrentAccessEndpoint { get; set; } = "暂无访问地址";

    [ObservableProperty]
    public partial string CreateProxyName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CreateLocalIp { get; set; } = "127.0.0.1";

    [ObservableProperty]
    public partial int CreateLocalPort { get; set; } = 25565;

    [ObservableProperty]
    public partial int CreateRemotePort { get; set; }

    [ObservableProperty]
    public partial string CreateProtocol { get; set; } = "tcp";

    public MeFrpViewModel(MeFrpService meFrpService)
    {
        _meFrpService = meFrpService;
        _dispatcherQueue = App.MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        IsInstalled = MeFrpService.IsMeFrpInstalled();
        OnPropertyChanged(nameof(InstallStateText));
        OnPropertyChanged(nameof(LoginStateText));

        _meFrpService.ProgressChanged += (_, e) =>
        {
            EnqueueOnUi(() =>
            {
                DownloadProgress = e.Progress;
            });
        };

        _meFrpService.StateChanged += (_, e) =>
        {
            EnqueueOnUi(() =>
            {
                IsDownloading = e.IsDownloading;
                DownloadStatus = e.Message;
                if (!string.IsNullOrWhiteSpace(e.Message))
                {
                    AddLog(e.Message);
                }

                if (e.IsComplete)
                {
                    IsInstalled = true;
                }
            });
        };

        _meFrpService.TunnelStateChanged += (_, e) =>
        {
            EnqueueOnUi(() =>
            {
                RunningProxyId = e.IsRunning ? e.ProxyId : null;
                RunningProxyName = e.IsRunning && !string.IsNullOrWhiteSpace(e.ProxyName) ? e.ProxyName : "未启动";
                if (!e.IsRunning)
                {
                    CurrentAccessEndpoint = "暂无访问地址";
                }
                RefreshDisplayedLogs();
                OnPropertyChanged(nameof(RunningTunnelText));
            });
        };

        _meFrpService.TunnelLogReceived += (_, e) =>
        {
            EnqueueOnUi(() =>
            {
                UpdateAccessEndpointFromLog(e.Message);
                if (SelectedProxy?.ProxyId == e.ProxyId || RunningProxyId == e.ProxyId)
                {
                    RefreshDisplayedLogs();
                }
            });
        };
    }

    public async Task InitializeAsync()
    {
        IsInstalled = MeFrpService.IsMeFrpInstalled();
        RunningProxyId = _meFrpService.CurrentRunningProxyId;
        RunningProxyName = string.IsNullOrWhiteSpace(_meFrpService.CurrentRunningProxyName) ? "未启动" : _meFrpService.CurrentRunningProxyName;
        var saved = await _meFrpService.GetSavedTokenAsync();
        TokenInput = saved ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(saved))
        {
            await ValidateAndLoadAsync(saved, removeIfInvalid: true);
        }
        else
        {
            IsLoggedIn = false;
            StatusMessage = IsInstalled ? "请输入访问令牌以登录 MeFrp" : "检测到未安装 MeFrp 客户端";
        }

        RefreshDisplayedLogs();
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (IsDownloading)
        {
            return;
        }

        IsBusy = true;
        DownloadProgress = 0;
        DownloadStatus = "正在准备安装...";
        var ok = await _meFrpService.DownloadAndInstallAsync();
        IsInstalled = ok || MeFrpService.IsMeFrpInstalled();
        StatusMessage = IsInstalled ? "MeFrp 客户端已就绪" : "MeFrp 客户端安装失败";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        await ValidateAndLoadAsync(TokenInput, removeIfInvalid: false);
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _meFrpService.ClearTokenAsync();
        IsLoggedIn = false;
        UserInfo = null;
        Proxies = new List<MeFrpProxy>();
        ProxyNodes = new List<MeFrpNode>();
        CreateNodes = new List<MeFrpNode>();
        SelectedProxy = null;
        StatusMessage = "已退出 MeFrp 登录";
        AddLog(StatusMessage);
        RefreshDisplayedLogs();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var token = await _meFrpService.GetSavedTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            StatusMessage = "请先登录";
            IsLoggedIn = false;
            return;
        }

        await ValidateAndLoadAsync(token, removeIfInvalid: true);
    }

    [RelayCommand]
    private async Task AutoFillFreePortAsync()
    {
        var token = await _meFrpService.GetSavedTokenAsync();
        if (string.IsNullOrWhiteSpace(token) || SelectedCreateNode == null)
        {
            return;
        }

        var port = await _meFrpService.GetFreePortAsync(token, SelectedCreateNode.NodeId, CreateProtocol);
        if (port.HasValue)
        {
            CreateRemotePort = port.Value;
            AddLog($"已获取空闲端口：{port.Value}");
        }
        else
        {
            AddLog("获取空闲端口失败");
        }
    }

    [RelayCommand]
    private async Task CreateTunnelAsync()
    {
        var token = await _meFrpService.GetSavedTokenAsync();
        if (string.IsNullOrWhiteSpace(token) || SelectedCreateNode == null)
        {
            AddLog("请先登录并选择节点");
            return;
        }

        if (string.IsNullOrWhiteSpace(CreateProxyName) || CreateLocalPort <= 0 || CreateRemotePort <= 0)
        {
            AddLog("请完整填写隧道信息");
            return;
        }

        IsBusy = true;
        var result = await _meFrpService.CreateProxyAsync(token, new
        {
            nodeId = SelectedCreateNode.NodeId,
            proxyName = CreateProxyName,
            localIp = string.IsNullOrWhiteSpace(CreateLocalIp) ? "127.0.0.1" : CreateLocalIp,
            localPort = CreateLocalPort,
            remotePort = CreateRemotePort,
            domain = string.Empty,
            proxyType = CreateProtocol,
            accessKey = string.Empty,
            httpPlugin = string.Empty,
            httpUser = string.Empty,
            httpPassword = string.Empty,
            crtPath = string.Empty,
            keyPath = string.Empty,
            proxyProtocolVersion = string.Empty,
            useEncryption = false,
            useCompression = false,
            transportProtocol = CreateProtocol,
            locations = string.Empty,
            hostHeaderRewrite = string.Empty,
            requestHeaders = new Dictionary<string, string>(),
            responseHeaders = new Dictionary<string, string>()
        });

        AddLog(result.Message);
        if (result.Success)
        {
            await RefreshAsync();
        }

        IsBusy = false;
    }

    [RelayCommand]
    private async Task StartTunnelAsync()
    {
        var token = await _meFrpService.GetSavedTokenAsync();
        if (string.IsNullOrWhiteSpace(token) || SelectedProxy == null)
        {
            AddLog("请先选择隧道");
            return;
        }

        var result = await _meFrpService.GetProxyConfigAsync(token, SelectedProxy.ProxyId);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Config))
        {
            AddLog(result.Message);
            return;
        }

        var startResult = await _meFrpService.StartTunnelAsync(SelectedProxy.ProxyId, SelectedProxy.ProxyName, result.Config);
        CurrentAccessEndpoint = "正在等待访问地址...";
        RefreshDisplayedLogs();
        AddLog(startResult.Message);
    }

    [RelayCommand]
    private async Task StopTunnelAsync()
    {
        if (!RunningProxyId.HasValue)
        {
            AddLog("当前没有运行中的隧道");
            return;
        }

        var proxyName = RunningProxyName;
        await _meFrpService.StopTunnelAsync();
        CurrentAccessEndpoint = "暂无访问地址";
        AddLog($"已停止隧道：{proxyName}");
    }

    partial void OnSelectedCreateNodeChanged(MeFrpNode? value)
    {
        SelectedNodeDetail = value == null
            ? "请选择节点查看详情"
            : $"节点：{value.Name}\n地区：{value.Region}\n带宽：{value.Bandwidth}\n端口范围：{value.AllowPort}\n允许协议：{value.AllowType}\n负载：{value.LoadPercent}%\n说明：{value.Description}";
    }

    partial void OnSelectedProxyChanged(MeFrpProxy? value)
    {
        RefreshDisplayedLogs();
    }

    partial void OnIsInstalledChanged(bool value)
    {
        OnPropertyChanged(nameof(InstallStateText));
    }

    partial void OnIsLoggedInChanged(bool value)
    {
        OnPropertyChanged(nameof(LoginStateText));
    }

    private async Task ValidateAndLoadAsync(string token, bool removeIfInvalid)
    {
        if (!IsInstalled)
        {
            StatusMessage = "请先安装 MeFrp 客户端";
            return;
        }

        IsBusy = true;
        var result = await _meFrpService.ValidateTokenAsync(token);
        if (!result.Success || result.User == null)
        {
            IsLoggedIn = false;
            UserInfo = null;
            Proxies = new List<MeFrpProxy>();
            ProxyNodes = new List<MeFrpNode>();
            CreateNodes = new List<MeFrpNode>();
            StatusMessage = result.Message;
            AddLog($"登录失败：{result.Message}");
            if (removeIfInvalid)
            {
                await _meFrpService.ClearTokenAsync();
                TokenInput = string.Empty;
            }

            IsBusy = false;
            return;
        }

        await _meFrpService.SaveTokenAsync(token);
        TokenInput = token;
        UserInfo = result.User;
        IsLoggedIn = true;
        StatusMessage = $"已登录 MeFrp：{result.User.Username}";
        AddLog(StatusMessage);

        var proxies = await _meFrpService.GetProxiesAsync(token);
        var nodes = await _meFrpService.GetProxyNodesAsync(token);
        var createData = await _meFrpService.GetCreateProxyDataAsync(token);

        Proxies = proxies;
        ProxyNodes = nodes;
        CreateNodes = createData?.Nodes ?? new List<MeFrpNode>();
        SelectedCreateNode = CreateNodes.FirstOrDefault(node => node.IsOnline && !node.IsDisabled) ?? CreateNodes.FirstOrDefault();
        IsBusy = false;
    }

    private void EnqueueOnUi(Action action)
    {
        if (!_dispatcherQueue.TryEnqueue(() => action()))
        {
            action();
        }
    }

    private void AddLog(string message)
    {
        _generalLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        if (_generalLogs.Count > 200)
        {
            _generalLogs.RemoveAt(0);
        }

        RefreshDisplayedLogs();
    }

    private void UpdateAccessEndpointFromLog(string message)
    {
        var match = AccessEndpointRegex.Match(message);
        if (match.Success)
        {
            CurrentAccessEndpoint = match.Groups["endpoint"].Value;
        }
    }

    private void RefreshDisplayedLogs()
    {
        var targetProxy = SelectedProxy ?? Proxies.FirstOrDefault(proxy => proxy.ProxyId == RunningProxyId);
        if (targetProxy == null)
        {
            LogTargetTitle = "通用日志";
            Logs = _generalLogs.ToList();
            return;
        }

        LogTargetTitle = RunningProxyId == targetProxy.ProxyId
            ? $"当前运行隧道：{targetProxy.ProxyName}"
            : $"隧道日志：{targetProxy.ProxyName}";
        Logs = _meFrpService.GetLogsForProxy(targetProxy.ProxyId);
    }

    public void ClearLogs()
    {
        if (SelectedProxy != null)
        {
            _meFrpService.ClearLogsForProxy(SelectedProxy.ProxyId);
        }
        else if (RunningProxyId.HasValue)
        {
            _meFrpService.ClearLogsForProxy(RunningProxyId.Value);
        }

        RefreshDisplayedLogs();
    }

    public string RunningTunnelText => RunningProxyId.HasValue ? RunningProxyName : "未启动";

    public string UserNameText => UserInfo?.Username ?? "未登录";
    public string UserEmailText => UserInfo?.Email ?? "-";
    public string RealnameStatusText => UserInfo == null ? "未知" : (UserInfo.IsRealname ? "已实名" : "未实名");
    public string ProxyCountText => UserInfo == null ? "0 / 0" : $"{UserInfo.UsedProxies} / {UserInfo.MaxProxies}";
    public string TrafficText => UserInfo == null ? "-" : FormatTraffic(UserInfo.Traffic);
    public string InstallStateText => IsInstalled ? "客户端已安装" : "客户端未安装";
    public string LoginStateText => IsLoggedIn ? $"已登录：{UserNameText}" : "未登录";

    partial void OnUserInfoChanged(MeFrpUserInfo? value)
    {
        OnPropertyChanged(nameof(UserNameText));
        OnPropertyChanged(nameof(UserEmailText));
        OnPropertyChanged(nameof(RealnameStatusText));
        OnPropertyChanged(nameof(ProxyCountText));
        OnPropertyChanged(nameof(TrafficText));
        OnPropertyChanged(nameof(LoginStateText));
    }

    private static string FormatTraffic(long mb)
    {
        if (mb >= 1024)
        {
            return $"{mb / 1024d:F2} GB";
        }

        return $"{mb:F0} MB";
    }

    public async Task ShowInstallDialogIfNeededAsync(XamlRoot xamlRoot)
    {
        if (IsInstalled)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "检测到未安装 MeFrp 客户端",
            Content = "继续使用 MeFrp 前需要先下载客户端组件。下载后会自动解压并保存为 frpc/mefrp.exe。",
            PrimaryButtonText = "立即下载",
            CloseButtonText = "稍后再说",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await InstallAsync();
        }
    }
}
