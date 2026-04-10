using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.DependencyInjection;
using ZMSL.App.Services;
using ZMSL.App.ViewModels;
using ZMSL.App.Models;
using System;
using System.Threading.Tasks;

namespace ZMSL.App.Views
{
    public sealed partial class LinuxNodePage : Page
    {
        private readonly LinuxNodeService _nodeService;
        public LinuxNodeViewModel ViewModel => _viewModel;
        private readonly LinuxNodeViewModel _viewModel;

        public LinuxNodePage()
        {
            this.InitializeComponent();
            _nodeService = App.Services.GetRequiredService<LinuxNodeService>();
            _viewModel = App.Services.GetRequiredService<LinuxNodeViewModel>();
            Loaded += OnPageLoaded;
        }

        private async void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            await LoadNodesAsync();
        }

        private async Task LoadNodesAsync()
        {
            LoadingRing.IsActive = true;

            try
            {
                await _viewModel.LoadNodesAsync();
                
                EmptyState.Visibility = _viewModel.Nodes.Count == 0 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载节点异常: {ex}");
                await ShowMessageAsync("加载节点失败", $"加载节点时发生错误: {ex.Message}");
            }
            finally
            {
                LoadingRing.IsActive = false;
            }
        }

        private async void TestConnection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Control control && control.DataContext is LinuxNode node)
            {
                control.IsEnabled = false;
                try
                {
                    var (success, message) = await _nodeService.TestConnectionAsync(node);
                    await ShowMessageAsync(success ? "连接成功" : "连接失败", message);
                    await LoadNodesAsync();
                }
                finally
                {
                    control.IsEnabled = true;
                }
            }
        }

        private void ManageNode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is LinuxNode node)
            {
                Frame.Navigate(typeof(LinuxNodeDetailPage), node);
            }
        }

        private async void EditNode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is LinuxNode node)
            {
                var dialog = new ContentDialog
                {
                    Title = "编辑节点",
                    PrimaryButtonText = "保存",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot
                };

                var panel = new StackPanel { Spacing = 12 };
                var nameBox = new TextBox { Header = "节点名称", Text = node.Name };
                panel.Children.Add(nameBox);
                var hostBox = new TextBox { Header = "主机地址", Text = node.Host };
                panel.Children.Add(hostBox);
                var portBox = new NumberBox { Header = "端口", Value = node.Port, Minimum = 1, Maximum = 65535 };
                panel.Children.Add(portBox);
                var tokenBox = new TextBox { Header = "Token", Text = node.Token };
                panel.Children.Add(tokenBox);

                dialog.Content = panel;

                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    node.Name = nameBox.Text;
                    node.Host = hostBox.Text;
                    node.Port = (int)portBox.Value;
                    node.Token = tokenBox.Text;

                    var (success, message) = await _viewModel.UpdateNodeAsync(node);
                    await ShowMessageAsync(success ? "成功" : "失败", message);
                }
            }
        }

        private async void DeleteNode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is LinuxNode node)
            {
                var confirmDialog = new ContentDialog
                {
                    Title = "确认删除",
                    Content = $"确定要删除节点 {node.Name} 吗？\n删除后无法恢复。",
                    PrimaryButtonText = "删除",
                    CloseButtonText = "取消",
                    XamlRoot = this.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                var result = await confirmDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    var (success, message) = await _viewModel.DeleteNodeAsync(node);
                    await ShowMessageAsync(success ? "成功" : "失败", message);
                }
            }
        }

        private async void AddNode_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "添加节点",
                PrimaryButtonText = "添加",
                CloseButtonText = "取消",
                XamlRoot = this.XamlRoot
            };

            var panel = new StackPanel { Spacing = 12 };
            
            // 平台选择
            var platformLabel = new TextBlock { Text = "节点平台", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            panel.Children.Add(platformLabel);
            var platformPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            var linuxRadio = new RadioButton { Content = "Linux", IsChecked = true, GroupName = "Platform" };
            var windowsRadio = new RadioButton { Content = "Windows", GroupName = "Platform" };
            platformPanel.Children.Add(linuxRadio);
            platformPanel.Children.Add(windowsRadio);
            panel.Children.Add(platformPanel);
            
            var nameBox = new TextBox { Header = "节点名称", PlaceholderText = "例如: 生产服务器" };
            panel.Children.Add(nameBox);
            var hostBox = new TextBox { Header = "主机地址", PlaceholderText = "例如: 192.168.1.100" };
            panel.Children.Add(hostBox);
            var portBox = new NumberBox { Header = "端口", Value = 8080, Minimum = 1, Maximum = 65535 };
            panel.Children.Add(portBox);
            var tokenBox = new TextBox { Header = "Token", PlaceholderText = "节点端生成的Token" };
            panel.Children.Add(tokenBox);

            dialog.Content = panel;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text) || string.IsNullOrWhiteSpace(hostBox.Text) || string.IsNullOrWhiteSpace(tokenBox.Text))
                {
                    await ShowMessageAsync("验证失败", "请填写所有必填项");
                    return;
                }

                var node = new LinuxNode
                {
                    Name = nameBox.Text,
                    Host = hostBox.Text,
                    Port = (int)portBox.Value,
                    Token = tokenBox.Text,
                    Platform = windowsRadio.IsChecked == true ? NodePlatform.Windows : NodePlatform.Linux,
                    CreatedAt = DateTime.Now
                };

                var (success, message) = await _viewModel.AddNodeAsync(node);
                await ShowMessageAsync(success ? "成功" : "失败", message);

                if (success)
                {
                    await LoadNodesAsync();
                }
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadNodesAsync();
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}