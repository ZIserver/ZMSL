using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZMSL.App.Models;
using ZMSL.App.Services;

namespace ZMSL.App.ViewModels;

public partial class GroupServerDetailViewModel : ObservableObject
{
    private readonly ServerManagerService _serverManager;
    private readonly DatabaseService _db;
    
    [ObservableProperty]
    public partial LocalServer? Server { get; set; }

    partial void OnServerChanged(LocalServer? value)
    {
        if (value != null)
        {
            ServerName = value.Name;
            IsRunning = _serverManager.IsServerRunning(value.Id);
            _ = LoadConfigAsync();
            _ = LoadAvailableServersAsync();
        }
    }

    [ObservableProperty]
    public partial string ServerName { get; set; } = "Loading...";

    [ObservableProperty]
    public partial string ServerStatus { get; set; } = "Offline";

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string ConsoleOutput { get; set; } = "";

    [ObservableProperty]
    public partial string CommandInput { get; set; } = "";

    [ObservableProperty]
    public partial double CpuUsage { get; set; }

    [ObservableProperty]
    public partial double MemoryUsage { get; set; }

    [ObservableProperty]
    public partial string MemoryUsageText { get; set; } = "0 MB";

    [ObservableProperty]
    public partial string PlayerCountText { get; set; } = "0/500"; // Velocity 总玩家数

    // 配置
    [ObservableProperty]
    public partial int BindPort { get; set; } = 25565;
    
    [ObservableProperty]
    public partial string Motd { get; set; } = "";
    
    [ObservableProperty]
    public partial int MaxPlayers { get; set; } = 500;
    
    [ObservableProperty]
    public partial bool OnlineMode { get; set; } = true;
    
    [ObservableProperty]
    public partial bool ForceKeyAuthentication { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowMaxPlayers { get; set; } = true;

    [ObservableProperty]
    public partial ObservableCollection<SubServerItem> SubServers { get; set; } = new();

    [ObservableProperty]
    public partial ObservableCollection<string> TryServers { get; set; } = new();

    [ObservableProperty]
    public partial ObservableCollection<PlayerItem> Players { get; set; } = new();

    [ObservableProperty]
    public partial ObservableCollection<SelectableServerItem> AvailableServers { get; set; } = new();

    public GroupServerDetailViewModel(ServerManagerService serverManager, DatabaseService db)
    {
        _serverManager = serverManager;
        _db = db;
        
        _serverManager.ServerStatusChanged += OnServerStatusChanged;
        _serverManager.ServerOutput += OnOutputReceived;
    }

    [RelayCommand]
    private async Task LoadAvailableServersAsync()
    {
        var servers = await _serverManager.GetServersAsync();
        var list = new List<SelectableServerItem>();
        
        foreach (var s in servers)
        {
            // 跳过自己（群组服务器）
            if (s.Id == Server?.Id) continue;
            
            // 跳过其他群组服务器（velocity）以避免嵌套代理
            if (string.Equals(s.CoreType, "velocity", StringComparison.OrdinalIgnoreCase)) continue;

            // 确定是否已选中
            // 优先使用 EnglishAlias 作为显示/选择键（如果可用）
            string keyName = string.IsNullOrWhiteSpace(s.EnglishAlias) ? s.Name : s.EnglishAlias;
            string address = $"127.0.0.1:{s.Port}";
            
            // 首先通过别名检查选择匹配，然后回退到原始名称
            bool isSelected = SubServers.Any(sub => sub.Name == keyName || sub.Name == s.Name);
            bool isMainLobby = SubServers.Any(sub => (sub.Name == keyName || sub.Name == s.Name) && sub.IsTry);
            
            // 构建详细信息字符串：核心类型 + 版本 + 端口 +（如果有别名）
            string detailText = $"{s.CoreType} {s.MinecraftVersion} ({s.Port})";
            if (!string.IsNullOrWhiteSpace(s.EnglishAlias) && s.EnglishAlias != s.Name)
            {
                detailText += $" | 别名: {s.EnglishAlias}";
            }

            list.Add(new SelectableServerItem 
            { 
                Server = s, 
                IsSelected = isSelected,
                IsMainLobby = isMainLobby,
                DisplayName = s.Name, // 使用真实名称作为主要显示
                Detail = detailText
            });
        }
        
        AvailableServers = new ObservableCollection<SelectableServerItem>(list);
    }

    [RelayCommand]
    private void SetAsMainLobby(SelectableServerItem item)
    {
        if (item == null) return;
        
        // 取消选中其他项
        foreach(var s in AvailableServers)
        {
            s.IsMainLobby = (s == item);
            if (s.IsMainLobby) s.IsSelected = true; // 如果设置为主城，则自动选中
        }
    }

    private int FindAvailablePort(IEnumerable<int> usedPorts, int startPort = 25566)
    {
        int port = startPort;
        while (usedPorts.Contains(port))
        {
            port++;
        }
        return port;
    }

    [RelayCommand]
    private async Task ConfirmServerSelectionAsync()
    {
        // 1. 获取密钥
        string secret = "";
        if (Server != null)
        {
            var secretPath = Path.Combine(Server.ServerPath, "forwarding.secret");
            if (File.Exists(secretPath))
            {
                secret = await File.ReadAllTextAsync(secretPath);
                secret = secret.Trim();
            }
        }

        // 2. 根据选择重建子服务器列表
        var newSubServers = new List<SubServerItem>();
        var existingMap = SubServers.ToDictionary(s => s.Name); // Key by Name
        
        // 收集已使用的端口
        var usedPorts = new HashSet<int>();
        if (Server != null) usedPorts.Add(Server.Port); // 群组服务器端口
        
        // 添加保留的子服务器端口
        foreach (var item in AvailableServers)
        {
            if (item.IsSelected && existingMap.ContainsKey(string.IsNullOrWhiteSpace(item.Server.EnglishAlias) ? item.Server.Name : item.Server.EnglishAlias))
            {
                // 如果是现有的映射服务器，尝试保留其端口（如果有效）
                // 但是等等，端口来自反映 DB 状态的 item.Server 对象。
                // 我们应该尊重 DB 状态。
                usedPorts.Add(item.Server.Port);
            }
        }

        foreach (var item in AvailableServers)
        {
            if (item.IsSelected)
            {
                string name = string.IsNullOrWhiteSpace(item.Server.EnglishAlias) ? item.Server.Name : item.Server.EnglishAlias;
                int port = item.Server.Port;
                
                // 检查端口冲突（例如与代理相同，或者如果有多个服务器以某种方式具有相同的端口而重复）
                // 注意：我们通常信任 DB 具有唯一的端口，但为了安全起见。
                // 如果端口是默认的 25565（通常与代理冲突），我们必须重新分配。
        if (port == 25565 || port == Server?.Port)
                {
                    // 需要分配一个新端口
                    int newPort = FindAvailablePort(usedPorts);
                    usedPorts.Add(newPort);
                    port = newPort;
                    
                    // 更新 DB 和配置中的服务器端口
                    item.Server.Port = newPort;
                    await _serverManager.UpdateServerAsync(item.Server);
                }
                else
                {
                    usedPorts.Add(port);
                }

                string address = $"127.0.0.1:{port}";
                
                // 配置服务器（更新 properties 和 paper.yml）
                await ConfigureSubServerAsync(item.Server, secret);

                if (existingMap.TryGetValue(name, out var existing))
                {
                    existing.Address = address;
                    existing.IsTry = item.IsMainLobby;
                    existing.DisplayName = item.Server.Name;
                    existing.LocalServerId = item.Server.Id;
                    existing.LocalServer = item.Server;
                    newSubServers.Add(existing);
                }
                else if (existingMap.TryGetValue(item.Server.Name, out var existingByOriginal))
                {
                    existingByOriginal.Address = address;
                    existingByOriginal.Name = name;
                    existingByOriginal.IsTry = item.IsMainLobby;
                    existingByOriginal.DisplayName = item.Server.Name;
                    existingByOriginal.LocalServerId = item.Server.Id;
                    existingByOriginal.LocalServer = item.Server;
                    newSubServers.Add(existingByOriginal);
                }
                else
                {
                    var newItem = new SubServerItem 
                    { 
                        Name = name, 
                        DisplayName = item.Server.Name,
                        Address = address,
                        IsTry = item.IsMainLobby,
                        LocalServerId = item.Server.Id,
                        LocalServer = item.Server
                    };
                    newSubServers.Add(newItem);
                }
            }
        }
        
        // 确保列表不为空时至少有一个服务器被标记为 Try
        if (newSubServers.Count > 0 && !newSubServers.Any(s => s.IsTry))
        {
            newSubServers[0].IsTry = true;
        }
        
        SubServers = new ObservableCollection<SubServerItem>(newSubServers);
        
        // 保存配置
        await SaveConfigAsync();
    }

    private async Task ConfigureSubServerAsync(LocalServer server, string secret)
    {
        if (server == null) return;
        
        // 更新 server.properties
        var propsPath = Path.Combine(server.ServerPath, "server.properties");
        if (File.Exists(propsPath))
        {
            var lines = await File.ReadAllLinesAsync(propsPath);
            var newLines = new List<string>();
            bool onlineFound = false;
            bool secureFound = false;
            bool portFound = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("online-mode="))
                {
                    newLines.Add("online-mode=false");
                    onlineFound = true;
                }
                else if (line.StartsWith("enforce-secure-profile="))
                {
                    newLines.Add("enforce-secure-profile=false");
                    secureFound = true;
                }
                else if (line.StartsWith("server-port="))
                {
                    newLines.Add($"server-port={server.Port}");
                    portFound = true;
                }
                else
                {
                    newLines.Add(line);
                }
            }
            
            if (!onlineFound) newLines.Add("online-mode=false");
            if (!secureFound) newLines.Add("enforce-secure-profile=false");
            if (!portFound) newLines.Add($"server-port={server.Port}");
            
            await File.WriteAllLinesAsync(propsPath, newLines);
        }

        // 更新 paper-global.yml
        if (!string.IsNullOrEmpty(secret))
        {
            var paperConfigPath = Path.Combine(server.ServerPath, "config", "paper-global.yml");
            if (File.Exists(paperConfigPath))
            {
                 var paperLines = await File.ReadAllLinesAsync(paperConfigPath);
                 var newPaperLines = new List<string>();
                 bool insideProxies = false;
                 bool insideVelocity = false;
                 int velocityIndent = -1;
                 
                 for (int i = 0; i < paperLines.Length; i++)
                 {
                     var line = paperLines[i];
                     var trim = line.Trim();
                     
                     // 跳过注释以进行结构检测，但在输出中保留它们
                     if (trim.StartsWith("#"))
                     {
                         newPaperLines.Add(line);
                         continue;
                     }

                     if (string.IsNullOrWhiteSpace(trim))
                     {
                         newPaperLines.Add(line);
                         continue;
                     }

                     // 检测缩进
                     int currentIndent = line.Length - line.TrimStart().Length;

                     // 根据缩进检查是否退出了部分
                     if (insideVelocity && currentIndent <= velocityIndent)
                     {
                         insideVelocity = false;
                         // 如果回到了 proxies 层级，我们可能仍然在 proxies 内部
                         if (currentIndent == 0) insideProxies = false; 
                     }
                     
                     if (trim == "proxies:" || trim.StartsWith("proxies:"))
                     {
                         insideProxies = true;
                         insideVelocity = false; // 重置 velocity 状态以防万一
                     }
                     else if (insideProxies && (trim == "velocity:" || trim.StartsWith("velocity:")))
                     {
                         insideVelocity = true;
                         velocityIndent = currentIndent;
                     }
                     
                     if (insideVelocity)
                     {
                         if (trim.StartsWith("enabled:"))
                         {
                             newPaperLines.Add($"{new string(' ', currentIndent)}enabled: true");
                             continue;
                         }
                         if (trim.StartsWith("online-mode:"))
                         {
                             // 用户指令："If velocity online-mode is true: online-mode change to false"
                             // 还有 "If velocity online-mode is false: online-mode change to false"
                             // 所以总是 false。
                             newPaperLines.Add($"{new string(' ', currentIndent)}online-mode: false");
                             continue;
                         }
                         if (trim.StartsWith("secret:"))
                         {
                             newPaperLines.Add($"{new string(' ', currentIndent)}secret: '{secret}'");
                             continue;
                         }
                     }

                     newPaperLines.Add(line);
                 }
                 await File.WriteAllLinesAsync(paperConfigPath, newPaperLines);
            }
        }
    }

    public async Task InitializeAsync(string serverId)
    {
        // serverId 格式："local_{id}"
        if (serverId.StartsWith("local_"))
        {
            int id = int.Parse(serverId.Substring(6));
            var servers = await _serverManager.GetServersAsync();
            Server = servers.FirstOrDefault(s => s.Id == id);
        }
    }

    private void CheckStatus()
    {
        if (Server == null) return;
        IsRunning = _serverManager.IsServerRunning(Server.Id);
        ServerStatus = IsRunning ? "Running" : "Offline";
    }

    [RelayCommand]
    private async Task StartServerAsync()
    {
        if (Server == null || IsRunning) return;
        await _serverManager.StartServerAsync(Server.Id);
    }

    [RelayCommand]
    private async Task StopServerAsync()
    {
        if (Server == null || !IsRunning) return;
        await _serverManager.StopServerAsync(Server.Id);
    }

    [RelayCommand]
    private async Task RestartServerAsync()
    {
        await StopServerAsync();
        await Task.Delay(5000); 
        await StartServerAsync();
    }

    [RelayCommand]
    private void UpdateResourceUsage(System.Diagnostics.Process process)
    {
        try 
        {
            if (process != null && !process.HasExited)
            {
                process.Refresh();
                var memoryMb = process.WorkingSet64 / 1024 / 1024;
                var memoryPercent = (double)memoryMb / Server?.MaxMemoryMB * 100 ?? 0;
                MemoryUsage = Math.Min(memoryPercent, 100);
                MemoryUsageText = $"{memoryMb} MB";
                // 没有历史记录很难计算 CPU，暂时假设为 0 或实现复杂的监控
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task SendCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(CommandInput) || Server == null) return;
        await _serverManager.SendCommandAsync(Server.Id, CommandInput);
        CommandInput = "";
    }
    
    // 当用户按 Enter 时从视图调用的快速方法
    public async Task SendCommand(string command)
    {
        if (Server == null) return;
        
        await _serverManager.SendCommandAsync(Server.Id, CommandInput);
        CommandInput = "";
    }

    [RelayCommand]
    private async Task StartSubServerAsync(SubServerItem item)
    {
        if (item.LocalServerId > 0 && !item.IsRunning)
        {
            await _serverManager.StartServerAsync(item.LocalServerId);
        }
    }

    [RelayCommand]
    private async Task StopSubServerAsync(SubServerItem item)
    {
        if (item.LocalServerId > 0 && item.IsRunning)
        {
            await _serverManager.StopServerAsync(item.LocalServerId);
        }
    }

    [RelayCommand]
    private async Task StartAllSubServersAsync()
    {
        foreach(var sub in SubServers)
        {
            if (sub.LocalServerId > 0 && !sub.IsRunning)
            {
                await _serverManager.StartServerAsync(sub.LocalServerId);
                // 稍微延迟以防止大量资源峰值？
                await Task.Delay(2000);
            }
        }
    }

    // 配置逻辑
    public async Task LoadConfigAsync()
    {
        if (Server == null) return;
        
        var configPath = System.IO.Path.Combine(Server.ServerPath, "velocity.toml");
        if (!System.IO.File.Exists(configPath)) return;

        try 
        {
            var lines = await File.ReadAllLinesAsync(configPath);
            var subServers = new List<SubServerItem>();
            var tryList = new List<string>();
            bool inServersSection = false;

            // 获取所有本地服务器以进行映射
            var allServers = await _serverManager.GetServersAsync();
            
            foreach (var line in lines)
            {
                var trim = line.Trim();
                if (string.IsNullOrEmpty(trim) || trim.StartsWith("#")) continue;

                if (trim == "[servers]")
                {
                    inServersSection = true;
                    continue;
                }
                if (trim.StartsWith("[")) inServersSection = false;

                // 解析键值对
                if (trim.StartsWith("bind")) 
                {
                    // bind = "0.0.0.0:25565"
                    var val = ParseString(trim);
                    if (val.Contains(":"))
                    {
                        var parts = val.Split(':');
                        if (int.TryParse(parts[parts.Length - 1], out int p)) BindPort = p;
                    }
                }
                else if (trim.StartsWith("motd")) Motd = ParseString(trim);
                else if (trim.StartsWith("show-max-players")) MaxPlayers = ParseInt(trim);
                else if (trim.StartsWith("online-mode")) OnlineMode = ParseBool(trim);
                else if (trim.StartsWith("force-key-authentication")) ForceKeyAuthentication = ParseBool(trim);
                else if (trim.StartsWith("try")) 
                {
                    // 解析尝试列表：try = ["lobby"]
                    var content = trim.Split('=')[1].Trim();
                    content = content.Trim('[', ']');
                    // 按逗号分割，但处理可能的引号
                    var parts = content.Split(',');
                    foreach(var p in parts)
                    {
                        var s = p.Trim().Trim('"');
                        if(!string.IsNullOrEmpty(s)) tryList.Add(s);
                    }
                }

                if (inServersSection && trim.Contains("="))
                {
                    // server = "address"
                    var parts = trim.Split('=');
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        // 处理带引号的键："Server Name" = ...
                        key = key.Trim('"');
                        
                        if (!key.StartsWith("#") && key != "try") 
                        {
                            string address = parts[1].Trim().Trim('"');
                            int localId = 0;
                            bool isRunning = false;
                            LocalServer? localServer = null;

                            // 尝试与本地服务器匹配
                            // 地址通常是 "127.0.0.1:25566"
                            if (address.Contains(":"))
                            {
                                var addrParts = address.Split(':');
                                if (addrParts.Length == 2 && int.TryParse(addrParts[1], out int port))
                                {
                                    localServer = allServers.FirstOrDefault(s => s.Port == port);
                                    if (localServer != null)
                                    {
                                        localId = localServer.Id;
                                        isRunning = _serverManager.IsServerRunning(localId);
                                    }
                                }
                            }

                            var subItem = new SubServerItem 
                            { 
                                Name = key, 
                                DisplayName = localServer != null ? localServer.Name : key,
                                Address = address,
                                LocalServerId = localId,
                                IsRunning = isRunning,
                                LocalServer = localServer
                            };
                            subServers.Add(subItem);
                        }
                    }
                }
            }

            // 如果有重复项则移除（例如，如果文件有多个部分）
            // 并确保 "try" 如果被错误解析没有被添加为服务器
            subServers.RemoveAll(s => s.Name == "try");

            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                SubServers = new ObservableCollection<SubServerItem>(subServers);
                TryServers = new ObservableCollection<string>(tryList);
                
                // 标记尝试服务器
                foreach(var sub in SubServers)
                {
                    if (TryServers.Contains(sub.Name)) sub.IsTry = true;
                }
            });
        }
        catch (Exception ex)
        {
            ConsoleOutput += $"\n[Error] Failed to load config: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveConfigAsync()
    {
        await SaveSettingsAsync(false);
    }

    private async Task SaveSettingsAsync(bool silent)
    {
        if (Server == null) return;
        var configPath = Path.Combine(Server.ServerPath, "velocity.toml");
        
        try 
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Config version. Do not change this");
            sb.AppendLine("config-version = \"2.7\"");
            sb.AppendLine("");
            sb.AppendLine("# 代理应该绑定到哪个端口？默认情况下，我们将绑定到端口 25565 上的所有地址。");
            sb.AppendLine($"bind = \"0.0.0.0:{BindPort}\"");
            sb.AppendLine("");
            sb.AppendLine("# MOTD 应该是什么？当玩家将您的服务器添加到服务器列表时，会显示此内容。仅接受 MiniMessage 格式。");
            sb.AppendLine($"motd = \"{Motd}\"");
            sb.AppendLine("");
            sb.AppendLine("# 我们应该显示的最大玩家数是多少？（Velocity 不支持限制在线玩家数量。）");
            sb.AppendLine($"show-max-players = {MaxPlayers}");
            sb.AppendLine("");
            sb.AppendLine("# 我们应该通过 Mojang 验证玩家吗？默认情况下，这是开启的。");
            sb.AppendLine($"online-mode = {OnlineMode.ToString().ToLower()}");
            sb.AppendLine("");
            sb.AppendLine("# 代理是否应该强制执行新的公钥安全标准？默认情况下，这是开启的。");
            sb.AppendLine($"force-key-authentication = {ForceKeyAuthentication.ToString().ToLower()}");
            sb.AppendLine("");
            sb.AppendLine("# 如果从此代理发送的客户端 ISP/AS 与 Mojang 验证服务器的不同，玩家将被踢出。这禁止了一些 VPN 和代理连接，但这是一种弱保护形式。");
            sb.AppendLine("prevent-client-proxy-connections = false");
            sb.AppendLine("");
            sb.AppendLine("# 我们应该将 IP 地址和其他数据转发到后端服务器吗？");
            sb.AppendLine("# 可用选项：");
            sb.AppendLine("# - \"none\":        不进行转发。所有玩家将显示为从代理连接，并将具有离线模式 UUID。");
            sb.AppendLine("# - \"legacy\":      以 BungeeCord 兼容格式转发玩家 IP 和 UUID。如果您运行使用 Minecraft 1.12 或更低版本的服务器，请使用此选项。");
            sb.AppendLine("# - \"bungeeguard\": 以 BungeeGuard 插件支持的格式转发玩家 IP 和 UUID。如果您运行使用 Minecraft 1.12 或更低版本的服务器，并且无法实施网络级防火墙（在共享主机上），请使用此选项。");
            sb.AppendLine("# - \"modern\":      使用 Velocity 的原生转发在登录过程中转发玩家 IP 和 UUID。仅适用于 Minecraft 1.13 或更高版本。");
            sb.AppendLine("player-info-forwarding-mode = \"modern\"");
            sb.AppendLine("");
            sb.AppendLine("# 如果您使用的是 modern 或 BungeeGuard IP 转发，请在此处配置包含唯一密钥的文件。");
            sb.AppendLine("# 该文件应为 UTF-8 编码且不为空。");
            sb.AppendLine("# forwarding-secret-file = \"forwarding.secret\"");
            sb.AppendLine("");
            sb.AppendLine("# 宣布您的服务器是否支持 Forge。如果您运行模组服务器，建议开启此选项。");
            sb.AppendLine("announce-forge = false");
            sb.AppendLine("");
            sb.AppendLine("# 如果启用（默认为 false）且代理处于在线模式，如果尝试重复连接，Velocity 将踢出任何在线的现有玩家。");
            sb.AppendLine("kick-existing-players = false");
            sb.AppendLine("");
            sb.AppendLine("# Velocity 是否应该将服务器列表 ping 请求传递给后端服务器？");
            sb.AppendLine("ping-passthrough = \"DISABLED\"");
            sb.AppendLine("");
            sb.AppendLine("# 如果启用（默认为 false），当鼠标悬停在服务器列表中的玩家计数上时，将显示代理上的在线玩家示例。");
            sb.AppendLine("sample-players-in-ping = false");
            sb.AppendLine("");
            sb.AppendLine("# 如果未启用（默认为 true），日志中的玩家 IP 地址将被替换为 <ip address withheld>");
            sb.AppendLine("enable-player-address-logging = true");
            sb.AppendLine("");
            sb.AppendLine("[servers]");
            
            var tryList = new List<string>();
            foreach(var sub in SubServers)
            {
                // 确保名称对 TOML 是安全的。
                // 如果包含空格、纯数字或特殊字符，则需要引号。
                // 但是：检查 SubServers 中的名称是否已经有引号？
                // 问题可能是双重引号：""name""
                
                string rawName = sub.Name.Trim('"'); // 去除现有的引号（如果有）
                // 始终引用服务器名称键以避免歧义并确保一致性
                string safeName = $"\"{rawName}\"";
                
                sb.AppendLine($"{safeName} = \"{sub.Address}\"");
                if (sub.IsTry) 
                {
                    tryList.Add(safeName);
                }
            }
            
            sb.AppendLine("");
            sb.AppendLine($"try = [{string.Join(", ", tryList)}]");
            sb.AppendLine("");
            sb.AppendLine("[forced-hosts]");
            sb.AppendLine("# 默认情况下未配置强制主机");
            sb.AppendLine("");
            sb.AppendLine("[advanced]");
            sb.AppendLine("compression-threshold = 256");
            sb.AppendLine("compression-level = -1");
            sb.AppendLine("login-ratelimit = 3000");
            sb.AppendLine("connection-timeout = 5000");
            sb.AppendLine("read-timeout = 30000");
            sb.AppendLine("haproxy-protocol = false");
            sb.AppendLine("tcp-fast-open = false");
            sb.AppendLine("bungee-plugin-message-channel = true");
            sb.AppendLine("show-ping-requests = false");
            sb.AppendLine("failover-on-unexpected-server-disconnect = true");
            sb.AppendLine("announce-proxy-commands = true");
            sb.AppendLine("log-command-executions = false");
            sb.AppendLine("log-player-connections = true");
            sb.AppendLine("accepts-transfers = false");
            sb.AppendLine("enable-reuse-port = false");
            sb.AppendLine("command-rate-limit = 50");
            sb.AppendLine("forward-commands-if-rate-limited = true");
            sb.AppendLine("kick-after-rate-limited-commands = 0");
            sb.AppendLine("tab-complete-rate-limit = 10");
            sb.AppendLine("kick-after-rate-limited-tab-completes = 0");
            sb.AppendLine("");
            sb.AppendLine("[query]");
            sb.AppendLine("enabled = false");
            sb.AppendLine("port = 25565");
            sb.AppendLine("map = \"Velocity\"");
            sb.AppendLine("show-plugins = false");

            await File.WriteAllTextAsync(configPath, sb.ToString());
            
            if (!silent)
            {
                 App.MainWindowInstance?.DispatcherQueue.TryEnqueue(async () =>
                 {
                     var dialog = new ContentDialog
                     {
                         Title = "保存成功",
                         Content = "配置已保存。如果服务器正在运行，请重启以应用更改。",
                         CloseButtonText = "确定",
                         XamlRoot = App.MainWindowInstance.Content.XamlRoot
                     };
                     await dialog.ShowAsync();
                 });
            }
        }
        catch (Exception ex)
        {
             App.MainWindowInstance?.DispatcherQueue.TryEnqueue(async () =>
             {
                 var dialog = new ContentDialog
                 {
                     Title = "保存失败",
                     Content = ex.Message,
                     CloseButtonText = "确定",
                     XamlRoot = App.MainWindowInstance.Content.XamlRoot
                 };
                 await dialog.ShowAsync();
             });
        }
    }
    
    [RelayCommand]
    private void AddSubServer()
    {
        int maxPort = 30000;
        foreach(var s in SubServers)
        {
             // 尝试从地址解析端口
             var parts = s.Address.Split(':');
             if (parts.Length == 2 && int.TryParse(parts[1], out int p))
             {
                 if (p > maxPort) maxPort = p;
             }
        }
        string newName = $"server{DateTimeOffset.Now.ToUnixTimeSeconds()}";
        SubServers.Add(new SubServerItem { Name = newName, DisplayName = newName, Address = $"127.0.0.1:{maxPort + 1}" });
    }

    [RelayCommand]
    private void RemoveSubServer(SubServerItem item)
    {
        if (SubServers.Contains(item))
        {
            SubServers.Remove(item);
        }
    }

    // 解析辅助方法
    private int ParseInt(string line)
    {
        try {
             var parts = line.Split('=');
             if (parts.Length < 2) return 0;
             var val = parts[1].Trim();
             // 处理行尾的注释（如果有的话），虽然在我们生成的配置中通常在单独的行
             // 但 TOML 允许：key = 123 # 注释
             var commentIndex = val.IndexOf('#');
             if (commentIndex > 0) val = val.Substring(0, commentIndex).Trim();
             return int.Parse(val);
        } catch { return 0; }
    }
    
    private string ParseString(string line)
    {
        try {
             var parts = line.Split('=');
             if (parts.Length < 2) return "";
             var val = parts[1].Trim();
             // 处理注释
             // TOML 字符串是带引号的。
             // 如果有引号，找到结束引号。
             if (val.StartsWith("\""))
             {
                 var endQuote = val.IndexOf('"', 1);
                 if (endQuote > 0) return val.Substring(1, endQuote - 1);
             }
             return val.Trim('"');
        } catch { return ""; }
    }

    private bool ParseBool(string line)
    {
        try {
             var parts = line.Split('=');
             if (parts.Length < 2) return false;
             var val = parts[1].Trim();
             var commentIndex = val.IndexOf('#');
             if (commentIndex > 0) val = val.Substring(0, commentIndex).Trim();
             return bool.Parse(val);
        } catch { return false; }
    }

    // 事件
    private void OnServerStatusChanged(object? sender, ServerStatusEventArgs e)
    {
        if (Server != null && e.ServerId == Server.Id)
        {
            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                IsRunning = e.IsRunning;
                ServerStatus = IsRunning ? "Running" : "Offline";
            });
        }
        
        // 更新子服务器状态
        App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
        {
            var sub = SubServers.FirstOrDefault(s => s.LocalServerId == e.ServerId);
            if (sub != null)
            {
                sub.IsRunning = e.IsRunning;
            }
        });
    }

    private void OnOutputReceived(object? sender, ServerOutputEventArgs e)
    {
        if (Server != null && e.ServerId == Server.Id)
        {
            App.MainWindowInstance?.DispatcherQueue.TryEnqueue(() =>
            {
                // 暂时简单追加，可能需要针对大日志进行优化
                if (ConsoleOutput.Length > 50000) ConsoleOutput = ConsoleOutput.Substring(ConsoleOutput.Length - 40000);
                ConsoleOutput += e.Message + "\n";

                // 更新玩家计数
                var match = Regex.Match(e.Message, @"There are (\d+) of a max of (\d+) players online");
                if (match.Success)
                {
                     PlayerCountText = $"{match.Groups[1].Value}/{match.Groups[2].Value}";
                }

                // TODO: 如果需要，从 /glist 或类似命令解析完整的玩家列表，
                // 但 Velocity 输出各不相同。目前仅显示计数。
            });
        }
    }
}

public partial class SubServerItem : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string DisplayName { get; set; } = "";
    
    [ObservableProperty]
    public partial string Address { get; set; } = "";
    
    [ObservableProperty]
    public partial bool IsTry { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial int LocalServerId { get; set; }

    public LocalServer? LocalServer { get; set; }
}

public partial class PlayerItem : ObservableObject
{
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Server { get; set; } = "";
}

public partial class SelectableServerItem : ObservableObject
{
    public required LocalServer Server { get; set; }
    
    [ObservableProperty]
    public partial bool IsSelected { get; set; }
    
    [ObservableProperty]
    public partial string DisplayName { get; set; } = "";
    
    [ObservableProperty]
    public partial string Detail { get; set; } = "";
    
    [ObservableProperty]
    public partial bool IsMainLobby { get; set; }
}
