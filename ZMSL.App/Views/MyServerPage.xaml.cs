using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using ZMSL.App.Models;
using ZMSL.App.Services;

namespace ZMSL.App.Views;

public sealed partial class MyServerPage : Page
{
    private readonly ServerManagerService _serverManager;
    private readonly DatabaseService _db;
    private readonly ViewModels.MyServerViewModel _viewModel;

    public MyServerPage()
    {
        this.InitializeComponent();
        _serverManager = App.Services.GetRequiredService<ServerManagerService>();
        _db = App.Services.GetRequiredService<DatabaseService>();
        _viewModel = App.Services.GetRequiredService<ViewModels.MyServerViewModel>();
        DataContext = _viewModel;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadServersAsync();
        
        var emptyPanel = this.FindName("EmptyPanel") as StackPanel;
        if (emptyPanel != null)
        {
            emptyPanel.Visibility = _viewModel.Servers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 导入服务器按钮点击
    /// </summary>
    private async void ImportServer_Click(object sender, RoutedEventArgs e)
    {
        // 创建文件夹选择器
        var folderPicker = new FolderPicker();
        folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        folderPicker.FileTypeFilter.Add("*");

        // 获取窗口句柄
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder == null) return;

        var serverPath = folder.Path;

        // 检查是否是有效的服务器目录
        var validationResult = await _serverManager.ValidateServerDirectoryAsync(serverPath);
        if (!validationResult.IsValid)
        {
            var errorDialog = new ContentDialog
            {
                Title = "无效的服务器目录",
                Content = validationResult.Message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await errorDialog.ShowAsync();
            return;
        }

        // 显示导入配置对话框
        var importDialog = new ContentDialog
        {
            Title = "导入服务器",
            XamlRoot = this.XamlRoot,
            PrimaryButtonText = "导入",
            CloseButtonText = "取消"
        };

        var dialogContent = new StackPanel { Spacing = 16, MinWidth = 400 };

        // 服务器名称
        var nameBox = new TextBox
        {
            Header = "服务器名称",
            Text = validationResult.SuggestedName ?? System.IO.Path.GetFileName(serverPath),
            PlaceholderText = "输入服务器名称"
        };
        dialogContent.Children.Add(nameBox);

        // 检测到的信息
        var infoPanel = new StackPanel { Spacing = 4 };
        infoPanel.Children.Add(new TextBlock
        {
            Text = "检测到的服务器信息:",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        infoPanel.Children.Add(new TextBlock
        {
            Text = $"核心类型: {validationResult.DetectedCoreType}",
            Opacity = 0.8
        });
        infoPanel.Children.Add(new TextBlock
        {
            Text = $"Minecraft 版本: {validationResult.DetectedMcVersion}",
            Opacity = 0.8
        });
        infoPanel.Children.Add(new TextBlock
        {
            Text = $"JAR 文件: {validationResult.DetectedJarFile}",
            Opacity = 0.8
        });
        dialogContent.Children.Add(infoPanel);

        // 内存配置
        var memoryPanel = new StackPanel { Spacing = 8 };
        memoryPanel.Children.Add(new TextBlock { Text = "内存配置", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });

        var minMemoryBox = new NumberBox
        {
            Header = "最小内存 (MB)",
            Value = 1024,
            Minimum = 512,
            Maximum = 32768,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        var maxMemoryBox = new NumberBox
        {
            Header = "最大内存 (MB)",
            Value = 2048,
            Minimum = 512,
            Maximum = 32768,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        memoryPanel.Children.Add(minMemoryBox);
        memoryPanel.Children.Add(maxMemoryBox);
        dialogContent.Children.Add(memoryPanel);

        // 端口配置
        var portBox = new NumberBox
        {
            Header = "服务器端口",
            Value = validationResult.DetectedPort > 0 ? validationResult.DetectedPort : 25565,
            Minimum = 1,
            Maximum = 65535,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };
        dialogContent.Children.Add(portBox);

        importDialog.Content = dialogContent;

        var result = await importDialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        // 验证服务器名称
        var serverName = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(serverName))
        {
            var errorDialog = new ContentDialog
            {
                Title = "错误",
                Content = "服务器名称不能为空",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await errorDialog.ShowAsync();
            return;
        }

        // 检查名称是否已存在
        if (await _serverManager.IsServerNameExistsAsync(serverName))
        {
            var errorDialog = new ContentDialog
            {
                Title = "错误",
                Content = $"服务器名称 '{serverName}' 已存在",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await errorDialog.ShowAsync();
            return;
        }

        // 创建服务器记录
        var importResult = await _serverManager.ImportServerAsync(
            serverName,
            serverPath,
            validationResult.DetectedJarFile ?? "",
            validationResult.DetectedCoreType ?? "unknown",
            validationResult.DetectedMcVersion ?? "",
            (int)minMemoryBox.Value,
            (int)maxMemoryBox.Value,
            (int)portBox.Value
        );

        if (importResult.Success)
        {
            // 刷新列表
            await _viewModel.LoadServersAsync();
            EmptyPanel.Visibility = _viewModel.Servers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            var successDialog = new ContentDialog
            {
                Title = "导入成功",
                Content = $"服务器 '{serverName}' 已成功导入",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await successDialog.ShowAsync();
        }
        else
        {
            var errorDialog = new ContentDialog
            {
                Title = "导入失败",
                Content = importResult.Message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await errorDialog.ShowAsync();
        }
    }

    /// <summary>
    /// 插件按钮点击
    /// </summary>
    private async void PluginButton_Click(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("=== 插件按钮被点击 ===");
        System.Diagnostics.Debug.WriteLine($"sender类型: {sender?.GetType().Name}");

        if (sender is Button button)
        {
            System.Diagnostics.Debug.WriteLine($"Button.Tag: {button.Tag} (类型: {button.Tag?.GetType().Name})");
            System.Diagnostics.Debug.WriteLine($"Button.DataContext: {button.DataContext} (类型: {button.DataContext?.GetType().Name})");

            // 优先使用Tag，其次使用DataContext
            var serverViewModel = button.Tag as ServerViewModel ?? button.DataContext as ServerViewModel;

            if (serverViewModel != null)
            {
                if (serverViewModel.LocalServer != null)
                {
                    // 本地服务器
                    System.Diagnostics.Debug.WriteLine($"导航到本地插件管理页, 服务器: {serverViewModel.Name}");
                    Frame.Navigate(typeof(PluginManagerPage), serverViewModel.LocalServer);
                }
                else if (serverViewModel.RemoteServer != null)
                {
                    // 远程服务器
                    System.Diagnostics.Debug.WriteLine($"导航到远程插件管理页, 服务器: {serverViewModel.Name}");
                    var nodeService = App.Services.GetRequiredService<Services.LinuxNodeService>();
                    var nodes = await nodeService.GetNodesAsync();
                    var node = nodes.FirstOrDefault(n => n.Id == serverViewModel.NodeId);

                    if (node != null)
                    {
                        Frame.Navigate(typeof(RemotePluginManagerPage), (node, serverViewModel.RemoteServer));
                    }
                    else
                    {
                        var dialog = new ContentDialog
                        {
                            Title = "错误",
                            Content = "找不到对应的节点信息",
                            CloseButtonText = "确定",
                            XamlRoot = this.XamlRoot
                        };
                        await dialog.ShowAsync();
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("错误: serverViewModel为null!");
                var dialog = new ContentDialog
                {
                    Title = "调试信息",
                    Content = $"Tag: {button.Tag}\nDataContext: {button.DataContext}",
                    CloseButtonText = "确定",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"sender不是Button: {sender?.GetType().Name}");
        }
    }

    /// <summary>
    /// 导航到服务器详情页
    /// </summary>
    private async void NavigateToServer(ServerViewModel serverViewModel)
    {
        if (serverViewModel.Type == "Local" && serverViewModel.LocalServer != null)
        {
            Frame.Navigate(typeof(ServerDetailPage), serverViewModel.LocalServer);
        }
        else if (serverViewModel.Type == "Remote" && serverViewModel.RemoteServer != null)
        {
            var nodeService = App.Services.GetRequiredService<Services.LinuxNodeService>();
            var nodes = await nodeService.GetNodesAsync();
            var node = nodes.FirstOrDefault(n => n.Id == serverViewModel.NodeId);
            if (node != null)
            {
                Frame.Navigate(typeof(RemoteServerDetailPage), (node, serverViewModel.RemoteServer));
            }
        }
    }

    /// <summary>
    /// 服务器项点击 - 导航到详情页或切换选择状态
    /// </summary>
    private void ServerItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ServerViewModel serverViewModel)
        {
            if (_viewModel.IsSelectionMode)
            {
                serverViewModel.IsSelected = !serverViewModel.IsSelected;
            }
            else
            {
                NavigateToServer(serverViewModel);
            }
        }
    }

    /// <summary>
    /// 管理按钮点击
    /// </summary>
    private void ManageServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && (btn.Tag as ServerViewModel ?? btn.DataContext as ServerViewModel) is ServerViewModel vm)
        {
            NavigateToServer(vm);
        }
    }

    /// <summary>
    /// 启动服务器按钮点击
    /// </summary>
    private async void StartServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && (btn.Tag as ServerViewModel ?? btn.DataContext as ServerViewModel) is ServerViewModel vm)
        {
            // 如果已经在运行，则不做操作
            if (vm.IsRunning) return;

            // 调用 ViewModel 的 StartServer 方法需要先选中该服务器
            _viewModel.SelectedServer = vm;
            await _viewModel.StartServerCommand.ExecuteAsync(null);
        }
    }

    /// <summary>
    /// 停止服务器按钮点击
    /// </summary>
    private async void StopServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && (btn.Tag as ServerViewModel ?? btn.DataContext as ServerViewModel) is ServerViewModel vm)
        {
            // 如果未运行，则不做操作
            if (!vm.IsRunning) return;

            // 调用 ViewModel 的 StopServer 方法需要先选中该服务器
            _viewModel.SelectedServer = vm;
            await _viewModel.StopServerCommand.ExecuteAsync(null);
        }
    }

    private void NewServer_Click(object sender, RoutedEventArgs e)
    {
        // 跳转到新的模式选择页面
        Frame.Navigate(typeof(CreateModeSelectionPage));
    }

    /// <summary>
    /// 加载服务器自定义图标。优先使用服务器目录下的 server-icon.png，其次是数据库存储的 IconPath。
    /// </summary>
    private void ServerIcon_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.Controls.Image img) return;

        // Tag 绑定了 LocalServer.IconPath，但我们还需要拿到 ServerPath
        // 通过 DataContext (ServerViewModel) 获取 LocalServer
        ServerViewModel? vm = null;
        DependencyObject? current = img;
        while (current != null)
        {
            if (current is FrameworkElement fe && fe.DataContext is ServerViewModel svm)
            {
                vm = svm;
                break;
            }
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }

        string? iconPath = null;

        // 优先检查服务器目录下的 server-icon.png
        if (vm?.LocalServer?.ServerPath is string serverPath)
        {
            var serverIconPath = System.IO.Path.Combine(serverPath, "server-icon.png");
            if (System.IO.File.Exists(serverIconPath))
                iconPath = serverIconPath;
        }

        // 降级到数据库存储的 IconPath
        if (iconPath == null && vm?.LocalServer?.IconPath is string dbPath && System.IO.File.Exists(dbPath))
            iconPath = dbPath;

        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            try
            {
                img.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPath));
                img.Visibility = Visibility.Visible;

                // 隐藏同层的默认 FontIcon
                if (img.Parent is Grid grid)
                {
                    foreach (var child in grid.Children)
                    {
                        if (child is FontIcon fi)
                            fi.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch
            {
                img.Visibility = Visibility.Collapsed;
            }
        }
        else
        {
            img.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 批量操作按钮点击
    /// </summary>
    private void BatchButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleSelectionModeCommand.Execute(null);
    }
}
