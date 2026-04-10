using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Extensions.DependencyInjection;
using ZMSL.App.Models;
using ZMSL.App.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ZMSL.App.Views;

public sealed partial class CreateRemoteServerPage : Page
{
    private readonly LinuxNodeService _nodeService;
    private readonly ServerDownloadService _downloadService;
    private LinuxNode? _currentNode;
    private List<ServerDownloadService.ServerCoreCategory> _categories = new();
    private List<NodeJavaInfo>? _javaList;

    public CreateRemoteServerPage()
    {
        this.InitializeComponent();
        _nodeService = App.Services.GetRequiredService<LinuxNodeService>();
        _downloadService = App.Services.GetRequiredService<ServerDownloadService>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        
        if (e.Parameter is LinuxNode node)
        {
            _currentNode = node;
            NodeInfoText.Text = $"节点: {node.Name} ({node.Host}:{node.Port}) - {node.PlatformDisplayName}";
            await LoadServerCores();
            await LoadJavaListAsync();
        }
        else
        {
            Frame.GoBack();
        }
    }

    private async Task LoadServerCores()
    {
        try
        {
            // 加载服务端核心列表
            _categories = await _downloadService.GetServerCategoriesAsync();
            
            // 填充核心类型下拉框（从所有分类中提取核心名称）
            var coreTypes = _categories.SelectMany(c => c.Cores).Distinct().ToList();
            CoreTypeBox.ItemsSource = coreTypes;
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"加载服务端列表失败: {ex.Message}");
        }
    }

    private async Task LoadJavaListAsync()
    {
        if (_currentNode == null) return;
        
        try
        {
            JavaComboBox.IsEnabled = false;
            JavaPathText.Text = "正在加载 Java 列表...";
            
            _javaList = await _nodeService.ListJavaAsync(_currentNode);
            
            if (_javaList != null && _javaList.Count > 0)
            {
                // 显示 Java 版本列表
                var javaDisplayList = _javaList.Select(j => $"Java {j.Version}").ToList();
                JavaComboBox.ItemsSource = javaDisplayList;
                JavaComboBox.SelectedIndex = 0;
                JavaPathText.Text = $"检测到 {_javaList.Count} 个 Java 版本";
            }
            else
            {
                JavaComboBox.ItemsSource = new List<string> { "使用系统默认 Java" };
                JavaComboBox.SelectedIndex = 0;
                JavaPathBox.Text = "java";
                JavaPathText.Text = "未检测到 Java，将使用系统默认";
            }
        }
        catch (Exception ex)
        {
            JavaComboBox.ItemsSource = new List<string> { "使用系统默认 Java" };
            JavaComboBox.SelectedIndex = 0;
            JavaPathBox.Text = "java";
            JavaPathText.Text = $"加载失败: {ex.Message}";
        }
        finally
        {
            JavaComboBox.IsEnabled = true;
        }
    }

    private void JavaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (JavaComboBox.SelectedIndex >= 0 && _javaList != null && JavaComboBox.SelectedIndex < _javaList.Count)
        {
            var selected = _javaList[JavaComboBox.SelectedIndex];
            JavaPathBox.Text = selected.Path ?? "java";
            JavaPathText.Text = $"路径: {selected.Path}";
        }
        else
        {
            JavaPathBox.Text = "java";
        }
        CheckFormValid();
    }

    private async void RefreshJava_Click(object sender, RoutedEventArgs e)
    {
        await LoadJavaListAsync();
    }

    private async void CoreType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CoreTypeBox.SelectedItem is string coreType)
        {
            try
            {
                // 加载该核心的版本列表
                var versions = await _downloadService.GetAvailableVersionsAsync(coreType);
                VersionBox.ItemsSource = versions;
                VersionBox.IsEnabled = versions.Count > 0;
                if (versions.Count > 0)
                {
                    VersionBox.SelectedIndex = 0;
                }
                CheckFormValid();
            }
            catch (Exception ex)
            {
                await ShowErrorDialog($"加载版本列表失败: {ex.Message}");
            }
        }
    }

    private void FormField_Changed(object sender, RoutedEventArgs e)
    {
        CheckFormValid();
    }

    private void FormField_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        CheckFormValid();
    }

    private void AutoDownload_Changed(object sender, RoutedEventArgs e)
    {
        CheckFormValid();
    }

    private void CheckFormValid()
    {
        // 避免在初始化期间调用，此时控件可能为 null
        if (ServerNameBox == null || CoreTypeBox == null || VersionBox == null || 
            JavaPathBox == null || MinMemoryBox == null || MaxMemoryBox == null || 
            PortBox == null || CreateButton == null)
        {
            return;
        }

        bool isValid = !string.IsNullOrWhiteSpace(ServerNameBox.Text) &&
                      CoreTypeBox.SelectedItem != null &&
                      VersionBox.SelectedItem != null &&
                      !string.IsNullOrWhiteSpace(JavaPathBox.Text) &&
                      MinMemoryBox.Value > 0 &&
                      MaxMemoryBox.Value >= MinMemoryBox.Value &&
                      PortBox.Value > 0;

        CreateButton.IsEnabled = isValid;
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNode == null) return;

        // 显示加载遮罩
        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingText.Text = "正在连接节点...";

        try
        {
            var coreType = CoreTypeBox.SelectedItem as string ?? "";
            var version = VersionBox.SelectedItem as string ?? "";
            var jarFileName = $"{coreType.ToLower()}-{version}.jar";

            // 获取下载链接（如果启用自动下载）
            string? downloadUrl = null;
            if (AutoDownloadCheckBox.IsChecked == true)
            {
                LoadingText.Text = "正在获取下载链接...";
                downloadUrl = await GetDownloadUrlAsync(coreType, version);
            }

            var request = new CreateServerRequest
            {
                Name = ServerNameBox.Text,
                CoreType = coreType,
                MinecraftVersion = version,
                JarFileName = jarFileName,
                JavaPath = JavaPathBox.Text,
                MinMemoryMB = (int)MinMemoryBox.Value,
                MaxMemoryMB = (int)MaxMemoryBox.Value,
                JvmArgs = JvmArgsBox.Text,
                Port = (int)PortBox.Value,
                DownloadUrl = downloadUrl
            };

            LoadingText.Text = "正在创建服务器...";
            var (success, message, serverId) = await _nodeService.CreateServerAsync(_currentNode, request);

            if (success)
            {
                LoadingText.Text = "服务器创建成功！";
                await Task.Delay(500);

                // 显示成功对话框
                var dialog = new ContentDialog
                {
                    Title = "创建成功",
                    Content = $"服务器 '{request.Name}' 已在节点上创建成功！\n\n{(downloadUrl != null ? "核心正在后台下载中，请稍后查看状态。" : "请手动上传服务端核心文件。")}",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();

                // 返回节点详情页
                Frame.GoBack();
            }
            else
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                await ShowErrorDialog(message);
            }
        }
        catch (Exception ex)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            await ShowErrorDialog($"创建失败: {ex.Message}");
        }
    }

    private async Task<string?> GetDownloadUrlAsync(string coreType, string version)
    {
        try
        {
            var downloadData = await _downloadService.GetDownloadUrlAsync(coreType, version);
            return downloadData?.Url;
        }
        catch
        {
            return null;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
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
