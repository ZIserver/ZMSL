using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using ZMSL.App.Models;
using ZMSL.App.Views;

namespace ZMSL.App.ViewModels;

public partial class GroupServerListViewModel : ObservableObject
{
    private readonly Services.ServerManagerService _serverManager;
    private readonly Services.DatabaseService _db;
    
    [ObservableProperty]
    public partial ObservableCollection<ServerViewModel> GroupServers { get; set; } = new();

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsCardView { get; set; } = true;

    public GroupServerListViewModel(Services.ServerManagerService serverManager, Services.DatabaseService db)
    {
        _serverManager = serverManager;
        _db = db;
        
        _serverManager.ServerStatusChanged += OnServerStatusChanged;
        
        // Load view preference
        LoadViewPreferenceAsync();
    }

    private async void LoadViewPreferenceAsync()
    {
        try
        {
            var settings = await _db.GetSettingsAsync();
            IsCardView = settings.UseCardView; // Reuse same setting or add new one if needed? 
            // For now let's reuse or just default to true. 
            // If user wants separate setting, we need to add to AppSettings.
            // Let's assume shared setting for now or just local state.
            // But usually users expect persistence.
            // Let's just use local state default true for now to avoid DB migration complexity in this turn unless requested.
            // Wait, MyServerViewModel uses settings.UseCardView. If I use the same, they will sync, which might be good or bad.
            // Let's use the same setting for consistency across the app.
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
    public async Task LoadGroupServersAsync()
    {
        IsLoading = true;
        try
        {
            var allServers = await _serverManager.GetServersAsync();
            var velocityServers = allServers
                .Where(s => s.CoreType?.ToLower() == "velocity")
                .Select(s => new ServerViewModel
                {
                    Id = $"local_{s.Id}",
                    Name = s.Name,
                    Type = "Local",
                    Location = "本地",
                    CoreType = s.CoreType,
                    MinecraftVersion = s.MinecraftVersion,
                    IsRunning = _serverManager.IsServerRunning(s.Id),
                    CreatedAt = s.CreatedAt,
                    MinMemoryMB = s.MinMemoryMB,
                    MaxMemoryMB = s.MaxMemoryMB,
                    LocalServer = s
                })
                .OrderByDescending(s => s.CreatedAt);

            GroupServers = new ObservableCollection<ServerViewModel>(velocityServers);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void CreateGroupServer()
    {
        App.MainWindowInstance?.ContentFramePublic.Navigate(typeof(CreateGroupServerPage));
    }

    [RelayCommand]
    private void OpenServerDetail(ServerViewModel server)
    {
        if (server != null)
        {
            App.MainWindowInstance?.ContentFramePublic.Navigate(typeof(GroupServerDetailPage), server.Id);
        }
    }

    [RelayCommand]
    private async Task StartServerAsync(ServerViewModel server)
    {
        if (server?.LocalServer == null) return;
        try 
        {
             await _serverManager.StartServerAsync(server.LocalServer.Id);
        }
        catch { }
    }

    [RelayCommand]
    private async Task StopServerAsync(ServerViewModel server)
    {
        if (server?.LocalServer == null) return;
        try 
        {
             await _serverManager.StopServerAsync(server.LocalServer.Id);
        }
        catch { }
    }
    
    [RelayCommand]
    private async Task DeleteServerAsync(ServerViewModel server)
    {
        if (server?.LocalServer == null) return;
        
        // Simple confirmation
        var confirmDialog = new Microsoft.UI.Xaml.Controls.ContentDialog
        {
            Title = "确认删除",
            Content = $"确定要删除群组服务器 {server.Name} 吗？此操作不可恢复。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Close,
            XamlRoot = App.MainWindow.Content.XamlRoot
        };

        var result = await confirmDialog.ShowAsync();
        if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
        {
            try 
            {
                 await _serverManager.DeleteServerAsync(server.LocalServer.Id);
                 await LoadGroupServersAsync();
            }
            catch { }
        }
    }

    private void OnServerStatusChanged(object? sender, Services.ServerStatusEventArgs e)
    {
        App.MainWindow.DispatcherQueue.TryEnqueue(() =>
        {
            var server = GroupServers.FirstOrDefault(s => s.LocalServer?.Id == e.ServerId);
            if (server != null)
            {
                server.IsRunning = e.IsRunning;
            }
        });
    }
}
