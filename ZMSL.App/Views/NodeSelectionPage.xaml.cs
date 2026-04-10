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

public sealed partial class NodeSelectionPage : Page
{
    private readonly LinuxNodeService _nodeService;
    private List<LinuxNode> _nodes = new();

    public NodeSelectionPage()
    {
        this.InitializeComponent();
        _nodeService = App.Services.GetRequiredService<LinuxNodeService>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadNodesAsync();
    }

    private async Task LoadNodesAsync()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            _nodes = await _nodeService.GetNodesAsync();
            
            if (_nodes.Count > 0)
            {
                NodesListView.ItemsSource = _nodes;
                EmptyPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                NodesListView.ItemsSource = null;
                EmptyPanel.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"加载节点列表失败: {ex.Message}");
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void NodesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        NextButton.IsEnabled = NodesListView.SelectedItem != null;
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (NodesListView.SelectedItem is LinuxNode selectedNode)
        {
            // 导航到远程服务器创建页面（复用现有页面）
            Frame.Navigate(typeof(CreateRemoteServerPage), selectedNode);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private void GoToAddNode_Click(object sender, RoutedEventArgs e)
    {
        // 导航到节点管理页面
        var mainWindow = App.MainWindow as MainWindow;
        mainWindow?.NavigateToPage("LinuxNode");
        
        // 关闭当前创建流程
        Frame.Navigate(typeof(HomePage));
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
