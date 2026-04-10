using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using ZMSL.App.Services;
using ZMSL.App.ViewModels;
using Windows.System;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Collections.Generic;

namespace ZMSL.App.Views;

public sealed partial class HomePage : Page
{
    private readonly HomeViewModel _viewModel;
    private readonly FrpService _frpService;
    private readonly SystemResourceSampler _resourceSampler;
    private readonly HashSet<string> _loadingImages = new();
    private DispatcherTimer? _chartTimer;
    private readonly List<double> _cpuSamples = new();
    private readonly List<double> _memorySamples = new();
    private const int MaxSamples = 120; // 约 2 分钟

    public HomePage()
    {
        this.InitializeComponent();
        _viewModel = App.Services.GetRequiredService<HomeViewModel>();
        _frpService = App.Services.GetRequiredService<FrpService>();
        var serverManager = App.Services.GetRequiredService<ServerManagerService>();
        _resourceSampler = new SystemResourceSampler(serverManager);
        DataContext = _viewModel;

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(HomeViewModel.FrpStatusText))
                UpdateFrpStatusBorder(_viewModel.FrpStatusText == "已连接");
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadDataCommand.ExecuteAsync(null);

        UpdateFrpStatusBorder(_viewModel.FrpStatusText == "已连接");

        if (_viewModel.Announcements.Count > 0)
        {
            AnnouncementList.ItemsSource = _viewModel.Announcements;
            NoAnnouncementText.Visibility = Visibility.Collapsed;
        }
        else
        {
            NoAnnouncementText.Visibility = Visibility.Visible;
        }

        if (_viewModel.Advertisements.Count > 0)
        {
            AdvertisementList.ItemsSource = _viewModel.Advertisements;
            NoAdvertisementText.Visibility = Visibility.Collapsed;
        }
        else
        {
            NoAdvertisementText.Visibility = Visibility.Visible;
        }

        StartChartTimer();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _chartTimer?.Stop();
        _chartTimer = null;
    }

    private void StartChartTimer()
    {
        _chartTimer?.Stop();
        _chartTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _chartTimer.Tick += (s, _) =>
        {
            var (cpu, memory) = _resourceSampler.Sample();
            _cpuSamples.Add(cpu);
            _memorySamples.Add(memory);
            if (_cpuSamples.Count > MaxSamples) _cpuSamples.RemoveAt(0);
            if (_memorySamples.Count > MaxSamples) _memorySamples.RemoveAt(0);
            UpdateChartPoints();
        };
        _chartTimer.Start();
    }

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateChartPoints();
    }

    private void UpdateChartPoints()
    {
        if (CpuChartCanvas.ActualWidth <= 0 || CpuChartCanvas.ActualHeight <= 0) return;

        // 确保在 UI 线程上执行
        DispatcherQueue.TryEnqueue(() =>
        {
            // 1. 更新 CPU 图表
            double maxCpu = _cpuSamples.Count > 0 ? _cpuSamples.Max() : 0;
            double cpuCeiling = GetNiceCeiling(maxCpu);
            
            // 绘制 CPU 网格线和标签
            DrawGrid(CpuGridCanvas, CpuYAxisPanel, CpuXAxisPanel, cpuCeiling, "%");
            
            // 绘制 CPU 折线
            double wCpu = CpuChartCanvas.ActualWidth, hCpu = CpuChartCanvas.ActualHeight;
            var cpuPoints = new PointCollection();
            for (int i = 0; i < _cpuSamples.Count; i++)
            {
                double x = _cpuSamples.Count > 1 ? (i * (wCpu - 1) / (_cpuSamples.Count - 1)) : 0;
                // 归一化到 ceiling
                double val = _cpuSamples[i];
                if (val > cpuCeiling) val = cpuCeiling; // 理论上不会发生，但以防万一
                double y = hCpu * (1 - val / cpuCeiling);
                cpuPoints.Add(new Windows.Foundation.Point(x, y));
            }
            CpuPolyline.Points = cpuPoints;

            // 2. 更新内存 图表
            double maxMem = _memorySamples.Count > 0 ? _memorySamples.Max() : 0;
            double memCeiling = GetNiceCeiling(maxMem);
            
            // 绘制内存网格线和标签
            DrawGrid(MemoryGridCanvas, MemoryYAxisPanel, MemoryXAxisPanel, memCeiling, "%");
            
            // 绘制内存折线
            double wMem = MemoryChartCanvas.ActualWidth, hMem = MemoryChartCanvas.ActualHeight;
            var memPoints = new PointCollection();
            for (int i = 0; i < _memorySamples.Count; i++)
            {
                double x = _memorySamples.Count > 1 ? (i * (wMem - 1) / (_memorySamples.Count - 1)) : 0;
                double val = _memorySamples[i];
                if (val > memCeiling) val = memCeiling;
                double y = hMem * (1 - val / memCeiling);
                memPoints.Add(new Windows.Foundation.Point(x, y));
            }
            MemoryPolyline.Points = memPoints;
        });
    }

    /// <summary>
    /// 获取一个“好看”的上限值 (例如 3.2 -> 5, 18 -> 20, 85 -> 100)
    /// </summary>
    private double GetNiceCeiling(double max)
    {
        if (max <= 0) return 5;
        if (max <= 5) return 5;
        if (max <= 10) return 10;
        if (max <= 20) return 20;
        if (max <= 25) return 25;
        if (max <= 50) return 50;
        if (max <= 100) return 100;
        // 超过100的情况（例如内存可能超过物理内存百分比? 不太可能，但为了通用性）
        return Math.Ceiling(max / 50.0) * 50.0;
    }

    /// <summary>
    /// 绘制网格线和标签
    /// </summary>
    private void DrawGrid(Canvas gridCanvas, StackPanel yAxisPanel, StackPanel xAxisPanel, double ceiling, string unit)
    {
        gridCanvas.Children.Clear();
        yAxisPanel.Children.Clear();
        xAxisPanel.Children.Clear();

        double width = gridCanvas.ActualWidth;
        double height = gridCanvas.ActualHeight;
        
        if (width <= 0 || height <= 0) return;

        // 绘制5条水平线 (0, 25%, 50%, 75%, 100% of ceiling)
        int steps = 4; // 4个间隔，5条线
        
        for (int i = 0; i <= steps; i++)
        {
            double ratio = (double)i / steps;
            double y = height * (1 - ratio);
            
            // 虚线网格
            var line = new Line
            {
                X1 = 0,
                Y1 = y,
                X2 = width,
                Y2 = y,
                Stroke = new SolidColorBrush(Microsoft.UI.Colors.Gray),
                StrokeThickness = 1,
                Opacity = 0.3,
                StrokeDashArray = new DoubleCollection { 4, 2 } // 虚线效果
            };
            gridCanvas.Children.Add(line);
        }

        // 重新循环添加标签（从上到下：100% -> 0%）
        for (int i = steps; i >= 0; i--)
        {
            double ratio = (double)i / steps;
            double value = ceiling * ratio;
            
            // 使用 Canvas 放置标签
            var label = new TextBlock
            {
                Text = $"{value:0}{unit}",
                FontSize = 10,
                Opacity = 0.6,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray)
            };
            // 测量尺寸需要 UI 线程，这里估算位置
            // Y 坐标：grid line 的 Y - fontHeight/2
            double y = height * (1 - ratio) - 7; 
            
            Canvas.SetLeft(label, -30); // 放在左侧
            Canvas.SetTop(label, y);
            gridCanvas.Children.Add(label);
        }
        
        // X轴标签 (0s, 30s, 60s...)
        // 也是直接画在 Canvas 上比较方便
        int xSteps = 4;
        for (int i = 0; i <= xSteps; i++)
        {
            // 0s 在最右边 (i=xSteps), 120s 在最左边 (i=0) ?
            // 列表是 append 的，index 0 是旧的，index count-1 是新的
            // 图表绘制：x=0 是旧的，x=width 是新的 (Current)
            // 所以最右边是 0s ago，最左边是 120s ago
            
            double xRatio = (double)i / xSteps;
            double x = width * xRatio;
            
            // i=0 (Left) -> -120s
            // i=4 (Right) -> 0s
            int secondsAgo = 120 - (int)(120 * xRatio);
            
            var label = new TextBlock
            {
                Text = secondsAgo == 0 ? "Now" : $"{secondsAgo}s",
                FontSize = 10,
                Opacity = 0.6,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Gray)
            };
            
            Canvas.SetLeft(label, x - 10); // 居中微调
            Canvas.SetTop(label, height + 5);
            gridCanvas.Children.Add(label);
        }
    }

    private void UpdateFrpStatusBorder(bool isConnected)
    {
        if (FrpStatusBorder == null) return;
        
        // 确保在 UI 线程上执行
        DispatcherQueue.TryEnqueue(() =>
        {
            FrpStatusBorder.Background = isConnected
                ? new SolidColorBrush(Microsoft.UI.Colors.LimeGreen)
                : new SolidColorBrush(Microsoft.UI.Colors.Gray);
        });
    }

    private void NewServer_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(MyServerPage));
    }

    private void GoToServerList_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(MyServerPage));
        if (App.MainWindowInstance != null)
        {
            var mainWindow = App.MainWindowInstance;
            var navViewField = mainWindow.GetType().GetField("NavView",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (navViewField?.GetValue(mainWindow) is Microsoft.UI.Xaml.Controls.NavigationView navView)
            {
                foreach (var item in navView.MenuItems)
                {
                    if (item is Microsoft.UI.Xaml.Controls.NavigationViewItem navItem &&
                        navItem.Tag?.ToString() == "MyServer")
                    {
                        navView.SelectedItem = navItem;
                        break;
                    }
                }
            }
        }
    }

    private void DownloadCore_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(ServerCorePage));
    }

    private void SetupFrp_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(FrpPage));
    }

    private async void Advertisement_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.Tag is string linkUrl && !string.IsNullOrEmpty(linkUrl))
        {
            try { await Launcher.LaunchUriAsync(new Uri(linkUrl)); } catch { }
        }
    }

    private void Advertisement_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border) border.Opacity = 0.8;
    }

    private void Advertisement_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border border) border.Opacity = 1.0;
    }

    private void Image_Failed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image image && image.DataContext is ZMSL.Shared.DTOs.AdvertisementDto ad)
        {
            if (_loadingImages.Contains(ad.ImageUrl)) return;
            _ = LoadImageAsync(image, ad.ImageUrl);
        }
    }

    private async Task LoadImageAsync(Image image, string imageUrl)
    {
        if (!_loadingImages.Add(imageUrl)) return;
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "ZMSL-Client/1.0");
            var response = await httpClient.GetAsync(imageUrl);
            if (response.IsSuccessStatusCode)
            {
                var imageData = await response.Content.ReadAsByteArrayAsync();
                var bitmap = new BitmapImage();
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await stream.WriteAsync(imageData.AsBuffer());
                stream.Seek(0);
                await bitmap.SetSourceAsync(stream);
                DispatcherQueue.TryEnqueue(() => image.Source = bitmap);
            }
        }
        catch { }
        finally { _loadingImages.Remove(imageUrl); }
    }
}
