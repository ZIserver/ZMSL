using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Extensions.DependencyInjection;
using ZMSL.App.Models;
using ZMSL.App.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace ZMSL.App.Views;

public sealed partial class LocalJavaManagePage : Page
{
    private readonly JavaManagerService _javaManager;
    private readonly DatabaseService _db;
    private ObservableCollection<ZMSL.App.Models.JavaInfo> _javaList = new();
    private bool _isScanning = false;

    public LocalJavaManagePage()
    {
        this.InitializeComponent();
        _javaManager = App.Services.GetRequiredService<JavaManagerService>();
        _db = App.Services.GetRequiredService<DatabaseService>();
        JavaListView.ItemsSource = _javaList;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadJavaListAsync();
    }

    private async Task LoadJavaListAsync()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        StatusText.Text = "正在加载 Java 列表...";
        _javaList.Clear();

        try
        {
            var javaList = await _db.ExecuteWithLockAsync(async db => 
                await db.JavaInstallations.OrderByDescending(j => j.Version).ToListAsync());

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

    private async void ScanJava_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanning) return;
        _isScanning = true;
        
        LoadingOverlay.Visibility = Visibility.Visible;
        StatusText.Text = "正在扫描 Java 环境...";

        try
        {
            // 1. 执行扫描
            var progress = new Progress<JavaInstallation>(java =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    StatusText.Text = $"扫描中... 已找到: {java.Version} ({java.Path})";
                });
            });

            var installed = await Task.Run(() => _javaManager.DetectInstalledJavaAsync(progress));

            // 2. 保存到数据库
            await _db.ExecuteWithLockAsync(async db =>
            {
                // 获取现有记录
                var existing = await db.JavaInstallations.ToListAsync();
                
                foreach (var java in installed)
                {
                    var record = existing.FirstOrDefault(j => j.Path == java.Path);
                    if (record != null)
                    {
                        record.Version = java.Version;
                        record.Source = java.Source;
                        record.DetectedAt = DateTime.Now;
                        record.IsValid = true;
                        db.JavaInstallations.Update(record);
                    }
                    else
                    {
                        db.JavaInstallations.Add(java.ToModel());
                    }
                }
                
                await db.SaveChangesAsync();
            });

            await LoadJavaListAsync();
            await ShowSuccessDialog($"扫描完成，共找到 {installed.Count} 个 Java 环境");
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"扫描失败: {ex.Message}");
        }
        finally
        {
            _isScanning = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadJavaListAsync();
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