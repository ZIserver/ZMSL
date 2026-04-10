using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZMSL.App.Models;
using ZMSL.App.Services;

namespace ZMSL.App.Views;

public sealed partial class BackupManagerPage : Page
{
    private readonly BackupService _backupService;
    private readonly ServerManagerService _serverManager;
    private LocalServer? _server;

    public BackupManagerPage()
    {
        this.InitializeComponent();
        _backupService = App.Services.GetRequiredService<BackupService>();
        _serverManager = App.Services.GetRequiredService<ServerManagerService>();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is LocalServer server)
        {
            _server = server;
            ServerNameText.Text = server.Name;
        }
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        LoadBackups();
    }

    private async void LoadBackups()
    {
        if (_server == null) return;

        try
        {
            var backups = await _backupService.GetServerBackupsAsync(_server.Id);
            
            var displayItems = backups.Select(b => new BackupDisplayItem
            {
                Id = b.Id,
                CreatedAt = b.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                FileSizeBytes = FormatFileSize(b.FileSizeBytes),
                BackupPath = b.BackupPath,
                BackupData = b
            }).ToList();

            BackupItemsControl.ItemsSource = displayItems;
            NoBackupsText.Visibility = displayItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载备份列表失败: {ex.Message}");
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;

        var button = sender as Button;
        if (button != null)
        {
            button.IsEnabled = false;
            button.Content = "备份中...";
        }

        try
        {
            await _backupService.BackupServerAsync(_server.Id);
            LoadBackups();

            var dialog = new ContentDialog
            {
                Title = "备份成功",
                Content = $"服务器 {_server.Name} 已成功备份",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "备份失败",
                Content = $"备份失败: {ex.Message}",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
        finally
        {
            if (button != null)
            {
                button.IsEnabled = true;
                button.Content = "立即备份";
            }
        }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_server == null) return;
        if (sender is not Button btn || btn.Tag is not BackupDisplayItem item) return;

        // 检查服务器是否运行
        if (_serverManager.IsServerRunning(_server.Id))
        {
            var dialog = new ContentDialog
            {
                Title = "无法恢复",
                Content = "请先停止服务器后再进行恢复操作",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
            return;
        }

        var confirmDialog = new ContentDialog
        {
            Title = "确认恢复",
            Content = $"恢复备份将覆盖当前服务器数据，当前数据将被移动到 _old 文件夹\n\n备份时间: {item.CreatedAt}\n备份大小: {item.FileSizeBytes}\n\n是否继续？",
            PrimaryButtonText = "确认恢复",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await _backupService.RestoreBackupAsync(item.BackupData.Id, _server.ServerPath);

            var successDialog = new ContentDialog
            {
                Title = "恢复成功",
                Content = "备份已成功恢复",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await successDialog.ShowAsync();
        }
        catch (Exception ex)
        {
            var errorDialog = new ContentDialog
            {
                Title = "恢复失败",
                Content = $"恢复失败: {ex.Message}",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await errorDialog.ShowAsync();
        }
    }

    private async void DeleteBackup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BackupDisplayItem item) return;

        var confirmDialog = new ContentDialog
        {
            Title = "确认删除",
            Content = $"确定要删除这个备份吗？\n\n备份时间: {item.CreatedAt}\n备份大小: {item.FileSizeBytes}",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await _backupService.DeleteBackupAsync(item.BackupData.Id);
            LoadBackups();
        }
        catch (Exception ex)
        {
            var errorDialog = new ContentDialog
            {
                Title = "删除失败",
                Content = $"删除失败: {ex.Message}",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await errorDialog.ShowAsync();
        }
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private class BackupDisplayItem
    {
        public int Id { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string FileSizeBytes { get; set; } = string.Empty;
        public string BackupPath { get; set; } = string.Empty;
        public ServerBackup BackupData { get; set; } = null!;
    }
}
