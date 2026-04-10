using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZMSL.App.Models;
using System.Collections.ObjectModel;

namespace ZMSL.App.ViewModels;

public partial class MyServerViewModel : ObservableObject
{
    private readonly Services.ServerManagerService _serverManager;
    private readonly Services.DatabaseService _db;
    private readonly Services.LinuxNodeService _nodeService;

    [ObservableProperty]
    public partial ObservableCollection<ServerViewModel> Servers { get; set; } = new();

    [ObservableProperty]
    public partial ServerViewModel? SelectedServer { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<string> ConsoleOutput { get; set; } = new();

    [ObservableProperty]
    public partial string CommandInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsServerRunning { get; set; }

    [ObservableProperty]
    public partial bool IsCardView { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSelectionMode { get; set; }

    public MyServerViewModel(Services.ServerManagerService serverManager, Services.DatabaseService db, Services.LinuxNodeService nodeService)
    {
        _serverManager = serverManager;
        _db = db;
        _nodeService = nodeService;

        _serverManager.ServerOutput += OnServerOutput;
        _serverManager.ServerStatusChanged += OnServerStatusChanged;
        
        // 加载视图偏好
        LoadViewPreferenceAsync();
    }

    private async void LoadViewPreferenceAsync()
    {
        try
        {
            var settings = await _db.GetSettingsAsync();
            IsCardView = settings.UseCardView;
        }
        catch { }
    }

    [RelayCommand]
    private async Task ToggleViewModeAsync()
    {
        IsCardView = !IsCardView;
        try
        {
            var settings = await _db.GetSettingsAsync();
            settings.UseCardView = IsCardView;
            await _db.SaveSettingsAsync(settings);
        }
        catch { }
    }

    [RelayCommand]
    private void ToggleSelectionMode()
    {
        IsSelectionMode = !IsSelectionMode;
        if (!IsSelectionMode)
        {
            // 退出选择模式时清空选择
            foreach (var server in Servers)
            {
                server.IsSelected = false;
            }
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var server in Servers)
        {
            server.IsSelected = true;
        }
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var server in Servers)
        {
            server.IsSelected = false;
        }
    }

    [RelayCommand]
    private async Task BatchStartAsync()
    {
        var selectedServers = Servers.Where(s => s.IsSelected).ToList();
        if (!selectedServers.Any()) return;

        // 使用 SemaphoreSlim 限制并发数，防止同时启动太多进程卡死系统
        // 这里设置为 3，意味着最多同时启动 3 个服务器
        using var semaphore = new SemaphoreSlim(3);
        var tasks = selectedServers.Select(async server =>
        {
            await semaphore.WaitAsync();
            try
            {
                if (server.LocalServer != null)
                {
                    if (!_serverManager.IsServerRunning(server.LocalServer.Id))
                    {
                        await _serverManager.StartServerAsync(server.LocalServer.Id);
                    }
                }
                // TODO: 远程服务器批量启动
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BatchStart] 启动服务器 {server.Name} 失败: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        });

        // 等待所有启动任务完成（或至少发起了启动指令）
        await Task.WhenAll(tasks);
        
        IsSelectionMode = false;
    }

    [RelayCommand]
    private async Task BatchStopAsync()
    {
        var selectedServers = Servers.Where(s => s.IsSelected).ToList();
        if (!selectedServers.Any()) return;

        // 并行停止所有选中的本地服务器
        var tasks = selectedServers
            .Where(s => s.LocalServer != null && _serverManager.IsServerRunning(s.LocalServer.Id))
            .Select(async server =>
            {
                try { await _serverManager.StopServerAsync(server.LocalServer!.Id); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[BatchStop] 停止服务器 {server.Name} 失败: {ex.Message}");
                }
            });
        // TODO: 远程服务器批量停止

        await Task.WhenAll(tasks);

        IsSelectionMode = false;
    }

    [RelayCommand]
    private async Task BatchDeleteAsync()
    {
        var selectedServers = Servers.Where(s => s.IsSelected).ToList();
        if (!selectedServers.Any()) return;

        // 并行删除所有选中服务器
        var tasks = selectedServers
            .Where(s => s.LocalServer != null)
            .Select(async server =>
            {
                try { await _serverManager.DeleteServerAsync(server.LocalServer!.Id); }
                catch { }
            });
        // TODO: 远程服务器批量删除

        await Task.WhenAll(tasks);

        await LoadServersAsync();
        IsSelectionMode = false;
    }

    [RelayCommand]
    public async Task LoadServersAsync()
    {
        IsLoading = true;
        try
        {
            // 第一步：只加载本地服务器 + 数据库中的远程服务器（不等待节点 API），立即显示
            var localServers = await _serverManager.GetServersAsync();
            var nodes = await _nodeService.GetNodesAsync();
            var remoteServers = await _db.ExecuteWithLockAsync(async db =>
                await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(db.RemoteServers));

            var allServers = new List<ServerViewModel>();

            foreach (var server in localServers)
            {
                // Exclude velocity servers
                if (server.CoreType?.ToLower() == "velocity") continue;

                allServers.Add(new ServerViewModel
                {
                    Id = $"local_{server.Id}",
                    Name = server.Name,
                    Type = "Local",
                    Location = "本地",
                    CoreType = server.CoreType ?? string.Empty,
                    MinecraftVersion = server.MinecraftVersion ?? string.Empty,
                    IsRunning = _serverManager.IsServerRunning(server.Id),
                    CreatedAt = server.CreatedAt,
                    MinMemoryMB = server.MinMemoryMB,
                    MaxMemoryMB = server.MaxMemoryMB,
                    LocalServer = server
                });
            }

            foreach (var remoteServer in remoteServers)
            {
                var node = nodes.FirstOrDefault(n => n.Id == remoteServer.NodeId);
                allServers.Add(new ServerViewModel
                {
                    Id = $"remote_{remoteServer.NodeId}_{remoteServer.RemoteServerId}",
                    Name = remoteServer.Name,
                    Type = "Remote",
                    Location = node?.Name ?? "未知节点",
                    CoreType = remoteServer.CoreType,
                    MinecraftVersion = remoteServer.MinecraftVersion,
                    IsRunning = remoteServer.IsRunning,
                    NodeId = remoteServer.NodeId,
                    CreatedAt = remoteServer.CreatedAt,
                    MinMemoryMB = remoteServer.MinMemoryMB,
                    MaxMemoryMB = remoteServer.MaxMemoryMB,
                    RemoteServer = remoteServer
                });
            }

            Servers = new ObservableCollection<ServerViewModel>(allServers.OrderByDescending(s => s.CreatedAt));
        }
        finally
        {
            IsLoading = false;
        }

        // 第二步：后台同步节点数据，同步完成后刷新列表（不阻塞界面）
        _ = SyncNodesAndRefreshAsync();
    }

    /// <summary>
    /// 后台同步所有节点并刷新远程服务器列表（不阻塞界面）
    /// </summary>
    private async Task SyncNodesAndRefreshAsync()
    {
        try
        {
            var nodes = await _nodeService.GetNodesAsync();
            if (nodes.Count == 0) return;

            var syncTasks = nodes.Select(async node =>
            {
                try
                {
                    var (updated, _) = await _nodeService.SyncRemoteServersAsync(node);
                    if (updated > 0)
                        System.Diagnostics.Debug.WriteLine($"节点 {node.Name} 同步了 {updated} 个服务器");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"同步节点 {node.Name} 失败: {ex.Message}");
                }
            }).ToList();

            await Task.WhenAll(syncTasks);

            // 同步完成后在 UI 线程更新列表
            var remoteServers = await _db.ExecuteWithLockAsync(async db =>
                await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(db.RemoteServers));

            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                var localItems = Servers.Where(s => s.LocalServer != null).ToList();
                var remoteViewModels = remoteServers.Select(rs =>
                {
                    var node = nodes.FirstOrDefault(n => n.Id == rs.NodeId);
                    return new ServerViewModel
                    {
                        Id = $"remote_{rs.NodeId}_{rs.RemoteServerId}",
                        Name = rs.Name,
                        Type = "Remote",
                        Location = node?.Name ?? "未知节点",
                        CoreType = rs.CoreType,
                        MinecraftVersion = rs.MinecraftVersion,
                        IsRunning = rs.IsRunning,
                        NodeId = rs.NodeId,
                        CreatedAt = rs.CreatedAt,
                        MinMemoryMB = rs.MinMemoryMB,
                        MaxMemoryMB = rs.MaxMemoryMB,
                        RemoteServer = rs
                    };
                }).ToList();

                var merged = localItems.Concat(remoteViewModels).OrderByDescending(s => s.CreatedAt).ToList();
                Servers = new ObservableCollection<ServerViewModel>(merged);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MyServer] 后台同步节点失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CreateServerAsync(LocalServer server)
    {
        await _serverManager.CreateServerAsync(server);
        await LoadServersAsync();
    }

    [RelayCommand]
    private async Task DeleteServerAsync()
    {
        if (SelectedServer == null) return;
        
        if (SelectedServer.LocalServer != null)
        {
            await _serverManager.DeleteServerAsync(SelectedServer.LocalServer.Id);
        }
        // TODO: 删除远程服务器
        
        await LoadServersAsync();
    }

    [RelayCommand]
    private async Task StartServerAsync()
    {
        if (SelectedServer == null) return;
        ConsoleOutput.Clear();
        
        if (SelectedServer.LocalServer != null)
        {
            try
            {
                await _serverManager.StartServerAsync(SelectedServer.LocalServer.Id);
            }
            catch (Exception ex)
            {
                ConsoleOutput.Add($"[错误] {ex.Message}");
            }
        }
        // TODO: 启动远程服务器
    }

    [RelayCommand]
    private async Task StopServerAsync()
    {
        if (SelectedServer == null) return;
        
        if (SelectedServer.LocalServer != null)
        {
            await _serverManager.StopServerAsync(SelectedServer.LocalServer.Id);
        }
        // TODO: 停止远程服务器
    }

    [RelayCommand]
    private async Task SendCommandAsync()
    {
        if (SelectedServer == null || string.IsNullOrWhiteSpace(CommandInput)) return;
        
        if (SelectedServer.LocalServer != null)
        {
            await _serverManager.SendCommandAsync(SelectedServer.LocalServer.Id, CommandInput);
            ConsoleOutput.Add($"> {CommandInput}");
        }
        // TODO: 发送命令到远程服务器
        
        CommandInput = string.Empty;
    }

    [RelayCommand]
    private async Task SaveServerAsync()
    {
        if (SelectedServer == null) return;
        
        if (SelectedServer.LocalServer != null)
        {
            await _serverManager.UpdateServerAsync(SelectedServer.LocalServer);
        }
        // TODO: 保存远程服务器
    }

    partial void OnSelectedServerChanged(ServerViewModel? value)
    {
        if (value != null)
        {
            if (value.LocalServer != null)
            {
                IsServerRunning = _serverManager.IsServerRunning(value.LocalServer.Id);
            }
            else if (value.RemoteServer != null)
            {
                IsServerRunning = value.RemoteServer.IsRunning;
            }
            ConsoleOutput.Clear();
        }
    }

    private void OnServerOutput(object? sender, Services.ServerOutputEventArgs e)
    {
        if (SelectedServer?.LocalServer?.Id == e.ServerId)
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                ConsoleOutput.Add(e.IsError ? $"[ERR] {e.Message}" : e.Message);
                // 限制输出行数
                while (ConsoleOutput.Count > 1000)
                {
                    ConsoleOutput.RemoveAt(0);
                }
            });
        }
    }

    private void OnServerStatusChanged(object? sender, Services.ServerStatusEventArgs e)
    {
        if (SelectedServer?.LocalServer?.Id == e.ServerId)
        {
            App.MainWindow.DispatcherQueue.TryEnqueue(() =>
            {
                IsServerRunning = e.IsRunning;
            });
        }

        // 更新主窗口状态
        if (e.IsRunning)
        {
            var server = Servers.FirstOrDefault(s => s.LocalServer?.Id == e.ServerId);
            (App.MainWindow as MainWindow)?.UpdateServerStatus(true, server?.Name);
        }
        else
        {
            (App.MainWindow as MainWindow)?.UpdateServerStatus(false);
        }
    }
}
