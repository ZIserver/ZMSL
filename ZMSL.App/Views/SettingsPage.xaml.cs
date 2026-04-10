using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using System;
using System.Threading;
using System.Threading.Tasks;
using ZMSL.App.Services;
using ZMSL.App.ViewModels;
using ZMSL.Shared.DTOs;
using Windows.System;

namespace ZMSL.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;
    private readonly ApiService _apiService;
    private bool _isInitializing = true;
    private CancellationTokenSource? _saveCts;

    public SettingsPage()
    {
        this.InitializeComponent();
        _viewModel = App.Services.GetRequiredService<SettingsViewModel>();
        _apiService = App.Services.GetRequiredService<ApiService>();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadSettingsCommand.ExecuteAsync(null);

        // 填充UI
        DetectedJavaText.Text = _viewModel.DetectedJavaPath ?? "未检测到";
        UseDetectedButton.IsEnabled = !string.IsNullOrEmpty(_viewModel.DetectedJavaPath);

        JavaPathBox.Text = _viewModel.Settings.DefaultJavaPath ?? "";
        ServerPathBox.Text = _viewModel.Settings.DefaultServerPath;
        
        // 备份设置
        EnableAutoBackupToggle.IsOn = _viewModel.Settings.EnableAutoBackup;
        
        // 启动与运行设置
        EnableMicaEffectToggle.IsOn = _viewModel.Settings.EnableMicaEffect;
        StartOnBootToggle.IsOn = _viewModel.Settings.StartOnBoot;
        AutoStartLastServerToggle.IsOn = _viewModel.Settings.AutoStartLastServer;
        AutoRestartAppOnCrashToggle.IsOn = _viewModel.Settings.AutoRestartAppOnCrash;
        AutoRestartServerOnCrashToggle.IsOn = _viewModel.Settings.AutoRestartServerOnCrash;

        // 下载与控制台设置
        ForceMultiThreadToggle.IsOn = _viewModel.Settings.ForceMultiThread;
        DownloadThreadsSlider.Value = _viewModel.Settings.DownloadThreads > 0 ? _viewModel.Settings.DownloadThreads : 8;
        
        // 控制台字体大小
        ConsoleFontSizeSlider.Value = _viewModel.Settings.ConsoleFontSize > 0 ? _viewModel.Settings.ConsoleFontSize : 12;
        
        // 背景设置
        UseCustomBackgroundToggle.IsOn = _viewModel.Settings.UseCustomBackground;
        BackgroundImagePathBox.Text = _viewModel.Settings.BackgroundImagePath ?? "";
        BackgroundImagePathBox.IsEnabled = UseCustomBackgroundToggle.IsOn;
        MicaIntensitySlider.Value = _viewModel.Settings.MicaIntensity;
        
        // 控制台编码
        string encoding = _viewModel.Settings.ConsoleEncoding ?? "UTF-8";
        foreach (ComboBoxItem item in ConsoleEncodingCombo.Items)
        {
            if (item.Tag?.ToString() == encoding)
            {
                ConsoleEncodingCombo.SelectedItem = item;
                break;
            }
        }

        // 镜像源设置
        string mirrorSource = _viewModel.Settings.DownloadMirrorSource ?? "MSL";
        foreach (ComboBoxItem item in MirrorSourceCombo.Items)
        {
            if (item.Tag?.ToString() == mirrorSource)
            {
                MirrorSourceCombo.SelectedItem = item;
                break;
            }
        }
        UpdateMirrorSourceHint(mirrorSource);

        // 关于与更新
        VersionText.Text = "版本 " + App.UpdateService.CurrentVersion;
        AutoCheckUpdateToggle.IsOn = _viewModel.Settings.AutoCheckUpdate;
        PendingUpdatePanel.Visibility = App.UpdateService.HasPendingUpdate ? Visibility.Visible : Visibility.Collapsed;
        
        // 将总分钟数转换为小时和分钟
        var totalMinutes = _viewModel.Settings.BackupIntervalMinutes;
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        
        // 如果小时为0且分钟为0，则默认设置为1小时 (60分钟)
        if (hours == 0 && minutes == 0)
        {
            hours = 1;
            minutes = 0;
        }
        
        // 设置分钟的最小值：如果小时为0，分钟最小为1；否则为0
        BackupIntervalMinutesBox.Minimum = (hours == 0) ? 1 : 0;
        
        BackupIntervalHoursBox.Value = hours;
        BackupIntervalMinutesBox.Value = minutes;
        UpdateBackupIntervalHint();
        
        BackupRetentionBox.Value = _viewModel.Settings.BackupRetentionCount;
        
        // 初始化完成，允许触发事件
        _isInitializing = false;

        // 绑定文本框事件
        JavaPathBox.TextChanged += (s, e) => TriggerAutoSave();
        ServerPathBox.TextChanged += (s, e) => TriggerAutoSave();
        BackgroundImagePathBox.TextChanged += (s, e) => TriggerAutoSave();
        BackupRetentionBox.ValueChanged += (s, e) => TriggerAutoSave();

        // 加载鸣谢信息
        _ = LoadAcknowledgmentsAsync();
    }

    private void TriggerAutoSave()
    {
        if (_isInitializing) return;

        _saveCts?.Cancel();
        _saveCts = new CancellationTokenSource();
        var token = _saveCts.Token;

        Task.Delay(1000, token).ContinueWith(t => 
        {
            if (t.IsCanceled) return;
            DispatcherQueue.TryEnqueue(async () => 
            {
                UpdateSettingsFromUI();
                await _viewModel.SaveSettingsCommand.ExecuteAsync(null);
                StatusText.Text = "自动保存: " + _viewModel.StatusMessage;
            });
        });
    }

    private void UpdateSettingsFromUI()
    {
        // 更新设置
        _viewModel.Settings.DefaultJavaPath = string.IsNullOrWhiteSpace(JavaPathBox.Text) ? null : JavaPathBox.Text;
        _viewModel.Settings.DefaultServerPath = ServerPathBox.Text;
        _viewModel.Settings.AutoCheckUpdate = AutoCheckUpdateToggle.IsOn;
        _viewModel.Settings.UseCustomBackground = UseCustomBackgroundToggle.IsOn;
        _viewModel.Settings.BackgroundImagePath = string.IsNullOrWhiteSpace(BackgroundImagePathBox.Text) ? null : BackgroundImagePathBox.Text;
        
        // 备份设置 - 将小时和分钟转换为总分钟数
        _viewModel.Settings.EnableAutoBackup = EnableAutoBackupToggle.IsOn;
        var hours = (int)BackupIntervalHoursBox.Value;
        var minutes = (int)BackupIntervalMinutesBox.Value;
        
        // 确保至少有1分钟
        if (hours == 0 && minutes == 0)
        {
            minutes = 1;
            BackupIntervalMinutesBox.Value = 1;
        }
        
        _viewModel.Settings.BackupIntervalMinutes = hours * 60 + minutes;
        
        _viewModel.Settings.BackupRetentionCount = (int)BackupRetentionBox.Value;

        // 保存新设置
        _viewModel.Settings.EnableMicaEffect = EnableMicaEffectToggle.IsOn;
        _viewModel.Settings.StartOnBoot = StartOnBootToggle.IsOn;
        _viewModel.Settings.AutoStartLastServer = AutoStartLastServerToggle.IsOn;
        _viewModel.Settings.AutoRestartAppOnCrash = AutoRestartAppOnCrashToggle.IsOn;
        _viewModel.Settings.AutoRestartServerOnCrash = AutoRestartServerOnCrashToggle.IsOn;
        
        _viewModel.Settings.ForceMultiThread = ForceMultiThreadToggle.IsOn;
        _viewModel.Settings.DownloadThreads = (int)DownloadThreadsSlider.Value;
        _viewModel.Settings.MicaIntensity = (int)MicaIntensitySlider.Value;
        _viewModel.Settings.ConsoleFontSize = (int)ConsoleFontSizeSlider.Value;
        
        if (ConsoleEncodingCombo.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
        {
            _viewModel.Settings.ConsoleEncoding = selectedItem.Tag.ToString() ?? "UTF-8";
        }

        if (MirrorSourceCombo.SelectedItem is ComboBoxItem mirrorItem && mirrorItem.Tag is string source)
        {
            _viewModel.Settings.DownloadMirrorSource = source;
        }

        // 应用开机自启
        SetStartOnBoot(StartOnBootToggle.IsOn);
    }

    private void UseDetectedJava_Click(object sender, RoutedEventArgs e)
    {
        JavaPathBox.Text = _viewModel.DetectedJavaPath ?? "";
    }

    private async void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.BrowseJavaPathCommand.ExecuteAsync(null);
        JavaPathBox.Text = _viewModel.Settings.DefaultJavaPath ?? "";
    }

    private async void BrowseServerPath_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.BrowseServerPathCommand.ExecuteAsync(null);
        ServerPathBox.Text = _viewModel.Settings.DefaultServerPath;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        UpdateSettingsFromUI();
        await _viewModel.SaveSettingsCommand.ExecuteAsync(null);
        StatusText.Text = _viewModel.StatusMessage;
    }

    private void SetStartOnBoot(bool enable)
    {
        try
        {
            string appName = "ZMSL";
            string? appPath = Environment.ProcessPath;
            
            if (string.IsNullOrEmpty(appPath)) return;

            // 仅适用于非打包应用（自包含.exe）
            using (RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
            {
                if (enable)
                {
                    key?.SetValue(appName, $"\"{appPath}\"");
                }
                else
                {
                    key?.DeleteValue(appName, false);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set start on boot: {ex.Message}");
        }
    }

    // 事件处理程序
    private void EnableMicaEffectToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        
        var isEnabled = EnableMicaEffectToggle.IsOn;
        _viewModel.Settings.EnableMicaEffect = isEnabled;
        
        if (App.MainWindowInstance != null)
        {
             App.MainWindowInstance.UpdateSystemBackdrop(isEnabled, (int)MicaIntensitySlider.Value);
        }
        TriggerAutoSave();
    }
    
    private void UseCustomBackgroundToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        var enabled = UseCustomBackgroundToggle.IsOn;
        _viewModel.Settings.UseCustomBackground = enabled;
        BackgroundImagePathBox.IsEnabled = enabled;
        
        if (App.MainWindowInstance != null)
        {
            App.MainWindowInstance.UpdateCustomBackground(enabled, _viewModel.Settings.BackgroundImagePath);
            if (!enabled)
            {
                App.MainWindowInstance.UpdateSystemBackdrop(EnableMicaEffectToggle.IsOn, (int)MicaIntensitySlider.Value);
            }
        }
        TriggerAutoSave();
    }
    
    private async void BrowseBackgroundImage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
        
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        
        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            _viewModel.Settings.BackgroundImagePath = file.Path;
            BackgroundImagePathBox.Text = file.Path;
            
            if (UseCustomBackgroundToggle.IsOn && App.MainWindowInstance != null)
            {
                App.MainWindowInstance.UpdateCustomBackground(true, file.Path);
            }
        }
    }
    
    private void MicaIntensitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isInitializing) return;
        _viewModel.Settings.MicaIntensity = (int)e.NewValue;
        if (App.MainWindowInstance != null && EnableMicaEffectToggle.IsOn && !UseCustomBackgroundToggle.IsOn)
        {
            App.MainWindowInstance.UpdateSystemBackdrop(true, (int)e.NewValue);
        }
        TriggerAutoSave();
    }
    
    private void StartOnBootToggle_Toggled(object sender, RoutedEventArgs e) => TriggerAutoSave();
    private void AutoStartLastServerToggle_Toggled(object sender, RoutedEventArgs e) => TriggerAutoSave();
    private void AutoRestartAppOnCrashToggle_Toggled(object sender, RoutedEventArgs e) => TriggerAutoSave();
    private void AutoRestartServerOnCrashToggle_Toggled(object sender, RoutedEventArgs e) => TriggerAutoSave();
    private void ForceMultiThreadToggle_Toggled(object sender, RoutedEventArgs e) => TriggerAutoSave();
    private void DownloadThreadsSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => TriggerAutoSave();
    private void ConsoleFontSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e) => TriggerAutoSave();
    private void ConsoleEncodingCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => TriggerAutoSave();

    private void MirrorSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MirrorSourceCombo.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag is string source)
        {
            _viewModel.Settings.DownloadMirrorSource = source;
            UpdateMirrorSourceHint(source);
            
            // 不检查 _isInitializing，直接保存
            TriggerAutoSave();
        }
    }

    private void UpdateMirrorSourceHint(string source)
    {
        var config = MirrorSourceService.GetSourceConfig(source);
        MirrorSourceHintText.Text = config.Description;
    }

    private void BackupIntervalHoursBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        // 在初始化期间不处理事件
        if (_isInitializing) return;
        
        // 动态调整分钟框的最小值
        if (BackupIntervalMinutesBox != null)
        {
            if (sender.Value == 0)
            {
                // 小时为0时，分钟最小为1
                BackupIntervalMinutesBox.Minimum = 1;
                if (BackupIntervalMinutesBox.Value < 1)
                {
                    BackupIntervalMinutesBox.Value = 1;
                }
            }
            else
            {
                // 小时不为0时，分钟可以为0
                BackupIntervalMinutesBox.Minimum = 0;
            }
        }
        
        UpdateBackupIntervalHint();
        TriggerAutoSave();
    }

    private void BackupIntervalMinutesBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        // 在初始化期间不处理事件
        if (_isInitializing) return;
        
        // 当小时为0时，确保分钟至少为1
        if (BackupIntervalHoursBox?.Value == 0)
        {
            if (sender.Value < 1)
            {
                sender.Value = 1;
            }
            else if (sender.Value > 59)
            {
                sender.Value = 59;
            }
        }
        else
        {
            // 当小时不为0时，分钟可以为0-59
            if (sender.Value < 0)
            {
                sender.Value = 0;
            }
            else if (sender.Value > 59)
            {
                sender.Value = 59;
            }
            
            // 如果分钟被设置为0，需要更新最小值
            if (BackupIntervalMinutesBox != null)
            {
                BackupIntervalMinutesBox.Minimum = 0;
            }
        }
        UpdateBackupIntervalHint();
        TriggerAutoSave();
    }

    private void UpdateBackupIntervalHint()
    {
        // 在初始化期间或控件未就绪时不处理
        if (_isInitializing || BackupIntervalHoursBox == null || BackupIntervalMinutesBox == null || BackupIntervalHintText == null) 
            return;
        
        var hours = (int)BackupIntervalHoursBox.Value;
        var minutes = (int)BackupIntervalMinutesBox.Value;
        var totalMinutes = hours * 60 + minutes;
        
        if (hours > 0 && minutes > 0)
        {
            BackupIntervalHintText.Text = $"总计: {hours} 小时 {minutes} 分钟 ({totalMinutes} 分钟)";
        }
        else if (hours > 0)
        {
            BackupIntervalHintText.Text = $"总计: {hours} 小时 ({totalMinutes} 分钟)";
        }
        else
        {
            BackupIntervalHintText.Text = $"总计: {minutes} 分钟";
        }
    }

    private void EnableAutoBackupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        // 在初始化期间不处理事件
        if (_isInitializing) return;
        
        // 当启用自动备份时，更新分钟最小值的逻辑
        if (EnableAutoBackupToggle.IsOn && BackupIntervalHoursBox?.Value == 0)
        {
            if (BackupIntervalMinutesBox != null)
            {
                BackupIntervalMinutesBox.Minimum = 1;
            }
        }
        TriggerAutoSave();
    }

    private void AutoCheckUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _viewModel.Settings.AutoCheckUpdate = AutoCheckUpdateToggle.IsOn;
        TriggerAutoSave();
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        var updateService = App.UpdateService;
        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButton.Content = "检查中...";
        try
        {
            var result = await updateService.CheckForUpdateAsync();
            await ShowUpdateResultDialogAsync(result, updateService);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
            CheckUpdateButton.Content = "检查更新";
            PendingUpdatePanel.Visibility = updateService.HasPendingUpdate ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async Task ShowUpdateResultDialogAsync(UpdateCheckResult result, UpdateService updateService)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "检查更新"
        };
        string message;
        string primaryText = "关闭";
        Action? primaryAction = null;

        if (!result.Success)
        {
            message = result.ErrorMessage ?? "检查更新失败，请稍后重试。";
        }
        else if (result.HasUpdate && !string.IsNullOrEmpty(result.LatestVersion))
        {
            var sizeStr = result.FileSize > 0 ? $" ({FormatFileSize(result.FileSize)})" : "";
            message = $"发现新版本 {result.LatestVersion}{sizeStr}\n\n";
            if (!string.IsNullOrWhiteSpace(result.Changelog))
                message += "更新内容：\n" + result.Changelog.Trim();
            else
                message += "请更新以获取最新功能与修复。";

            if (updateService.HasPendingUpdate)
            {
                primaryText = "立即重启并更新";
                primaryAction = () =>
                {
                    if (updateService.ApplyUpdate())
                        Application.Current.Exit();
                };
            }
            else if (!string.IsNullOrEmpty(result.DownloadUrl))
            {
                primaryText = "下载更新";
                primaryAction = async () =>
                {
                    dialog.Hide();
                    await DownloadAndNotifyAsync(result, updateService);
                };
            }
        }
        else
        {
            message = "当前已是最新版本。";
        }

        var contentPanel = new StackPanel { Spacing = 12 };
        contentPanel.Children.Add(new TextBlock 
        { 
            Text = message, 
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        });

        var scroll = new ScrollViewer
        {
            Content = contentPanel,
            MaxHeight = 320
        };
        dialog.Content = scroll;
        dialog.PrimaryButtonText = primaryText;
        dialog.CloseButtonText = primaryAction != null ? "稍后" : "关闭";

        if (primaryAction != null)
        {
            dialog.PrimaryButtonClick += (_, _) =>
            {
                primaryAction();
            };
        }

        await dialog.ShowAsync();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private async Task DownloadAndNotifyAsync(UpdateCheckResult result, UpdateService updateService)
    {
        var progressPanel = new StackPanel { Spacing = 12 };
        var progressBar = new ProgressBar { IsIndeterminate = true, Height = 8 };
        var progressText = new TextBlock { Text = "正在下载更新...", FontSize = 14, Margin = new Thickness(0, 8, 0, 0) };
        var progressDetails = new TextBlock { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        
        progressPanel.Children.Add(progressBar);
        progressPanel.Children.Add(progressText);
        progressPanel.Children.Add(progressDetails);

        var progressDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "下载更新",
            Content = progressPanel,
            CloseButtonText = "取消"
        };
        
        // 订阅下载进度事件
        void OnProgressChanged(object? sender, double progress)
        {
            progressDetails.DispatcherQueue.TryEnqueue(() =>
            {
                progressDetails.Text = $"进度：{progress:F1}%";
            });
        }
        
        updateService.DownloadProgressChanged += OnProgressChanged;
        progressDialog.Closed += (_, _) => updateService.CancelDownload();
        
        _ = progressDialog.ShowAsync();

        try
        {
            var downloadResult = await updateService.DownloadUpdateAsync(result.DownloadUrl!, result.FileHash);
            updateService.DownloadProgressChanged -= OnProgressChanged;
            
            try { progressDialog.Hide(); } catch { }
            
            if (downloadResult.Success)
            {
                PendingUpdatePanel.Visibility = Visibility.Visible;
                var notify = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "下载完成",
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = $"新版本 {result.LatestVersion} 已下载完成！",
                                FontSize = 14,
                                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                                TextWrapping = TextWrapping.Wrap
                            },
                            new TextBlock
                            {
                                Text = "\n安装步骤：\n1. 点击「立即重启并更新」或关闭应用后重新打开\n2. 应用将自动打开安装程序\n3. 按照安装向导完成安装\n4. 安装完成后应用将自动重新启动",
                                FontSize = 13,
                                TextWrapping = TextWrapping.Wrap,
                                Opacity = 0.8
                            }
                        }
                    },
                    PrimaryButtonText = "立即重启并更新",
                    CloseButtonText = "稍后"
                };
                notify.PrimaryButtonClick += (_, _) =>
                {
                    if (updateService.ApplyUpdate())
                        Application.Current.Exit();
                };
                await notify.ShowAsync();
            }
            else if (downloadResult.ErrorMessage != "下载已取消")
            {
                var err = new ContentDialog
                {
                    XamlRoot = XamlRoot,
                    Title = "下载失败",
                    Content = new TextBlock 
                    { 
                        Text = downloadResult.ErrorMessage ?? "未知错误",
                        TextWrapping = TextWrapping.Wrap
                    },
                    CloseButtonText = "关闭"
                };
                await err.ShowAsync();
            }
        }
        finally
        {
            updateService.DownloadProgressChanged -= OnProgressChanged;
            try { progressDialog.Hide(); } catch { }
        }
    }

    private void RestartAndUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (App.UpdateService.ApplyUpdate())
            Application.Current.Exit();
    }

    private async Task LoadAcknowledgmentsAsync()
    {
        try
        {
            var response = await _apiService.GetAcknowledgmentsAsync();
            
            AcknowledgmentsPanel.Children.Clear();
            
            if (response.Success && response.Data != null && response.Data.Count > 0)
            {
                foreach (var ack in response.Data)
                {
                    var button = new Button
                    {
                        Tag = ack.Link,
                        Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(12),
                        CornerRadius = new CornerRadius(8),
                        Width = 140,
                        Height = 80
                    };

                    var stackPanel = new StackPanel
                    {
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                    if (!string.IsNullOrEmpty(ack.ImageUrl))
                    {
                        var image = new Image
                        {
                            Source = new BitmapImage(new Uri(ack.ImageUrl)),
                            Width = 32,
                            Height = 32,
                            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Center
                        };
                        stackPanel.Children.Add(image);
                    }

                    var textBlock = new TextBlock
                    {
                        Text = ack.Name,
                        FontSize = 12,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.NoWrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        MaxWidth = 120
                    };
                    stackPanel.Children.Add(textBlock);

                    button.Content = stackPanel;
                    button.Click += Acknowledgment_Click;

                    AcknowledgmentsPanel.Children.Add(button);
                }
                NoAcknowledgmentsText.Visibility = Visibility.Collapsed;
            }
            else
            {
                NoAcknowledgmentsText.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Settings] 加载鸣谢失败: {ex.Message}");
            NoAcknowledgmentsText.Visibility = Visibility.Visible;
        }
        finally
        {
            AcknowledgmentsLoadingRing.IsActive = false;
        }
    }

    private async void Acknowledgment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string link && !string.IsNullOrEmpty(link))
        {
            try
            {
                await Launcher.LaunchUriAsync(new Uri(link));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] 打开链接失败: {ex.Message}");
            }
        }
    }
}
