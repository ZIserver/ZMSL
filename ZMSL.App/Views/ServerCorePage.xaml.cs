using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZMSL.App.Services;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System;

namespace ZMSL.App.Views;

public sealed partial class ServerCorePage : Page
{
    private readonly ServerDownloadService _downloadService;
    private CancellationTokenSource? _cts;
    private string? _selectedCoreName;
    private string? _selectedCoreDisplayName;
    private string? _selectedCoreCategory;
    private string? _selectedCoreDescription;
    private string? _selectedVersion;

    public ServerCorePage()
    {
        this.InitializeComponent();
        _downloadService = App.Services.GetRequiredService<ServerDownloadService>();
        _downloadService.DownloadProgress += OnDownloadProgress;
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
            
            System.Diagnostics.Debug.WriteLine($"[ServerCorePage] 使用镜像源: {mirrorSource}");
            _downloadService.SetMirrorSource(mirrorSource);
            
            // 根据镜像源更新 UI
            UpdateMirrorSourceUI(mirrorSource);
            
            System.Diagnostics.Debug.WriteLine($"[ServerCorePage] 正在获取服务端分类...");
            var categories = await _downloadService.GetServerCategoriesAsync();
            
            if (categories == null || categories.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[ServerCorePage] API 返回空分类列表");
                throw new Exception("API 返回空分类列表");
            }
            
            System.Diagnostics.Debug.WriteLine($"[ServerCorePage] 获取到 {categories.Count} 个分类");
            
            var flattenedCores = new List<ServerDownloadService.ServerCoreItem>();

            foreach (var category in categories)
            {
                System.Diagnostics.Debug.WriteLine($"[ServerCorePage] 分类: {category.DisplayName}, 核心数: {category.Cores.Count}");
                
                foreach (var coreName in category.Cores)
                {
                    // 简单的格式化: paper -> Paper, arclight-forge -> Arclight-Forge
                    var displayName = FormatCoreName(coreName);
                    
                    flattenedCores.Add(new ServerDownloadService.ServerCoreItem
                    {
                        CoreName = coreName,
                        DisplayName = displayName,
                        Category = category.DisplayName,
                        Description = $"{displayName} 是一个{category.DisplayName}。" // 简单的默认描述
                    });
                }
            }
            
            if (flattenedCores.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[ServerCorePage] 没有获取到任何核心");
                throw new Exception("没有获取到任何核心");
            }
            
            System.Diagnostics.Debug.WriteLine($"[ServerCorePage] 总共加载 {flattenedCores.Count} 个核心");
            
            CoreListView.ItemsSource = flattenedCores;
            
            // 默认选中第一个
            if (flattenedCores.Count > 0)
            {
                CoreListView.SelectedIndex = 0;
            }
        }
        catch (Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"[ServerCorePage] API加载失败: {ex.Message}\n{ex.StackTrace}");
             
             // 显示错误信息给用户
             var errorDialog = new ContentDialog
             {
                 Title = "加载服务端列表失败",
                 Content = $"无法从 API 获取服务端列表。\n\n错误: {ex.Message}\n\n请检查网络连接或镜像源设置。",
                 CloseButtonText = "关闭",
                 XamlRoot = Content.XamlRoot
             };
             await errorDialog.ShowAsync();
             
             // 不使用硬编码的推荐列表，保持空列表
             CoreListView.ItemsSource = new List<ServerDownloadService.ServerCoreItem>();
        }
    }

    private void UpdateMirrorSourceUI(string mirrorSource)
    {
        // 根据镜像源更新标题栏的提示
        var titleBar = this.FindName("TitleBar") as Grid;
        var mslLink = this.FindName("MSLLink") as HyperlinkButton;
        
        if (mslLink != null)
        {
            // 如果选择的是 ZSync，隐藏 MSL 的链接
            mslLink.Visibility = (mirrorSource == "ZSync") ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private string FormatCoreName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        
        // 特殊处理
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

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedCoreName) || string.IsNullOrEmpty(_selectedVersion))
        {
            return;
        }
        
        await DownloadServerCore(
            _selectedCoreName, 
            _selectedCoreDisplayName ?? _selectedCoreName, 
            _selectedVersion
        );
    }

    private async System.Threading.Tasks.Task DownloadServerCore(string coreName, string displayName, string version)
    {
        try
        {
            DownloadPanel.Visibility = Visibility.Visible;
            DownloadStatusText.Text = $"准备下载 {displayName} {version}...";
            DownloadProgressBar.Value = 0;
            
            _cts = new CancellationTokenSource();
            
            // 选择下载位置
            var folderPicker = new Windows.Storage.Pickers.FolderPicker();
            folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
            folderPicker.FileTypeFilter.Add("*");
            
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
            
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder == null)
            {
                DownloadPanel.Visibility = Visibility.Collapsed;
                return;
            }
            
            // 开始下载
            var filePath = await _downloadService.DownloadServerCoreAsync(
                coreName, 
                version, 
                folder.Path,
                "latest",
                _cts.Token
            );
            
            DownloadPanel.Visibility = Visibility.Collapsed;
            
            // 显示成功消息
            var successDialog = new ContentDialog
            {
                Title = "下载完成",
                Content = $"{displayName} {version} 已下载到:\n{filePath}",
                CloseButtonText = "关闭",
                XamlRoot = Content.XamlRoot
            };
            await successDialog.ShowAsync();
        }
        catch (System.OperationCanceledException)
        {
            DownloadPanel.Visibility = Visibility.Collapsed;
            DownloadStatusText.Text = "下载已取消";
        }
        catch (System.Exception ex)
        {
            DownloadPanel.Visibility = Visibility.Collapsed;
            
            var errorDialog = new ContentDialog
            {
                Title = "下载失败",
                Content = $"错误: {ex.Message}",
                CloseButtonText = "关闭",
                XamlRoot = Content.XamlRoot
            };
            await errorDialog.ShowAsync();
        }
    }

    private void OnDownloadProgress(object? sender, DownloadProgressEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            DownloadStatusText.Text = $"正在下载: {e.FileName} - {e.DownloadedBytes / 1024.0 / 1024.0:F2}MB / {e.TotalBytes / 1024.0 / 1024.0:F2}MB ({e.Progress:F1}%)";
            DownloadProgressBar.Value = e.Progress;
        });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }
}
