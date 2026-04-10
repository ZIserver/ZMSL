using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;
using ZMSL.App.Models;
using ZMSL.App.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.ObjectModel; // Add this

namespace ZMSL.App.Views;

public sealed partial class AdvancedModePage : Page
{
    private readonly ServerDownloadService _downloadService;
    private readonly JavaManagerService _javaManager;
    private List<ServerDownloadService.ServerCoreCategory> _categories = new();
    private string? _selectedCore;
    private string? _selectedVersion;
    private string? _javaPath;
    private bool _isLocalImport = false;
    private string? _localCorePath;
    private string? _localMinecraftVersion;
    private bool _isLoading = false;

    public class LocalJavaInstallation
    {
        public string DisplayText { get; set; } = "";
        public string Path { get; set; } = "";
        public int Version { get; set; }
        public string Source { get; set; } = "";
    }

    // 配置结果
    public LocalServer ConfiguredServer { get; private set; } = new();

    private ObservableCollection<LocalJavaInstallation> _availableJavas = new();

    public AdvancedModePage()
    {
        this.InitializeComponent();
        _downloadService = App.Services.GetRequiredService<ServerDownloadService>();
        _javaManager = App.Services.GetRequiredService<JavaManagerService>();
        
        // 初始化配置对象
        ConfiguredServer.Mode = CreateMode.Advanced;
        ConfiguredServer.UseLatestPurpur = false;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        _isLoading = true;

        // 1. 启动后台 Java 检测 (不阻塞 UI)
        var javaTask = DetectAllJavaVersionsAsync();

        try
        {
            // 2. 优先加载服务端列表并显示
            await LoadServerCategories();
            UpdateSummary();
        }
        catch (Exception ex)
        {
            await ShowErrorDialog($"加载失败: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }

        // 3. 等待 Java 检测完成 (仅为了捕获异常，不阻塞上述 UI 加载)
        try
        {
            await javaTask;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Background Java detection error: {ex}");
        }
    }

    /// <summary>
    /// 检测所有可用的Java版本 - 重构版
    /// </summary>
    private async Task DetectAllJavaVersionsAsync()
    {
        try
        {
            // 1. 设置加载状态
            DispatcherQueue.TryEnqueue(() =>
            {
                 JavaInfoText.Text = "正在从数据库加载 Java...";
                 JavaVersionComboBox.PlaceholderText = "加载中...";
                 JavaVersionComboBox.IsEnabled = false;
                 _availableJavas.Clear();
            });
            
            // 2. 从数据库读取
             var db = App.Services.GetRequiredService<DatabaseService>();
             var javas = await db.ExecuteWithLockAsync(async context => 
                 await context.JavaInstallations
                     .Where(j => j.IsValid)
                     .OrderByDescending(j => j.Version)
                     .ToListAsync());

            // 3. 如果数据库为空，启动自动扫描
            if (javas.Count == 0)
            {
                DispatcherQueue.TryEnqueue(() => JavaInfoText.Text = "数据库为空，正在进行快速全盘扫描...");
                
                // 扫描逻辑
                var scanned = await _javaManager.DetectInstalledJavaAsync(); // 不再使用进度回调，减少 UI 交互
                
                // 批量保存
                if (scanned.Count > 0)
                {
                    await db.ExecuteWithLockAsync(async context =>
                    {
                        foreach (var java in scanned)
                        {
                            if (!await context.JavaInstallations.AnyAsync(j => j.Path == java.Path))
                            {
                                context.JavaInstallations.Add(java.ToModel());
                            }
                        }
                        await context.SaveChangesAsync();
                    });
                    
                    // 重新读取
                    javas = await db.ExecuteWithLockAsync(async context => 
                        await context.JavaInstallations
                            .Where(j => j.IsValid)
                            .OrderByDescending(j => j.Version)
                            .ToListAsync());
                }
            }

            // 4. 更新 UI（一次性操作，避免复杂的增量更新）
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (JavaVersionComboBox == null || JavaInfoText == null) return;

                    _availableJavas.Clear();
                    foreach (var java in javas)
                    {
                        _availableJavas.Add(new LocalJavaInstallation 
                        {
                            DisplayText = $"Java {java.Version} ({java.Path})",
                            Path = java.Path,
                            Version = java.Version,
                            Source = java.Source
                        });
                    }
                    
                    // 确保 ItemsSource 已设置
                    if (JavaVersionComboBox.ItemsSource == null)
                    {
                        JavaVersionComboBox.ItemsSource = _availableJavas;
                    }
                    
                    JavaVersionComboBox.IsEnabled = true;
                    
                    if (_availableJavas.Count > 0)
                    {
                        // 尝试恢复选择
                        var targetPath = _javaPath;
                        // 如果之前没选，且当前有列表，默认选第一个
                        if (string.IsNullOrEmpty(targetPath)) 
                        {
                            JavaVersionComboBox.SelectedIndex = 0;
                        }
                        else
                        {
                            var selected = _availableJavas.FirstOrDefault(j => j.Path == targetPath);
                            if (selected != null)
                            {
                                JavaVersionComboBox.SelectedItem = selected;
                            }
                            else
                            {
                                JavaVersionComboBox.SelectedIndex = 0;
                            }
                        }
                        
                        JavaInfoText.Text = $"已加载 {_availableJavas.Count} 个 Java 版本";
                    }
                    else
                    {
                        JavaInfoText.Text = "未找到 Java，请在设置中手动扫描";
                        JavaVersionComboBox.PlaceholderText = "未找到 Java";
                    }
                }
                catch (Exception ex) 
                { 
                    System.Diagnostics.Debug.WriteLine($"[AdvancedModePage] UI Update Error: {ex}");
                }
            });
        }
        catch (Exception ex)
        {
            DispatcherQueue.TryEnqueue(() => 
            {
                JavaInfoText.Text = $"加载失败: {ex.Message}";
                JavaVersionComboBox.IsEnabled = true;
            });
        }
    }

    /// <summary>
    /// Java版本选择改变
    /// </summary>
    private void JavaVersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (JavaVersionComboBox.SelectedItem is LocalJavaInstallation selected)
        {
            _javaPath = selected.Path;
            JavaInfoText.Text = $"已选择: Java {selected.Version}\n路径: {selected.Path}";
        }
    }

    private async Task LoadServerCategories()
    {
        _categories = await _downloadService.GetServerCategoriesAsync();
        
        // 构造核心分类列表
        var categoryData = _categories.Select(cat => new 
        { 
            Name = cat.Name,
            DisplayName = cat.DisplayName
        }).ToList();
        
        CoreCategoriesPanel.ItemsSource = categoryData;
    }

    private void Category_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string categoryName)
        {
            var category = _categories.FirstOrDefault(c => c.Name == categoryName);
            if (category != null)
            {
                SelectedCategoryText.Text = $"{category.DisplayName}";
                CoreListView.ItemsSource = category.Cores;
                _selectedCore = null;
                _selectedVersion = null;
                VersionSelectionBorder.Visibility = Visibility.Collapsed;
                UpdateSummary();
                UpdateNextButtonState();
            }
        }
    }

    private async void Core_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string coreName)
        {
            _selectedCore = coreName;
            SummaryCoreText.Text = $"{coreName}";
            SummaryCoreText2.Text = $"核心: {coreName}";
            
            try
            {
                var versions = await _downloadService.GetAvailableVersionsAsync(coreName);
                VersionListView.ItemsSource = versions;
                VersionSelectionBorder.Visibility = Visibility.Visible;
                _selectedVersion = versions.FirstOrDefault();
                
                if (!string.IsNullOrEmpty(_selectedVersion))
                {
                    SummaryCoreText.Text = $"{coreName} ({_selectedVersion})";
                    SummaryCoreText2.Text = $"核心: {coreName} {_selectedVersion}";
                    await DetectAndSetupJavaAsync(_selectedVersion);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorDialog($"加载版本失败: {ex.Message}");
            }
            
            UpdateSummary();
            UpdateNextButtonState();
        }
    }

    private async void Version_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string version)
        {
            _selectedVersion = version;
            SummaryCoreText.Text = $"{_selectedCore} ({version})";
            SummaryCoreText2.Text = $"核心: {_selectedCore} {version}";
            await DetectAndSetupJavaAsync(version);
            UpdateSummary();
            UpdateNextButtonState();
        }
    }

    private void MemorySlider_ValueChanged(object sender, RoutedEventArgs e)
    {
        // 在InitializeComponent期间，某些控件可能还未初始化
        if (MinMemorySlider == null || MaxMemorySlider == null || 
            MinMemoryValueText == null || MaxMemoryValueText == null)
        {
            return;
        }
        
        // 确保最小内存不超过最大内存
        if (MinMemorySlider.Value > MaxMemorySlider.Value)
        {
            MinMemorySlider.Value = MaxMemorySlider.Value;
        }
        
        MinMemoryValueText.Text = $"{MinMemorySlider.Value} MB";
        MaxMemoryValueText.Text = $"{MaxMemorySlider.Value} MB";
        
        // 检查内存警告
        CheckMemoryWarning();
        UpdateSummary();
    }

    private void CheckMemoryWarning()
    {
        if (MemoryWarningInfoBar == null || MaxMemorySlider == null)
        {
            return;
        }
        
        var maxMemoryMB = MaxMemorySlider.Value;
        var totalMemoryMB = (Environment.WorkingSet / 1024 / 1024) * 2; // 粗略估计
        
        if (maxMemoryMB > totalMemoryMB * 0.8) // 超过物理内存的80%
        {
            MemoryWarningInfoBar.IsOpen = true;
            MemoryWarningInfoBar.Message = $"建议最大内存({maxMemoryMB}MB)不要超过系统内存的80%({totalMemoryMB * 0.8:F0}MB)";
        }
        else
        {
            MemoryWarningInfoBar.IsOpen = false;
        }
    }

    /// <summary>
    /// 检测并设置Java
    /// </summary>
    private async Task DetectAndSetupJavaAsync(string mcVersion)
    {
        if (JavaInfoText == null) return;

        try
        {
            // 根据MC版本获取推荐的Java版本
            var recommendedVersion = JavaManagerService.GetRecommendedJavaVersion(mcVersion);
            
            // 查找已检测到的匹配版本
            var matching = _availableJavas.FirstOrDefault(j => j.Version == recommendedVersion);
            
            if (matching != null)
            {
                // 自动选择匹配的Java
                JavaVersionComboBox.SelectedItem = matching;
                _javaPath = matching.Path;
                JavaInfoText.Text = $"已自动选择: Java {matching.Version}\n路径: {matching.Path}";
            }
            else
            {
                // 没有匹配的，需要下载
                JavaInfoText.Text = $"需要 Java {recommendedVersion}，正在下载...";
                
                var javaPath = await _javaManager.GetOrDownloadJavaAsync(recommendedVersion, new Progress<double>(p =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        JavaInfoText.Text = $"正在下载 Java {recommendedVersion}... {p:F1}%";
                    });
                }));
                
                if (!string.IsNullOrEmpty(javaPath))
                {
                    _javaPath = javaPath;
                    
                    // 添加到列表
                    var newJava = new LocalJavaInstallation
                    {
                        DisplayText = $"Java {recommendedVersion} (已下载)",
                        Path = javaPath,
                        Version = recommendedVersion,
                        Source = "Downloaded"
                    };
                    _availableJavas.Add(newJava);
                    
                    // 重新排序
                    var sortedList = _availableJavas.OrderByDescending(j => j.Version).ToList();
                    _availableJavas.Clear();
                    foreach (var item in sortedList)
                    {
                        _availableJavas.Add(item);
                    }
                    
                    JavaVersionComboBox.ItemsSource = null;
                    JavaVersionComboBox.ItemsSource = _availableJavas;
                    JavaVersionComboBox.SelectedItem = newJava;
                    
                    JavaInfoText.Text = $"Java {recommendedVersion} 已就绪\n路径: {javaPath}";
                }
                else
                {
                    JavaInfoText.Text = $"无法获取 Java {recommendedVersion}，请检查网络";
                }
            }
        }
        catch (Exception ex)
        {
            JavaInfoText.Text = $"Java检测失败: {ex.Message}";
        }
    }

    private void CoreSource_Checked(object sender, RoutedEventArgs e)
    {
        if (OnlineModeRadio == null || LocalModeRadio == null || OnlineSelectionGrid == null || LocalImportPanel == null)
            return;

        if (OnlineModeRadio.IsChecked == true)
        {
            _isLocalImport = false;
            OnlineSelectionGrid.Visibility = Visibility.Visible;
            LocalImportPanel.Visibility = Visibility.Collapsed;
            
            // Restore online summary
            if (!string.IsNullOrEmpty(_selectedCore))
            {
                SummaryCoreText.Text = string.IsNullOrEmpty(_selectedVersion) ? $"{_selectedCore}" : $"{_selectedCore} ({_selectedVersion})";
                SummaryCoreText2.Text = string.IsNullOrEmpty(_selectedVersion) ? $"核心: {_selectedCore}" : $"核心: {_selectedCore} {_selectedVersion}";
            }
            else
            {
                SummaryCoreText.Text = "未选择核心";
                SummaryCoreText2.Text = "核心: 未选择";
            }
        }
        else if (LocalModeRadio.IsChecked == true)
        {
            _isLocalImport = true;
            OnlineSelectionGrid.Visibility = Visibility.Collapsed;
            LocalImportPanel.Visibility = Visibility.Visible;
            
            // Update summary immediately for local mode
            if (!string.IsNullOrEmpty(_localCorePath))
            {
                 var fileName = System.IO.Path.GetFileName(_localCorePath);
                 SummaryCoreText.Text = fileName;
                 SummaryCoreText2.Text = $"核心: {fileName}";
            }
            else
            {
                 SummaryCoreText.Text = "本地导入";
                 SummaryCoreText2.Text = "核心: 本地导入";
            }
        }
        UpdateNextButtonState();
    }

    private async void BrowseLocalCore_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        picker.FileTypeFilter.Add(".jar");

        // Initialize picker with window handle
        var window = App.MainWindow;
        if (window != null)
        {
            var hWnd = WindowNative.GetWindowHandle(window);
            InitializeWithWindow.Initialize(picker, hWnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            _localCorePath = file.Path;
            LocalCorePathTextBox.Text = file.Path;
            
            SummaryCoreText.Text = file.Name;
            SummaryCoreText2.Text = $"核心: {file.Name}";
            
            UpdateNextButtonState();
        }
    }

    private async void LocalCoreVersion_TextChanged(object sender, TextChangedEventArgs e)
    {
        _localMinecraftVersion = LocalCoreVersionTextBox.Text.Trim();
        UpdateNextButtonState();
        
        // Try to detect Java for this version
        if (!string.IsNullOrEmpty(_localMinecraftVersion))
        {
             // Debounce slightly or just call it (it's async)
             await DetectAndSetupJavaAsync(_localMinecraftVersion);
        }
    }

    private void UpdateSummary()
    {
        if (SummaryMemoryText == null || MinMemorySlider == null || 
            MaxMemorySlider == null || SummaryPortText == null || PortNumberBox == null)
        {
            return;
        }
        
        var minGB = MinMemorySlider.Value / 1024;
        var maxGB = MaxMemorySlider.Value / 1024;
        SummaryMemoryText.Text = $"内存: {minGB:F1}-{maxGB:F1} GB";
        SummaryPortText.Text = $"端口: {(int)PortNumberBox.Value}";
    }

    private void UpdateNextButtonState()
    {
        bool canProceed = !string.IsNullOrEmpty(ServerNameTextBox.Text.Trim());
        
        if (_isLocalImport)
        {
            canProceed = canProceed && !string.IsNullOrEmpty(_localCorePath) && !string.IsNullOrEmpty(_localMinecraftVersion);
        }
        else
        {
            canProceed = canProceed && !string.IsNullOrEmpty(_selectedCore) && !string.IsNullOrEmpty(_selectedVersion);
        }
        
        NextButton.IsEnabled = canProceed;
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await ValidateInput())
        {
            return;
        }

        // 设置配置
        ConfiguredServer.Name = ServerNameTextBox.Text.Trim();
        ConfiguredServer.MinMemoryMB = (int)MinMemorySlider.Value;
        ConfiguredServer.MaxMemoryMB = (int)MaxMemorySlider.Value;
        ConfiguredServer.JavaPath = _javaPath; // 使用自动检测的Java路径
        ConfiguredServer.JvmArgs = JvmArgsTextBox.Text.Trim();
        ConfiguredServer.Port = (int)PortNumberBox.Value;
        ConfiguredServer.AutoAcceptEula = AutoAcceptEulaCheckBox.IsChecked == true;

        if (_isLocalImport)
        {
             ConfiguredServer.CoreType = "Custom";
             ConfiguredServer.CoreVersion = "Local";
             ConfiguredServer.MinecraftVersion = _localMinecraftVersion!;
             ConfiguredServer.ImportSourcePath = _localCorePath;
        }
        else
        {
            ConfiguredServer.CoreType = _selectedCore!;
            ConfiguredServer.CoreVersion = _selectedVersion!;
            ConfiguredServer.MinecraftVersion = _selectedVersion!;
            ConfiguredServer.ImportSourcePath = null;
        }

        // 导航到确认页面
        Frame.Navigate(typeof(CreateConfirmationPage), ConfiguredServer);
    }

    private async Task<bool> ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(ServerNameTextBox.Text))
        {
            await ShowErrorDialog("请输入服务器名称");
            return false;
        }

        if (_isLocalImport)
        {
            if (string.IsNullOrEmpty(_localCorePath))
            {
                await ShowErrorDialog("请选择本地核心文件");
                return false;
            }
             if (string.IsNullOrEmpty(_localMinecraftVersion))
            {
                await ShowErrorDialog("请输入Minecraft版本");
                return false;
            }
        }
        else
        {
            if (string.IsNullOrEmpty(_selectedCore))
            {
                await ShowErrorDialog("请选择服务端核心");
                return false;
            }

            if (string.IsNullOrEmpty(_selectedVersion))
            {
                await ShowErrorDialog("请选择版本");
                return false;
            }
        }

        if (MinMemorySlider.Value > MaxMemorySlider.Value)
        {
            await ShowErrorDialog("最小内存不能大于最大内存");
            return false;
        }

        if (MinMemorySlider.Value < 512)
        {
            await ShowErrorDialog("最小内存至少512MB");
            return false;
        }

        if (MaxMemorySlider.Value > 32768)
        {
            await ShowErrorDialog("最大内存不应超过32GB");
            return false;
        }

        if (PortNumberBox.Value < 1 || PortNumberBox.Value > 65535)
        {
            await ShowErrorDialog("端口号必须在1-65535之间");
            return false;
        }

        return true;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.GoBack();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(CreateModeSelectionPage));
    }

    private async Task ShowErrorDialog(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "配置错误",
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
