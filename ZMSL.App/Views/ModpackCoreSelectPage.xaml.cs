using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZMSL.App.Services;
using System.Diagnostics;

namespace ZMSL.App.Views;

/// <summary>
/// 整合包创建时的核心选择页面
/// </summary>
public sealed partial class ModpackCoreSelectPage : Page
{
    private readonly ServerDownloadService _downloadService;
    private readonly ServerManagerService _serverManager;
    private CancellationTokenSource? _cts;

    private string? _selectedCoreName;
    private string? _selectedCoreDisplayName;
    private string? _selectedCoreCategory;
    private string? _selectedCoreDescription;
    private string? _selectedVersion;

    // 从上一页传递过来的参数
    private ModpackCreateParams? _createParams;

    public ModpackCoreSelectPage()
    {
        this.InitializeComponent();
        _downloadService = App.Services.GetRequiredService<ServerDownloadService>();
        _serverManager = App.Services.GetRequiredService<ServerManagerService>();
        _downloadService.DownloadProgress += OnDownloadProgress;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is ModpackCreateParams createParams)
        {
            _createParams = createParams;
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // 从API加载服务端分类并展平
        try
        {
            // 应用镜像源设置
            var db = App.Services.GetRequiredService<Services.DatabaseService>();
            var settings = await db.GetSettingsAsync();
            var mirrorSource = settings.DownloadMirrorSource ?? "MSL";
            
            System.Diagnostics.Debug.WriteLine($"[ModpackCoreSelectPage] 使用镜像源: {mirrorSource}");
            _downloadService.SetMirrorSource(mirrorSource);
            
            System.Diagnostics.Debug.WriteLine($"[ModpackCoreSelectPage] 正在获取服务端分类...");
            var categories = await _downloadService.GetServerCategoriesAsync();
            
            if (categories == null || categories.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[ModpackCoreSelectPage] API 返回空分类列表");
                throw new Exception("API 返回空分类列表");
            }
            
            System.Diagnostics.Debug.WriteLine($"[ModpackCoreSelectPage] 获取到 {categories.Count} 个分类");
            
            var flattenedCores = new List<ServerDownloadService.ServerCoreItem>();

            foreach (var category in categories)
            {
                System.Diagnostics.Debug.WriteLine($"[ModpackCoreSelectPage] 分类: {category.DisplayName}, 核心数: {category.Cores.Count}");
                
                foreach (var coreName in category.Cores)
                {
                    var displayName = FormatCoreName(coreName);
                    
                    flattenedCores.Add(new ServerDownloadService.ServerCoreItem
                    {
                        CoreName = coreName,
                        DisplayName = displayName,
                        Category = category.DisplayName,
                        Description = $"{displayName} 是一个{category.DisplayName}。"
                    });
                }
            }
            
            if (flattenedCores.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[ModpackCoreSelectPage] 没有获取到任何核心");
                throw new Exception("没有获取到任何核心");
            }
            
            System.Diagnostics.Debug.WriteLine($"[ModpackCoreSelectPage] 总共加载 {flattenedCores.Count} 个核心");
            
            CoreListView.ItemsSource = flattenedCores;
            
            // 默认选中第一个
            if (flattenedCores.Count > 0)
            {
                CoreListView.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ModpackCoreSelectPage] API加载失败: {ex.Message}\n{ex.StackTrace}");
            
            // 显示错误信息给用户
            var errorDialog = new ContentDialog
            {
                Title = "加载服务端列表失败",
                Content = $"无法从 API 获取服务端列表。\n\n错误: {ex.Message}\n\n请检查网络连接或镜像源设置。",
                CloseButtonText = "关闭",
                XamlRoot = this.XamlRoot
            };
            await errorDialog.ShowAsync();
            
            // 不使用硬编码的推荐列表，保持空列表
            CoreListView.ItemsSource = new List<ServerDownloadService.ServerCoreItem>();
        }

        // 显示整合包信息
        if (_createParams != null)
        {
            ModpackNameText.Text = $"整合包: {_createParams.ModpackName}";
            ModpackServerNameText.Text = $"服务器名称: {_createParams.ServerName}";
        }
    }

    private string FormatCoreName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        
        if (name.Contains("-"))
        {
            var parts = name.Split('-');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                    parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
            }
            return string.Join("-", parts);
        }
        
        return char.ToUpper(name[0]) + name.Substring(1);
    }

    private async void CoreListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CoreListView.SelectedItem is ServerDownloadService.ServerCoreItem coreItem)
        {
            _selectedCoreName = coreItem.CoreName;
            _selectedCoreDisplayName = coreItem.DisplayName;
            _selectedCoreCategory = coreItem.Category;
            _selectedCoreDescription = coreItem.Description;

            // 获取版本列表
            var versions = await _downloadService.GetAvailableVersionsAsync(coreItem.CoreName);

            if (versions != null && versions.Count > 0)
            {
                // 只显示前20个版本
                VersionItemsControl.ItemsSource = versions.Take(20).ToList();
            }
            else
            {
                VersionItemsControl.ItemsSource = new List<string> { "无可用版本" };
            }

            // 隐藏信息卡片，显示空状态
            ServerInfoPanel.Visibility = Visibility.Collapsed;
            EmptyInfoHint.Visibility = Visibility.Visible;
        }
    }

    private void Version_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string version)
        {
            _selectedVersion = version;

            // 更新服务端信息卡片
            ServerNameText.Text = _selectedCoreDisplayName ?? "未知";
            ServerCategoryText.Text = _selectedCoreCategory ?? "未知";
            ServerVersionText.Text = version;
            ServerDescriptionText.Text = _selectedCoreDescription ?? "";

            // 显示信息卡片，隐藏空状态
            ServerInfoPanel.Visibility = Visibility.Visible;
            EmptyInfoHint.Visibility = Visibility.Collapsed;
        }
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (_createParams == null)
        {
            await ShowDialogAsync("错误", "缺少整合包参数");
            return;
        }

        if (string.IsNullOrEmpty(_selectedCoreName) || string.IsNullOrEmpty(_selectedVersion))
        {
            await ShowDialogAsync("错误", "请选择核心和版本");
            return;
        }

        CreateButton.IsEnabled = false;
        DownloadPanel.Visibility = Visibility.Visible;
        DownloadStatusText.Text = "正在创建服务器...";
        DownloadProgressBar.IsIndeterminate = true;

        _cts = new CancellationTokenSource();

        try
        {
            // 创建服务器
            var result = await _serverManager.CreateServerFromModpackWithCoreAsync(
                _createParams.ServerName,
                _createParams.ModpackPath,
                _createParams.MinMemory,
                _createParams.MaxMemory,
                _createParams.Port,
                _createParams.SaveClientPack,
                _selectedCoreName,
                _selectedVersion,
                _cts.Token,
                (status, progress) =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        DownloadStatusText.Text = status;
                        if (progress >= 0)
                        {
                            DownloadProgressBar.IsIndeterminate = false;
                            DownloadProgressBar.Value = progress;
                        }
                        else
                        {
                            DownloadProgressBar.IsIndeterminate = true;
                        }
                    });
                });

            DownloadPanel.Visibility = Visibility.Collapsed;

            if (result.Success)
            {
                await ShowDialogAsync("成功", $"服务器 \"{_createParams.ServerName}\" 创建成功！");
                // 返回到服务器列表
                if (Frame.CanGoBack)
                {
                    // 返回两次（跳过整合包选择页）
                    Frame.GoBack();
                    if (Frame.CanGoBack)
                    {
                        Frame.GoBack();
                    }
                }
            }
            else
            {
                await ShowDialogAsync("创建失败", result.Message);
            }
        }
        catch (OperationCanceledException)
        {
            DownloadPanel.Visibility = Visibility.Collapsed;
            DownloadStatusText.Text = "已取消";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"创建服务器失败: {ex.Message}");
            DownloadPanel.Visibility = Visibility.Collapsed;
            await ShowDialogAsync("错误", $"创建服务器失败: {ex.Message}");
        }
        finally
        {
            CreateButton.IsEnabled = true;
            DownloadProgressBar.IsIndeterminate = false;
        }
    }

    private void OnDownloadProgress(object? sender, DownloadProgressEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            DownloadStatusText.Text = $"正在下载: {e.FileName} - {e.DownloadedBytes / 1024.0 / 1024.0:F2}MB / {e.TotalBytes / 1024.0 / 1024.0:F2}MB ({e.Progress:F1}%)";
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = e.Progress;
        });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private async Task ShowDialogAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }
}

/// <summary>
/// 整合包创建参数
/// </summary>
public class ModpackCreateParams
{
    public string ServerName { get; set; } = string.Empty;
    public string ModpackName { get; set; } = string.Empty;
    public string ModpackPath { get; set; } = string.Empty;
    public int MinMemory { get; set; }
    public int MaxMemory { get; set; }
    public int Port { get; set; }
    public bool SaveClientPack { get; set; }
}
