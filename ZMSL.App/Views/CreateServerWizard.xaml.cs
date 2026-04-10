using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZMSL.App.Models;
using ZMSL.App.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ZMSL.App.Views;

public sealed partial class CreateServerWizard : Page
{
    private readonly ServerDownloadService _downloadService;
    private readonly ServerManagerService _serverManager;
    private readonly DatabaseService _db;
    
    // 高手模式变量
    private List<ServerDownloadService.ServerCoreCategory> _categories = new();
    private string? _selectedServerName;
    private List<string> _selectedVersions = new();
    private string? _selectedVersion;

    public LocalServer? CreatedServer { get; private set; }

    public CreateServerWizard()
    {
        this.InitializeComponent();
        _downloadService = App.Services.GetRequiredService<ServerDownloadService>();
        _serverManager = App.Services.GetRequiredService<ServerManagerService>();
        _db = App.Services.GetRequiredService<DatabaseService>();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _categories = await _downloadService.GetServerCategoriesAsync();
            
            // 构造核心类型列表
            var coreData = _categories.SelectMany(cat => 
                cat.Cores.Select(core => new 
                { 
                    Name = core,
                    DisplayName = core,
                    Description = $"{cat.DisplayName} - {core}"
                })
            ).ToList();
            
            CoreTypesPanel.ItemsSource = coreData;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CreateServer] 加载分类失败: {ex.Message}");
        }
    }

    private async void CoreType_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string coreName)
        {
            _selectedServerName = coreName;
            
            // 更新标题显示
            SelectedCoreText.Text = $"{coreName} 可用版本";
            
            try
            {
                // 隐藏空状态提示
                EmptyVersionHint.Visibility = Visibility.Collapsed;
                
                _selectedVersions = await _downloadService.GetAvailableVersionsAsync(coreName);
                VersionListView.ItemsSource = _selectedVersions;
                
                // 清空之前的选择
                _selectedVersion = null;
                NextButton.IsEnabled = false;
            }
            catch (Exception ex)
            {
                await ShowErrorDialog($"加载版本失败: {ex.Message}");
            }
        }
    }

    private void Version_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string version)
        {
            _selectedVersion = version;
            NextButton.IsEnabled = true;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.GoBack();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.GoBack();
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        // 检查是否都已选择
        if (string.IsNullOrEmpty(_selectedServerName))
        {
            await ShowErrorDialog("请选择服务端核心");
            return;
        }
        if (string.IsNullOrEmpty(_selectedVersion))
        {
            await ShowErrorDialog("请选择版本");
            return;
        }
        
        await CreateServer();
    }

    private async Task CreateServer()
    {
        try
        {
            var serverName = "我的服务器";
            var serverPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                "ZMSL", "servers", serverName);
            Directory.CreateDirectory(serverPath);
            
            var jarPath = await _downloadService.DownloadServerCoreAsync(
                _selectedServerName!, 
                _selectedVersion!, 
                serverPath
            );
            
            CreatedServer = new LocalServer
            {
                Name = serverName,
                ServerPath = serverPath,
                CoreType = _selectedServerName!,
                CoreVersion = _selectedVersion!,
                JarFileName = Path.GetFileName(jarPath),
                MinMemoryMB = 1024,
                MaxMemoryMB = 4096,
                MinecraftVersion = _selectedVersion!,
                CreatedAt = DateTime.Now
            };
            
            await _serverManager.CreateServerAsync(CreatedServer);
            Frame.Navigate(typeof(ServerDetailPage), CreatedServer);
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"创建服务器失败: {ex.Message}");
        }
    }

    private async Task ShowErrorDialog(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "错误",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }
}