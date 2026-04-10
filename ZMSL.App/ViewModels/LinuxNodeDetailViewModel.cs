using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZMSL.App.Models;
using ZMSL.App.Services;

namespace ZMSL.App.ViewModels;

public class LinuxNodeDetailViewModel : INotifyPropertyChanged
{
    private readonly LinuxNodeService _nodeService;
    private LinuxNode? _node;
    private NodeSystemInfo? _systemInfo;
    private NodeSystemResources? _resources;
    private ObservableCollection<RemoteServerInfo> _servers = new();
    private bool _isLoading;

    public LinuxNode? Node
    {
        get => _node;
        set { _node = value; OnPropertyChanged(); }
    }

    public NodeSystemInfo? SystemInfo
    {
        get => _systemInfo;
        set { _systemInfo = value; OnPropertyChanged(); }
    }

    public NodeSystemResources? Resources
    {
        get => _resources;
        set { _resources = value; OnPropertyChanged(); }
    }

    public ObservableCollection<RemoteServerInfo> Servers
    {
        get => _servers;
        set { _servers = value; OnPropertyChanged(); }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set { _isLoading = value; OnPropertyChanged(); }
    }

    public LinuxNodeDetailViewModel(LinuxNodeService nodeService)
    {
        _nodeService = nodeService;
    }

    public async Task RefreshResourcesAsync()
    {
        if (Node == null) return;
        try
        {
            var systemInfoTask = _nodeService.GetSystemInfoAsync(Node);
            var resourcesTask = _nodeService.GetSystemResourcesAsync(Node);
            await Task.WhenAll(systemInfoTask, resourcesTask);
            SystemInfo = await systemInfoTask;
            Resources = await resourcesTask;
        }
        catch { }
    }

    public async Task RefreshServersAsync()
    {
        if (Node == null) return;
        IsLoading = true;
        try
        {
            var servers = await _nodeService.ListServersAsync(Node);
            Servers.Clear();
            if (servers != null)
            {
                foreach (var server in servers)
                {
                    Servers.Add(server);
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task InitializeAsync(LinuxNode node)
    {
        Node = node;
        await Task.WhenAll(RefreshResourcesAsync(), RefreshServersAsync());
    }

    public async Task<(bool Success, string Message)> StartServerAsync(string serverId)
    {
        if (Node == null) return (false, "节点未初始化");
        var result = await _nodeService.StartServerAsync(Node, serverId);
        await RefreshServersAsync();
        return result;
    }

    public async Task<(bool Success, string Message)> StopServerAsync(string serverId)
    {
        if (Node == null) return (false, "节点未初始化");
        var result = await _nodeService.StopServerAsync(Node, serverId);
        await RefreshServersAsync();
        return result;
    }

    public async Task<List<RemoteServer>> GetLocalRemoteServersAsync()
    {
        if (Node == null) return new List<RemoteServer>();
        return await _nodeService.GetLocalRemoteServersAsync(Node.Id);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
