using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.RegularExpressions;
using ZMSL.App.Models;
using ZMSL.App.Services;

namespace ZMSL.App.ViewModels;

public partial class StarryFrpViewModel : ObservableObject
{
    private readonly StarryFrpService _starryFrpService;
    private readonly List<string> _generalLogs = new();
    private readonly DispatcherQueue _dispatcherQueue;
    private static readonly Regex AccessEndpointRegex = new(@"使用\s*\[(?<endpoint>[^\[\]]+)\]\s*来连接到你的隧道|\[(?<endpoint>[^\[\]]+)\]", RegexOptions.Compiled);

    [ObservableProperty] public partial bool IsLoggedIn { get; set; }
    [ObservableProperty] public partial bool IsInstalled { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsDownloading { get; set; }
    [ObservableProperty] public partial double DownloadProgress { get; set; }
    [ObservableProperty] public partial string DownloadStatus { get; set; } = string.Empty;
    [ObservableProperty] public partial string TokenInput { get; set; } = string.Empty;
    [ObservableProperty] public partial StarryFrpUserInfo? UserInfo { get; set; }
    [ObservableProperty] public partial List<StarryFrpProxy> Proxies { get; set; } = new();
    [ObservableProperty] public partial List<StarryFrpNode> ProxyNodes { get; set; } = new();
    [ObservableProperty] public partial List<StarryFrpNode> CreateNodes { get; set; } = new();
    [ObservableProperty] public partial StarryFrpProxy? SelectedProxy { get; set; }
    [ObservableProperty] public partial StarryFrpNode? SelectedCreateNode { get; set; }
    [ObservableProperty] public partial string SelectedNodeDetail { get; set; } = "请选择节点查看详情";
    [ObservableProperty] public partial string StatusMessage { get; set; } = "请先安装并登录 StarryFrp";
    [ObservableProperty] public partial List<string> Logs { get; set; } = new();
    [ObservableProperty] public partial int? RunningProxyId { get; set; }
    [ObservableProperty] public partial string RunningProxyName { get; set; } = "未启动";
    [ObservableProperty] public partial string LogTargetTitle { get; set; } = "请选择隧道查看日志";
    [ObservableProperty] public partial string CurrentAccessEndpoint { get; set; } = "暂无访问地址";
    [ObservableProperty] public partial string CreateProxyRemark { get; set; } = string.Empty;
    [ObservableProperty] public partial string CreateLocalIp { get; set; } = "127.0.0.1";
    [ObservableProperty] public partial int CreateLocalPort { get; set; } = 25565;
    [ObservableProperty] public partial int CreateRemotePort { get; set; }
    [ObservableProperty] public partial string CreateProtocol { get; set; } = "tcp";

    public StarryFrpViewModel(StarryFrpService starryFrpService)
    {
        _starryFrpService = starryFrpService;
        _dispatcherQueue = App.MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        IsInstalled = StarryFrpService.IsStarryFrpInstalled();
        OnPropertyChanged(nameof(InstallStateText));
        OnPropertyChanged(nameof(LoginStateText));
        _starryFrpService.ProgressChanged += (_, e) => EnqueueOnUi(() => DownloadProgress = e.Progress);
        _starryFrpService.StateChanged += (_, e) => EnqueueOnUi(() => { IsDownloading = e.IsDownloading; DownloadStatus = e.Message; if (!string.IsNullOrWhiteSpace(e.Message)) AddLog(e.Message); if (e.IsComplete) IsInstalled = true; });
        _starryFrpService.TunnelStateChanged += (_, e) => EnqueueOnUi(() => { RunningProxyId = e.IsRunning ? e.ProxyId : null; RunningProxyName = e.IsRunning && !string.IsNullOrWhiteSpace(e.ProxyName) ? e.ProxyName : "未启动"; if (!e.IsRunning) CurrentAccessEndpoint = "暂无访问地址"; RefreshDisplayedLogs(); OnPropertyChanged(nameof(RunningTunnelText)); });
        _starryFrpService.TunnelLogReceived += (_, e) => EnqueueOnUi(() => { UpdateAccessEndpointFromLogs(e.ProxyId); if (SelectedProxy?.ProxyId == e.ProxyId || RunningProxyId == e.ProxyId) RefreshDisplayedLogs(); });
    }

    public async Task InitializeAsync()
    {
        IsInstalled = StarryFrpService.IsStarryFrpInstalled();
        RunningProxyId = _starryFrpService.CurrentRunningProxyId;
        RunningProxyName = string.IsNullOrWhiteSpace(_starryFrpService.CurrentRunningProxyName) ? "未启动" : _starryFrpService.CurrentRunningProxyName;
        TokenInput = await _starryFrpService.GetSavedTokenAsync() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(TokenInput)) await ValidateAndLoadAsync(TokenInput, true); else { IsLoggedIn = false; StatusMessage = IsInstalled ? "请输入访问令牌以登录 StarryFrp" : "检测到未安装 StarryFrp 客户端"; }
        RefreshDisplayedLogs();
    }

    [RelayCommand] private async Task InstallAsync() { if (IsDownloading) return; IsBusy = true; DownloadProgress = 0; DownloadStatus = "正在准备安装..."; var ok = await _starryFrpService.DownloadAndInstallAsync(); IsInstalled = ok || StarryFrpService.IsStarryFrpInstalled(); StatusMessage = IsInstalled ? "StarryFrp 客户端已就绪" : "StarryFrp 客户端安装失败"; IsBusy = false; }
    [RelayCommand] private async Task LoginAsync() => await ValidateAndLoadAsync(TokenInput, false);
    [RelayCommand] private async Task LogoutAsync() { await _starryFrpService.ClearTokenAsync(); IsLoggedIn = false; UserInfo = null; Proxies = new(); ProxyNodes = new(); CreateNodes = new(); SelectedProxy = null; StatusMessage = "已退出 StarryFrp 登录"; AddLog(StatusMessage); RefreshDisplayedLogs(); }
    [RelayCommand] private async Task RefreshAsync() { var token = await _starryFrpService.GetSavedTokenAsync(); if (string.IsNullOrWhiteSpace(token)) { StatusMessage = "请先登录"; IsLoggedIn = false; return; } await ValidateAndLoadAsync(token, true); }
    [RelayCommand] private async Task AutoFillFreePortAsync() { var token = await _starryFrpService.GetSavedTokenAsync(); if (string.IsNullOrWhiteSpace(token) || SelectedCreateNode == null) return; var port = await _starryFrpService.GetFreePortAsync(token, SelectedCreateNode.Id); if (port.HasValue) { CreateRemotePort = port.Value; AddLog($"已获取空闲端口：{port.Value}"); } else AddLog("获取空闲端口失败"); }
    [RelayCommand]
    private async Task CreateTunnelAsync()
    {
        var token = await _starryFrpService.GetSavedTokenAsync();
        if (string.IsNullOrWhiteSpace(token) || SelectedCreateNode == null) { AddLog("请先登录并选择节点"); return; }
        if (CreateLocalPort <= 0 || ((CreateProtocol == "tcp" || CreateProtocol == "udp") && CreateRemotePort <= 0)) { AddLog("请完整填写隧道信息"); return; }
        IsBusy = true;
        var tunnelName = SelectedCreateNode.Name;
        var tunnelRemark = string.IsNullOrWhiteSpace(CreateProxyRemark) ? "-" : CreateProxyRemark;
        var result = await _starryFrpService.CreateTunnelAsync(token, new { node_id = SelectedCreateNode.Id, tunnel_name = tunnelName, tunnel_remark = tunnelRemark, local_ip = string.IsNullOrWhiteSpace(CreateLocalIp) ? "127.0.0.1" : CreateLocalIp, local_port = CreateLocalPort, remote_port = (CreateProtocol == "http" || CreateProtocol == "https") ? (int?)null : CreateRemotePort, proxy_type = CreateProtocol, bind_domain = string.Empty, sk = string.Empty, use_encryption = "false", use_compression = "false", locations = string.Empty, host_header_rewrite = string.Empty, header_X_From_Where = string.Empty, accessAuth_password = string.Empty, accessAuth_totpSecret = string.Empty, accessAuth_allowPersist = "false", accessAuth_authTimeout = 3600 });
        AddLog(result.Message); if (result.Success) await RefreshAsync(); IsBusy = false;
    }
    [RelayCommand]
    private async Task StartTunnelAsync()
    {
        var token = await _starryFrpService.GetSavedTokenAsync();
        if (string.IsNullOrWhiteSpace(token) || SelectedProxy == null) { AddLog("请先选择隧道"); return; }
        var result = await _starryFrpService.GetProxyConfigAsync(token, SelectedProxy.NodeId);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Config)) { AddLog(result.Message); return; }
        var startResult = await _starryFrpService.StartTunnelAsync(SelectedProxy.ProxyId, SelectedProxy.ProxyName, result.Config); CurrentAccessEndpoint = "正在等待访问地址..."; RefreshDisplayedLogs(); AddLog(startResult.Message);
    }
    [RelayCommand] private async Task StopTunnelAsync() { if (!RunningProxyId.HasValue) { AddLog("当前没有运行中的隧道"); return; } var name = RunningProxyName; await _starryFrpService.StopTunnelAsync(); CurrentAccessEndpoint = "暂无访问地址"; AddLog($"已停止隧道：{name}"); }
    partial void OnSelectedCreateNodeChanged(StarryFrpNode? value) => SelectedNodeDetail = value == null ? "请选择节点查看详情" : $"节点：{value.Name}\n地区：{value.Region}\n负载：{value.LoadStatus}\nIP：{value.Ip ?? "-"}\n域名：{value.Domain ?? "-"}\n说明：{value.Description}";
    partial void OnSelectedProxyChanged(StarryFrpProxy? value) => RefreshDisplayedLogs();
    partial void OnIsInstalledChanged(bool value) => OnPropertyChanged(nameof(InstallStateText));
    partial void OnIsLoggedInChanged(bool value) => OnPropertyChanged(nameof(LoginStateText));
    private async Task ValidateAndLoadAsync(string token, bool removeIfInvalid)
    {
        if (!IsInstalled) { StatusMessage = "请先安装 StarryFrp 客户端"; return; }
        IsBusy = true; var result = await _starryFrpService.ValidateTokenAsync(token);
        if (!result.Success || result.User == null) { IsLoggedIn = false; UserInfo = null; Proxies = new(); ProxyNodes = new(); CreateNodes = new(); StatusMessage = result.Message; AddLog($"登录失败：{result.Message}"); if (removeIfInvalid) { await _starryFrpService.ClearTokenAsync(); TokenInput = string.Empty; } IsBusy = false; return; }
        await _starryFrpService.SaveTokenAsync(token); TokenInput = token; UserInfo = result.User; IsLoggedIn = true; StatusMessage = $"已登录 StarryFrp：{result.User.Username}"; AddLog(StatusMessage); Proxies = await _starryFrpService.GetProxiesAsync(token); ProxyNodes = await _starryFrpService.GetNodesAsync(token); CreateNodes = ProxyNodes; SelectedCreateNode = CreateNodes.FirstOrDefault(node => node.IsOnline) ?? CreateNodes.FirstOrDefault(); IsBusy = false;
    }
    private void EnqueueOnUi(Action action) { if (!_dispatcherQueue.TryEnqueue(() => action())) action(); }
    private void AddLog(string message) { _generalLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}"); if (_generalLogs.Count > 200) _generalLogs.RemoveAt(0); RefreshDisplayedLogs(); }
    private void UpdateAccessEndpointFromLogs(int proxyId)
    {
        var allLogs = string.Join("\n", _starryFrpService.GetLogsForProxy(proxyId));
        if (string.IsNullOrWhiteSpace(allLogs))
        {
            return;
        }

        var exactMatches = Regex.Matches(allLogs, @"使用\s*\[(?<endpoint>[^\[\]]+)\]\s*来连接到你的隧道");
        if (exactMatches.Count > 0)
        {
            CurrentAccessEndpoint = exactMatches[^1].Groups["endpoint"].Value;
            return;
        }

        var matches = AccessEndpointRegex.Matches(allLogs);
        if (matches.Count > 0)
        {
            CurrentAccessEndpoint = matches[^1].Groups["endpoint"].Value;
        }
    }
    private void RefreshDisplayedLogs() { var target = SelectedProxy ?? Proxies.FirstOrDefault(proxy => proxy.ProxyId == RunningProxyId); if (target == null) { LogTargetTitle = "通用日志"; Logs = _generalLogs.ToList(); return; } LogTargetTitle = RunningProxyId == target.ProxyId ? $"当前运行隧道：{target.ProxyName}" : $"隧道日志：{target.ProxyName}"; Logs = _starryFrpService.GetLogsForProxy(target.ProxyId); }
    public void ClearLogs() { if (SelectedProxy != null) _starryFrpService.ClearLogsForProxy(SelectedProxy.ProxyId); else if (RunningProxyId.HasValue) _starryFrpService.ClearLogsForProxy(RunningProxyId.Value); RefreshDisplayedLogs(); }
    public string RunningTunnelText => RunningProxyId.HasValue ? RunningProxyName : "未启动";
    public string UserNameText => UserInfo?.Username ?? "未登录";
    public string UserEmailText => UserInfo?.Email ?? "-";
    public string RealnameStatusText => UserInfo == null ? "未知" : (UserInfo.Verified ? "已实名" : "未实名");
    public string ProxyCountText => UserInfo == null ? "0" : UserInfo.Proxies.ToString();
    public string TrafficText => UserInfo == null ? "-" : FormatTraffic(UserInfo.Traffic.Total);
    public string InstallStateText => IsInstalled ? "客户端已安装" : "客户端未安装";
    public string LoginStateText => IsLoggedIn ? $"已登录：{UserNameText}" : "未登录";
    partial void OnUserInfoChanged(StarryFrpUserInfo? value) { OnPropertyChanged(nameof(UserNameText)); OnPropertyChanged(nameof(UserEmailText)); OnPropertyChanged(nameof(RealnameStatusText)); OnPropertyChanged(nameof(ProxyCountText)); OnPropertyChanged(nameof(TrafficText)); OnPropertyChanged(nameof(LoginStateText)); }
    private static string FormatTraffic(long totalMb) { return totalMb >= 1024 ? $"{totalMb / 1024d:F2} GB" : $"{totalMb:F2} MB"; }
    public async Task ShowInstallDialogIfNeededAsync(XamlRoot xamlRoot) { if (IsInstalled) return; var dialog = new ContentDialog { Title = "检测到未安装 StarryFrp 客户端", Content = "继续使用 StarryFrp 前需要先下载客户端组件。下载后会自动保存为 frpc/starryfrp.exe。", PrimaryButtonText = "立即下载", CloseButtonText = "稍后再说", DefaultButton = ContentDialogButton.Primary, XamlRoot = xamlRoot }; if (await dialog.ShowAsync() == ContentDialogResult.Primary) await InstallAsync(); }
}
