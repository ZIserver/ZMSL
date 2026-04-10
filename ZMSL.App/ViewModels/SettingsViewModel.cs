using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZMSL.App.Models;

namespace ZMSL.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly Services.DatabaseService _db;
    private readonly Services.ServerManagerService _serverManager;
    private readonly Services.BackupService _backupService;

    [ObservableProperty]
    public partial AppSettings Settings { get; set; } = new();

    [ObservableProperty]
    public partial string? DetectedJavaPath { get; set; }

    [ObservableProperty]
    public partial bool IsSaving { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    public SettingsViewModel(Services.DatabaseService db, Services.ServerManagerService serverManager, Services.BackupService backupService)
    {
        _db = db;
        _serverManager = serverManager;
        _backupService = backupService;
    }

    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        Settings = await _db.GetSettingsAsync();
        DetectedJavaPath = await _serverManager.DetectJavaAsync();
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        IsSaving = true;
        try
        {
            await _db.SaveSettingsAsync(Settings);
            
            // 重启备份服务以应用新设置
            _backupService.Stop();
            await _backupService.StartAsync();
            
            StatusMessage = "设置已保存";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    [RelayCommand]
    private void UseDetectedJava()
    {
        if (!string.IsNullOrEmpty(DetectedJavaPath))
        {
            Settings.DefaultJavaPath = DetectedJavaPath;
            OnPropertyChanged(nameof(Settings));
        }
    }

    [RelayCommand]
    private async Task BrowseServerPathAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            Settings.DefaultServerPath = folder.Path;
            OnPropertyChanged(nameof(Settings));
        }
    }

    [RelayCommand]
    private async Task BrowseJavaPathAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
        picker.FileTypeFilter.Add(".exe");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            Settings.DefaultJavaPath = file.Path;
            OnPropertyChanged(nameof(Settings));
        }
    }
}
