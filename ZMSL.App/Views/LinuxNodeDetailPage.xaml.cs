using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Extensions.DependencyInjection;
using ZMSL.App.Models;
using ZMSL.App.ViewModels;
using System;

namespace ZMSL.App.Views;

public sealed partial class LinuxNodeDetailPage : Page
{
    private readonly LinuxNodeDetailViewModel _viewModel;
    private DispatcherTimer? _refreshTimer;

    public LinuxNodeDetailPage()
    {
        this.InitializeComponent();
        _viewModel = App.Services.GetRequiredService<LinuxNodeDetailViewModel>();
        
        // 监听属性变化更新UI
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is LinuxNode node)
        {
            await _viewModel.InitializeAsync(node);
            UpdateUI();
            
            // 启动定时刷新资源（每5秒）
            StartRefreshTimer();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        StopRefreshTimer();
    }

    private void StartRefreshTimer()
    {
        _refreshTimer = new DispatcherTimer();
        // 降低刷新频率,从5秒改为10秒,减少网络和磁盘I/O
        _refreshTimer.Interval = TimeSpan.FromSeconds(10);
        _refreshTimer.Tick += async (s, e) => await _viewModel.RefreshResourcesAsync();
        _refreshTimer.Start();
    }

    private void StopRefreshTimer()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName == nameof(_viewModel.Resources) ||
                e.PropertyName == nameof(_viewModel.SystemInfo) ||
                e.PropertyName == nameof(_viewModel.IsLoading))
            {
                UpdateUI();
            }
        });
    }

    private void UpdateUI()
    {
        if (_viewModel.Node != null)
        {
            NodeNameText.Text = _viewModel.Node.Name;
            NodeAddressText.Text = $"{_viewModel.Node.Host}:{_viewModel.Node.Port}";
        }

        if (_viewModel.Resources != null)
        {
            CpuText.Text = $"{_viewModel.Resources.CpuPercent:F1}%";
            CpuProgress.Value = _viewModel.Resources.CpuPercent;

            double usedGb = _viewModel.Resources.MemoryUsed / 1024.0 / 1024.0 / 1024.0;
            double totalGb = _viewModel.Resources.MemoryTotal / 1024.0 / 1024.0 / 1024.0;
            RamText.Text = $"{usedGb:F2} / {totalGb:F2} GB";
            RamProgress.Value = _viewModel.Resources.MemoryPercent;

            double diskUsedGb = _viewModel.Resources.DiskUsed / 1024.0 / 1024.0 / 1024.0;
            double diskTotalGb = _viewModel.Resources.DiskTotal / 1024.0 / 1024.0 / 1024.0;
            DiskText.Text = $"{diskUsedGb:F1} / {diskTotalGb:F1} GB";
            DiskProgress.Value = _viewModel.Resources.DiskPercent;
            DiskPercentText.Text = $"{_viewModel.Resources.DiskPercent:F1}%";
        }

        if (_viewModel.SystemInfo != null)
        {
            OsText.Text = _viewModel.SystemInfo.Os;
            ArchText.Text = $"{_viewModel.SystemInfo.Architecture} | {_viewModel.SystemInfo.CpuCores} 核心";
        }

        LoadingRing.IsActive = _viewModel.IsLoading;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshResourcesAsync();
    }

    private void JavaManage_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Node == null) return;
        Frame.Navigate(typeof(JavaManagePage), _viewModel.Node);
    }
}
