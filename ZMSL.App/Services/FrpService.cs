using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Timers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using ZMSL.App.Models;
using ZMSL.Shared.DTOs;

namespace ZMSL.App.Services;

public class FrpService
{
    private readonly ApiService _apiService;
    private readonly AuthService _authService;
    private readonly DatabaseService _db;
    private Process? _frpcProcess;
    private readonly List<string> _logs = new();
    private readonly object _logsLock = new();
    private const int MaxLogLines = 500;
    private int? _currentTunnelId;
    
    // 实时流量统计
    private System.Timers.Timer? _trafficTimer;
    private long _initialServerTrafficUsed = 0; // 启动时的服务端已用流量
    private const int TrafficReportIntervalMs = 30000; // 30 秒上报一次
    
    // 实时流量事件（预留，供未来使用）
#pragma warning disable CS0067
    public event EventHandler<TrafficEventArgs>? TrafficUpdated;
    public event EventHandler<int>? LatencyUpdated;
#pragma warning restore CS0067

    public bool IsConnected => _frpcProcess != null && !_frpcProcess.HasExited;
    public int? CurrentTunnelId => _currentTunnelId;
    public string CurrentTunnelName { get; private set; } = "";
    public string CurrentConnectAddress { get; private set; } = "";
    public string CurrentServerHost { get; private set; } = "";

    public event EventHandler<FrpStatusEventArgs>? StatusChanged;
    public event EventHandler<string>? LogReceived;

    // 获取历史日志
    public List<string> GetLogs() 
    {
        lock (_logsLock)
        {
            return new List<string>(_logs);
        }
    }

    private void AddLog(string message)
    {
        var logLine = $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (_logsLock)
        {
            _logs.Add(logLine);
            if (_logs.Count > MaxLogLines) _logs.RemoveAt(0);
        }
        LogReceived?.Invoke(this, message);
        // Debug output for troubleshooting
        Debug.WriteLine($"[FrpService] {message}");
    }

    public void ClearLogs() 
    {
        lock (_logsLock)
        {
            _logs.Clear();
        }
    }

    public FrpService(ApiService apiService, AuthService authService, DatabaseService db)
    {
        _apiService = apiService;
        _authService = authService;
        _db = db;
    }

    public async Task RestoreStateAsync()
    {
        // 如果连接正常但状态丢失
        if (IsConnected && (_currentTunnelId == null || string.IsNullOrEmpty(CurrentConnectAddress)))
        {
            AddLog("检测到状态丢失，尝试从配置文件恢复...");
            try
            {
                var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ZMSL", "frp", "frpc.toml");
                if (File.Exists(appDataPath))
                {
                     var lines = await File.ReadAllLinesAsync(appDataPath);
                     string? serverAddr = null;
                     string? remotePort = null;
                     string? name = null;
                     int? tunnelId = null;
                     
                     foreach(var line in lines)
                     {
                         if (line.StartsWith("# TunnelID:")) 
                         {
                             if (int.TryParse(line.Replace("# TunnelID:", "").Trim(), out int id))
                                 tunnelId = id;
                         }
                         if (line.StartsWith("serverAddr")) serverAddr = line.Split('=')[1].Trim().Trim('"');
                         if (line.StartsWith("remotePort")) remotePort = line.Split('=')[1].Trim();
                         if (line.StartsWith("name")) name = line.Split('=')[1].Trim().Trim('"');
                     }
                     
                     if (serverAddr != null && remotePort != null)
                     {
                         CurrentConnectAddress = $"{serverAddr}:{remotePort}";
                         CurrentServerHost = serverAddr;
                         if (name != null) CurrentTunnelName = name;
                         if (tunnelId != null) _currentTunnelId = tunnelId;
                         
                         AddLog($"状态已恢复: ID={_currentTunnelId}, Addr={CurrentConnectAddress}");
                     }
                }
            }
            catch (Exception ex)
            {
                AddLog($"状态恢复失败: {ex.Message}");
            }
        }
    }

    public async Task<List<FrpNodeDto>> GetNodesAsync()
    {
        var result = await _apiService.GetFrpNodesAsync();
        var nodes = result.Data ?? new List<FrpNodeDto>();

        // 测试延迟
        foreach (var node in nodes)
        {
            node.Latency = await TestLatencyAsync(node.Host);
        }

        return nodes.OrderBy(n => n.Latency).ToList();
    }

    private async Task<int> TestLatencyAsync(string host)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, 3000);
            return reply.Status == IPStatus.Success ? (int)reply.RoundtripTime : 9999;
        }
        catch
        {
            return 9999;
        }
    }

    public async Task<List<TunnelDto>> GetMyTunnelsAsync()
    {
        if (!_authService.IsLoggedIn) return new List<TunnelDto>();
        var result = await _apiService.GetMyTunnelsAsync();
        return result.Data ?? new List<TunnelDto>();
    }

    public async Task<(bool Success, string? Message, TunnelDto? Tunnel)> CreateTunnelAsync(CreateTunnelRequest request)
    {
        if (!_authService.IsLoggedIn)
            return (false, "请先登录", null);

        var result = await _apiService.CreateTunnelAsync(request);
        return (result.Success, result.Message, result.Data);
    }

    public async Task<(bool Success, string? Message)> DeleteTunnelAsync(int tunnelId)
    {
        var result = await _apiService.DeleteTunnelAsync(tunnelId);
        return (result.Success, result.Message);
    }

    public async Task<bool> StartTunnelAsync(int tunnelId)
    {
        if (IsConnected)
        {
            await StopTunnelAsync();
        }

        var configResult = await _apiService.GetTunnelConfigAsync(tunnelId);
        if (!configResult.Success || configResult.Data == null)
        {
            AddLog($"获取隧道配置失败: {configResult.Message}");
            return false;
        }

        var config = configResult.Data;
        
        // 从本地数据库读取高级配置
        bool useProxyProtocol = false;
        try 
        {
            useProxyProtocol = await _db.ExecuteWithLockAsync(async db => 
            {
                var localTunnel = await db.FrpTunnels.FirstOrDefaultAsync(t => t.RemoteTunnelId == tunnelId);
                return localTunnel?.EnableProxyProtocol ?? false;
            });
        }
        catch (Exception ex)
        {
            AddLog($"读取本地配置失败: {ex.Message}，使用默认设置");
        }
        
        var configPath = await GenerateFrpcConfigAsync(config, tunnelId, useProxyProtocol);
        var frpcPath = GetFrpcPath();

        // 记录当前连接信息
        _currentTunnelId = tunnelId;
        CurrentTunnelName = config.ProxyName;
        CurrentConnectAddress = $"{config.ServerHost}:{config.RemotePort}";
        CurrentServerHost = config.ServerHost;
        
        AddLog($"设置当前连接信息: ID={_currentTunnelId}, Name={CurrentTunnelName}, Addr={CurrentConnectAddress}");

        AddLog($"frpc路径: {frpcPath}");
        AddLog($"配置文件: {configPath}");

        if (!File.Exists(frpcPath))
        {
            AddLog($"frpc.exe 不存在: {frpcPath}");
            return false;
        }

        _currentTunnelId = tunnelId; // 记录当前隧道ID
        AddLog("正在启动frpc进程...");

        _frpcProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = frpcPath,
                Arguments = $"-c \"{configPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        _frpcProcess.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                // 过滤掉 frpc 的健康检查日志
                if (e.Data.Contains("/api/status") || e.Data.Contains("ping")) 
                    return;
                    
                AddLog(e.Data);
            }
        };

        _frpcProcess.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                AddLog($"[ERROR] {e.Data}");
            }
        };

        _frpcProcess.Exited += (s, e) =>
        {
            AddLog($"frpc进程已退出, ExitCode: {_frpcProcess?.ExitCode}");
            StatusChanged?.Invoke(this, new FrpStatusEventArgs { IsConnected = false });
        };

        try
        {
            _frpcProcess.Start();
            _frpcProcess.BeginOutputReadLine();
            _frpcProcess.BeginErrorReadLine();

            // 将进程添加到 Job Object，确保启动器关闭时子进程也会终止
            var added = ChildProcessManager.Instance.AddProcess(_frpcProcess);
            System.Diagnostics.Debug.WriteLine($"[FrpService] frpc进程 {_frpcProcess.Id} 添加到 Job Object: {added}, 当前 Job 中进程数: {ChildProcessManager.Instance.GetProcessCount()}");

            AddLog($"frpc进程已启动, PID: {_frpcProcess.Id}");
            StatusChanged?.Invoke(this, new FrpStatusEventArgs { IsConnected = true });

            // 启动实时流量统计定时器
            await StartTrafficMonitorAsync();

            return true;
        }
        catch (Exception ex)
        {
            AddLog($"启动失败: {ex.Message}");
            return false;
        }
    }

    private async Task StartTrafficMonitorAsync()
    {
        _initialServerTrafficUsed = 0;
        
        // 获取初始服务端流量
        try 
        {
            var userResult = await _apiService.GetCurrentUserAsync();
            if (userResult.Success && userResult.Data != null)
            {
                _initialServerTrafficUsed = userResult.Data.TrafficUsed ?? 0;
                AddLog($"流量监控已启动: 初始已用={FormatBytes(_initialServerTrafficUsed)}");
            }
        }
        catch (Exception ex)
        {
            AddLog($"获取初始流量失败: {ex.Message}");
        }

        _trafficTimer = new System.Timers.Timer(TrafficReportIntervalMs);
        _trafficTimer.Elapsed += async (s, e) => await PollAndCheckTrafficAsync(true);
        _trafficTimer.AutoReset = true;
        _trafficTimer.Start();
        
        // 首次立即检查一次（可选，防止刚启动就应该停止）
        _ = PollAndCheckTrafficAsync(true);
    }

    
    private void StopTrafficMonitor()
    {
        if (_trafficTimer != null)
        {
            _trafficTimer.Stop();
            _trafficTimer.Dispose();
            _trafficTimer = null;
        }
    }

    public async Task StopTunnelAsync()
    {
        AddLog($"正在停止隧道... 当前ID={_currentTunnelId}");
        // 停止流量监控
        StopTrafficMonitor();
        
        if (_frpcProcess != null && !_frpcProcess.HasExited)
        {
            // 停止前最后一次检查 (不检查流量耗尽，防止死循环)
            await PollAndCheckTrafficAsync(checkExhaustion: false);
            
            _frpcProcess.Kill();
            await _frpcProcess.WaitForExitAsync();
            _frpcProcess = null;
        }
        _currentTunnelId = null;
        CurrentTunnelName = "";
        CurrentConnectAddress = "";
        CurrentServerHost = "";
        StatusChanged?.Invoke(this, new FrpStatusEventArgs { IsConnected = false });
        AddLog("隧道已停止并清除状态");
    }

    /// <summary>
    /// 定时轮询frpc获取流量并检查是否耗尽
    /// </summary>
    private async Task PollAndCheckTrafficAsync(bool checkExhaustion = true)
    {
        if (_currentTunnelId == null || !_authService.IsLoggedIn)
            return;
    }


    /// <summary>
    /// 检查流量是否用尽，用尽则关闭连接并发通知
    /// </summary>
    private async Task CheckAndHandleTrafficExhaustedAsync(long sessionTotal = 0)
    {
        try 
        {
            // 刷新用户信息获取最新流量
            var userResult = await _apiService.GetCurrentUserAsync();
            if (!userResult.Success || userResult.Data == null)
            {
                AddLog($"获取用户信息失败: {userResult.Message}");
                return;
            }

            var user = userResult.Data;
            
            long totalLimit = (user.TrafficQuota ?? 0) + (user.PurchasedTraffic ?? 0);
            
            // 流量耗尽判断：取服务端已用与本地估算值的最大者，防止服务端更新延迟
            // 注意：_initialServerTrafficUsed 是启动隧道时获取的服务器已用流量
            long currentUsed = Math.Max(user.TrafficUsed ?? 0, _initialServerTrafficUsed + sessionTotal);
            
            // 调试日志：显示详细计算过程，方便排查为何不停止
            //AddLog($"[DEBUG] 流量检查: 服务端={FormatBytes(user.TrafficUsed)}, 初始={FormatBytes(_initialServerTrafficUsed)}, 增量={FormatBytes(sessionTotal)}, 判定已用={FormatBytes(currentUsed)}, 限额={FormatBytes(totalLimit)}");

            if (currentUsed >= totalLimit)
            {
                AddLog($"❌ 流量已用尽 (已用: {FormatBytes(currentUsed)} / 限额: {FormatBytes(totalLimit)})，正在关闭 FRP 连接...");
                
                // 关闭连接
                await StopTunnelAsync();
                
                // 发送 Windows 通知
                SendTrafficExhaustedNotification(currentUsed, totalLimit);
                
                // 尝试弹窗提示
                try 
                {
                    if (App.MainWindowInstance != null)
                    {
                        App.MainWindowInstance.DispatcherQueue.TryEnqueue(async () =>
                        {
                            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
                            {
                                Title = "流量耗尽提醒",
                                Content = $"您的 FRP 隧道流量已耗尽！\n\n已用流量：{FormatBytes(currentUsed)}\n总限额：{FormatBytes(totalLimit)}\n\n隧道已自动停止，请购买流量包后继续使用。",
                                CloseButtonText = "知道了",
                                XamlRoot = App.MainWindowInstance.Content.XamlRoot
                            };
                            await dialog.ShowAsync();
                        });
                    }
                }
                catch (Exception ex)
                {
                    AddLog($"弹窗提示失败: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            AddLog($"检查流量耗尽逻辑异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 发送流量用尽的 Windows 通知
    /// </summary>
    private void SendTrafficExhaustedNotification(long used, long quota)
    {
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText("流量已用尽")
                .AddText($"您的 FRP 流量已用完（{FormatBytes(used)} / {FormatBytes(quota)}）")
                .AddText("请购买套餐后继续使用");

            var notification = builder.BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception ex)
        {
            AddLog($"发送通知失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 手动获取当前流量（用于UI刷新）
    /// </summary>
    public async Task<(long TrafficIn, long TrafficOut)?> GetCurrentTrafficAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await client.GetAsync($"http://127.0.0.1:7400/api/status");
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var status = JsonSerializer.Deserialize<FrpcStatusResponse>(json, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });

                FrpcProxyStatus? proxy = status?.Tcp?.FirstOrDefault() ?? status?.Udp?.FirstOrDefault();
                if (proxy != null)
                {
                    return (proxy.TrafficIn, proxy.TrafficOut);
                }
            }
        }
        catch { }
        return null;
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private async Task<string> GenerateFrpcConfigAsync(FrpConfigDto config, int tunnelId, bool useProxyProtocol = false)
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZMSL", "frp");
        Directory.CreateDirectory(appDataPath);
        var configPath = Path.Combine(appDataPath, "frpc.toml");

        var proxyProtocolConfig = useProxyProtocol ? "transport.proxyProtocolVersion = \"v2\"" : "";

        // 配置文件包含 webServer 用于获取流量统计
        // 添加 TunnelID 注释用于状态恢复
        var tomlContent = $@"# TunnelID: {tunnelId}
serverAddr = ""{config.ServerHost}""
serverPort = {config.ServerPort}
auth.token = ""{config.Token}""


[[proxies]]
name = ""{config.ProxyName}""
type = ""{config.Protocol}""
localIP = ""127.0.0.1""
localPort = {config.LocalPort}
remotePort = {config.RemotePort}
{proxyProtocolConfig}
";

        await File.WriteAllTextAsync(configPath, tomlContent);
        return configPath;
    }

    public static string GetFrpcPath()
    {
        var appPath = AppContext.BaseDirectory;
        return Path.Combine(appPath, "frpc", "frpc.exe");
    }

    public static bool IsFrpcInstalled()
    {
        return File.Exists(GetFrpcPath());
    }

    public async Task<List<LocalFrpTunnel>> GetLocalTunnelsAsync()
    {
        return await _db.ExecuteWithLockAsync(async db => await db.FrpTunnels.ToListAsync());
    }

    public async Task SaveLocalTunnelAsync(LocalFrpTunnel tunnel)
    {
        await _db.ExecuteWithLockAsync(async db => 
        {
            var existing = await db.FrpTunnels.FirstOrDefaultAsync(t => t.RemoteTunnelId == tunnel.RemoteTunnelId);
            if (existing != null)
            {
                db.Entry(existing).CurrentValues.SetValues(tunnel);
            }
            else
            {
                db.FrpTunnels.Add(tunnel);
            }
            await db.SaveChangesAsync();
        });
    }
}

public class FrpStatusEventArgs : EventArgs
{
    public bool IsConnected { get; set; }
}

public class TrafficEventArgs : EventArgs
{
    public long TotalIn { get; set; }
    public long TotalOut { get; set; }
    public long DeltaIn { get; set; }
    public long DeltaOut { get; set; }
}

// frpc admin API 响应模型 (frp 0.50+ 版本)
public class FrpcStatusResponse
{
    [JsonPropertyName("tcp")]
    public List<FrpcProxyStatus>? Tcp { get; set; }
    
    [JsonPropertyName("udp")]
    public List<FrpcProxyStatus>? Udp { get; set; }
}

public class FrpcProxyStatus
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
    
    [JsonPropertyName("local_addr")]
    public string LocalAddr { get; set; } = "";
    
    [JsonPropertyName("remote_addr")]
    public string RemoteAddr { get; set; } = "";
    
    [JsonPropertyName("traffic_in")]
    public long TrafficIn { get; set; }
    
    [JsonPropertyName("traffic_out")]
    public long TrafficOut { get; set; }
}
