using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZMSL.App.Models;
using ZMSL.App.ViewModels;

namespace ZMSL.App.Views;

public sealed partial class MeFrpPage : Page
{
    public MeFrpViewModel ViewModel { get; }

    public MeFrpPage()
    {
        this.InitializeComponent();
        ViewModel = App.Services.GetRequiredService<MeFrpViewModel>();
        DataContext = ViewModel;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        TokenBox.Password = ViewModel.TokenInput;
        await ViewModel.ShowInstallDialogIfNeededAsync(XamlRoot);
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.InstallCommand.ExecuteAsync(null);
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.TokenInput = TokenBox.Password;
        await ViewModel.LoginCommand.ExecuteAsync(null);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RefreshCommand.ExecuteAsync(null);
        TokenBox.Password = ViewModel.TokenInput;
    }

    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.LogoutCommand.ExecuteAsync(null);
        TokenBox.Password = string.Empty;
    }

    private async void StopTunnel_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.StopTunnelCommand.ExecuteAsync(null);
    }

    private void ProxyList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((sender as ListView)?.SelectedItem is MeFrpProxy proxy)
        {
            ViewModel.SelectedProxy = proxy;
        }
    }

    private async void StartTunnel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int proxyId)
        {
            var proxy = ViewModel.Proxies.FirstOrDefault(item => item.ProxyId == proxyId);
            if (proxy != null)
            {
                ViewModel.SelectedProxy = proxy;
                await ViewModel.StartTunnelCommand.ExecuteAsync(null);
            }
        }
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearLogs();
    }

    private async void OpenCreateDialog_Click(object sender, RoutedEventArgs e)
    {
        var nodeCombo = new ComboBox
        {
            Header = "节点",
            ItemsSource = ViewModel.CreateNodes,
            DisplayMemberPath = nameof(MeFrpNode.DisplayName),
            SelectedItem = ViewModel.SelectedCreateNode,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var protocolCombo = new ComboBox
        {
            Header = "协议类型",
            ItemsSource = new[] { "tcp", "udp", "http", "https" },
            SelectedItem = ViewModel.CreateProtocol,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var tunnelNameBox = new TextBox { Header = "隧道名称", Text = ViewModel.CreateProxyName };
        var localIpBox = new TextBox { Header = "本地地址", Text = ViewModel.CreateLocalIp };
        var localPortBox = new NumberBox { Header = "本地端口", Value = ViewModel.CreateLocalPort, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var remotePortBox = new NumberBox { Header = "远程端口", Value = ViewModel.CreateRemotePort, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var nodeDetail = new TextBlock
        {
            Text = ViewModel.SelectedNodeDetail,
            TextWrapping = TextWrapping.Wrap
        };

        nodeCombo.SelectionChanged += (_, _) =>
        {
            ViewModel.SelectedCreateNode = nodeCombo.SelectedItem as MeFrpNode;
            nodeDetail.Text = ViewModel.SelectedNodeDetail;
        };

        var freePortButton = new Button
        {
            Content = "获取空闲端口",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        freePortButton.Click += async (_, _) =>
        {
            ViewModel.SelectedCreateNode = nodeCombo.SelectedItem as MeFrpNode;
            ViewModel.CreateProtocol = protocolCombo.SelectedItem?.ToString() ?? "tcp";
            await ViewModel.AutoFillFreePortCommand.ExecuteAsync(null);
            remotePortBox.Value = ViewModel.CreateRemotePort;
        };

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        content.ColumnDefinitions.Add(new ColumnDefinition());

        var formPanel = new StackPanel { Spacing = 12 };
        formPanel.Children.Add(nodeCombo);
        formPanel.Children.Add(protocolCombo);
        formPanel.Children.Add(tunnelNameBox);
        formPanel.Children.Add(localIpBox);
        formPanel.Children.Add(localPortBox);
        formPanel.Children.Add(remotePortBox);
        formPanel.Children.Add(freePortButton);

        var detailBorder = new Border
        {
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "节点详情", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    nodeDetail
                }
            }
        };

        Grid.SetColumn(formPanel, 0);
        Grid.SetColumn(detailBorder, 2);
        content.Children.Add(formPanel);
        content.Children.Add(detailBorder);

        var dialog = new ContentDialog
        {
            Title = "创建 MeFrp 隧道",
            Content = content,
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.SelectedCreateNode = nodeCombo.SelectedItem as MeFrpNode;
            ViewModel.CreateProtocol = protocolCombo.SelectedItem?.ToString() ?? "tcp";
            ViewModel.CreateProxyName = tunnelNameBox.Text;
            ViewModel.CreateLocalIp = localIpBox.Text;
            ViewModel.CreateLocalPort = (int)localPortBox.Value;
            ViewModel.CreateRemotePort = (int)remotePortBox.Value;
            await ViewModel.CreateTunnelCommand.ExecuteAsync(null);
        }
    }
}
