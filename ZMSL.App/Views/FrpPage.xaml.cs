using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using ZMSL.App.ViewModels;
using ZMSL.Shared.DTOs;

namespace ZMSL.App.Views;

public sealed partial class FrpPage : Page
{
    private readonly FrpViewModel _viewModel;

    public FrpPage()
    {
        this.InitializeComponent();
        _viewModel = App.Services.GetRequiredService<FrpViewModel>();
        
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.Logs.CollectionChanged += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                LogsControl.ItemsSource = null;
                LogsControl.ItemsSource = _viewModel.Logs;
            });
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateLoginState();
        UpdateConnectionStatus(); // 初始化连接状态 UI
        LoadingRing.IsActive = true;
        await _viewModel.LoadDataCommand.ExecuteAsync(null);
        NodeComboBox.ItemsSource = _viewModel.Nodes;
        TunnelListView.ItemsSource = _viewModel.Tunnels;
        LogsControl.ItemsSource = _viewModel.Logs; // 加载历史日志
        LoadingRing.IsActive = false;
        
        // 加载流量配额信息
        await UpdateTrafficQuotaAsync();
        
        // 检查是否安装了 frpc.exe
        CheckFrpcInstallation();
    }

    private async Task UpdateTrafficQuotaAsync()
    {
        if (!_viewModel.IsLoggedIn) return;
        
        try
        {
            var apiService = App.Services.GetRequiredService<Services.ApiService>();
            var result = await apiService.GetCurrentUserAsync();
            
            if (result.Success && result.Data != null)
            {
                var user = result.Data;
                DispatcherQueue.TryEnqueue(() =>
                {
                    TrafficQuotaPanel.Visibility = Visibility.Visible;
                    TrafficUsedRun.Text = FormatTraffic(user.TrafficUsed ?? 0);
                    TrafficQuotaRun.Text = FormatTraffic(user.TrafficQuota ?? 0);
                    
                    // 计算进度百分比
                    double percentage = (user.TrafficQuota ?? 0) > 0 
                        ? (double)(user.TrafficUsed ?? 0) / (user.TrafficQuota ?? 0) * 100 
                        : 0;
                    TrafficProgressBar.Value = Math.Min(percentage, 100);
                    
                    // 根据使用量设置进度条颜色
                    if (percentage >= 90)
                        TrafficProgressBar.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
                    else if (percentage >= 70)
                        TrafficProgressBar.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
                    else
                        TrafficProgressBar.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LimeGreen);
                });
            }
        }
        catch { }
    }

    private static string FormatTraffic(long bytes)
    {
        if (bytes >= 1073741824)
            return $"{bytes / 1073741824.0:F2} GB";
        if (bytes >= 1048576)
            return $"{bytes / 1048576.0:F2} MB";
        return $"{bytes / 1024.0:F2} KB";
    }

    private void UpdateConnectionStatus()
    {
        var isConnected = _viewModel.IsConnected;
        
        StatusIndicator.Fill = isConnected
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LimeGreen)
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
        StatusText.Text = isConnected ? "已连接" : "未连接";
        StopButton.IsEnabled = isConnected;
        
        // 更新连接地址显示
        if (isConnected && !string.IsNullOrEmpty(_viewModel.CurrentConnectAddress))
        {
            ConnectAddressText.Text = _viewModel.CurrentConnectAddress;
            CurrentTunnelText.Text = _viewModel.CurrentTunnelName;
            LatencyPanel.Visibility = Visibility.Visible;
            UpdateLatencyDisplay();
            CopyAddressBtn.Visibility = Visibility.Visible;
        }
        else
        {
            ConnectAddressText.Text = "-";
            CurrentTunnelText.Text = "";
            LatencyPanel.Visibility = Visibility.Collapsed;
            CopyAddressBtn.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateLatencyDisplay()
    {
        if (LatencyValueText == null) return;
        LatencyValueText.Text = _viewModel.LatencyText;
        try 
        {
            var color = _viewModel.LatencyColor;
            if (color == "Red") LatencyValueText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            else if (color == "#F59E0B") LatencyValueText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange);
            else if (color == "#10B981") LatencyValueText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LimeGreen);
            else LatencyValueText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
        }
        catch {}
    }

    private async void UpdateLoginState()
    {
        if (_viewModel.IsLoggedIn)
        {
            LoginHint.Visibility = Visibility.Collapsed;
            MainContent.Opacity = 1;
            MainContent.IsHitTestVisible = true;
            
            // 登录后显示流量配额
            await UpdateTrafficQuotaAsync();
        }
        else
        {
            LoginHint.Visibility = Visibility.Visible;
            MainContent.Opacity = 0.5;
            MainContent.IsHitTestVisible = false;
            TrafficQuotaPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void CreateTunnel_Click(object sender, RoutedEventArgs e)
    {
        if (NodeComboBox.SelectedItem is FrpNodeDto node)
        {
            _viewModel.SelectedNode = node;
            _viewModel.TunnelName = TunnelNameBox.Text;
            _viewModel.TunnelProtocol = (ProtocolComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToLower() ?? "tcp";
            _viewModel.TunnelLocalPort = (int)LocalPortBox.Value;

            await _viewModel.CreateTunnelCommand.ExecuteAsync(null);
            TunnelListView.ItemsSource = null;
            TunnelListView.ItemsSource = _viewModel.Tunnels;
            TunnelNameBox.Text = string.Empty;
        }
    }

    private void TunnelListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectedTunnel = TunnelListView.SelectedItem as TunnelDto;
    }

    private async void StartTunnel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TunnelDto tunnel)
        {
            _viewModel.SelectedTunnel = tunnel;
            await _viewModel.StartTunnelCommand.ExecuteAsync(null);
        }
    }

    private async void DeleteTunnel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is TunnelDto tunnel)
        {
            _viewModel.SelectedTunnel = tunnel;
            await _viewModel.DeleteTunnelCommand.ExecuteAsync(null);
            TunnelListView.ItemsSource = null;
            TunnelListView.ItemsSource = _viewModel.Tunnels;
        }
    }

    private async void StopTunnel_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.StopTunnelCommand.ExecuteAsync(null);
    }

    private void ClearLogs_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ClearLogsCommand.Execute(null);
    }

    private void CopyAddress_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_viewModel.CurrentConnectAddress))
        {
            var dataPackage = new DataPackage();
            dataPackage.SetText(_viewModel.CurrentConnectAddress);
            Clipboard.SetContent(dataPackage);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName == nameof(_viewModel.IsConnected) || 
                e.PropertyName == nameof(_viewModel.CurrentConnectAddress))
            {
                UpdateConnectionStatus();
            }
            else if (e.PropertyName == nameof(_viewModel.Tunnels))
            {
                // 刷新隧道列表以显示运行状态
                TunnelListView.ItemsSource = null;
                TunnelListView.ItemsSource = _viewModel.Tunnels;
            }
            else if (e.PropertyName == nameof(_viewModel.IsLoggedIn))
            {
                UpdateLoginState();
            }
            else if (e.PropertyName == nameof(_viewModel.LatencyText) || e.PropertyName == nameof(_viewModel.LatencyColor))
            {
                UpdateLatencyDisplay();
            }
            else if (e.PropertyName == nameof(_viewModel.IsFrpcInstalled))
            {
                CheckFrpcInstallation();
            }
        });
    }

    private void CheckFrpcInstallation()
    {
        if (!_viewModel.IsFrpcInstalled)
        {
            // 使用 MainWindow 的全局对话框
            (App.MainWindow as MainWindow)?.ShowFrpcDownloadDialog();
        }
        else
        {
            (App.MainWindow as MainWindow)?.HideFrpcDownloadDialog();
        }
    }
}
