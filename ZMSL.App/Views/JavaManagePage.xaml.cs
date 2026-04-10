using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Extensions.DependencyInjection;
using ZMSL.App.Models;
using ZMSL.App.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace ZMSL.App.Views;

public sealed partial class JavaManagePage : Page
{
    private readonly LinuxNodeService _nodeService;
    private LinuxNode? _node;
    private ObservableCollection<NodeJavaInfo> _javaList = new();

    public JavaManagePage()
    {
        this.InitializeComponent();
        _nodeService = App.Services.GetRequiredService<LinuxNodeService>();
        JavaListView.ItemsSource = _javaList;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is LinuxNode node)
        {
            _node = node;
            NodeNameText.Text = node.Name;
            await LoadJavaList();
        }
        else
        {
            Frame.GoBack();
        }
    }

    private async Task LoadJavaList()
    {
        if (_node == null) return;

        LoadingOverlay.Visibility = Visibility.Visible;
        StatusText.Text = "正在加载 Java 列表...";
        _javaList.Clear();

        try
        {
            var javaList = await _nodeService.ListJavaAsync(_node);
            if (javaList != null && javaList.Count > 0)
            {
                foreach (var java in javaList)
                {
                    _javaList.Add(java);
                }
                EmptyHint.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyHint.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"加载失败: {ex.Message}");
            EmptyHint.Visibility = Visibility.Visible;
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private async void InstallJava_Click(object sender, RoutedEventArgs e)
    {
        if (_node == null) return;

        var versionInput = new TextBox
        {
            PlaceholderText = "例如：8, 11, 17, 21",
            Text = "17"
        };

        var methodComboBox = new ComboBox
        {
            ItemsSource = new List<string> { "系统包管理器（apt/yum）", "下载安装（MSL API）" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var inputDialog = new ContentDialog
        {
            Title = "安装 Java",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "请选择安装方式：" },
                    methodComboBox,
                    new TextBlock 
                    { 
                        Text = "请输入 Java 版本号：",
                        Margin = new Thickness(0, 8, 0, 0)
                    },
                    versionInput,
                    new TextBlock
                    {
                        Text = "提示：\n· 系统包管理器：需要 sudo 权限\n· 下载安装：无需 sudo，安装到 /opt/java",
                        FontSize = 12,
                        Opacity = 0.7,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 8, 0, 0)
                    }
                }
            },
            PrimaryButtonText = "安装",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        if (await inputDialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (!string.IsNullOrWhiteSpace(versionInput.Text))
            {
                var useDownload = methodComboBox.SelectedIndex == 1;
                var methodName = useDownload ? "下载" : "系统包管理器";
                
                LoadingOverlay.Visibility = Visibility.Visible;
                StatusText.Text = $"正在使用{methodName}安装 Java {versionInput.Text}...";

                try
                {
                    (bool success, string message) result;
                    
                    if (useDownload)
                    {
                        result = await _nodeService.InstallJavaFromDownloadAsync(_node, versionInput.Text);
                    }
                    else
                    {
                        result = await _nodeService.InstallJavaAsync(_node, versionInput.Text);
                    }

                    if (result.success)
                    {
                        StatusText.Text = "安装成功！";
                        await ShowSuccessDialog(result.message);
                        await Task.Delay(1500);
                        await LoadJavaList();
                    }
                    else
                    {
                        await ShowErrorDialog($"安装失败: {result.message}");
                    }
                }
                catch (Exception ex)
                {
                    await ShowErrorDialog($"安装失败: {ex.Message}");
                }
                finally
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            }
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadJavaList();
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

    private async Task ShowSuccessDialog(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "成功",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
