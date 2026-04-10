using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using ZMSL.App.Models;
using System;

namespace ZMSL.App.Views;

public sealed partial class CreateModeSelectionPage : Page
{
    public CreateMode SelectedMode { get; private set; } = CreateMode.Beginner;

    public CreateModeSelectionPage()
    {
        this.InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // 页面加载完成，不再默认设置样式
    }

    private void UpdateButtonStyles()
    {
        // 移除默认样式设置，保持卡片中性显示
        // 两个卡片现在都使用相同的默认样式
    }

    private void BeginnerMode_Click(object sender, RoutedEventArgs e)
    {
        SelectedMode = CreateMode.Beginner;
        NavigateToConfiguration();
    }

    private void AdvancedMode_Click(object sender, RoutedEventArgs e)
    {
        SelectedMode = CreateMode.Advanced;
        NavigateToConfiguration();
    }

    private void ModpackMode_Click(object sender, RoutedEventArgs e)
    {
        // 导航到整合包开服页面
        Frame.Navigate(typeof(ModpackServerPage));
    }

    private void NodeMode_Click(object sender, RoutedEventArgs e)
    {
        // 导航到节点选择页面
        Frame.Navigate(typeof(NodeSelectionPage));
    }

    private void NavigateToConfiguration()
    {
        try
        {
            if (SelectedMode == CreateMode.Beginner)
            {
                Frame.Navigate(typeof(BeginnerModePage));
            }
            else
            {
                Frame.Navigate(typeof(AdvancedModePage));
            }
        }
        catch (Exception ex)
        {
            ShowErrorDialog($"导航失败: {ex.Message}");
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
        else
        {
            Frame.Navigate(typeof(HomePage));
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        
        // 如果是从其他页面传入的数据
        if (e.Parameter is CreateMode mode)
        {
            SelectedMode = mode;
            // 不再更新样式
        }
    }

    private async void ShowErrorDialog(string message)
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